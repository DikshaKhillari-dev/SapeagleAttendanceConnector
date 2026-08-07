using System.Runtime.InteropServices;

namespace SapeagleAttendanceConnector.Hikvision;

internal static class HCNetSDKNative
{
    private const string Dll = "HCNetSDK.dll";

    public const int SERIALNO_LEN = 48;
    public const int NET_DVR_DEV_ADDRESS_MAX_LEN = 129;
    public const int NET_DVR_LOGIN_USERNAME_MAX_LEN = 64;
    public const int NET_DVR_LOGIN_PASSWD_MAX_LEN = 64;
    public const int ACS_CARD_NO_LEN = 32;
    public const int MACADDR_LEN = 6;
    public const int MAX_NAMELEN = 16;
    public const int NET_SDK_MONITOR_ID_LEN = 64;
    public const int NET_SDK_EMPLOYEE_NO_LEN = 32;

    public const int NET_DVR_GET_ACS_EVENT = 2514;
    public const int NET_SDK_GET_NEXT_STATUS_SUCCESS = 1000;
    public const int NET_SDK_GET_NEXT_STATUS_NEED_WAIT = 1001;
    public const int NET_SDK_GET_NEXT_STATUS_FINISH = 1002;
    public const int NET_SDK_GET_NEXT_STATUS_FAILED = 1003;


    public static readonly HashSet<uint> SuccessMinorCodes = new()
    {
        0x10, // MINOR_MULTI_VERIFY_SUCCESS
        0x26, // MINOR_FINGERPRINT_COMPARE_PASS
        0x28, // MINOR_CARD_FINGERPRINT_VERIFY_PASS
        0x2b, // MINOR_CARD_FINGERPRINT_PASSWD_VERIFY_PASS
        0x2e, // MINOR_FINGERPRINT_PASSWD_VERIFY_PASS
        0x36, // MINOR_FACE_AND_FP_VERIFY_PASS
        0x39, // MINOR_FACE_AND_PW_VERIFY_PASS
        0x3c, // MINOR_FACE_AND_CARD_VERIFY_PASS
        0x3f, // MINOR_FACE_AND_PW_AND_FP_VERIFY_PASS
        0x42, // MINOR_FACE_CARD_AND_FP_VERIFY_PASS
        0x45, // MINOR_EMPLOYEENO_AND_FP_VERIFY_PASS
        0x48, // MINOR_EMPLOYEENO_AND_FP_AND_PW_VERIFY_PASS
        0x4b, // MINOR_FACE_VERIFY_PASS  <-- most common one for AW4350 face-only punches
        0x4d, // MINOR_EMPLOYEENO_AND_FACE_VERIFY_PASS
        0x99, // MINOR_COMBINED_VERIFY_PASS
    };

