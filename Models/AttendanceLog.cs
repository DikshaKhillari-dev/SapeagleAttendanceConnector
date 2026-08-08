namespace SapeagleAttendanceConnector.Models;

public class AttendanceLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ComId { get; set; }
    public int MachineId { get; set; }
    public string DeviceLabel { get; set; } = "";
    public string EnrollNumber { get; set; } = "";
    public int VerifyMode { get; set; }
    public int InOutMode { get; set; }
    public DateTime Timestamp { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string? AttendanceType { get; set; }
}