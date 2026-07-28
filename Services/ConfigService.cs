using System.Text.Json;
using SapeagleAttendanceConnector.Models;

namespace SapeagleAttendanceConnector.Services;

public class ConfigService
{
    private readonly string _configPath;

    public ConfigService()
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SapeagleAttendanceConnector");
        Directory.CreateDirectory(dataDir);
        _configPath = Path.Combine(dataDir, "config.json");
    }

    public CompanyConfig Load()
    {
        if (!File.Exists(_configPath)) return new CompanyConfig();
        try
        {
            return JsonSerializer.Deserialize<CompanyConfig>(File.ReadAllText(_configPath)) ?? new CompanyConfig();
        }
        catch { return new CompanyConfig(); }
    }

    public void Save(CompanyConfig config)
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_configPath, json);
    }
}