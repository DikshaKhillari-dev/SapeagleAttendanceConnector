using System.Runtime.InteropServices;
using System.Text;
using SapeagleAttendanceConnector.Services;

namespace SapeagleAttendanceConnector.Hikvision;

public class HikvisionProvider : IAttendanceProvider
{
    private static bool _sdkInitialized;
    private static readonly object _sdkInitLock = new();

    private readonly string _ip;
    private readonly int _port;
    private readonly string _username;
    private readonly string _password;
    private readonly string _deviceKey;
    private readonly CheckpointService _checkpoint;

    private int _userId = -1;
    private bool _connected;

    public HikvisionProvider(string ip, int port, string username, string password, int machineNumber, CheckpointService checkpoint)
    {
        _ip = ip;
        _port = port > 0 ? port : 8000;
        _username = string.IsNullOrWhiteSpace(username) ? "admin" : username;
        _password = password;
        _checkpoint = checkpoint;
        _deviceKey = $"Hikvision:{ip}:{machineNumber}";

        EnsureSdkInitialized();
    }

    private static void EnsureSdkInitialized()
    {
        if (_sdkInitialized) return;
        lock (_sdkInitLock)
        {
            if (_sdkInitialized) return;
            HCNetSDKNative.NET_DVR_Init();
            try { HCNetSDKNative.NET_DVR_SetLogToFile(3, "./SdkLog/", true); } catch { }
            _sdkInitialized = true;
        }
    }

    public bool Connect()
    {
        if (_connected) return true;

        var loginInfo = new HCNetSDKNative.NET_DVR_USER_LOGIN_INFO
        {
            sDeviceAddress = _ip,
            wPort = (ushort)_port,
            sUserName = _username,
            sPassword = _password,
            byLoginMode = 0,
            byUseTransport = 0,
            bUseAsynLogin = false,
            byRes3 = new byte[119]
        };

        var deviceInfo = new HCNetSDKNative.NET_DVR_DEVICEINFO_V40();

        _userId = HCNetSDKNative.NET_DVR_Login_V40(ref loginInfo, ref deviceInfo);
        _connected = _userId >= 0;

        Logger.Log(_connected
            ? $"[Hikvision] Connected {_ip}:{_port} (UserID={_userId})"
            : $"[Hikvision] Connect failed {_ip}:{_port} ErrorCode={HCNetSDKNative.NET_DVR_GetLastError()}");

        return _connected;
    }

    public void Disconnect()
    {
        if (!_connected) return;
        try { HCNetSDKNative.NET_DVR_Logout_V30(_userId); }
        finally { _connected = false; _userId = -1; }
    }

