namespace SapeagleAttendanceConnector.Models;

public enum AttendanceLogStatus
{
    /// <summary>Durably stored, waiting to be sent or retried.</summary>
    Pending,
    /// <summary>Successfully delivered to the ERP.</summary>
    Sent,
    /// <summary>Permanent failure (or exhausted MaxRetryCount) — will not be retried further.</summary>
    Failed
}

public class AttendanceLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Deterministic identity of the physical punch this record represents (see
    /// Services.AttendanceIdentity). Used to make durable persistence idempotent: re-reading
    /// the same device record (e.g. after a crash, before the checkpoint advanced) must never
    /// create a second queue entry for the same physical punch.
    /// </summary>
    public string EventId { get; set; } = "";

    /// <summary>Stable per-device key (see IAttendanceProvider.DeviceKey), used together with
    /// EnrollNumber for machine isolation of session/dedup state.</summary>
    public string DeviceKey { get; set; } = "";

    public int ComId { get; set; }
    public int MachineId { get; set; }
    public string DeviceLabel { get; set; } = "";
    public string EnrollNumber { get; set; } = "";
    public int VerifyMode { get; set; }
    public int InOutMode { get; set; }

    /// <summary>The original punch time exactly as read from the device. Never overwritten
    /// by a later retry/processing date — see "DO NOT BREAK HISTORICAL ATTENDANCE DATE".</summary>
    public DateTime Timestamp { get; set; }

    public AttendanceLogStatus Status { get; set; } = AttendanceLogStatus.Pending;

    public int RetryCount { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? SentAt { get; set; }

    public string? AttendanceType { get; set; }
}