using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SapeagleAttendanceConnector.Models;

namespace SapeagleAttendanceConnector.Services;

public class ActivationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int ComId { get; set; }
    public string CompanyName { get; set; } = "";
    public int MachineId { get; set; }
    public string MachineName { get; set; } = "";
}

public class ApiService
{
    private readonly HttpClient _http;
    private readonly Dictionary<int, Dictionary<string, long>> _empCacheByComId = new();
    private readonly Dictionary<int, DateTime> _empCacheLoadedAtByComId = new();
    private static readonly TimeSpan EmpCacheTtl = TimeSpan.FromMinutes(15);

    private const string ApiUrl = "https://erpapi.sapeagleerp.com";
    private const string ApiKey = "SapeagleerpIH0bhqizXUUiBvzU0qxvYAcfvbz9CevkpYF6xYtfwMW1wabnfV2QAeh8Rn9b54An";

    public ApiService()
    {
        _http = new HttpClient { BaseAddress = new Uri(ApiUrl) };
        _http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<ActivationResult> ActivateAsync(string companyCode, string activationKey, CancellationToken ct = default)
    {
        try
        {
            var payload = new { CompanyCode = companyCode, ActivationKey = activationKey };
            var resp = await _http.PostAsJsonAsync("/api/AttendanceConnector/activate", payload, ct);

            var rawJson = await resp.Content.ReadAsStringAsync(ct);
            Logger.Log($"[Api] ActivateAsync: CompanyCode='{companyCode}' -> HTTP {(int)resp.StatusCode} {resp.StatusCode}. Raw response: {rawJson}");

            var body = System.Text.Json.JsonSerializer.Deserialize<ActivationResult>(rawJson,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (body == null)
            {
                Logger.Log("[Api] ActivateAsync: deserialized body is null.");
                return new ActivationResult { Success = false, Message = "Invalid response from server." };
            }

            body.Success = resp.IsSuccessStatusCode && body.Success;

            Logger.Log($"[Api] ActivateAsync: parsed -> Success={body.Success}, ComId={body.ComId}, CompanyName='{body.CompanyName}', MachineId={body.MachineId}, MachineName='{body.MachineName}'");

            return body;
        }
        catch (Exception ex)
        {
            Logger.Log($"[Api] ActivateAsync: exception - {ex}");
            return new ActivationResult { Success = false, Message = $"Connection error: {ex.Message}" };
        }
    }

    public async Task<List<MachineConfig>> GetActiveMachinesAsync(int comId, CancellationToken ct = default)
    {
        try
        {
            var machines = await _http.GetFromJsonAsync<List<MachineConfig>>($"/api/AttendanceMachine/list?comId={comId}", ct);
            var active = machines?.Where(m => m.IsActive).ToList() ?? new List<MachineConfig>();
            Logger.Log($"[Api] GetActiveMachinesAsync: ComId={comId} -> total returned={machines?.Count ?? 0}, active={active.Count}. " +
                       $"DeviceIds=[{string.Join(", ", active.Select(m => $"{m.Id}:{m.DeviceId}"))}]");
            return active;
        }
        catch (Exception ex)
        {
            Logger.Log($"[Api] GetActiveMachinesAsync: exception for ComId={comId}: {ex.Message}");
            return new List<MachineConfig>();
        }
    }

    /// <summary>
    /// Asks the ERP database what the latest attendance record already saved for this
    /// machine is. Used only as a fallback to seed a local checkpoint when this connector
    /// install has no local checkpoint yet for the device (fresh install, checkpoints.json
    /// lost, etc.) — so it never blindly re-reads a device's full backlog and creates
    /// duplicates in the ERP.
    /// </summary>
    public async Task<DateTime?> GetLastSyncedTimestampAsync(int comId, int machineId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync($"/api/attendance/last-synced-timestamp?comId={comId}&machineId={machineId}", ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                Logger.Log($"[Api] GetLastSyncedTimestampAsync: ComId={comId} MachineId={machineId} -> HTTP {(int)resp.StatusCode} {resp.StatusCode}. Body: {body}");
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("lastSyncedTimestamp", out var el) && el.ValueKind != JsonValueKind.Null)
            {
                var ts = el.GetDateTime();
                Logger.Log($"[Api] GetLastSyncedTimestampAsync: ComId={comId} MachineId={machineId} -> {ts:yyyy-MM-dd HH:mm:ss}");
                return ts;
            }

            Logger.Log($"[Api] GetLastSyncedTimestampAsync: ComId={comId} MachineId={machineId} -> no prior record in ERP DB.");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Log($"[Api] GetLastSyncedTimestampAsync: exception for ComId={comId} MachineId={machineId}: {ex.Message}");
            return null;
        }
    }

    public async Task<MachineConfig?> GetMachineAsync(int machineId, CancellationToken ct = default)
    {
        try
        {
            var machine = await _http.GetFromJsonAsync<MachineConfig>($"/api/AttendanceMachine/view/{machineId}", ct);
            Logger.Log(machine == null
                ? $"[Api] GetMachineAsync: MachineId={machineId} -> null response."
                : $"[Api] GetMachineAsync: MachineId={machineId} -> Id={machine.Id}, ComId={machine.ComId}, " +
                  $"MachineName='{machine.MachineName}', DeviceId(raw)='{machine.DeviceId}', MachineType='{machine.MachineType}', " +
                  $"Ip={machine.IpAddress}:{machine.Port}, IsActive={machine.IsActive}");
            return machine;
        }
        catch (Exception ex)
        {
            Logger.Log($"[Api] GetMachineAsync: exception for MachineId={machineId}: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> SendPunchAsync(AttendanceLog punch, CancellationToken ct = default)
    {
        long empId = await ResolveEmpIdAsync(punch.ComId, punch.EnrollNumber, ct);
        if (empId == 0)
        {
            Logger.Log($"[Api] SendPunchAsync: could not resolve EmpId for EnrollNumber='{punch.EnrollNumber}' ComId={punch.ComId} — employee not matched, punch NOT sent.");
            return false;
        }

        try
        {
            bool ambiguous = punch.InOutMode != 0 && punch.InOutMode != 1;
            bool tryCheckInFirst = punch.InOutMode == 0 || ambiguous;

            var (resp, body) = tryCheckInFirst
                ? await SendCheckInAsync(empId, punch, ct)
                : await SendCheckOutAsync(empId, punch, ct);

            Logger.Log($"[Api] SendPunchAsync: EmpId={empId} InOutMode={punch.InOutMode} -> HTTP {(int)resp.StatusCode} {resp.StatusCode}. Response: {body}");

            if (resp.IsSuccessStatusCode)
                return true;

            if (ambiguous && LooksLikeAlreadyCheckedIn(resp.StatusCode, body))
            {
                var (resp2, body2) = await SendCheckOutAsync(empId, punch, ct);
                Logger.Log($"[Api] SendPunchAsync: retry as Check-Out for EmpId={empId} -> HTTP {(int)resp2.StatusCode} {resp2.StatusCode}. Response: {body2}");
                return resp2.IsSuccessStatusCode;
            }

            if (!ambiguous && punch.InOutMode != 0 && LooksLikeNoOpenCheckIn(resp.StatusCode, body))
            {
                var (resp2, body2) = await SendCheckInAsync(empId, punch, ct);
                Logger.Log($"[Api] SendPunchAsync: retry as Check-In for EmpId={empId} -> HTTP {(int)resp2.StatusCode} {resp2.StatusCode}. Response: {body2}");
                return resp2.IsSuccessStatusCode;
            }

            return false;
        }
        catch (Exception ex)
        {
            Logger.Log($"[Api] SendPunchAsync: exception calling ERP API for EmpId={empId}: {ex.Message}");
            return false;
        }
    }

    private Task<(HttpResponseMessage resp, string body)> SendCheckInAsync(long empId, AttendanceLog punch, CancellationToken ct)
        => PostAndReadAsync("/api/attendance/check-in",
            new { EmpId = empId, ComId = punch.ComId, MachineId = punch.MachineId, Device = punch.DeviceLabel, AttendanceImage = (string?)null, PunchTime = punch.Timestamp }, ct);

    private Task<(HttpResponseMessage resp, string body)> SendCheckOutAsync(long empId, AttendanceLog punch, CancellationToken ct)
        => PostAndReadAsync($"/api/attendance/check-out/{empId}",
            new { MachineId = punch.MachineId, Device = punch.DeviceLabel, PunchTime = punch.Timestamp }, ct);

    public async Task<bool> SendManualAttendanceAsync(AttendanceLog punch, CancellationToken ct = default)
    {
        long empId = await ResolveEmpIdAsync(punch.ComId, punch.EnrollNumber, ct);
        if (empId == 0)
        {
            Logger.Log($"[Api] SendManualAttendanceAsync: could not resolve EmpId for EnrollNumber='{punch.EnrollNumber}' ComId={punch.ComId} — employee not matched, punch NOT sent.");
            return false;
        }

        var attendanceType = string.IsNullOrEmpty(punch.AttendanceType) ? "PunchIn" : punch.AttendanceType;

        try
        {
            var (resp, body) = await PostManualAttendanceAsync(empId, punch, attendanceType, ct);

            Logger.Log($"[Api] SendManualAttendanceAsync: EmpId={empId} AttendanceType={attendanceType} " +
                       $"Date={punch.Timestamp:yyyy-MM-dd} Time={punch.Timestamp:HH:mm:ss} -> HTTP {(int)resp.StatusCode} {resp.StatusCode}. Response: {body}");

            if (resp.IsSuccessStatusCode)
                return true;

            // Our own per-(employee, date) open/closed tracking is in-memory only, so a
            // connector restart mid-backlog-sync (or any drift) can leave it out of step with
            // what the server actually has. Rather than get permanently stuck, self-correct
            // the same way the live check-in/check-out flow does: if the server says a
            // PunchIn is redundant, retry as PunchOut for that date, and vice versa.
            if (attendanceType == "PunchIn" && LooksLikeAlreadyCheckedIn(resp.StatusCode, body))
            {
                var (resp2, body2) = await PostManualAttendanceAsync(empId, punch, "PunchOut", ct);
                Logger.Log($"[Api] SendManualAttendanceAsync: retry as PunchOut for EmpId={empId} Date={punch.Timestamp:yyyy-MM-dd} -> HTTP {(int)resp2.StatusCode} {resp2.StatusCode}. Response: {body2}");
                return resp2.IsSuccessStatusCode;
            }

            if (attendanceType == "PunchOut" && LooksLikeNoActiveCheckInForDate(resp.StatusCode, body))
            {
                var (resp2, body2) = await PostManualAttendanceAsync(empId, punch, "PunchIn", ct);
                Logger.Log($"[Api] SendManualAttendanceAsync: retry as PunchIn for EmpId={empId} Date={punch.Timestamp:yyyy-MM-dd} -> HTTP {(int)resp2.StatusCode} {resp2.StatusCode}. Response: {body2}");
                return resp2.IsSuccessStatusCode;
            }

            return false;
        }
        catch (Exception ex)
        {
            Logger.Log($"[Api] SendManualAttendanceAsync: exception calling ERP API for EmpId={empId}: {ex.Message}");
            return false;
        }
    }

    private Task<(HttpResponseMessage resp, string body)> PostManualAttendanceAsync(long empId, AttendanceLog punch, string attendanceType, CancellationToken ct)
        => PostAndReadAsync("/api/attendance/manual-attendance", new
        {
            EmpId = empId,
            AttendanceType = attendanceType,
            AttendanceDate = punch.Timestamp.Date,
            AttendanceTime = punch.Timestamp.TimeOfDay,
            MachineId = punch.MachineId
        }, ct);

    private static bool LooksLikeNoActiveCheckInForDate(System.Net.HttpStatusCode status, string body)
        => status == System.Net.HttpStatusCode.NotFound &&
           body.Contains("No active check-in found for this date", StringComparison.OrdinalIgnoreCase);

    private async Task<(HttpResponseMessage resp, string body)> PostAndReadAsync(string url, object payload, CancellationToken ct)
    {
        var resp = await _http.PostAsJsonAsync(url, payload, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        return (resp, body);
    }

    private static bool LooksLikeNoOpenCheckIn(System.Net.HttpStatusCode status, string body)
        => status == System.Net.HttpStatusCode.NotFound &&
           body.Contains("No open check-in", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeAlreadyCheckedIn(System.Net.HttpStatusCode status, string body)
        => !status.Equals(System.Net.HttpStatusCode.OK) &&
           (body.Contains("already checked in", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("open check-in already", StringComparison.OrdinalIgnoreCase));

    private async Task<long> ResolveEmpIdAsync(int comId, string empcode, CancellationToken ct)
    {
        bool cacheStale = !_empCacheLoadedAtByComId.TryGetValue(comId, out var loadedAt)
                           || DateTime.UtcNow - loadedAt > EmpCacheTtl;

        if (!cacheStale && _empCacheByComId.TryGetValue(comId, out var cache) && cache.TryGetValue(empcode, out long cached))
        {
            Logger.Log($"[Api] ResolveEmpIdAsync: '{empcode}' resolved from cache -> EmpId={cached}");
            return cached;
        }

        try
        {
            var resp = await _http.GetAsync($"/api/attendance/employees-by-company/{comId}", ct);
            if (!resp.IsSuccessStatusCode)
            {
                Logger.Log($"[Api] ResolveEmpIdAsync: employees-by-company/{comId} returned HTTP {(int)resp.StatusCode} {resp.StatusCode}");
                return _empCacheByComId.TryGetValue(comId, out var fb) && fb.TryGetValue(empcode, out long f) ? f : 0;
            }

            var data = await resp.Content.ReadFromJsonAsync<EmployeesByCompanyResponse>(cancellationToken: ct);
            var newCache = new Dictionary<string, long>();
            if (data?.Employees != null)
                foreach (var e in data.Employees)
                    newCache[e.EmpCode] = e.Id;

            var mapping = await GetEmployeeMappingAsync(comId, ct);
            foreach (var kv in mapping)
                newCache[kv.Key] = kv.Value;

            _empCacheByComId[comId] = newCache;
            _empCacheLoadedAtByComId[comId] = DateTime.UtcNow;

            Logger.Log($"[Api] ResolveEmpIdAsync: loaded {newCache.Count} employee(s) for ComId={comId}. Codes: {string.Join(", ", newCache.Keys)}");

            var resolved = newCache.TryGetValue(empcode, out long r) ? r : 0;
            Logger.Log(resolved == 0
                ? $"[Api] ResolveEmpIdAsync: '{empcode}' NOT FOUND in employee list for ComId={comId}."
                : $"[Api] ResolveEmpIdAsync: '{empcode}' resolved -> EmpId={resolved}");

            return resolved;
        }
        catch (Exception ex)
        {
            Logger.Log($"[Api] ResolveEmpIdAsync: exception fetching employees for ComId={comId}: {ex.Message}");
            return 0;
        }
    }

    private class EmployeesByCompanyResponse
    {
        public long ComId { get; set; }
        public int TotalEmployees { get; set; }
        public List<EmployeeItem> Employees { get; set; } = new();
    }

    private class EmployeeItem
    {
        public long Id { get; set; }
        public string EmpCode { get; set; } = "";
        public string EmpLoginName { get; set; } = "";
    }

    public async Task<List<ErpEmployee>> GetEmployeesForSyncAsync(int comId, int? departmentId, CancellationToken ct = default)
    {
        var result = new List<ErpEmployee>();
        try
        {
            string url = departmentId.HasValue
                ? $"/api/attendance/employees-by-department/{comId}?departmentId={departmentId.Value}"
                : $"/api/attendance/employees-by-company/{comId}";

            var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Logger.Log($"[Api] GetEmployeesForSyncAsync(dept): HTTP {(int)resp.StatusCode} {resp.StatusCode} for {url}");
                return result;
            }

            var data = await resp.Content.ReadFromJsonAsync<EmployeesByCompanyResponse>(cancellationToken: ct);
            if (data?.Employees != null)
                foreach (var e in data.Employees)
                    result.Add(new ErpEmployee { EmployeeCode = e.EmpCode, EmployeeName = e.EmpLoginName, EmployeeId = e.Id });
            Logger.Log($"[Api] GetEmployeesForSyncAsync(dept): fetched {result.Count} employee(s) for ComId={comId}, DepartmentId={(departmentId.HasValue ? departmentId.Value.ToString() : "All")}.");
        }
        catch (Exception ex)
        {
            Logger.Log($"[Api] GetEmployeesForSyncAsync(dept): exception - {ex.Message}");
        }

        return result;
    }

    public async Task<Dictionary<string, long>> GetEmployeeMappingAsync(int comId, CancellationToken ct = default)
    {
        var result = new Dictionary<string, long>();
        try
        {
            var resp = await _http.GetAsync($"/api/AttendanceEmployeeMapping/list?comId={comId}", ct);
            if (!resp.IsSuccessStatusCode) return result;

            var data = await resp.Content.ReadFromJsonAsync<MappingListResponse>(cancellationToken: ct);
            if (data?.Data != null)
                foreach (var m in data.Data)
                    result[m.EnrollNumber] = m.EmpId;
        }
        catch (Exception ex)
        {
            Logger.Log($"[Api] GetEmployeeMappingAsync: exception - {ex.Message}");
        }
        return result;
    }

    public async Task<bool> SaveEmployeeMappingAsync(List<EmployeeMappingEntry> mappings, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("/api/AttendanceEmployeeMapping/save-bulk", mappings, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Logger.Log($"[Api] SaveEmployeeMappingAsync: exception - {ex.Message}");
            return false;
        }
    }

    private class MappingListResponse
    {
        public bool Success { get; set; }
        public List<MappingItem> Data { get; set; } = new();
    }
    private class MappingItem
    {
        public string EnrollNumber { get; set; } = "";
        public long EmpId { get; set; }
    }

    public async Task<List<Department>> GetDepartmentsAsync(int comId, CancellationToken ct = default)
    {
        var result = new List<Department>();
        try
        {
            var resp = await _http.GetAsync($"/api/Department?comId={comId}", ct);
            if (!resp.IsSuccessStatusCode)
            {
                Logger.Log($"[Api] GetDepartmentsAsync: HTTP {(int)resp.StatusCode} {resp.StatusCode}");
                return result;
            }

            var raw = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            // The department array's wrapper key isn't confirmed yet (data / departments / result),
            // so we look for the first array we can find rather than hardcoding one.
            JsonElement listEl = default;
            bool found = false;

            if (root.ValueKind == JsonValueKind.Array)
            {
                listEl = root;
                found = true;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        listEl = prop.Value;
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                Logger.Log($"[Api] GetDepartmentsAsync: no array found in response: {raw}");
                return result;
            }

            foreach (var item in listEl.EnumerateArray())
            {
                int id = TryGetInt(item, "id", "Id", "departmentId", "DepartmentId", "recordId");
                string name = TryGetString(item, "name", "Name", "departmentName", "DepartmentName", "displayValue", "DisplayValue");

                if (id != 0 && !string.IsNullOrWhiteSpace(name))
                    result.Add(new Department { Id = id, Name = name });
            }

            Logger.Log($"[Api] GetDepartmentsAsync: fetched {result.Count} department(s) for ComId={comId}.");
        }
        catch (Exception ex)
        {
            Logger.Log($"[Api] GetDepartmentsAsync: exception - {ex.Message}");
        }

        return result;
    }

    private static int TryGetInt(JsonElement item, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (item.TryGetProperty(k, out var el))
            {
                if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n)) return n;
                if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var s)) return s;
            }
        }
        return 0;
    }

    private static string TryGetString(JsonElement item, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (item.TryGetProperty(k, out var el) && el.ValueKind == JsonValueKind.String)
                return el.GetString() ?? "";
        }
        return "";
    }
}