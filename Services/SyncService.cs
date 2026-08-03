using SapeagleAttendanceConnector.Models;

namespace SapeagleAttendanceConnector.Services;

public class SyncService
{
    private readonly ApiService _apiService;
    private readonly QueueService _queueService;
    private readonly CheckpointService _checkpointService;
    private readonly Dictionary<int, IAttendanceProvider> _deviceProviders = new();

    private readonly Dictionary<string, DateTime> _lastProcessed = new();
    private static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(60);


    private readonly Dictionary<(string EnrollNumber, DateTime Date), bool> _backlogSessionOpen = new();

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
        await FlushQueueAsync(ct);

        if (machineIds.Count == 0)
        {
            StatusChanged?.Invoke("No machines activated.");
            LastSyncTime = DateTime.Now;
            return;
        }

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

            await SyncMachineAsync(machine, ct);
        }

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

        int sent = 0, queued = 0, skipped = 0;


        foreach (var p in punches)
        {
            if (_lastProcessed.TryGetValue(p.EnrollNumber, out var last) &&
                p.Timestamp >= last && p.Timestamp - last < DedupWindow)
            {
                Logger.Log($"[Sync] Punch EnrollNumber={p.EnrollNumber} Time={p.Timestamp:HH:mm:ss} " +
                           $"skipped - only {(p.Timestamp - last).TotalSeconds:F0}s since last processed punch " +
                           $"({last:HH:mm:ss}), treated as duplicate/backlog-replay.");
                skipped++;
                continue;
            }

            var log = new AttendanceLog
            {
                ComId = machine.ComId,
                DeviceLabel = deviceLabel,
                EnrollNumber = p.EnrollNumber,
                VerifyMode = p.VerifyMode,
                InOutMode = p.InOutMode,
                Timestamp = p.Timestamp
            };

            bool ok;
            if (p.Timestamp.Date == DateTime.Today)
            {
                ok = await _apiService.SendPunchAsync(log, ct);
                Logger.Log($"[Sync] Punch EnrollNumber={p.EnrollNumber} Time={p.Timestamp:HH:mm:ss} InOutMode={p.InOutMode} (live) -> SendPunchAsync result={ok}");
            }
            else
            {
                var sessionKey = (p.EnrollNumber, p.Timestamp.Date);
                bool sessionOpen = _backlogSessionOpen.TryGetValue(sessionKey, out var open) && open;
                log.AttendanceType = sessionOpen ? "PunchOut" : "PunchIn";

                ok = await _apiService.SendManualAttendanceAsync(log, ct);
                Logger.Log($"[Sync] Punch EnrollNumber={p.EnrollNumber} Date={p.Timestamp:yyyy-MM-dd} Time={p.Timestamp:HH:mm:ss} " +
                           $"(backlog, AttendanceType={log.AttendanceType}) -> SendManualAttendanceAsync result={ok}");

                if (ok) _backlogSessionOpen[sessionKey] = !sessionOpen;
            }

            if (ok)
            {
                sent++;
                if (p.Timestamp.Date == DateTime.Today) RegisterTodayPunch();
            }
            else { queued++; _queueService.Enqueue(log); }

            _lastProcessed[p.EnrollNumber] = p.Timestamp;
        }

        Logger.Log($"[Sync] {deviceLabel}: {sent} sent, {queued} queued, {skipped} skipped as duplicate.");
        StatusChanged?.Invoke($"{deviceLabel}: {sent} sent, {queued} queued, {skipped} skipped as duplicate.");
    }

    private async Task FlushQueueAsync(CancellationToken ct)
    {
        var pending = _queueService.LoadAll();
        if (pending.Count == 0) return;

        StatusChanged?.Invoke($"Syncing {pending.Count} pending record(s)...");

        foreach (var log in pending)
        {
            if (ct.IsCancellationRequested) break;

            var ok = string.IsNullOrEmpty(log.AttendanceType)
                ? await _apiService.SendPunchAsync(log, ct)
                : await _apiService.SendManualAttendanceAsync(log, ct);

            if (ok) _queueService.Remove(log.Id);
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