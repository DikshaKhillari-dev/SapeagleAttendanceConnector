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
        try { return JsonSerializer.Deserialize<List<AttendanceLog>>(File.ReadAllText(_queuePath)) ?? new(); }
        catch { return new List<AttendanceLog>(); }
    }

    private void SaveInternal(List<AttendanceLog> list)
    {
        File.WriteAllText(_queuePath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
    }
}