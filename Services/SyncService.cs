using SapeagleAttendanceConnector.Models;

namespace SapeagleAttendanceConnector.Services;

public class SyncService
{
    private readonly ApiService _apiService;
    private readonly QueueService _queueService;
    private readonly CheckpointService _checkpointService;
    private readonly Dictionary<int, IAttendanceProvider> _deviceProviders = new();

    // Keyed on (DeviceKey, EnrollNumber, Date) so the same EnrollNumber on two different
    // machines can never affect each other's PunchIn/PunchOut toggle state — see "MACHINE
    // ISOLATION". DeviceKey already carries the machine's stable identity (MachineConfig.Id).
    private readonly Dictionary<(string DeviceKey, string EnrollNumber, DateTime Date), bool> _backlogSessionOpen = new();

    /// <summary>
    /// Maximum number of previously-queued (older) backlog records processed per sync cycle,
    /// after this cycle's freshly read live punches have already been sent. Keeps a large
    /// backlog from ever delaying a brand-new punch — see "LIVE PUNCH MUST HAVE PRIORITY".
    /// Configurable; default chosen to keep a single cycle fast even with a large backlog.
    /// </summary>
    public int MaxBacklogBatchPerCycle { get; set; } = 50;

    /// <summary>Maximum retry attempts for a transient failure before a record is moved to
    /// Failed/DeadLetter. Configurable — see "QUEUE RETRY — NO INFINITE RETRY".</summary>
    public int MaxRetryCount { get; set; } = QueueService.DefaultMaxRetryCount;

    // Guards against auto-sync (timer tick) and manual actions (Map Users / Employee Sync)
    // touching the same physical device connection concurrently. The SBXPC OCX is only
    // documented as thread-safe across *different* machine numbers, not for two threads
    // hitting the same machine number at once (see SBXPC manual, section 6). Concurrent
    // access there is what was causing the sporadic "connect failed" and app crashes when
    // Map Users / Sync Employees was clicked while an auto-sync cycle was still in flight.
    public readonly SemaphoreSlim DeviceLock = new(1, 1);

    public event Action<string>? StatusChanged;
    public DateTime? LastSyncTime { get; private set; }
    public int PunchesToday { get; private set; }
    private DateTime _punchesTodayDate = DateTime.Today;

    public SyncService(ApiService apiService, QueueService queueService, CheckpointService checkpointService)
    {
        _apiService = apiService;
        _queueService = queueService;
        _checkpointService = checkpointService;
    }

    public async Task RunCycleAsync(List<int> machineIds, CancellationToken ct = default)
    {
        Logger.Log($"[Sync] RunCycleAsync started for {machineIds.Count} machine(s). MachineIds=[{string.Join(", ", machineIds)}]");

        if (machineIds.Count == 0)
        {
            StatusChanged?.Invoke("No machines activated.");
            LastSyncTime = DateTime.Now;
            return;
        }

        // Read new device punches FIRST and get them sent, before touching any older backlog —
        // a large backlog must never block a live punch. See "LIVE PUNCH MUST HAVE PRIORITY".
        foreach (var machineId in machineIds)
        {
            if (ct.IsCancellationRequested) break;

            var machine = await _apiService.GetMachineAsync(machineId, ct);
            if (machine == null || !machine.IsActive)
            {
                Logger.Log($"[Sync] MachineId={machineId}: not found or inactive, skipped. (machine==null: {machine == null}, IsActive: {machine?.IsActive})");
                StatusChanged?.Invoke($"MachineId={machineId}: not found or inactive.");
                continue;
            }

            Logger.Log($"[Sync] MachineId={machineId}: fetched '{machine.MachineName}' DeviceId(raw)='{machine.DeviceId}' " +
                       $"MachineType='{machine.MachineType}' Ip={machine.IpAddress}:{machine.Port} IsActive={machine.IsActive}");

            await DeviceLock.WaitAsync(ct);
            try { await SyncMachineAsync(machine, ct); }
            finally { DeviceLock.Release(); }
        }

        // Only now process a bounded batch of older, previously-queued backlog records (from
        // this or earlier cycles) that are due for a retry.
        await ProcessBacklogBatchAsync(ct);

        LastSyncTime = DateTime.Now;
        Logger.Log("[Sync] RunCycleAsync completed.");
    }

