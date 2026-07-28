using System.Runtime.InteropServices;

namespace SapeagleAttendanceConnector.SBXPC;

internal static class SBXPCNative
{
    private const string Dll64 = "SBXPCDLL64.dll";

    [DllImport(Dll64, EntryPoint = "_ConnectTcpip", CallingConvention = CallingConvention.Winapi)]
    static extern byte _ConnectTcpip_64(int machineNo, ref IntPtr ip, int port, int password);

    public static bool ConnectTcpip(int machineNo, string ip, int port, int password)
    {
        IntPtr p = Marshal.StringToBSTR(ip);
        try { return _ConnectTcpip_64(machineNo, ref p, port, password) > 0; }
        finally { Marshal.FreeBSTR(p); }
    }

    [DllImport(Dll64, EntryPoint = "_Disconnect", CallingConvention = CallingConvention.Winapi)]
    public static extern void Disconnect(int machineNo);

    [DllImport(Dll64, EntryPoint = "_ReadGeneralLogData", CallingConvention = CallingConvention.Winapi)]
    static extern byte _ReadGeneralLogData_64(int machineNo, byte readMark);

    public static bool ReadGeneralLogData(int machineNo, byte readMark = 1)
        => _ReadGeneralLogData_64(machineNo, readMark) > 0;

    [DllImport(Dll64, EntryPoint = "_ReadAllGLogData", CallingConvention = CallingConvention.Winapi)]
    static extern byte _ReadAllGLogData_64(int machineNo);

    public static bool ReadAllGLogData(int machineNo)
        => _ReadAllGLogData_64(machineNo) > 0;

    [DllImport(Dll64, EntryPoint = "_GetGeneralLogData", CallingConvention = CallingConvention.Winapi)]
    static extern byte _GetGeneralLogData_64(int machineNo, IntPtr tMc, IntPtr enroll, IntPtr eMc,
        IntPtr verify, IntPtr yr, IntPtr mo, IntPtr dy, IntPtr hr, IntPtr mi, IntPtr sc);

    public static bool GetGeneralLogData(int machineNo, out int enrollNumber, out int verifyMode,
        out int year, out int month, out int day, out int hour, out int minute)
    {
        enrollNumber = verifyMode = year = month = day = hour = minute = 0;

        byte[] bTMc = new byte[4], bEnroll = new byte[4], bEMc = new byte[4], bVerify = new byte[4];
        byte[] bYear = new byte[4], bMonth = new byte[4], bDay = new byte[4];
        byte[] bHour = new byte[4], bMinute = new byte[4], bSecond = new byte[4];

        GCHandle hTMc = GCHandle.Alloc(bTMc, GCHandleType.Pinned);
        GCHandle hEnroll = GCHandle.Alloc(bEnroll, GCHandleType.Pinned);
        GCHandle hEMc = GCHandle.Alloc(bEMc, GCHandleType.Pinned);
        GCHandle hVerify = GCHandle.Alloc(bVerify, GCHandleType.Pinned);
        GCHandle hYear = GCHandle.Alloc(bYear, GCHandleType.Pinned);
        GCHandle hMonth = GCHandle.Alloc(bMonth, GCHandleType.Pinned);
        GCHandle hDay = GCHandle.Alloc(bDay, GCHandleType.Pinned);
        GCHandle hHour = GCHandle.Alloc(bHour, GCHandleType.Pinned);
        GCHandle hMinute = GCHandle.Alloc(bMinute, GCHandleType.Pinned);
        GCHandle hSecond = GCHandle.Alloc(bSecond, GCHandleType.Pinned);

        try
        {
            byte ret = _GetGeneralLogData_64(machineNo,
                hTMc.AddrOfPinnedObject(), hEnroll.AddrOfPinnedObject(), hEMc.AddrOfPinnedObject(),
                hVerify.AddrOfPinnedObject(), hYear.AddrOfPinnedObject(), hMonth.AddrOfPinnedObject(),
                hDay.AddrOfPinnedObject(), hHour.AddrOfPinnedObject(), hMinute.AddrOfPinnedObject(),
                hSecond.AddrOfPinnedObject());

            if (ret > 0)
            {
                enrollNumber = BitConverter.ToInt32(bEnroll, 0);
                verifyMode = BitConverter.ToInt32(bVerify, 0);
                year = BitConverter.ToInt32(bYear, 0);
                month = BitConverter.ToInt32(bMonth, 0);
                day = BitConverter.ToInt32(bDay, 0);
                hour = BitConverter.ToInt32(bHour, 0);
                minute = BitConverter.ToInt32(bMinute, 0);
            }

            return ret > 0;
        }
        finally
        {
            hTMc.Free(); hEnroll.Free(); hEMc.Free(); hVerify.Free();
            hYear.Free(); hMonth.Free(); hDay.Free(); hHour.Free(); hMinute.Free(); hSecond.Free();
        }
    }