    // Fetches attendance punches via the ISAPI "AcsEvent" search (POST /ISAPI/AccessControl/AcsEvent?format=json)
    // instead of the legacy binary NET_DVR_StartRemoteConfig(NET_DVR_GET_ACS_EVENT) call. On this device
    // (DS-K1T320EFWX) the legacy binary command consistently returns NET_DVR_PARAMETER_ERROR (17) even with a
    // byte-correct NET_DVR_ACS_EVENT_COND struct, whereas the ISAPI channel (already used for employee sync)
    // works once called with POST + a CRLF-terminated request line. Per HCNetSDK docs (E.6 / F.2 / F.3), this
    // reuses the same CallIsapi plumbing.
    public List<AttendancePunch> FetchNewAttendanceRecords()
    {
        var records = new List<AttendancePunch>();
        if (!_connected && !Connect()) return records;

        var lastSynced = _checkpoint.GetLastSynced(_deviceKey);
        var startTime = lastSynced == DateTime.MinValue ? DateTime.Now.AddDays(-30) : lastSynced;
        var endTime = DateTime.Now;

        try
        {
            int position = 0;
            const int pageSize = 30;
            string searchId = Guid.NewGuid().ToString("N")[..16];
            var seen = new List<AttendancePunch>();
            bool more = true;

            while (more)
            {
                string body = System.Text.Json.JsonSerializer.Serialize(new
                {
                    AcsEventCond = new
                    {
                        searchID = searchId,
                        searchResultPosition = position,
                        maxResults = pageSize,
                        major = 0,
                        minor = 0,
                        startTime = ToIsoWithOffset(startTime),
                        endTime = ToIsoWithOffset(endTime)
                    }
                });

                var (ok, response, statusCode) = CallIsapi("POST /ISAPI/AccessControl/AcsEvent?format=json", body);
                if (!ok)
                {
                    Logger.Log($"[Hikvision] AcsEvent search failed at position={position}, StatusCode={statusCode}, Response={response}");
                    break;
                }

                using var doc = System.Text.Json.JsonDocument.Parse(response);
                var root = doc.RootElement.GetProperty("AcsEvent");
                int numOfMatches = root.TryGetProperty("numOfMatches", out var nm) ? nm.GetInt32() : 0;
                int totalMatches = root.TryGetProperty("totalMatches", out var tm) ? tm.GetInt32() : 0;

                if (root.TryGetProperty("InfoList", out var infoList) &&
                    infoList.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var ev in infoList.EnumerateArray())
                    {
                        int minor = ev.TryGetProperty("minor", out var mn) ? mn.GetInt32() : 0;
                        if (!HCNetSDKNative.SuccessMinorCodes.Contains((uint)minor)) continue;

                        string enrollNumber = ev.TryGetProperty("employeeNoString", out var eno) ? (eno.GetString() ?? "") : "";
                        string timeStr = ev.TryGetProperty("time", out var t) ? (t.GetString() ?? "") : "";
                        if (string.IsNullOrEmpty(enrollNumber) || string.IsNullOrEmpty(timeStr)) continue;

                        if (DateTimeOffset.TryParse(timeStr, out var dto))
                        {
                            seen.Add(new AttendancePunch
                            {
                                EnrollNumber = enrollNumber,
                                VerifyMode = minor,
                                InOutMode = 2,
                                Timestamp = dto.LocalDateTime
                            });
                        }
                    }
                }

                position += numOfMatches;
                if (numOfMatches == 0 || position >= totalMatches) more = false;
            }

            records = seen.Where(r => r.Timestamp > lastSynced)
                           .OrderBy(r => r.Timestamp)
                           .ToList();

            Logger.Log($"[Hikvision] AcsEvent returned {seen.Count} verify-success event(s), " +
                       $"{records.Count} new since checkpoint {lastSynced:yyyy-MM-dd HH:mm:ss}.");

            if (records.Count > 0)
                _checkpoint.UpdateLastSynced(_deviceKey, records.Max(r => r.Timestamp));
        }
        catch (Exception ex)
        {
            Logger.Log($"[Hikvision] Fetch error: {ex.Message}");
            _connected = false;
        }

