using System;
using System.Collections.Generic;
using System.IO;

namespace DriveInsight.Services;

public static class SystemPathExclusions
{
    private static readonly HashSet<string> ProtectedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bootmgr",
        "BOOTNXT",
        "bootsect.bak",
        "DumpStack.log",
        "DumpStack.log.tmp",
        "hiberfil.sys",
        "MEMORY.DMP",
        "ntdetect.com",
        "ntldr",
        "pagefile.sys",
        "swapfile.sys"
    };

    private static readonly HashSet<string> ProtectedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "$SysReset",
        "$WinREAgent",
        "$Windows.~BT",
        "$Windows.~WS",
        "Config.Msi",
        "Recovery",
        "System Volume Information",
        "Windows"
    };

    public static bool ShouldExcludeFile(string filePath, StorageScanMode mode = StorageScanMode.Normal)
    {
        try
        {
            return ShouldExcludeFile(new FileInfo(filePath), mode);
        }
        catch
        {
            return true;
        }
    }

    public static bool ShouldExcludeFile(FileInfo file, StorageScanMode mode = StorageScanMode.Normal)
    {
        try
        {
            if (ProtectedFileNames.Contains(file.Name))
            {
                return true;
            }

            return mode == StorageScanMode.Normal &&
                   (HasProtectedFileAttributes(file.Attributes) ||
                    ContainsProtectedDirectorySegment(file.FullName));
        }
        catch
        {
            return true;
        }
    }

    public static bool ShouldExcludeDirectory(string directoryPath, StorageScanMode mode = StorageScanMode.Normal)
    {
        try
        {
            return ShouldExcludeDirectory(new DirectoryInfo(directoryPath), mode);
        }
        catch
        {
            return true;
        }
    }

    public static bool ShouldExcludeDirectory(DirectoryInfo directory, StorageScanMode mode = StorageScanMode.Normal)
    {
        try
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }

            return mode == StorageScanMode.Normal && ProtectedDirectoryNames.Contains(directory.Name);
        }
        catch
        {
            return true;
        }
    }

    private static bool HasProtectedFileAttributes(FileAttributes attributes)
    {
        return (attributes & FileAttributes.System) != 0;
    }

    private static bool ContainsProtectedDirectorySegment(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath);
        var remainingPath = string.IsNullOrEmpty(root) ? fullPath : fullPath[root.Length..];
        var segments = remainingPath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            if (ProtectedDirectoryNames.Contains(segment))
            {
                return true;
            }
        }

        return false;
    }
}
