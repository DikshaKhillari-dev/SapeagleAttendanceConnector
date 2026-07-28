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
            return JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(_path))
                   ?? new Dictionary<string, DateTime>();
        }
        catch { return new Dictionary<string, DateTime>(); }
    }

    private void SaveInternal(Dictionary<string, DateTime> data)
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    }
}