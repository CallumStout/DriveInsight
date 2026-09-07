using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DriveInsight.Services;

internal static class WindowsBackupPrivilege
{
    // Called only in the short-lived elevated scan helper. No ownership or ACL changes.
    public static bool TryEnable()
    {
        if (!OperatingSystem.IsWindows() ||
            !OpenProcessToken(GetCurrentProcess(), 0x20 | 0x8, out var token)) return false;
        using (token)
        {
            if (!LookupPrivilegeValueW(null, "SeBackupPrivilege", out var luid)) return false;
            var privileges = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 };
            return AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero) &&
                Marshal.GetLastWin32Error() == 0; // Success can still mean NOT_ALL_ASSIGNED.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges { public uint Count; public Luid Luid; public uint Attributes; }
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValueW(string? system, string name, out Luid luid);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token,
        [MarshalAs(UnmanagedType.Bool)] bool disableAll, ref TokenPrivileges state, uint length,
        IntPtr previous, IntPtr required);
}
