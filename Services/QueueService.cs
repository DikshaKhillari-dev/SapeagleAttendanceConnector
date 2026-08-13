using System.Text.Json;
using SapeagleAttendanceConnector.Models;
using System.Text.Json.Serialization;

namespace SapeagleAttendanceConnector.Services;

/// <summary>
/// The durable store for attendance events. This is the "durable queue" referenced throughout
/// the checkpoint/crash-safety design: every punch read from a device is persisted here
/// (Status=Pending) BEFORE it is sent to the ERP and BEFORE the device checkpoint is allowed
/// to advance past it. If the app crashes at any point after a punch has been persisted here,
/// the punch survives restart and will be retried — the checkpoint alone is never the only
/// record of a punch's existence.
///
/// Persistence is idempotent by EventId: re-persisting the same physical punch (e.g. because
/// the device was re-read before the checkpoint advanced) is a no-op, so this also serves as
/// the durable dedup mechanism that replaces the old 60-second in-memory time window.
/// </summary>
public class QueueService
{
    private readonly string _queuePath;
    private readonly object _lock = new();

    public const int DefaultMaxRetryCount = 5;

    public QueueService()
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SapeagleAttendanceConnector", "Data");
        Directory.CreateDirectory(dataDir);
        _queuePath = Path.Combine(dataDir, "queue.json");
    }

    /// <summary>
    /// Durably persists an event as Pending. Idempotent by EventId: if an entry for this exact
    /// physical punch already exists (in any status), this is a no-op and the existing entry
    /// is left untouched — it does not reset an already-Sent/Failed record back to Pending.
    /// Returns true if the event is now guaranteed to be durably stored (whether it was
    /// newly added or already existed), false only if the write itself failed.
    /// </summary>
    public bool TryPersist(AttendanceLog log) => TryPersist(log, out _);

    /// <summary>
    /// Same as <see cref="TryPersist(AttendanceLog)"/>, but also reports whether the event was
    /// newly added (<paramref name="wasNew"/> = true) or already existed from a prior cycle
    /// (false). SyncService uses this to only attempt an immediate "live" send for punches that
    /// are genuinely new this cycle — an event that already existed (e.g. because the device
    /// was re-read after a crash, before the checkpoint advanced) is left for the backlog
    /// processor, which respects its existing retry backoff instead of bypassing it.
    /// </summary>
    public bool TryPersist(AttendanceLog log, out bool wasNew)
    {
        lock (_lock)
        {
            try
            {
                var list = LoadInternal();

                if (!string.IsNullOrEmpty(log.EventId) &&
                    list.Any(x => x.EventId == log.EventId))
                {
                    wasNew = false;
                    return true; // already durably stored — nothing to do.
                }

                list.Add(log);
                SaveInternal(list);
                wasNew = true;
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"[Queue] TryPersist: failed to durably store EventId={log.EventId} EnrollNumber={log.EnrollNumber} Timestamp={log.Timestamp:yyyy-MM-dd HH:mm:ss} - {ex.Message}");
                wasNew = false;
                return false;
            }
        }
    }

    public void Enqueue(AttendanceLog log) => TryPersist(log);

    public List<AttendanceLog> LoadAll() { lock (_lock) return LoadInternal(); }

    /// <summary>
    /// Loads Pending records that are due for (re)processing right now — i.e. NextRetryAt is
    /// null or already in the past — ordered oldest-punch-first, capped to maxCount so a large
    /// backlog can never be processed all in one cycle (see "LIVE PUNCH MUST HAVE PRIORITY").
    /// </summary>
    public List<AttendanceLog> LoadDueBacklog(int maxCount)
    {
        lock (_lock)
        {
            var now = DateTime.Now;
            return LoadInternal()
                .Where(x => x.Status == AttendanceLogStatus.Pending && (x.NextRetryAt == null || x.NextRetryAt <= now))
                .OrderBy(x => x.Timestamp)
                .Take(maxCount)
                .ToList();
        }
    }

    public int CountPending()
    {
        lock (_lock) return LoadInternal().Count(x => x.Status == AttendanceLogStatus.Pending);
    }

    /// <summary>Marks a record as successfully delivered and removes it from the queue.</summary>
    public void MarkSent(Guid id)
    {
        lock (_lock)
        {
            var list = LoadInternal();
            list.RemoveAll(x => x.Id == id);
            SaveInternal(list);
        }
    }

    /// <summary>
    /// Records a failed delivery attempt. Transient failures get RetryCount incremented and a
    /// backoff (NextRetryAt) applied, up to MaxRetryCount, after which the record moves to
    /// Failed/DeadLetter. Permanent (non-retryable) failures move straight to Failed/DeadLetter
    /// without waiting for MaxRetryCount, since retrying them can never succeed.
    /// </summary>
    public void RecordFailure(Guid id, bool isTransient, string? errorMessage, int maxRetryCount = DefaultMaxRetryCount)
    {
        lock (_lock)
        {
            var list = LoadInternal();
            var entry = list.FirstOrDefault(x => x.Id == id);
            if (entry == null) return;

            entry.LastError = errorMessage;

            if (!isTransient)
            {
                entry.Status = AttendanceLogStatus.Failed;
                Logger.Log($"[Queue] EventId={entry.EventId} EnrollNumber={entry.EnrollNumber} -> permanent failure, moved to Failed/DeadLetter. Reason: {errorMessage}");
                SaveInternal(list);
                return;
            }

            entry.RetryCount++;

            if (entry.RetryCount >= maxRetryCount)
            {
                entry.Status = AttendanceLogStatus.Failed;
                Logger.Log($"[Queue] EventId={entry.EventId} EnrollNumber={entry.EnrollNumber} -> exceeded MaxRetryCount={maxRetryCount}, moved to Failed/DeadLetter. Last error: {errorMessage}");
            }
            else
            {
                // Exponential backoff: 30s, 60s, 120s, 240s, ... capped at 30 minutes.
                var backoffSeconds = Math.Min(30 * Math.Pow(2, entry.RetryCount - 1), 1800);
                entry.NextRetryAt = DateTime.Now.AddSeconds(backoffSeconds);
                Logger.Log($"[Queue] EventId={entry.EventId} EnrollNumber={entry.EnrollNumber} -> transient failure (attempt {entry.RetryCount}/{maxRetryCount}), " +
                           $"next retry at {entry.NextRetryAt:yyyy-MM-dd HH:mm:ss}. Reason: {errorMessage}");
            }

            SaveInternal(list);
        }
    }

    /// <summary>Legacy-compatible removal by Id (kept for any external callers); equivalent to MarkSent.</summary>
    public void Remove(Guid id) => MarkSent(id);

    private List<AttendanceLog> LoadInternal()
    {
        if (!File.Exists(_queuePath)) return new List<AttendanceLog>();
        try
        {
            var text = ReadWithRetry(_queuePath);
            return JsonSerializer.Deserialize<List<AttendanceLog>>(text, JsonOptions) ?? new();
        }
        catch (Exception ex)
        {
            Logger.Log($"[Queue] LoadInternal: failed after retries - {ex.Message}");
            return new List<AttendanceLog>();
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() } // keeps Status human-readable in queue.json for support/ops
    };

    private void SaveInternal(List<AttendanceLog> list)
    {
        var json = JsonSerializer.Serialize(list, JsonOptions);
        WriteWithRetry(_queuePath, json);
    }

    private static string ReadWithRetry(string path, int maxAttempts = 5)
    {
        Exception? last = null;
        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException ex) { last = ex; Thread.Sleep(150 * (i + 1)); }
            catch (UnauthorizedAccessException ex) { last = ex; Thread.Sleep(150 * (i + 1)); }
        }
        throw last ?? new IOException($"Could not read '{path}'");
    }

    private static void WriteWithRetry(string path, string content, int maxAttempts = 5)
    {
        Exception? last = null;
        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream);
                writer.Write(content);
                return;
            }
            catch (IOException ex) { last = ex; Thread.Sleep(150 * (i + 1)); }
            catch (UnauthorizedAccessException ex) { last = ex; Thread.Sleep(150 * (i + 1)); }
        }
        throw last ?? new IOException($"Could not write '{path}'");
    }
}