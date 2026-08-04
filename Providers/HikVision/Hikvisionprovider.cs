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
            try { HCNetSDKNative.NET_DVR_SetLogToFile(3, "./SdkLog/", true); } catch {  }
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

    public List<AttendancePunch> FetchNewAttendanceRecords()
    {
        var records = new List<AttendancePunch>();
        if (!_connected && !Connect()) return records;

        var lastSynced = _checkpoint.GetLastSynced(_deviceKey);

        var startTime = lastSynced == DateTime.MinValue ? DateTime.Now.AddDays(-30) : lastSynced;
        var endTime = DateTime.Now;

        var cond = new HCNetSDKNative.NET_DVR_ACS_EVENT_COND();
        cond.Init();
        cond.dwSize = (uint)Marshal.SizeOf(cond);
        cond.dwMajor = 0;  
        cond.dwMinor = 0;   
        cond.byPicEnable = 0; 
        cond.wInductiveEventType = 65535;
        cond.struStartTime = ToNetDvrTime(startTime);
        cond.struEndTime = ToNetDvrTime(endTime);

        int handle = -1;
        IntPtr ptrCond = IntPtr.Zero;

        try
        {
            uint size = cond.dwSize;
            ptrCond = Marshal.AllocHGlobal((int)size);
            Marshal.StructureToPtr(cond, ptrCond, false);

            handle = HCNetSDKNative.NET_DVR_StartRemoteConfig(
                _userId, HCNetSDKNative.NET_DVR_GET_ACS_EVENT, ptrCond, (int)size, null, IntPtr.Zero);

            if (handle == -1)
            {
                Logger.Log($"[Hikvision] StartRemoteConfig failed ErrorCode={HCNetSDKNative.NET_DVR_GetLastError()}");
                return records;
            }

            var cfg = new HCNetSDKNative.NET_DVR_ACS_EVENT_CFG();
            cfg.Init();
            cfg.dwSize = (uint)Marshal.SizeOf(cfg);
            int outSize = (int)cfg.dwSize;

            bool more = true;
            var seen = new List<AttendancePunch>();

            while (more)
            {
                int status = HCNetSDKNative.NET_DVR_GetNextRemoteConfig(handle, ref cfg, outSize);
                switch (status)
                {
                    case HCNetSDKNative.NET_SDK_GET_NEXT_STATUS_SUCCESS:
                        if (HCNetSDKNative.SuccessMinorCodes.Contains(cfg.dwMinor))
                        {
                            string enrollNumber = ExtractEmployeeNo(cfg.struAcsEventInfo);
                            if (!string.IsNullOrEmpty(enrollNumber))
                            {
                                seen.Add(new AttendancePunch
                                {
                                    EnrollNumber = enrollNumber,
                                    VerifyMode = (int)cfg.dwMinor,
                                   
                                    InOutMode = 2,
                                    Timestamp = FromNetDvrTime(cfg.struTime)
                                });
                            }
                        }
                       
                        cfg = new HCNetSDKNative.NET_DVR_ACS_EVENT_CFG();
                        cfg.Init();
                        cfg.dwSize = (uint)Marshal.SizeOf(cfg);
                        break;

                    case HCNetSDKNative.NET_SDK_GET_NEXT_STATUS_NEED_WAIT:
                        Thread.Sleep(150);
                        break;

                    case HCNetSDKNative.NET_SDK_GET_NEXT_STATUS_FINISH:
                        more = false;
                        break;

                    case HCNetSDKNative.NET_SDK_GET_NEXT_STATUS_FAILED:
                    default:
                        Logger.Log($"[Hikvision] GetNextRemoteConfig status={status} ErrorCode={HCNetSDKNative.NET_DVR_GetLastError()}");
                        more = false;
                        break;
                }
            }

            records = seen.Where(r => r.Timestamp > lastSynced)
                           .OrderBy(r => r.Timestamp)
                           .ToList();

            Logger.Log($"[Hikvision] Device returned {seen.Count} verify-success event(s), " +
                       $"{records.Count} new since checkpoint {lastSynced:yyyy-MM-dd HH:mm:ss}.");

            if (records.Count > 0)
                _checkpoint.UpdateLastSynced(_deviceKey, records.Max(r => r.Timestamp));
        }
        catch (Exception ex)
        {
            Logger.Log($"[Hikvision] Fetch error: {ex.Message}");
            _connected = false;
        }
        finally
        {
            if (handle != -1) HCNetSDKNative.NET_DVR_StopRemoteConfig(handle);
            if (ptrCond != IntPtr.Zero) Marshal.FreeHGlobal(ptrCond);
        }

        return records;
    }
    private static string ExtractEmployeeNo(HCNetSDKNative.NET_DVR_ACS_EVENT_DETAIL detail)
    {
        if (detail.byEmployeeNo != null)
        {
            string fromBytes = Encoding.ASCII.GetString(detail.byEmployeeNo).TrimEnd('\0').Trim();
            if (!string.IsNullOrEmpty(fromBytes)) return fromBytes;
        }
        return detail.dwEmployeeNo > 0 ? detail.dwEmployeeNo.ToString() : "";
    }

    private static HCNetSDKNative.NET_DVR_TIME ToNetDvrTime(DateTime dt) => new()
    {
        dwYear = dt.Year,
        dwMonth = dt.Month,
        dwDay = dt.Day,
        dwHour = dt.Hour,
        dwMinute = dt.Minute,
        dwSecond = dt.Second
    };

    private static DateTime FromNetDvrTime(HCNetSDKNative.NET_DVR_TIME t)
    {
        try { return new DateTime(t.dwYear, t.dwMonth, t.dwDay, t.dwHour, t.dwMinute, t.dwSecond); }
        catch { return DateTime.Now; }
    }

    public List<Models.MachineEmployee> ReadExistingEmployees()
    {
        Logger.Log("[Hikvision] ReadExistingEmployees: not implemented yet (needs ISAPI UserInfo/Search — see comment in HikvisionProvider.cs).");
        return new List<Models.MachineEmployee>();
    }

    public bool CreateEmployee(string enrollNumber, string employeeName, string? fallbackNumericId = null)
    {
        Logger.Log($"[Hikvision] CreateEmployee({enrollNumber}): not implemented yet — face terminals need a photo enrolled at the device anyway.");
        return false;
    }

    public bool DeleteEmployee(string enrollNumber)
    {
        Logger.Log($"[Hikvision] DeleteEmployee({enrollNumber}): not implemented yet (needs ISAPI UserInfo/Delete).");
        return false;
    }

    public void Dispose() => Disconnect();
}