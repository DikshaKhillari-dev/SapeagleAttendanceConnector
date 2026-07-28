using SapeagleAttendanceConnector.Services;

namespace SapeagleAttendanceConnector.SBXPC;

public class SBXPCProvider : IAttendanceProvider
{
    private readonly string _ip;
    private readonly int _port;
    private readonly int _machineNumber;
    private readonly int _password;
    private readonly string _deviceKey;
    private readonly CheckpointService _checkpoint;
    private bool _connected;

    public SBXPCProvider(string ip, int port, int machineNumber, int password, CheckpointService checkpoint)
    {
        _ip = ip; _port = port; _machineNumber = machineNumber; _password = password;
        _checkpoint = checkpoint;
        _deviceKey = $"SBXPC:{ip}:{machineNumber}";
    }

    public bool Connect()
    {
        if (_connected) return true;
        _connected = SBXPCNative.ConnectTcpip(_machineNumber, _ip, _port, _password);
        Logger.Log(_connected ? $"[SBXPC] Connected {_ip}:{_port}" : $"[SBXPC] Connect failed {_ip}:{_port}");
        return _connected;
    }

    public void Disconnect()
    {
        if (!_connected) return;
        try { SBXPCNative.Disconnect(_machineNumber); }
        finally { _connected = false; }
    }

    public List<AttendancePunch> FetchNewAttendanceRecords()
    {
        var records = new List<AttendancePunch>();
        if (!_connected && !Connect()) return records;

        var lastSynced = _checkpoint.GetLastSynced(_deviceKey);

        try
        {

            if (!SBXPCNative.ReadAllGLogData(_machineNumber))
            {
                Logger.Log($"[SBXPC] ReadAllGLogData returned false for MachineNumber={_machineNumber}");
                return records;
            }

            var allRecords = new List<AttendancePunch>();
            while (SBXPCNative.GetAllGLogData(_machineNumber, out int enroll, out int verify,
                       out int y, out int mo, out int d, out int h, out int mi))
            {
                allRecords.Add(new AttendancePunch
                {
                    EnrollNumber = enroll.ToString(),
                    VerifyMode = verify,
                    InOutMode = 0,
                    Timestamp = new DateTime(y, mo, d, h, mi, 0)
                });
            }

            records = allRecords.Where(r => r.Timestamp > lastSynced)
                                 .OrderBy(r => r.Timestamp)
                                 .ToList();

            Logger.Log($"[SBXPC] Device has {allRecords.Count} total record(s), " +
                       $"{records.Count} new since checkpoint {lastSynced:yyyy-MM-dd HH:mm:ss}.");

            if (records.Count > 0)
                _checkpoint.UpdateLastSynced(_deviceKey, records.Max(r => r.Timestamp));
        }
        catch (Exception ex)
        {
            Logger.Log($"[SBXPC] Fetch error: {ex.Message}");
            _connected = false;
        }

        return records;
    }


    public List<Models.MachineEmployee> ReadExistingEmployees()
    {
        var employees = new List<Models.MachineEmployee>();
        if (!_connected && !Connect()) return employees;

        if (!SBXPCNative.ReadAllUserID(_machineNumber))
        {
            Logger.Log($"[SBXPC] ReadAllUserID returned false for MachineNumber={_machineNumber}");
            return employees;
        }

        while (SBXPCNative.GetAllUserID(_machineNumber, out int enrollNumber))
        {
            string name = SBXPCNative.GetUserName1(_machineNumber, enrollNumber);
            employees.Add(new Models.MachineEmployee { EnrollNumber = enrollNumber.ToString(), Name = name });
        }

        Logger.Log($"[SBXPC] ReadExistingEmployees found {employees.Count} employee(s) on machine.");
        return employees;
    }

    public bool CreateEmployee(string enrollNumber, string employeeName)
    {
        if (!_connected && !Connect()) return false;
        if (!int.TryParse(enrollNumber, out int idNum))
        {
            Logger.Log($"[SBXPC] CreateEmployee: '{enrollNumber}' is not numeric, skipped.");
            return false;
        }

        bool created = SBXPCNative.SetEnrollData(_machineNumber, idNum);
        if (created) SBXPCNative.SetUserName1(_machineNumber, idNum, employeeName);

        Logger.Log($"[SBXPC] CreateEmployee EnrollNumber={enrollNumber} Name={employeeName} -> {created}");
        return created;
    }

    public bool DeleteEmployee(string enrollNumber)
    {
        if (!_connected && !Connect()) return false;
        if (!int.TryParse(enrollNumber, out int idNum)) return false;

        bool deleted = SBXPCNative.DeleteEnrollData(_machineNumber, idNum);
        Logger.Log($"[SBXPC] DeleteEmployee EnrollNumber={enrollNumber} -> {deleted}");
        return deleted;
    }

    public void Dispose() => Disconnect();
}