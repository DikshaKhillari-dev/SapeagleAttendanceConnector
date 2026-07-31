namespace SapeagleAttendanceConnector;

public interface IAttendanceProvider : IDisposable
{
    bool Connect();
    void Disconnect();
    List<AttendancePunch> FetchNewAttendanceRecords();

    List<Models.MachineEmployee> ReadExistingEmployees();

    bool CreateEmployee(string enrollNumber, string employeeName, string? fallbackNumericId = null);

    bool DeleteEmployee(string enrollNumber);
}