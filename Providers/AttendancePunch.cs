namespace SapeagleAttendanceConnector;

public class AttendancePunch
{
    public string EnrollNumber { get; set; } = "";
    public int VerifyMode { get; set; }
    public int InOutMode { get; set; }
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Deterministic identity for this exact physical punch (MachineId/DeviceKey +
    /// EnrollNumber + full timestamp incl. seconds + VerifyMode + InOutMode). Computed by
    /// the provider via <see cref="Services.AttendanceIdentity.ComputeEventId"/> so the same
    /// physical punch always yields the same EventId, even across a connector restart/replay.
    /// Used as the durable dedup key instead of a fixed 60-second time window, which could
    /// discard a legitimate punch that happens to fall within 60s of a prior one.
    /// </summary>
    public string EventId { get; set; } = "";
}