    private async Task SyncMachineAsync(MachineConfig machine, CancellationToken ct)
    {
        if (!int.TryParse(machine.DeviceId, out int machineNumber))
        {
            Logger.Log($"[Sync] '{machine.MachineName}' has invalid Device ID '{machine.DeviceId}', skipped.");
            StatusChanged?.Invoke($"'{machine.MachineName}' has invalid Device ID, skipped.");
            return;
        }

        Logger.Log($"[Sync] '{machine.MachineName}' DeviceId(raw)='{machine.DeviceId}' parsed -> machineNumber={machineNumber}");

        IAttendanceProvider provider;
        try { provider = GetOrCreateProvider(machine, machineNumber); }
        catch (NotSupportedException ex)
        {
            Logger.Log($"[Sync] MachineFactory.Create failed for '{machine.MachineName}': {ex.Message}");
            StatusChanged?.Invoke(ex.Message);
            return;
        }

        string deviceLabel = $"{machine.MachineName} ({machine.IpAddress})";

        // Safety net: if this connector install has no local checkpoint for this device yet
        // (fresh install, checkpoints.json lost/corrupted, machine re-activated, etc.), do NOT
        // let the provider fall back to "sync everything since the beginning of time". Ask the
        // ERP database what the last record actually saved for this machine was, and seed the
        // local checkpoint from that first. UpdateLastSynced only ever moves forward, so this is
        // always safe to call — it's a no-op if a real checkpoint already exists locally.
        if (_checkpointService.GetLastSynced(provider.DeviceKey) == DateTime.MinValue)
        {
            var dbLastSynced = await _apiService.GetLastSyncedTimestampAsync(machine.ComId, machine.Id, ct);
            if (dbLastSynced.HasValue)
            {
                _checkpointService.UpdateLastSynced(provider.DeviceKey, dbLastSynced.Value);
                Logger.Log($"[Sync] {deviceLabel}: no local checkpoint found — seeded from ERP DB's last synced record " +
                           $"({dbLastSynced.Value:yyyy-MM-dd HH:mm:ss}) instead of replaying full device backlog.");
            }
            else
            {
                Logger.Log($"[Sync] {deviceLabel}: no local checkpoint and no prior ERP record found for this machine — " +
                           "this looks like a genuinely new device, proceeding with a full first-time sync.");
            }
        }

        List<AttendancePunch> punches;

        try
        {
            punches = await Task.Run(() => provider.FetchNewAttendanceRecords(), ct);
        }
        catch (Exception ex)
        {
            Logger.Log($"[Sync] {deviceLabel}: device unreachable - {ex.Message}");
            StatusChanged?.Invoke($"{deviceLabel}: device unreachable ({ex.Message})");
            return;
        }

        Logger.Log($"[Sync] {deviceLabel}: FetchNewAttendanceRecords returned {punches.Count} punch(es).");

        if (punches.Count == 0)
        {
            StatusChanged?.Invoke($"{deviceLabel}: 0 new punches found.");
            return;
        }

        // ---- DURABLY PERSIST every new punch BEFORE the checkpoint is allowed to advance ----
        // This is the fix for the most critical bug: previously the checkpoint advanced right
        // after the device read, so a crash between read and send permanently lost the punch.
        // Now the checkpoint only ever advances up to the last punch that has actually reached
        // durable storage (queue.json), regardless of whether it has been sent yet.
        var newlyPersisted = new List<AttendanceLog>();
        DateTime? maxDurablyStoredTimestamp = null;

        foreach (var p in punches) // punches are already ordered ascending by Timestamp
        {
            var log = new AttendanceLog
            {
                EventId = p.EventId,
                DeviceKey = provider.DeviceKey,
                ComId = machine.ComId,
                MachineId = machine.Id,
                DeviceLabel = deviceLabel,
                EnrollNumber = p.EnrollNumber,
                VerifyMode = p.VerifyMode,
                InOutMode = p.InOutMode,
                Timestamp = p.Timestamp
            };

            if (p.Timestamp.Date != DateTime.Today)
            {
                var sessionKey = (provider.DeviceKey, p.EnrollNumber, p.Timestamp.Date);

                bool sessionOpen =
                    _backlogSessionOpen.TryGetValue(sessionKey, out var open) && open;

                // Trust the device's own attendanceStatus tag (InOutMode: 0=checkIn, 1=checkOut)
                // when it's known — same as the live path. Only fall back to guessing from
                // alternating order when the device itself sent an ambiguous/untagged event
                // (InOutMode==2), so a genuinely-tagged checkOut never gets mislabeled as a
                // PunchIn just because of in-memory toggle drift (e.g. after a restart).
                log.AttendanceType = p.InOutMode switch
                {
                    0 => "PunchIn",
                    1 => "PunchOut",
                    _ => sessionOpen ? "PunchOut" : "PunchIn"
                };

                // IMPORTANT:
                // Update the state immediately after classifying this punch.
                // The next punch of the same employee/date must see the
                // updated IN/OUT state. Previously this update was deferred until
                // after the API call succeeded, which meant two ambiguous (InOutMode=2)
                // punches in the same batch could both read sessionOpen=false and both
                // get classified as PunchIn.
                _backlogSessionOpen[sessionKey] =
                    log.AttendanceType == "PunchIn";
            }

            bool stored = _queueService.TryPersist(log, out bool wasNew);

            if (!stored)
            {
                // Durable write itself failed (e.g. disk I/O). Stop here: do NOT advance the
                // checkpoint past this point, and do NOT attempt to send this or any later
                // punch in this batch — they will all be re-read from the device and retried
                // next cycle, since the checkpoint hasn't moved past them.
                Logger.Log($"[Sync] {deviceLabel}: failed to durably persist EventId={log.EventId} EnrollNumber={log.EnrollNumber} " +
                           $"Time={log.Timestamp:yyyy-MM-dd HH:mm:ss} — stopping this cycle's batch here; checkpoint will not advance past it.");
                break;
            }

            maxDurablyStoredTimestamp = log.Timestamp;

            if (wasNew)
            {
                newlyPersisted.Add(log);
            }
            else
            {
                Logger.Log($"[Sync] EventId={log.EventId} EnrollNumber={log.EnrollNumber} Time={log.Timestamp:HH:mm:ss} " +
                           "already durably stored from a previous cycle (re-read before checkpoint advanced) — " +
                           "left for the backlog processor instead of being resent immediately.");
            }
        }

        if (maxDurablyStoredTimestamp.HasValue)
            _checkpointService.UpdateLastSynced(provider.DeviceKey, maxDurablyStoredTimestamp.Value);

        // ---- Now process/send this cycle's freshly-persisted (live/current) punches ----
        int sent = 0, queued = 0;

        foreach (var log in newlyPersisted)
        {
            var outcome = log.Timestamp.Date == DateTime.Today
                ? await _apiService.SendPunchAsync(log, ct)
                : await _apiService.SendManualAttendanceAsync(log, ct);

            Logger.Log($"[Sync] Punch EnrollNumber={log.EnrollNumber} Time={log.Timestamp:yyyy-MM-dd HH:mm:ss} " +
                       $"InOutMode={log.InOutMode} AttendanceType={log.AttendanceType ?? "(live)"} -> Success={outcome.Success}" +
                       (outcome.Success ? "" : $" IsTransient={outcome.IsTransient} Error={outcome.ErrorMessage}"));

            if (outcome.Success)
            {
                sent++;
                _queueService.MarkSent(log.Id);
                if (log.Timestamp.Date == DateTime.Today) RegisterTodayPunch();

                // NOTE: _backlogSessionOpen is now updated at classification time only
                // (see above, right after log.AttendanceType is set). Do NOT update it
                // here again — waiting for the API response before updating the toggle
                // state was the root cause of two ambiguous same-batch punches both
                // being classified as PunchIn.
            }
            else
            {
                queued++;
                _queueService.RecordFailure(log.Id, outcome.IsTransient, outcome.ErrorMessage, MaxRetryCount);
            }
        }

        Logger.Log($"[Sync] {deviceLabel}: {sent} sent, {queued} queued for retry (of {newlyPersisted.Count} new punch(es); " +
                   $"{punches.Count - newlyPersisted.Count} were already durably stored from a previous cycle).");
        StatusChanged?.Invoke($"{deviceLabel}: {sent} sent, {queued} queued.");
    }

