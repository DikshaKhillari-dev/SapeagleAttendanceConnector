namespace SapeagleAttendanceConnector;

public interface IAttendanceProvider : IDisposable
{
    /// <summary>
    /// Stable checkpoint key for this device. MUST be based on the machine's ERP-side
    /// primary key (MachineConfig.Id), NOT its IP address — IP can change (DHCP,
    /// reactivation, moved machine) and if the key changes, the checkpoint silently
    /// resets to "never synced", causing the device's entire backlog to be replayed
    /// as duplicates.
    /// </summary>
    string DeviceKey { get; }

    bool Connect();
    void Disconnect();
    List<AttendancePunch> FetchNewAttendanceRecords();

    List<Models.MachineEmployee> ReadExistingEmployees();

    bool CreateEmployee(string enrollNumber, string employeeName, string? fallbackNumericId = null);

    bool DeleteEmployee(string enrollNumber);
}