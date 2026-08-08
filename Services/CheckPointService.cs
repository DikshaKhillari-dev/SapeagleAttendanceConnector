using System.Text.Json;

namespace SapeagleAttendanceConnector.Services;

public class CheckpointService
{
    private readonly string _path;
    private readonly object _lock = new();
    private Dictionary<string, DateTime> _checkpoints;

    public CheckpointService()
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SapeagleAttendanceConnector", "Data");
        Directory.CreateDirectory(dataDir);
        _path = Path.Combine(dataDir, "checkpoints.json");
        _checkpoints = LoadInternal();
    }

    public DateTime GetLastSynced(string deviceKey)
    {
        lock (_lock)
        {
            return _checkpoints.TryGetValue(deviceKey, out var dt) ? dt : DateTime.MinValue;
        }
    }

    public void UpdateLastSynced(string deviceKey, DateTime timestamp)
    {
        lock (_lock)
        {
            if (!_checkpoints.TryGetValue(deviceKey, out var existing) || timestamp > existing)
            {
                _checkpoints[deviceKey] = timestamp;
                SaveInternal(_checkpoints);
            }
        }
    }

    private Dictionary<string, DateTime> LoadInternal()
    {
        if (!File.Exists(_path)) return new Dictionary<string, DateTime>();
        try
        {
            var text = ReadWithRetry(_path);
            return JsonSerializer.Deserialize<Dictionary<string, DateTime>>(text)
                   ?? new Dictionary<string, DateTime>();
        }
        catch (Exception ex)
        {
            Logger.Log($"[Checkpoint] LoadInternal: failed after retries - {ex.Message}");
            return new Dictionary<string, DateTime>();
        }
    }

    private void SaveInternal(Dictionary<string, DateTime> data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        WriteWithRetry(_path, json);
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

    public void MarkSyncedUpToNow(string deviceKey)
    {
        UpdateLastSynced(deviceKey, DateTime.Now);
    }

    public void ResetCheckpoint(string deviceKey)
    {
        lock (_lock)
        {
            if (_checkpoints.Remove(deviceKey))
                SaveInternal(_checkpoints);
        }
    }

    public IReadOnlyDictionary<string, DateTime> GetAllCheckpoints()
    {
        lock (_lock)
        {
            return new Dictionary<string, DateTime>(_checkpoints);
        }
    }
}