    /// <summary>
    /// Processes a bounded batch of previously-queued backlog records that are due for a retry
    /// (NextRetryAt null or in the past), oldest punch first, across all machines. Deliberately
    /// runs only after every machine's live punches have already been read and sent this cycle.
    /// </summary>
    private async Task ProcessBacklogBatchAsync(CancellationToken ct)
    {
        var batch = _queueService.LoadDueBacklog(MaxBacklogBatchPerCycle);
        if (batch.Count == 0) return;

        int totalPending = _queueService.CountPending();
        Logger.Log($"[Sync] ProcessBacklogBatchAsync: processing {batch.Count} of {totalPending} pending backlog record(s) due for retry.");
        StatusChanged?.Invoke($"Processing {batch.Count} pending backlog record(s)...");

        foreach (var log in batch)
        {
            if (ct.IsCancellationRequested) break;

            var outcome = string.IsNullOrEmpty(log.AttendanceType)
                ? await _apiService.SendPunchAsync(log, ct)
                : await _apiService.SendManualAttendanceAsync(log, ct);

            Logger.Log($"[Sync] Backlog EventId={log.EventId} EnrollNumber={log.EnrollNumber} Time={log.Timestamp:yyyy-MM-dd HH:mm:ss} -> Success={outcome.Success}" +
                       (outcome.Success ? "" : $" IsTransient={outcome.IsTransient} Error={outcome.ErrorMessage}"));

            if (outcome.Success)
            {
                _queueService.MarkSent(log.Id);
                if (log.Timestamp.Date == DateTime.Today) RegisterTodayPunch();

                // NOTE: _backlogSessionOpen is only ever updated at classification time
                // (in SyncMachineAsync). This retry loop must NOT touch it — these records
                // were already classified when first read, and re-touching the toggle here
                // on a later retry would corrupt the state for punches read in between.
            }
            else
            {
                _queueService.RecordFailure(log.Id, outcome.IsTransient, outcome.ErrorMessage, MaxRetryCount);
            }
        }
    }

