using System.Security.Cryptography;
using System.Text;

namespace SapeagleAttendanceConnector.Services;

/// <summary>
/// Computes a deterministic EventId used to identify a specific physical punch, so that
/// re-reading the same device log (e.g. after a crash/restart, before the checkpoint has
/// advanced) always produces the same identity for the same punch and can be safely
/// deduplicated — instead of relying on a fixed 60-second time window, which would discard
/// a legitimate second punch that happens to land within 60 seconds of a prior one.
///
/// Identity = DeviceKey + EnrollNumber + full timestamp (including seconds) + VerifyMode +
/// InOutMode. This matches "MACHINE ISOLATION" (section 5 of the implementation spec): the
/// DeviceKey component means the same EnrollNumber on two different machines never collides.
/// </summary>
public static class AttendanceIdentity
{
	public static string ComputeEventId(string deviceKey, string enrollNumber, DateTime timestamp, int verifyMode, int inOutMode)
	{
		var raw = $"{deviceKey}|{enrollNumber}|{timestamp:yyyy-MM-ddTHH:mm:ss}|{verifyMode}|{inOutMode}";
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
		// 24 hex chars (96 bits) is more than enough to avoid collisions for this use case
		// while keeping the id short and readable in logs.
		return Convert.ToHexString(hash)[..24];
	}
}