    [DllImport(Dll64, EntryPoint = "_GetAllGLogData", CallingConvention = CallingConvention.Winapi)]
    static extern byte _GetAllGLogData_64(int machineNo, IntPtr tMc, IntPtr enroll, IntPtr eMc,
        IntPtr verify, IntPtr yr, IntPtr mo, IntPtr dy, IntPtr hr, IntPtr mi, IntPtr sc);

    public static bool GetAllGLogData(int machineNo, out int enrollNumber, out int verifyMode,
        out int year, out int month, out int day, out int hour, out int minute)
    {
        enrollNumber = verifyMode = year = month = day = hour = minute = 0;

        byte[] bTMc = new byte[4], bEnroll = new byte[4], bEMc = new byte[4], bVerify = new byte[4];
        byte[] bYear = new byte[4], bMonth = new byte[4], bDay = new byte[4];
        byte[] bHour = new byte[4], bMinute = new byte[4], bSecond = new byte[4];

        GCHandle hTMc = GCHandle.Alloc(bTMc, GCHandleType.Pinned);
        GCHandle hEnroll = GCHandle.Alloc(bEnroll, GCHandleType.Pinned);
        GCHandle hEMc = GCHandle.Alloc(bEMc, GCHandleType.Pinned);
        GCHandle hVerify = GCHandle.Alloc(bVerify, GCHandleType.Pinned);
        GCHandle hYear = GCHandle.Alloc(bYear, GCHandleType.Pinned);
        GCHandle hMonth = GCHandle.Alloc(bMonth, GCHandleType.Pinned);
        GCHandle hDay = GCHandle.Alloc(bDay, GCHandleType.Pinned);
        GCHandle hHour = GCHandle.Alloc(bHour, GCHandleType.Pinned);
        GCHandle hMinute = GCHandle.Alloc(bMinute, GCHandleType.Pinned);
        GCHandle hSecond = GCHandle.Alloc(bSecond, GCHandleType.Pinned);

        try
        {
            byte ret = _GetAllGLogData_64(machineNo,
                hTMc.AddrOfPinnedObject(), hEnroll.AddrOfPinnedObject(), hEMc.AddrOfPinnedObject(),
                hVerify.AddrOfPinnedObject(), hYear.AddrOfPinnedObject(), hMonth.AddrOfPinnedObject(),
                hDay.AddrOfPinnedObject(), hHour.AddrOfPinnedObject(), hMinute.AddrOfPinnedObject(),
                hSecond.AddrOfPinnedObject());

            if (ret > 0)
            {
                enrollNumber = BitConverter.ToInt32(bEnroll, 0);
                verifyMode = BitConverter.ToInt32(bVerify, 0);
                year = BitConverter.ToInt32(bYear, 0);
                month = BitConverter.ToInt32(bMonth, 0);
                day = BitConverter.ToInt32(bDay, 0);
                hour = BitConverter.ToInt32(bHour, 0);
                minute = BitConverter.ToInt32(bMinute, 0);
            }

            return ret > 0;
        }
        finally
        {
            hTMc.Free(); hEnroll.Free(); hEMc.Free(); hVerify.Free();
            hYear.Free(); hMonth.Free(); hDay.Free(); hHour.Free(); hMinute.Free(); hSecond.Free();
        }
    }

    [DllImport(Dll64, EntryPoint = "_ReadAllUserID", CallingConvention = CallingConvention.Winapi)]
    static extern byte _ReadAllUserID_64(int machineNo);

    public static bool ReadAllUserID(int machineNo) => _ReadAllUserID_64(machineNo) > 0;

