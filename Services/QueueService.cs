using System.Text.Json;
using SapeagleAttendanceConnector.Models;

namespace SapeagleAttendanceConnector.Services;

public class QueueService
{
    private readonly string _queuePath;
    private readonly object _lock = new();

    public QueueService()
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SapeagleAttendanceConnector", "Data");
        Directory.CreateDirectory(dataDir);
        _queuePath = Path.Combine(dataDir, "queue.json");
    }

    public void Enqueue(AttendanceLog log)
    {
        lock (_lock)
        {
            var list = LoadInternal();
            list.Add(log);
            SaveInternal(list);
        }
    }

    public List<AttendanceLog> LoadAll() { lock (_lock) return LoadInternal(); }

    public void Remove(Guid id)
    {
        lock (_lock)
        {
            var list = LoadInternal();
            list.RemoveAll(x => x.Id == id);
            SaveInternal(list);
        }
    }

    private List<AttendanceLog> LoadInternal()
    {
        if (!File.Exists(_queuePath)) return new List<AttendanceLog>();
        try
        {
            var text = ReadWithRetry(_queuePath);
            return JsonSerializer.Deserialize<List<AttendanceLog>>(text) ?? new();
        }
        catch (Exception ex)
        {
            Logger.Log($"[Queue] LoadInternal: failed after retries - {ex.Message}");
            return new List<AttendanceLog>();
        }
    }

    private void SaveInternal(List<AttendanceLog> list)
    {
        var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
        WriteWithRetry(_queuePath, json);
    }

    private static string ReadWithRetry(string path, int maxAttempts = 5)
    {
        Exception? last = null;
        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException ex) { last = ex; Thread.Sleep(150 * (i + 1)); }
            catch (UnauthorizedAccessException ex) { last = ex; Thread.Sleep(150 * (i + 1)); }
        }
        throw last ?? new IOException($"Could not read '{path}'");
    }

    private static void WriteWithRetry(string path, string content, int maxAttempts = 5)
    {
        Exception? last = null;
        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream);
                writer.Write(content);
                return;
            }
            catch (IOException ex) { last = ex; Thread.Sleep(150 * (i + 1)); }
            catch (UnauthorizedAccessException ex) { last = ex; Thread.Sleep(150 * (i + 1)); }
        }
        throw last ?? new IOException($"Could not write '{path}'");
    }
}