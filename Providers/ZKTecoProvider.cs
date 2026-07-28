using SapeagleAttendanceConnector.Services;

namespace SapeagleAttendanceConnector;

public class ZKTecoProvider : IAttendanceProvider
{
    private readonly string _deviceIp;
    private readonly int _port;
    private readonly int _machineNumber;
    private readonly int _commPassword;
    private readonly string _deviceKey;
    private readonly CheckpointService _checkpoint;

    private dynamic? _device;
    private bool _isConnected;

    public ZKTecoProvider(string deviceIp, int port, int machineNumber, int commPassword, CheckpointService checkpoint)
    {
        _deviceIp = deviceIp;
        _port = port;
        _machineNumber = machineNumber;
        _commPassword = commPassword;
        _checkpoint = checkpoint;
        _deviceKey = $"ZKTeco:{deviceIp}:{machineNumber}";
    }

    public bool Connect()
    {
        if (_isConnected) return true;

        try
        {
            _device ??= CreateSdkInstance();

            if (_commPassword != 0)
            {
                try { _device.SetCommPassword(_commPassword); }
                catch (Exception ex) { Logger.Log($"[ZKTeco] SetCommPassword warning: {ex.Message}"); }
            }

            _isConnected = _device.Connect_Net(_deviceIp, _port);

            if (!_isConnected)
            {
                int errorCode = 0;
                _device.GetLastError(ref errorCode);
                Logger.Log($"[ZKTeco] Connect failed {_deviceIp}:{_port} ErrorCode={errorCode}");
                return false;
            }

            Logger.Log($"[ZKTeco] Connected {_deviceIp}:{_port} (MachineNumber={_machineNumber})");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"[ZKTeco] Exception: {ex.Message}");
            _isConnected = false;
            return false;
        }
    }

    public void Disconnect()
    {
        if (!_isConnected || _device == null) return;
        try
        {
            _device.EnableDevice(_machineNumber, false);
            _device.Disconnect();
        }
        catch (Exception ex) { Logger.Log($"[ZKTeco] Disconnect error: {ex.Message}"); }
        finally { _isConnected = false; }
    }

    public List<AttendancePunch> FetchNewAttendanceRecords()
    {
        var records = new List<AttendancePunch>();
        if (!_isConnected && !Connect()) return records;

        // Device's own pointer (ReadNewGLogData) is not trustworthy on its own — it resets
        // to "everything is new" if the device restarts, firmware resets, or the connector
        // is (re)activated. Our own checkpoint is the source of truth for what's already
        // been synced, independent of the device's internal state.
        var lastSynced = _checkpoint.GetLastSynced(_deviceKey);

        try
        {
            bool enabled = _device!.EnableDevice(_machineNumber, true);
            if (!enabled)
            {
                Logger.Log($"[ZKTeco] EnableDevice failed for MachineNumber={_machineNumber}");
                return records;
            }

            bool readOk = _device.ReadNewGLogData(_machineNumber);
            if (!readOk) readOk = _device.ReadGeneralLogData(_machineNumber);
            if (!readOk)
            {
                Logger.Log($"[ZKTeco] ReadGeneralLogData returned false for MachineNumber={_machineNumber}");
                return records;
            }

            var allRecords = new List<AttendancePunch>();
            while (_device.SSR_GetGeneralLogData(
                       _machineNumber,
                       out string enrollNumber, out int verifyMode, out int inOutMode,
                       out int year, out int month, out int day,
                       out int hour, out int minute, out int second, out int workCode))
            {
                allRecords.Add(new AttendancePunch
                {
                    EnrollNumber = enrollNumber,
                    VerifyMode = verifyMode,
                    InOutMode = inOutMode,
                    Timestamp = new DateTime(year, month, day, hour, minute, second)
                });
            }

            // Filter against our own checkpoint (not the device pointer) and sort
            // chronologically. The sort matters: SyncService sends punches to the ERP's
            // check-in/check-out toggle API in list order, so out-of-order punches for the
            // same employee (e.g. a check-out arriving before its check-in) would corrupt
            // that toggle state — this guarantees ascending time order regardless of the
            // order the device happened to return them in.
            records = allRecords.Where(r => r.Timestamp > lastSynced)
                                 .OrderBy(r => r.Timestamp)
                                 .ToList();

            Logger.Log($"[ZKTeco] Device has {allRecords.Count} total record(s), " +
                       $"{records.Count} new since checkpoint {lastSynced:yyyy-MM-dd HH:mm:ss}.");

            if (records.Count > 0)
                _checkpoint.UpdateLastSynced(_deviceKey, records.Max(r => r.Timestamp));
        }
        catch (Exception ex)
        {
            Logger.Log($"[ZKTeco] Fetch error: {ex.Message}");
            _isConnected = false;
        }

        Logger.Log($"[ZKTeco] FetchNewAttendanceRecords found {records.Count} record(s): " +
                   string.Join(", ", records.Select(r => $"{r.EnrollNumber}@{r.Timestamp:HH:mm:ss}")));

        return records;
    }

    public List<Models.MachineEmployee> ReadExistingEmployees()
    {
        var employees = new List<Models.MachineEmployee>();
        if (!_isConnected && !Connect()) return employees;

        try
        {
            _device!.EnableDevice(_machineNumber, false);
            _device.ReadAllUserID(_machineNumber);

            while (_device.SSR_GetAllUserInfo(
                       _machineNumber,
                       out string enrollNumber, out string name, out string password,
                       out int privilege, out bool enabled))
            {
                employees.Add(new Models.MachineEmployee { EnrollNumber = enrollNumber, Name = name });
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[ZKTeco] ReadExistingEmployees error: {ex.Message}");
        }
        finally
        {
            _device!.EnableDevice(_machineNumber, true);
        }

        Logger.Log($"[ZKTeco] ReadExistingEmployees found {employees.Count} employee(s) on machine.");
        return employees;
    }

    public bool CreateEmployee(string enrollNumber, string employeeName)
    {
        if (!_isConnected && !Connect()) return false;

        try
        {
            _device!.EnableDevice(_machineNumber, false);
            bool created = _device.SSR_SetUserInfo(_machineNumber, enrollNumber, employeeName, "", 0, true);
            Logger.Log($"[ZKTeco] CreateEmployee EnrollNumber={enrollNumber} Name={employeeName} -> {created}");
            return created;
        }
        catch (Exception ex)
        {
            Logger.Log($"[ZKTeco] CreateEmployee error: {ex.Message}");
            return false;
        }
        finally { _device!.EnableDevice(_machineNumber, true); }
    }

    public bool DeleteEmployee(string enrollNumber)
    {
        if (!_isConnected && !Connect()) return false;

        try
        {
            _device!.EnableDevice(_machineNumber, false);

            // Finger index 12 tells the ZKTeco SDK to delete the whole user record
            // (not just a fingerprint template). Deleting only indices 0-9 leaves the
            // user account on the device if they were enrolled via card/password
            // instead of fingerprint, which is why this previously always returned false.
            bool deleted = _device.SSR_DeleteEnrollData(_machineNumber, enrollNumber, 12);

            Logger.Log($"[ZKTeco] DeleteEmployee EnrollNumber={enrollNumber} (whole-user delete) -> {deleted}");
            return deleted;
        }
        catch (Exception ex)
        {
            Logger.Log($"[ZKTeco] DeleteEmployee error: {ex.Message}");
            return false;
        }
        finally { _device!.EnableDevice(_machineNumber, true); }
    }

    private static dynamic CreateSdkInstance()
    {
        var type = Type.GetTypeFromProgID("zkemkeeper.ZKEM")
                   ?? throw new InvalidOperationException("zkemkeeper COM component not registered.");
        return Activator.CreateInstance(type)!;
    }

    public void Dispose() => Disconnect();
}