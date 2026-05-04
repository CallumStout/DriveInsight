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

    public static bool ShouldExcludeFile(string filePath)
    {
        try
        {
            return ShouldExcludeFile(new FileInfo(filePath));
        }
        catch
        {
            return true;
        }
    }

    public static bool ShouldExcludeFile(FileInfo file)
    {
        try
        {
            return ProtectedFileNames.Contains(file.Name) ||
                   HasProtectedFileAttributes(file.Attributes) ||
                   ContainsProtectedDirectorySegment(file.FullName);
        }
        catch
        {
            return true;
        }
    }

    public static bool ShouldExcludeDirectory(string directoryPath)
    {
        try
        {
            return ShouldExcludeDirectory(new DirectoryInfo(directoryPath));
        }
        catch
        {
            return true;
        }
    }

    public static bool ShouldExcludeDirectory(DirectoryInfo directory)
    {
        try
        {
            return ProtectedDirectoryNames.Contains(directory.Name) ||
                   (directory.Attributes & FileAttributes.ReparsePoint) != 0;
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