    [StructLayout(LayoutKind.Sequential)]
    public struct NET_DVR_TIME
    {
        public int dwYear;
        public int dwMonth;
        public int dwDay;
        public int dwHour;
        public int dwMinute;
        public int dwSecond;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NET_DVR_IPADDR
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
        public string sIpV4;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128, ArraySubType = UnmanagedType.I1)]
        public byte[] byIPv6;

        public void Init() => byIPv6 = new byte[128];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NET_DVR_ACS_EVENT_DETAIL
    {
        public uint dwSize;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ACS_CARD_NO_LEN)]
        public byte[] byCardNo;
        public byte byCardType;
        public byte byWhiteListNo;
        public byte byReportChannel;
        public byte byCardReaderKind;
        public uint dwCardReaderNo;
        public uint dwDoorNo;
        public uint dwVerifyNo;
        public uint dwAlarmInNo;
        public uint dwAlarmOutNo;
        public uint dwCaseSensorNo;
        public uint dwRs485No;
        public uint dwMultiCardGroupNo;
        public ushort wAccessChannel;
        public byte byDeviceNo;
        public byte byDistractControlNo;
        public uint dwEmployeeNo;
        public ushort wLocalControllerID;
        public byte byInternetAccess;
        public byte byType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MACADDR_LEN)]
        public byte[] byMACAddr;
        public byte bySwipeCardType;
        public byte byRes2;
        public uint dwSerialNo;
        public byte byChannelControllerID;
        public byte byChannelControllerLampID;
        public byte byChannelControllerIRAdaptorID;
        public byte byChannelControllerIREmitterID;
        public uint dwRecordChannelNum;
        public IntPtr pRecordChannelData;
        public byte byUserType;
        public byte byCurrentVerifyMode;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] byRe2;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = NET_SDK_EMPLOYEE_NO_LEN)]
        public byte[] byEmployeeNo;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] byRes;

        public void Init()
        {
            byCardNo = new byte[ACS_CARD_NO_LEN];
            byMACAddr = new byte[MACADDR_LEN];
            byRe2 = new byte[2];
            byEmployeeNo = new byte[NET_SDK_EMPLOYEE_NO_LEN];
            byRes = new byte[64];
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NET_DVR_ACS_EVENT_CFG
    {
        public uint dwSize;
        public uint dwMajor;
        public uint dwMinor;
        public NET_DVR_TIME struTime;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_NAMELEN)]
        public byte[] sNetUser;
        public NET_DVR_IPADDR struRemoteHostAddr;
        public NET_DVR_ACS_EVENT_DETAIL struAcsEventInfo;
        public uint dwPicDataLen;
        public IntPtr pPicData;
        public ushort wInductiveEventType;
        public byte byTimeType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 61)]
        public byte[] byRes;

        public void Init()
        {
            sNetUser = new byte[MAX_NAMELEN];
            struRemoteHostAddr.Init();
            struAcsEventInfo.Init();
            byRes = new byte[61];
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NET_DVR_ACS_EVENT_COND
    {
        public uint dwSize;
        public uint dwMajor;
        public uint dwMinor;
        public NET_DVR_TIME struStartTime;
        public NET_DVR_TIME struEndTime;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ACS_CARD_NO_LEN, ArraySubType = UnmanagedType.I1)]
        public byte[] byCardNo;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_NAMELEN, ArraySubType = UnmanagedType.I1)]
        public byte[] byName;
        public byte byPicEnable;
        public byte byTimeType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2, ArraySubType = UnmanagedType.I1)]
        public byte[] byRes2;
        public uint dwBeginSerialNo;
        public uint dwEndSerialNo;
        public uint dwIOTChannelNo;
        public ushort wInductiveEventType;
        public byte bySearchType;
        public byte byRes1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = NET_SDK_MONITOR_ID_LEN)]
        public string szMonitorID;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = NET_SDK_EMPLOYEE_NO_LEN, ArraySubType = UnmanagedType.I1)]
        public byte[] byEmployeeNo;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 140, ArraySubType = UnmanagedType.I1)]
        public byte[] byRes;

        public void Init()
        {
            byCardNo = new byte[ACS_CARD_NO_LEN];
            byName = new byte[MAX_NAMELEN];
            byRes2 = new byte[2];
            byEmployeeNo = new byte[NET_SDK_EMPLOYEE_NO_LEN];
            byRes = new byte[140];
            szMonitorID = "";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NET_DVR_DEVICEINFO_V30
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = SERIALNO_LEN, ArraySubType = UnmanagedType.I1)]
        public byte[] sSerialNumber;
        public byte byAlarmInPortNum;
        public byte byAlarmOutPortNum;
        public byte byDiskNum;
        public byte byDVRType;
        public byte byChanNum;
        public byte byStartChan;
        public byte byAudioChanNum;
        public byte byIPChanNum;
        public byte byZeroChanNum;
        public byte byMainProto;
        public byte bySubProto;
        public byte bySupport;
        public byte bySupport1;
        public byte bySupport2;
        public ushort wDevType;
        public byte bySupport3;
        public byte byMultiStreamProto;
        public byte byStartDChan;
        public byte byStartDTalkChan;
        public byte byHighDChanNum;
        public byte bySupport4;
        public byte byLanguageType;
        public byte byVoiceInChanNum;
        public byte byStartVoiceInChanNo;
        public byte bySupport5;
        public byte bySupport6;
        public byte byMirrorChanNum;
        public ushort wStartMirrorChanNo;
        public byte bySupport7;
        public byte byRes2;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NET_DVR_DEVICEINFO_V40
    {
        public NET_DVR_DEVICEINFO_V30 struDeviceV30;
        public byte bySupportLock;
        public byte byRetryLoginTime;
        public byte byPasswordLevel;
        public byte byProxyType;
        public uint dwSurplusLockTime;
        public byte byCharEncodeType;
        public byte bySupportDev5;
        public byte bySupport;
        public byte byLoginMode;
        public uint dwOEMCode;
        public int iResidualValidity;
        public byte byResidualValidity;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 243)]
        public byte[] byRes2;
    }

    public delegate void LoginResultCallBack(int lUserID, uint dwResult, ref NET_DVR_DEVICEINFO_V30 lpDeviceInfo, IntPtr pUser);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct NET_DVR_USER_LOGIN_INFO
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = NET_DVR_DEV_ADDRESS_MAX_LEN)]
        public string sDeviceAddress;
        public byte byUseTransport;
        public ushort wPort;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = NET_DVR_LOGIN_USERNAME_MAX_LEN)]
        public string sUserName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = NET_DVR_LOGIN_PASSWD_MAX_LEN)]
        public string sPassword;
        public LoginResultCallBack? cbLoginResult;
        public IntPtr pUser;
        public bool bUseAsynLogin;
        public byte byProxyType;
        public byte byUseUTCTime;
        public byte byLoginMode;
        public byte byHttps;
        public int iProxyID;
        public byte byVerifyMode;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 119, ArraySubType = UnmanagedType.I1)]
        public byte[] byRes3;
    }

    public delegate void RemoteConfigCallback(uint dwType, IntPtr lpBuffer, uint dwBufLen, IntPtr pUserData);

    // Lets us issue ISAPI-style requests (e.g. "PUT /ISAPI/AccessControl/UserInfo/Record?format=json")
    // over the already-authenticated NET_DVR session, so employee create/read/delete don't need a
    // separate HTTP digest-auth client.
    // Matches tagNET_DVR_XML_CONFIG_INPUT in HCNetSDK.h exactly. Note: the request/in buffers
    // live here, but the OUT buffer and STATUS buffer are NOT part of this struct — they belong
    // to NET_DVR_XML_CONFIG_OUTPUT below. (Previous version of this struct incorrectly folded
    // lpOutBuffer/dwOutBufferSize/lpStatusBuffer/dwStatusSize into INPUT, which desynced every
    // field after them and made the SDK reject the whole call with NET_DVR_PARAMETER_ERROR.)
    [StructLayout(LayoutKind.Sequential)]
    public struct NET_DVR_XML_CONFIG_INPUT
    {
        public uint dwSize;
        public IntPtr lpRequestUrl;
        public uint dwRequestUrlLen;
        public IntPtr lpInBuffer;
        public uint dwInBufferSize;
        public uint dwRecvTimeOut;
        public byte byForceEncrpt;
        public byte byNumOfMultiPart;
        public byte byMIMEType;
        public byte byRes1;
        public uint dwSendTimeOut;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 24)]
        public byte[] byRes;

        public void Init() => byRes = new byte[24];
    }

    // Matches tagNET_DVR_XML_CONFIG_OUTPUT in HCNetSDK.h exactly (64-bit layout — lpDataBuffer
    // is a pointer with no extra padding bytes on win64/posix64 builds).
    [StructLayout(LayoutKind.Sequential)]
    public struct NET_DVR_XML_CONFIG_OUTPUT
    {
        public uint dwSize;
        public IntPtr lpOutBuffer;
        public uint dwOutBufferSize;
        public uint dwReturnSize; // dwReturnedXMLSize
        public IntPtr lpStatusBuffer;
        public uint dwStatusSize;
        public IntPtr lpDataBuffer;
        public byte byNumOfMultiPart;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 23)]
        public byte[] byRes;

        public void Init() => byRes = new byte[23];
    }

    [DllImport(Dll)] public static extern bool NET_DVR_Init();
    [DllImport(Dll)] public static extern bool NET_DVR_Cleanup();
    [DllImport(Dll)] public static extern uint NET_DVR_GetLastError();
    [DllImport(Dll)] public static extern bool NET_DVR_Logout_V30(int lUserID);
    [DllImport(Dll)] public static extern bool NET_DVR_SetLogToFile(int bLogEnable, string strLogDir, bool bAutoDel);

    [DllImport(Dll)]
    public static extern int NET_DVR_Login_V40(ref NET_DVR_USER_LOGIN_INFO pLoginInfo, ref NET_DVR_DEVICEINFO_V40 lpDeviceInfo);

    [DllImport(Dll)]
    public static extern int NET_DVR_StartRemoteConfig(int lUserID, int dwCommand, IntPtr lpInBuffer, int dwInBufferLen, RemoteConfigCallback? cbStateCallback, IntPtr pUserData);

    [DllImport(Dll)]
    public static extern int NET_DVR_GetNextRemoteConfig(int lHandle, ref NET_DVR_ACS_EVENT_CFG lpOutBuff, int dwOutBuffSize);

    [DllImport(Dll)]
    public static extern bool NET_DVR_StopRemoteConfig(int lHandle);

    [DllImport(Dll)]
    public static extern bool NET_DVR_STDXMLConfig(int lUserID, ref NET_DVR_XML_CONFIG_INPUT lpInputParam, ref NET_DVR_XML_CONFIG_OUTPUT lpOutputParam);
}