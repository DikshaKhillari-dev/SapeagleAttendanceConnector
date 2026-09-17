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

    public string DeviceKey => _deviceKey;

    public SBXPCProvider(string ip, int port, int machineNumber, int password, int machineConfigId, CheckpointService checkpoint)
    {
        _ip = ip; _port = port; _machineNumber = machineNumber; _password = password;
        _checkpoint = checkpoint;
        // Keyed on the ERP machine's stable primary key, not IP — IP can change (DHCP,
        // reactivation) without the checkpoint being lost.
        _deviceKey = $"SBXPC:{machineConfigId}";
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

            SBXPCNative.EnableDevice(_machineNumber, 0);

            if (!SBXPCNative.ReadAllGLogData(_machineNumber))
            {
                SBXPCNative.GetLastError(_machineNumber, out int errCode);
                Logger.Log($"[SBXPC] ReadAllGLogData failed for MachineNumber={_machineNumber}, ErrorCode={errCode}");
                _connected = false;
                SBXPCNative.EnableDevice(_machineNumber, 1);
                return records;
            }

            var allRecords = new List<AttendancePunch>();
            while (SBXPCNative.GetAllGLogData(_machineNumber, out int enroll, out int verify,
                       out int y, out int mo, out int d, out int h, out int mi, out int sec))
            {
                // dwVerifyMode is a packed value (see SBXPC manual, GetGeneralLogData):
                //   Byte 0 (verify & 0xFF)        -> clocking mode, e.g. 51/52/53 = In
                //                                     (FP/PWD/Card), 101/102/103 = Out,
                //                                     151/152/153 = Extra.
                //   Byte 1 ((verify >> 8) & 0xFF) -> attendance status (duty on/off,
                //                                     overtime on/off, go in/out) — only
                //                                     meaningful if the device's "duty
                //                                     on/off" submenu has been configured.
                //
                // On this device Byte 1 is always 0 (that submenu was never set up), so
                // relying on it alone made every punch resolve to "in" — evening punches
                // included. Byte 0's In/Out/Extra clocking-mode value is checked first
                // since it's what this device actually populates; Byte 1 is used as a
                // secondary signal only when it carries something other than the
                // ambiguous "0" (which means both "duty on" and "not populated").
                int lowByte = verify & 0xFF;
                int highByte = (verify >> 8) & 0xFF;

                int inOutMode;
                if (lowByte is 51 or 52 or 53)
                {
                    inOutMode = 0; // In
                }
                else if (lowByte is 101 or 102 or 103)
                {
                    inOutMode = 1; // Out
                }
                else if (lowByte is 151 or 152 or 153)
                {
                    inOutMode = 2; // Extra — not a plain in/out, let the caller decide
                }
                else if (highByte is 1 or 2 or 3 or 4 or 5)
                {
                    // Byte 1 carries a non-zero, meaningful attendance status.
                    inOutMode = highByte switch { 2 or 4 => 0, 1 or 3 or 5 => 1, _ => 2 };
                }
                else
                {
                    // Neither byte gave a definitive signal (e.g. plain FP/PWD/Card verify
                    // with no In/Out submenu and Byte 1 == 0). Let SyncService's
                    // alternating-session fallback decide instead of guessing "in" here.
                    inOutMode = 2;
                }

                // Preserve the full punch timestamp including seconds — truncating to the
                // minute would make multiple genuine punches within the same minute
                // indistinguishable from each other (e.g. 09:01:10 / 09:01:40 / 09:01:55).
                var timestamp = new DateTime(y, mo, d, h, mi, sec);
                var enrollStr = enroll.ToString();

                allRecords.Add(new AttendancePunch
                {
                    EnrollNumber = enrollStr,
                    VerifyMode = verify,
                    InOutMode = inOutMode,
                    Timestamp = timestamp,
                    EventId = AttendanceIdentity.ComputeEventId(_deviceKey, enrollStr, timestamp, verify, inOutMode)
                });
            }

            records = allRecords.Where(r => r.Timestamp > lastSynced)
                                 .OrderBy(r => r.Timestamp)
                                 .ToList();

            Logger.Log($"[SBXPC] Device has {allRecords.Count} total record(s), " +
                       $"{records.Count} new since checkpoint {lastSynced:yyyy-MM-dd HH:mm:ss}.");

            // NOTE: the checkpoint is intentionally NOT advanced here. Advancing it right
            // after a device read (and before the punch is durably persisted) is exactly the
            // data-loss bug being fixed: if the app crashes between this read and the punch
            // reaching durable storage, the checkpoint would already have moved past it and
            // the punch would never be read from the device again. SyncService now advances
            // the checkpoint only after these records have been durably queued.
            SBXPCNative.EnableDevice(_machineNumber, 1);
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

    public bool CreateEmployee(string enrollNumber, string employeeName, string? fallbackNumericId = null)
    {
        if (!_connected && !Connect()) return false;

        if (!int.TryParse(enrollNumber, out int idNum))
        {
            if (string.IsNullOrEmpty(fallbackNumericId) || !int.TryParse(fallbackNumericId, out idNum))
            {
                Logger.Log($"[SBXPC] CreateEmployee: '{enrollNumber}' is not numeric and no valid fallback id, skipped.");
                return false;
            }
            Logger.Log($"[SBXPC] CreateEmployee: '{enrollNumber}' is not numeric, using fallback id '{fallbackNumericId}' instead.");
        }

        bool created = SBXPCNative.SetEnrollData(_machineNumber, idNum);
        if (created) SBXPCNative.SetUserName1(_machineNumber, idNum, employeeName);

        Logger.Log($"[SBXPC] CreateEmployee EnrollNumber={idNum} Name={employeeName} -> {created}");
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