        return records;
    }

    // Hikvision's ISAPI time fields want ISO-8601 with a UTC offset, e.g. "2026-08-07T14:01:39+05:30".
    private static string ToIsoWithOffset(DateTime dt)
    {
        var withOffset = new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Local));
        return withOffset.ToString("yyyy-MM-ddTHH:mm:sszzz");
    }

    // ---- ISAPI (AccessControl/UserInfo) over the authenticated NET_DVR session ----
    // Hikvision face terminals manage users through the device's ISAPI HTTP layer, not the
    // legacy attendance-log calls above. NET_DVR_STDXMLConfig lets us send an ISAPI request
    // ("METHOD /path?format=json" + a JSON body) through the SDK session we already logged
    // into with Connect(), so we don't need a separate HTTP client with digest auth.
    private (bool Ok, string Response, int StatusCode) CallIsapi(string requestLine, string? jsonBody = null)
    {
        if (!_connected && !Connect()) return (false, "", 0);

        const int OutBufferSize = 1024 * 1024; // 1MB is plenty for UserInfo JSON responses
        const int StatusBufferSize = 4096;

        // The device's ISAPI request-line parser requires a CRLF terminator (every official
        // Hikvision demo builds it as "METHOD /path\r\n"). Without it the device can't match
        // the route and replies with NET_DVR_NOSUPPORT even though the endpoint exists.
        string terminatedRequestLine = requestLine.EndsWith("\r\n") ? requestLine : requestLine + "\r\n";

        byte[] urlBytes = Encoding.ASCII.GetBytes(terminatedRequestLine);
        byte[]? inBytes = jsonBody != null ? Encoding.UTF8.GetBytes(jsonBody) : null;

        IntPtr urlPtr = Marshal.AllocHGlobal(urlBytes.Length);
        IntPtr inPtr = IntPtr.Zero;
        IntPtr outPtr = Marshal.AllocHGlobal(OutBufferSize);
        IntPtr statusPtr = Marshal.AllocHGlobal(StatusBufferSize);

        try
        {
            Marshal.Copy(urlBytes, 0, urlPtr, urlBytes.Length);
            if (inBytes != null)
            {
                inPtr = Marshal.AllocHGlobal(inBytes.Length);
                Marshal.Copy(inBytes, 0, inPtr, inBytes.Length);
            }

            var input = new HCNetSDKNative.NET_DVR_XML_CONFIG_INPUT();
            input.Init();
            input.dwSize = (uint)Marshal.SizeOf(input);
            input.lpRequestUrl = urlPtr;
            input.dwRequestUrlLen = (uint)urlBytes.Length;
            input.lpInBuffer = inPtr;
            input.dwInBufferSize = inBytes != null ? (uint)inBytes.Length : 0;
            input.dwRecvTimeOut = 5000;
            input.dwSendTimeOut = 5000;
            input.byForceEncrpt = 0;
            input.byNumOfMultiPart = 0;
            input.byMIMEType = 0; // 0 = json

            // lpOutBuffer/dwOutBufferSize/lpStatusBuffer/dwStatusSize belong on the OUTPUT
            // struct, not INPUT — the device SDK reads them from here.
            var output = new HCNetSDKNative.NET_DVR_XML_CONFIG_OUTPUT();
            output.Init();
            output.dwSize = (uint)Marshal.SizeOf(output);
            output.lpOutBuffer = outPtr;
            output.dwOutBufferSize = OutBufferSize;
            output.lpStatusBuffer = statusPtr;
            output.dwStatusSize = StatusBufferSize;

            bool ok = HCNetSDKNative.NET_DVR_STDXMLConfig(_userId, ref input, ref output);
            if (!ok)
            {
                Logger.Log($"[Hikvision] ISAPI {requestLine} failed ErrorCode={HCNetSDKNative.NET_DVR_GetLastError()}");
                return (false, "", 0);
            }

            int respLen = (int)output.dwReturnSize;
            byte[] respBytes = new byte[respLen];
            if (respLen > 0) Marshal.Copy(outPtr, respBytes, 0, respLen);
            string response = Encoding.UTF8.GetString(respBytes);

            byte[] statusBytes = new byte[StatusBufferSize];
            Marshal.Copy(statusPtr, statusBytes, 0, StatusBufferSize);
            string statusText = Encoding.UTF8.GetString(statusBytes).TrimEnd('\0');
            int statusCode = ExtractStatusCode(statusText);

            return (true, response, statusCode);
        }
        catch (Exception ex)
        {
            Logger.Log($"[Hikvision] ISAPI {requestLine} exception: {ex.Message}");
            return (false, "", 0);
        }
        finally
        {
            Marshal.FreeHGlobal(urlPtr);
            if (inPtr != IntPtr.Zero) Marshal.FreeHGlobal(inPtr);
            Marshal.FreeHGlobal(outPtr);
            Marshal.FreeHGlobal(statusPtr);
        }
    }

    // The status buffer holds a small fragment like {"statusCode":1,"statusString":"OK",...} —
    // statusCode == 1 means the device accepted the request.
    private static int ExtractStatusCode(string statusText)
    {
        var match = System.Text.RegularExpressions.Regex.Match(statusText, "statusCode[\">:]+(\\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : -1;
    }

    // Some ISAPI operations (UserInfo/Record, UserInfo/Delete) return status directly in the
    // response BODY as {"statusCode":1,...} instead of the separate NET_DVR status buffer, which
    // this device firmware leaves empty on POST/PUT (buffer-derived statusCode == -1). Prefer the
    // buffer status when it's actually populated; otherwise fall back to the body's statusCode.
    private static int ExtractBodyStatusCode(string response, int bufferStatusCode)
    {
        if (bufferStatusCode != -1) return bufferStatusCode;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("statusCode", out var sc))
                return sc.GetInt32();
        }
        catch { }
        return bufferStatusCode;
    }

    public List<Models.MachineEmployee> ReadExistingEmployees()
    {
        var employees = new List<Models.MachineEmployee>();
        if (!_connected && !Connect()) return employees;

        try
        {
            int position = 0;
            const int pageSize = 30; // Hikvision recommends <=30 per UserInfo/Search page
            string searchId = Guid.NewGuid().ToString("N")[..16];

            while (true)
            {
                string body = System.Text.Json.JsonSerializer.Serialize(new
                {
                    UserInfoSearchCond = new
                    {
                        searchID = searchId,
                        searchResultPosition = position,
                        maxResults = pageSize
                    }
                });

                // Per Hikvision's Person-Based Access Control SDK guide (E.168), Search is POST, not PUT.
                var (ok, response, statusCode) = CallIsapi("POST /ISAPI/AccessControl/UserInfo/Search?format=json", body);
                if (!ok)
                {
                    Logger.Log($"[Hikvision] ReadExistingEmployees: search failed at position={position}, StatusCode={statusCode}, Response={response}");
                    break;
                }

                using var doc = System.Text.Json.JsonDocument.Parse(response);
                var root = doc.RootElement.GetProperty("UserInfoSearch");
                int numOfMatches = root.TryGetProperty("numOfMatches", out var nm) ? nm.GetInt32() : 0;
                int totalMatches = root.TryGetProperty("totalMatches", out var tm) ? tm.GetInt32() : 0;

                if (root.TryGetProperty("UserInfo", out var userInfoArray) &&
                    userInfoArray.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var u in userInfoArray.EnumerateArray())
                    {
                        string empNo = u.TryGetProperty("employeeNo", out var eno) ? (eno.GetString() ?? "") : "";
                        string name = u.TryGetProperty("name", out var nn) ? (nn.GetString() ?? "") : "";
                        if (!string.IsNullOrEmpty(empNo))
                            employees.Add(new Models.MachineEmployee { EnrollNumber = empNo, Name = name });
                    }
                }

                position += numOfMatches;
                if (numOfMatches == 0 || position >= totalMatches) break;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[Hikvision] ReadExistingEmployees error: {ex.Message}");
        }

        Logger.Log($"[Hikvision] ReadExistingEmployees found {employees.Count} employee(s) on machine.");
        return employees;
    }

    public bool CreateEmployee(string enrollNumber, string employeeName, string? fallbackNumericId = null)
    {
        if (!_connected && !Connect()) return false;

        try
        {
            string body = System.Text.Json.JsonSerializer.Serialize(new
            {
                UserInfo = new
                {
                    employeeNo = enrollNumber,
                    name = employeeName,
                    userType = "normal",
                    Valid = new
                    {
                        enable = true,
                        beginTime = "2020-01-01T00:00:00",
                        endTime = "2037-12-31T23:59:59",
                        timeType = "local"
                    },
                    doorRight = "1",
                    RightPlan = new[] { new { doorNo = 1, planTemplateNo = "1" } }
                }
            });

            // Per Hikvision's Person-Based Access Control SDK guide (E.167), Record (add person) is POST, not PUT.
            var (ok, response, statusCode) = CallIsapi("POST /ISAPI/AccessControl/UserInfo/Record?format=json", body);
            bool created = ok && ExtractBodyStatusCode(response, statusCode) == 1;

            Logger.Log(created
                ? $"[Hikvision] CreateEmployee EnrollNumber={enrollNumber} Name={employeeName} -> success (user record only — no face photo enrolled)"
                : $"[Hikvision] CreateEmployee EnrollNumber={enrollNumber} failed. StatusCode={statusCode} Response={response}");

            return created;
        }
        catch (Exception ex)
        {
            Logger.Log($"[Hikvision] CreateEmployee error: {ex.Message}");
            return false;
        }
    }

    public bool DeleteEmployee(string enrollNumber)
    {
        if (!_connected && !Connect()) return false;

        try
        {
            string body = System.Text.Json.JsonSerializer.Serialize(new
            {
                UserInfoDetail = new
                {
                    mode = "byEmployeeNo",
                    EmployeeNoList = new[] { new { employeeNo = enrollNumber } }
                }
            });

            var (ok, response, statusCode) = CallIsapi("PUT /ISAPI/AccessControl/UserInfo/Delete?format=json", body);
            bool deleted = ok && ExtractBodyStatusCode(response, statusCode) == 1;

            Logger.Log(deleted
                ? $"[Hikvision] DeleteEmployee EnrollNumber={enrollNumber} -> success"
                : $"[Hikvision] DeleteEmployee EnrollNumber={enrollNumber} failed. StatusCode={statusCode} Response={response}");

            return deleted;
        }
        catch (Exception ex)
        {
            Logger.Log($"[Hikvision] DeleteEmployee error: {ex.Message}");
            return false;
        }
    }

    public void Dispose() => Disconnect();
}