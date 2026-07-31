namespace SapeagleAttendanceConnector.Models;

public class EmployeeMappingEntry
{
    public int ComId { get; set; }
    public string MachineType { get; set; } = "ZKTeco";
    public string EnrollNumber { get; set; } = "";
    public long EmpId { get; set; }
    public string? EmpName { get; set; }   
    public long CreatedBy { get; set; }
}