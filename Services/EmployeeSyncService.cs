using SapeagleAttendanceConnector.Models;

namespace SapeagleAttendanceConnector.Services;

public class EmployeeSyncPreview
{
    public IAttendanceProvider Provider { get; set; } = null!;
    public string DeviceLabel { get; set; } = "";
    public List<MachineEmployee> MachineEmployees { get; set; } = new();
    public List<ErpEmployee> ErpEmployees { get; set; } = new();
}

public class EmployeeSyncResult
{
    public int MachineEmployeeCountBefore { get; set; }
    public int ErpEmployeeCount { get; set; }
    public int CreatedNew { get; set; }
    public int Skipped { get; set; }
    public int Removed { get; set; }
    public int Failed { get; set; }
    public int MachineEmployeeCountAfter { get; set; }
    public List<string> FailedDetails { get; } = new();
}

public class EmployeeSyncService
{
    private readonly ApiService _apiService;
    private readonly CheckpointService _checkpointService;

    public event Action<string>? StatusChanged;

    public EmployeeSyncService(ApiService apiService, CheckpointService checkpointService)
    {
        _apiService = apiService;
        _checkpointService = checkpointService;
    }
    public async Task<EmployeeSyncPreview?> PrepareAsync(MachineConfig machine, int machineNumber, CancellationToken ct = default)
    {
        string deviceLabel = $"{machine.MachineName} ({machine.IpAddress})";
        Logger.Log($"[EmployeeSync] PrepareAsync: {deviceLabel} DeviceId(raw)='{machine.DeviceId}' machineNumber={machineNumber}");

        StatusChanged?.Invoke($"{deviceLabel}: Connecting...");
        var provider = MachineFactory.Create(machine, machineNumber, _checkpointService);

        if (!provider.Connect())
        {
            Logger.Log($"[EmployeeSync] {deviceLabel}: connect failed.");
            StatusChanged?.Invoke($"{deviceLabel}: connect failed.");
            provider.Dispose();
            return null;
        }

        StatusChanged?.Invoke($"{deviceLabel}: Reading existing employees...");
        var machineEmployees = provider.ReadExistingEmployees();

        StatusChanged?.Invoke($"{deviceLabel}: Fetching employees from ERP...");
        var erpEmployees = await _apiService.GetEmployeesForSyncAsync(machine.ComId, null, ct);

        Logger.Log($"[EmployeeSync] {deviceLabel}: Machine={machineEmployees.Count}, ERP={erpEmployees.Count}");

        return new EmployeeSyncPreview
        {
            Provider = provider,
            DeviceLabel = deviceLabel,
            MachineEmployees = machineEmployees,
            ErpEmployees = erpEmployees
        };
    }


    public EmployeeSyncResult Execute(
        EmployeeSyncPreview preview,
        bool deleteExisting,
        List<ErpEmployee> employeesToSync,
        CancellationToken ct = default)
    {
        var provider = preview.Provider;
        var result = new EmployeeSyncResult
        {
            MachineEmployeeCountBefore = preview.MachineEmployees.Count,
            ErpEmployeeCount = employeesToSync.Count
        };

        var currentIds = new HashSet<string>(preview.MachineEmployees.Select(m => m.EnrollNumber));
        var currentIdsSet = currentIds;

        if (deleteExisting)
        {
            StatusChanged?.Invoke($"{preview.DeviceLabel}: Deleting existing employees...");
            foreach (var machineId in preview.MachineEmployees.Select(m => m.EnrollNumber))
            {
                if (ct.IsCancellationRequested) break;

                if (provider.DeleteEmployee(machineId))
                {
                    result.Removed++;
                    currentIds.Remove(machineId);
                }
                else
                {
                    result.Failed++;
                    result.FailedDetails.Add($"{machineId} - DeleteEmployee failed");
                }
            }
        }
        StatusChanged?.Invoke($"{preview.DeviceLabel}: Creating employees...");
        foreach (var erpEmp in employeesToSync)
        {
            if (ct.IsCancellationRequested) break;

            if (!deleteExisting && (currentIds.Contains(erpEmp.EmployeeCode) || currentIds.Contains(erpEmp.EmployeeId.ToString())))
            {
                result.Skipped++;
                continue;
            }

            bool created = provider.CreateEmployee(erpEmp.EmployeeCode, erpEmp.EmployeeName, erpEmp.EmployeeId.ToString()); if (created)
            {
                result.CreatedNew++;
                currentIds.Add(erpEmp.EmployeeCode);
                currentIds.Add(erpEmp.EmployeeId.ToString());
            }
            else
            {
                result.Failed++;
                result.FailedDetails.Add($"{erpEmp.EmployeeCode} - CreateEmployee failed");
            }
        }

        StatusChanged?.Invoke($"{preview.DeviceLabel}: Verifying...");
        var afterSync = provider.ReadExistingEmployees();
        result.MachineEmployeeCountAfter = afterSync.Count;

        Logger.Log($"[EmployeeSync] {preview.DeviceLabel}: Completed. " +
                   $"CreatedNew={result.CreatedNew}, Skipped={result.Skipped}, " +
                   $"Removed={result.Removed}, Failed={result.Failed}, MachineNow={afterSync.Count}");

        StatusChanged?.Invoke(
            $"{preview.DeviceLabel}: Sync complete. Created={result.CreatedNew}, " +
            $"Skipped={result.Skipped}, Removed={result.Removed}, Failed={result.Failed}");

        return result;
    }
}