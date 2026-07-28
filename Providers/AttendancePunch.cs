namespace SapeagleAttendanceConnector;

public class AttendancePunch
{
    public string EnrollNumber { get; set; } = "";
    public int VerifyMode { get; set; }
	public int InOutMode { get; set; }
	public DateTime Timestamp { get; set; }
}