using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DriveInsight.Services;

public sealed class CleanupRemovalService
{
    public async Task DeleteAsync(IEnumerable<CleanupCandidate> candidates, CancellationToken ct = default)
    {
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            await DeletePathAsync(candidate.FullPath, ct);
        }
    }

    public async Task DeletePathAsync(string path, CancellationToken ct = default)
    {
        var targetPath = Path.GetFullPath(path);
        if (!IsAllowedCleanupTarget(targetPath) || !File.Exists(targetPath) && !Directory.Exists(targetPath))
        {
            return;
        }

        try
        {
            await Task.Run(() => DeletePath(targetPath, ct), ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            await DeleteWithElevationAsync(targetPath, ct);
        }
    }

    private static void DeletePath(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (File.Exists(path))
        {
            var file = new FileInfo(path);
            ClearAttributes(file);
            file.Delete();
            return;
        }

        if (!Directory.Exists(path))
        {
            return;
        }

        DeleteDirectory(new DirectoryInfo(path), ct);
    }

    private static void DeleteDirectory(DirectoryInfo directory, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (IsReparsePoint(directory))
        {
            return;
        }

        foreach (var childDirectory in SafeEnumerateDirectories(directory.FullName))
        {
            DeleteDirectory(new DirectoryInfo(childDirectory), ct);
        }

        foreach (var filePath in SafeEnumerateFiles(directory.FullName))
        {
            ct.ThrowIfCancellationRequested();
            var file = new FileInfo(filePath);
            ClearAttributes(file);
            file.Delete();
        }

        ClearAttributes(directory);
        directory.Delete();
    }

    private static async Task DeleteWithElevationAsync(string targetPath, CancellationToken ct)
    {
        var escapedPath = targetPath.Replace("'", "''");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Remove-Item -LiteralPath '{escapedPath}' -Recurse -Force -ErrorAction Stop\"",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        });

        if (process is not null)
        {
            await process.WaitForExitAsync(ct);
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string folderPath)
    {
        try
        {
            return Directory.EnumerateDirectories(folderPath);
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string folderPath)
    {
        try
        {
            return Directory.EnumerateFiles(folderPath);
        }
        catch
        {
            return [];
        }
    }

    private static void ClearAttributes(FileSystemInfo item)
    {
        try
        {
            item.Attributes &= ~(FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System);
        }
        catch
        {
        }
    }

    private static bool IsReparsePoint(FileSystemInfo item)
    {
        try
        {
            return (item.Attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return true;
        }
    }

    private static bool IsAllowedCleanupTarget(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root) || string.Equals(path.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var name = new DirectoryInfo(path).Name;
        if (string.Equals(name, "Windows", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Program Files", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Program Files (x86)", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Users", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}
