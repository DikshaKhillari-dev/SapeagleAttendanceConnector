namespace SapeagleAttendanceConnector;

public interface IAttendanceProvider : IDisposable
{
    bool Connect();
    void Disconnect();
    List<AttendancePunch> FetchNewAttendanceRecords();

    List<Models.MachineEmployee> ReadExistingEmployees();

    bool CreateEmployee(string enrollNumber, string employeeName);

    bool DeleteEmployee(string enrollNumber);
}