    private IAttendanceProvider GetOrCreateProvider(MachineConfig machine, int machineNumber)
    {
        if (_deviceProviders.TryGetValue(machine.Id, out var existing))
        {
            Logger.Log($"[Sync] GetOrCreateProvider: reusing cached provider for MachineConfig.Id={machine.Id} (machineNumber={machineNumber}).");
            return existing;
        }
        var provider = MachineFactory.Create(machine, machineNumber, _checkpointService);
        _deviceProviders[machine.Id] = provider;
        Logger.Log($"[Sync] GetOrCreateProvider: cached new provider for MachineConfig.Id={machine.Id} (machineNumber={machineNumber}).");
        return provider;
    }

    private void RegisterTodayPunch()
    {
        if (_punchesTodayDate != DateTime.Today)
        {
            _punchesTodayDate = DateTime.Today;
            PunchesToday = 0;
        }
        PunchesToday++;
    }

    public void DisconnectAll()
    {
        foreach (var p in _deviceProviders.Values) p.Disconnect();
    }

    public void DisconnectMachine(int machineConfigId)
    {
        if (_deviceProviders.TryGetValue(machineConfigId, out var provider))
        {
            Logger.Log($"[Sync] DisconnectMachine: releasing connection for MachineConfig.Id={machineConfigId} " +
                       "so another process can connect (e.g. Employee Sync).");
            provider.Disconnect();
        }
    }
}