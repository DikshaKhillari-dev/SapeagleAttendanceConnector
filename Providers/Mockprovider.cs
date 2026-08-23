using System.Text.Json;
using SapeagleAttendanceConnector.Services;

namespace SapeagleAttendanceConnector;

/// <summary>
/// Fake device for testing the connector end-to-end (Connect -> FetchNewAttendanceRecords ->
/// checkpoint -> queue -> sync -> API) without any physical hardware.
///
/// Instead of talking to a real SDK, this reads a local JSON file (one per DeviceKey) that
/// you can hand-edit while the app is running to simulate the device "seeing" a new punch:
///
///   %LOCALAPPDATA%\SapeagleAttendanceConnector\mock_devices\Mock_1.json
///
/// File format:
/// {
///   "employees": [ { "enrollNumber": "101", "name": "Test User" } ],
///   "punches": [
///     { "enrollNumber": "101", "verifyMode": 1, "inOutMode": 0, "timestamp": "2026-08-22T09:00:00" }
///   ]
/// }
///
/// Every FetchNewAttendanceRecords call re-reads the file, so you can append a new punch
/// entry, save, and watch it flow through the same sync cycle a real device's punch would.
/// </summary>
public class MockProvider : IAttendanceProvider
{
    private readonly string _deviceKey;
    private readonly string _filePath;
    private readonly CheckpointService _checkpoint;
    private bool _isConnected;

    public string DeviceKey => _deviceKey;

    public MockProvider(int machineConfigId, CheckpointService checkpoint)
    {
        _checkpoint = checkpoint;
        _deviceKey = $"Mock:{machineConfigId}";

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SapeagleAttendanceConnector", "mock_devices");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, $"Mock_{machineConfigId}.json");

        if (!File.Exists(_filePath))
            SeedFile();
    }

    public bool Connect()
    {
        _isConnected = true;
        Logger.Log($"[Mock] Connected (file={_filePath})");
        return true;
    }

    public void Disconnect()
    {
        _isConnected = false;
        Logger.Log("[Mock] Disconnected");
    }

    public List<AttendancePunch> FetchNewAttendanceRecords()
    {
        var records = new List<AttendancePunch>();
        if (!_isConnected && !Connect()) return records;

        var lastSynced = _checkpoint.GetLastSynced(_deviceKey);
        var data = ReadFile();

        var allRecords = data.Punches.Select(p =>
        {
            var timestamp = DateTime.Parse(p.Timestamp);
            return new AttendancePunch
            {
                EnrollNumber = p.EnrollNumber,
                VerifyMode = p.VerifyMode,
                InOutMode = p.InOutMode,
                Timestamp = timestamp,
                EventId = AttendanceIdentity.ComputeEventId(_deviceKey, p.EnrollNumber, timestamp, p.VerifyMode, p.InOutMode)
            };
        }).ToList();

        records = allRecords.Where(r => r.Timestamp > lastSynced)
                             .OrderBy(r => r.Timestamp)
                             .ToList();

        Logger.Log($"[Mock] File has {allRecords.Count} total record(s), " +
                   $"{records.Count} new since checkpoint {lastSynced:yyyy-MM-dd HH:mm:ss}.");

        return records;
    }

    public List<Models.MachineEmployee> ReadExistingEmployees()
    {
        var data = ReadFile();
        return data.Employees
            .Select(e => new Models.MachineEmployee { EnrollNumber = e.EnrollNumber, Name = e.Name })
            .ToList();
    }

    public bool CreateEmployee(string enrollNumber, string employeeName, string? fallbackNumericId = null)
    {
        var data = ReadFile();
        if (data.Employees.Any(e => e.EnrollNumber == enrollNumber))
            return true;

        data.Employees.Add(new MockEmployee { EnrollNumber = enrollNumber, Name = employeeName });
        WriteFile(data);
        Logger.Log($"[Mock] CreateEmployee EnrollNumber={enrollNumber} Name={employeeName} -> true");
        return true;
    }

    public bool DeleteEmployee(string enrollNumber)
    {
        var data = ReadFile();
        var removed = data.Employees.RemoveAll(e => e.EnrollNumber == enrollNumber) > 0;
        WriteFile(data);
        Logger.Log($"[Mock] DeleteEmployee EnrollNumber={enrollNumber} -> {removed}");
        return removed;
    }

    /// <summary>Appends a punch as if the device just recorded it "now". Handy from a debug button.</summary>
    public void SimulatePunch(string enrollNumber, int verifyMode = 1, int inOutMode = 0)
    {
        var data = ReadFile();
        data.Punches.Add(new MockPunch
        {
            EnrollNumber = enrollNumber,
            VerifyMode = verifyMode,
            InOutMode = inOutMode,
            Timestamp = DateTime.Now.ToString("s")
        });
        WriteFile(data);
    }

    private void SeedFile()
    {
        var seed = new MockData
        {
            Employees = new List<MockEmployee>
            {
                new() { EnrollNumber = "101", Name = "Test User One" },
                new() { EnrollNumber = "102", Name = "Test User Two" }
            },
            Punches = new List<MockPunch>()
        };
        WriteFile(seed);
    }

    private MockData ReadFile()
    {
        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<MockData>(json) ?? new MockData();
        }
        catch (Exception ex)
        {
            Logger.Log($"[Mock] ReadFile error: {ex.Message}");
            return new MockData();
        }
    }

    private void WriteFile(MockData data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }

    public void Dispose() => Disconnect();

    private class MockData
    {
        public List<MockEmployee> Employees { get; set; } = new();
        public List<MockPunch> Punches { get; set; } = new();
    }

    private class MockEmployee
    {
        public string EnrollNumber { get; set; } = "";
        public string Name { get; set; } = "";
    }

    private class MockPunch
    {
        public string EnrollNumber { get; set; } = "";
        public int VerifyMode { get; set; }
        public int InOutMode { get; set; }
        public string Timestamp { get; set; } = "";
    }
}