    [DllImport(Dll64, EntryPoint = "_GetAllUserID", CallingConvention = CallingConvention.Winapi)]
    static extern byte _GetAllUserID_64(int machineNo, IntPtr enroll, IntPtr eMc, IntPtr backup, IntPtr privilege, IntPtr enable);

    public static bool GetAllUserID(int machineNo, out int enrollNumber)
    {
        enrollNumber = 0;

        byte[] bEnroll = new byte[4], bEMc = new byte[4], bBackup = new byte[4];
        byte[] bPrivilege = new byte[4], bEnable = new byte[4];

        GCHandle hEnroll = GCHandle.Alloc(bEnroll, GCHandleType.Pinned);
        GCHandle hEMc = GCHandle.Alloc(bEMc, GCHandleType.Pinned);
        GCHandle hBackup = GCHandle.Alloc(bBackup, GCHandleType.Pinned);
        GCHandle hPrivilege = GCHandle.Alloc(bPrivilege, GCHandleType.Pinned);
        GCHandle hEnable = GCHandle.Alloc(bEnable, GCHandleType.Pinned);

        try
        {
            byte ret = _GetAllUserID_64(machineNo,
                hEnroll.AddrOfPinnedObject(), hEMc.AddrOfPinnedObject(), hBackup.AddrOfPinnedObject(),
                hPrivilege.AddrOfPinnedObject(), hEnable.AddrOfPinnedObject());

            if (ret > 0) enrollNumber = BitConverter.ToInt32(bEnroll, 0);
            return ret > 0;
        }
        finally
        {
            hEnroll.Free(); hEMc.Free(); hBackup.Free(); hPrivilege.Free(); hEnable.Free();
        }
    }

    [DllImport(Dll64, EntryPoint = "_SetEnrollData", CallingConvention = CallingConvention.Winapi)]
    static extern byte _SetEnrollData_64(int machineNo, int enrollNumber, int eMachineNumber, int backupNumber,
        int privilege, ref IntPtr enrollData, int password);

    public static bool SetEnrollData(int machineNo, int enrollNumber, int privilege = 0)
    {
        byte[] emptyTemplate = new byte[512];
        IntPtr p = Marshal.AllocHGlobal(emptyTemplate.Length);
        Marshal.Copy(emptyTemplate, 0, p, emptyTemplate.Length);
        try
        {
            return _SetEnrollData_64(machineNo, enrollNumber, machineNo, 0, privilege, ref p, 0) > 0;
        }
        finally { Marshal.FreeHGlobal(p); }
    }

    [DllImport(Dll64, EntryPoint = "_SetUserName1", CallingConvention = CallingConvention.Winapi)]
    static extern byte _SetUserName1_64(int machineNo, int enrollNumber, ref IntPtr userName);

    public static bool SetUserName1(int machineNo, int enrollNumber, string userName)
    {
        if (string.IsNullOrEmpty(userName)) return false;
        IntPtr p = Marshal.StringToHGlobalUni(userName);
        try { return _SetUserName1_64(machineNo, enrollNumber, ref p) > 0; }
        finally { Marshal.FreeHGlobal(p); }
    }

    [DllImport(Dll64, EntryPoint = "_GetUserName1", CallingConvention = CallingConvention.Winapi)]
    static extern byte _GetUserName1_64(int machineNo, int enrollNumber, ref IntPtr userName);

    public static string GetUserName1(int machineNo, int enrollNumber)
    {
        IntPtr p = IntPtr.Zero;
        try
        {
            byte ret = _GetUserName1_64(machineNo, enrollNumber, ref p);
            return ret > 0 && p != IntPtr.Zero ? Marshal.PtrToStringBSTR(p) : "";
        }
        catch
        {
            return "";
        }
        finally
        {
            if (p != IntPtr.Zero) Marshal.FreeBSTR(p);
        }
    }

    [DllImport(Dll64, EntryPoint = "_DeleteEnrollData", CallingConvention = CallingConvention.Winapi)]
    static extern byte _DeleteEnrollData_64(int machineNo, int enrollNumber, int eMachineNumber, int backupNumber);

    public static bool DeleteEnrollData(int machineNo, int enrollNumber)
    {
        bool anyDeleted = false;
        for (int backup = 0; backup < 10; backup++)
            if (_DeleteEnrollData_64(machineNo, enrollNumber, machineNo, backup) > 0)
                anyDeleted = true;
        return anyDeleted;
    }
}