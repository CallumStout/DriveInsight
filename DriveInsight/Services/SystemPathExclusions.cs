using System;
using System.IO;

namespace DriveInsight.Services;

public static class SystemPathExclusions
{
    public static bool ShouldExcludeFile(string filePath, StorageScanMode mode = StorageScanMode.Normal)
    {
        return false;
    }

    public static bool ShouldExcludeFile(FileInfo file, StorageScanMode mode = StorageScanMode.Normal)
    {
        return false;
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

            return false;
        }
        catch
        {
            return true;
        }
    }

}
