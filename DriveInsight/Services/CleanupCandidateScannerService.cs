using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DriveInsight.Services;

public sealed class CleanupCandidateScannerService
{
    private const long LargeReviewFileThreshold = 100L * 1024L * 1024L;
    private const int MaxDriveScanDirectories = 3000;
    private const int MaxDriveReviewCandidates = 120;

    private static readonly HashSet<string> InstallerAndArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".7z",
        ".bak",
        ".dmg",
        ".exe",
        ".iso",
        ".log",
        ".msi",
        ".rar",
        ".zip"
    };

    private static readonly HashSet<string> LargeMediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".avi",
        ".mkv",
        ".mov",
        ".mp4",
        ".psd",
        ".wav"
    };

    private static readonly HashSet<string> SkippedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "$Recycle.Bin",
        ".git",
        "Program Files",
        "Program Files (x86)",
        "ProgramData",
        "System Volume Information",
        "Windows",
        "Windows.old"
    };

    private static readonly HashSet<string> RepositoryBuildDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".gradle",
        ".next",
        ".nuxt",
        ".pytest_cache",
        ".vs",
        "__pycache__",
        "artifacts",
        "bin",
        "build",
        "coverage",
        "dist",
        "obj",
        "out",
        "target",
        "TestResults"
    };

    public async Task<IReadOnlyList<CleanupCandidate>> ScanAsync(string driveName, CancellationToken ct = default)
    {
        var driveRoot = ResolveDriveRoot(driveName);
        if (driveRoot is null)
        {
            return [];
        }

        return await Task.Run(() =>
        {
            var candidates = new List<CleanupCandidate>();
            AddWindowsOldCandidate(candidates, driveRoot, ct);
            AddRecycleBinCandidates(candidates, driveRoot, ct);
            AddTempCandidates(candidates, driveRoot, ct);
            AddCommonCacheCandidates(candidates, driveRoot, ct);
            AddRepositoryBuildCandidates(candidates, driveRoot, ct);
            AddUserReviewCandidates(candidates, driveRoot, ct);
            AddDriveReviewCandidates(candidates, driveRoot, ct);

            return candidates
                .GroupBy(candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Where(candidate => candidate.SizeBytes > 0)
                .OrderBy(candidate => candidate.IsSelectedByDefault ? 0 : 1)
                .ThenByDescending(candidate => candidate.SizeBytes)
                .Take(120)
                .ToList();
        }, ct);
    }

    private static void AddWindowsOldCandidate(List<CleanupCandidate> candidates, string driveRoot, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var windowsOldPath = Path.Combine(driveRoot, "Windows.old");
        if (!Directory.Exists(windowsOldPath))
        {
            return;
        }

        var size = GetFolderSize(windowsOldPath, ct);
        if (size <= 0)
        {
            return;
        }

        candidates.Add(new CleanupCandidate
        {
            Name = "Windows.old",
            FullPath = windowsOldPath,
            Reason = "Previous Windows installation files",
            Risk = "Low",
            SizeBytes = size,
            IsDirectory = true,
            IsSelectedByDefault = true
        });
    }

    private static void AddRecycleBinCandidates(List<CleanupCandidate> candidates, string driveRoot, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var recycleBinPath = Path.Combine(driveRoot, "$Recycle.Bin");
        if (!Directory.Exists(recycleBinPath))
        {
            return;
        }

        foreach (var item in EnumerateImmediateChildren(recycleBinPath).Take(30))
        {
            ct.ThrowIfCancellationRequested();
            AddCandidateForPath(candidates, item, "Recycle Bin contents", "Low", true, ct);
        }
    }

    private static void AddTempCandidates(List<CleanupCandidate> candidates, string driveRoot, CancellationToken ct)
    {
        var tempPaths = new[]
        {
            Path.GetTempPath(),
            Path.Combine(driveRoot, "Windows", "Temp")
        };

        foreach (var tempPath in tempPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();

            if (!IsPathOnDrive(tempPath, driveRoot) || !Directory.Exists(tempPath))
            {
                continue;
            }

            foreach (var item in EnumerateImmediateChildren(tempPath).Take(40))
            {
                ct.ThrowIfCancellationRequested();
                if (!IsOlderThan(item, TimeSpan.FromDays(1)))
                {
                    continue;
                }

                AddCandidateForPath(candidates, item, "Temporary files", "Low", true, ct);
            }
        }
    }

    private static void AddUserReviewCandidates(List<CleanupCandidate> candidates, string driveRoot, CancellationToken ct)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile) || !IsPathOnDrive(userProfile, driveRoot))
        {
            return;
        }

        var reviewFolders = new[]
        {
            Path.Combine(userProfile, "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };

        foreach (var folder in reviewFolders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();

            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var filePath in SafeEnumerateFiles(folder).Take(80))
            {
                ct.ThrowIfCancellationRequested();

                var extension = Path.GetExtension(filePath);
                if (SystemPathExclusions.ShouldExcludeFile(filePath))
                {
                    continue;
                }

                if (!InstallerAndArchiveExtensions.Contains(extension) || !IsOlderThan(filePath, TimeSpan.FromDays(30)))
                {
                    continue;
                }

                var size = GetFileSize(filePath);
                if (size < 50 * 1024 * 1024)
                {
                    continue;
                }

                candidates.Add(new CleanupCandidate
                {
                    Name = Path.GetFileName(filePath),
                    FullPath = filePath,
                    Reason = ResolveUserReviewReason(extension),
                    Risk = "Review",
                    SizeBytes = size,
                    IsDirectory = false,
                    IsSelectedByDefault = false
                });
            }
        }
    }

    private static void AddCommonCacheCandidates(List<CleanupCandidate> candidates, string driveRoot, CancellationToken ct)
    {
        foreach (var directory in EnumerateDirectoriesBounded(driveRoot, MaxDriveScanDirectories, ct))
        {
            ct.ThrowIfCancellationRequested();

            var name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!IsCacheDirectoryName(name))
            {
                continue;
            }

            AddCandidateForPath(candidates, directory, "Cache folder", "Low", true, ct);
        }
    }

    private static void AddDriveReviewCandidates(List<CleanupCandidate> candidates, string driveRoot, CancellationToken ct)
    {
        var added = 0;
        foreach (var filePath in EnumerateFilesBounded(driveRoot, MaxDriveScanDirectories, ct))
        {
            ct.ThrowIfCancellationRequested();
            if (added >= MaxDriveReviewCandidates)
            {
                return;
            }

            var extension = Path.GetExtension(filePath);
            if (SystemPathExclusions.ShouldExcludeFile(filePath))
            {
                continue;
            }

            var size = GetFileSize(filePath);
            if (size < LargeReviewFileThreshold)
            {
                continue;
            }

            var reason = ResolveDriveReviewReason(filePath, extension);
            if (reason is null)
            {
                continue;
            }

            candidates.Add(new CleanupCandidate
            {
                Name = Path.GetFileName(filePath),
                FullPath = filePath,
                Reason = reason,
                Risk = "Review",
                SizeBytes = size,
                IsDirectory = false,
                IsSelectedByDefault = false
            });
            added++;
        }
    }

    private static void AddRepositoryBuildCandidates(List<CleanupCandidate> candidates, string driveRoot, CancellationToken ct)
    {
        foreach (var repositoryPath in EnumerateRepositoryRoots(driveRoot, ct))
        {
            ct.ThrowIfCancellationRequested();

            foreach (var buildDirectory in EnumerateRepositoryBuildDirectories(repositoryPath, ct))
            {
                ct.ThrowIfCancellationRequested();
                AddCandidateForPath(candidates, buildDirectory, "Git repository build output", "Review", false, ct);
            }
        }
    }

    private static void AddCandidateForPath(
        List<CleanupCandidate> candidates,
        string path,
        string reason,
        string risk,
        bool isSelectedByDefault,
        CancellationToken ct)
    {
        try
        {
            var isDirectory = Directory.Exists(path);
            if (isDirectory && SystemPathExclusions.ShouldExcludeDirectory(path))
            {
                return;
            }

            if (!isDirectory && SystemPathExclusions.ShouldExcludeFile(path))
            {
                return;
            }

            var size = isDirectory ? GetFolderSize(path, ct) : GetFileSize(path);
            if (size <= 0)
            {
                return;
            }

            candidates.Add(new CleanupCandidate
            {
                Name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                FullPath = path,
                Reason = reason,
                Risk = risk,
                SizeBytes = size,
                IsDirectory = isDirectory,
                IsSelectedByDefault = isSelectedByDefault
            });
        }
        catch
        {
        }
    }

    private static IEnumerable<string> EnumerateImmediateChildren(string folderPath)
    {
        foreach (var directory in SafeEnumerateDirectories(folderPath))
        {
            yield return directory;
        }

        foreach (var file in SafeEnumerateFiles(folderPath))
        {
            yield return file;
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

    private static IEnumerable<string> EnumerateDirectoriesBounded(string rootPath, int maxDirectories, CancellationToken ct)
    {
        var visited = 0;
        var pending = new Queue<string>();
        pending.Enqueue(rootPath);

        while (pending.Count > 0 && visited < maxDirectories)
        {
            ct.ThrowIfCancellationRequested();
            var current = pending.Dequeue();
            visited++;

            foreach (var directory in SafeEnumerateDirectories(current))
            {
                ct.ThrowIfCancellationRequested();
                if (ShouldSkipDirectory(directory))
                {
                    continue;
                }

                yield return directory;
                pending.Enqueue(directory);
            }
        }
    }

    private static IEnumerable<string> EnumerateFilesBounded(string rootPath, int maxDirectories, CancellationToken ct)
    {
        var visited = 0;
        var pending = new Queue<string>();
        pending.Enqueue(rootPath);

        while (pending.Count > 0 && visited < maxDirectories)
        {
            ct.ThrowIfCancellationRequested();
            var current = pending.Dequeue();
            visited++;

            foreach (var file in SafeEnumerateFiles(current))
            {
                ct.ThrowIfCancellationRequested();
                if (SystemPathExclusions.ShouldExcludeFile(file))
                {
                    continue;
                }

                yield return file;
            }

            foreach (var directory in SafeEnumerateDirectories(current))
            {
                ct.ThrowIfCancellationRequested();
                if (!ShouldSkipDirectory(directory))
                {
                    pending.Enqueue(directory);
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateRepositoryRoots(string driveRoot, CancellationToken ct)
    {
        if (IsRepositoryRoot(driveRoot))
        {
            yield return driveRoot;
        }

        foreach (var directory in EnumerateDirectoriesBounded(driveRoot, MaxDriveScanDirectories, ct))
        {
            ct.ThrowIfCancellationRequested();
            if (IsRepositoryRoot(directory))
            {
                yield return directory;
            }
        }
    }

    private static IEnumerable<string> EnumerateRepositoryBuildDirectories(string repositoryPath, CancellationToken ct)
    {
        var visited = 0;
        var pending = new Queue<string>();
        pending.Enqueue(repositoryPath);

        while (pending.Count > 0 && visited < MaxDriveScanDirectories)
        {
            ct.ThrowIfCancellationRequested();
            var current = pending.Dequeue();
            visited++;

            foreach (var directory in SafeEnumerateDirectories(current))
            {
                ct.ThrowIfCancellationRequested();

                if (ShouldSkipDirectory(directory))
                {
                    continue;
                }

                var directoryName = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (RepositoryBuildDirectoryNames.Contains(directoryName))
                {
                    yield return directory;
                    continue;
                }

                pending.Enqueue(directory);
            }
        }
    }

    private static bool IsOlderThan(string path, TimeSpan age)
    {
        try
        {
            return DateTime.UtcNow - File.GetLastWriteTimeUtc(path) >= age;
        }
        catch
        {
            return false;
        }
    }

    private static long GetFileSize(string filePath)
    {
        try
        {
            return new FileInfo(filePath).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static long GetFolderSize(string folderPath, CancellationToken ct)
    {
        try
        {
            var root = new DirectoryInfo(folderPath);
            if (IsReparsePoint(root))
            {
                return 0;
            }

            long total = 0;
            var pending = new Stack<DirectoryInfo>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var current = pending.Pop();

                foreach (var file in SafeEnumerateFiles(current.FullName))
                {
                    if (SystemPathExclusions.ShouldExcludeFile(file))
                    {
                        continue;
                    }

                    total = checked(total + GetFileSize(file));
                }

                foreach (var childDirectory in SafeEnumerateDirectories(current.FullName))
                {
                    var directory = new DirectoryInfo(childDirectory);
                    if (!ShouldSkipDirectory(childDirectory))
                    {
                        pending.Push(directory);
                    }
                }
            }

            return total;
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsReparsePoint(DirectoryInfo directory)
    {
        try
        {
            return (directory.Attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return true;
        }
    }

    private static bool IsPathOnDrive(string path, string driveRoot)
    {
        try
        {
            var pathRoot = Path.GetPathRoot(Path.GetFullPath(path));
            return string.Equals(pathRoot, driveRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveDriveRoot(string driveName)
    {
        if (string.IsNullOrWhiteSpace(driveName))
        {
            return null;
        }

        var normalized = driveName.Trim();
        if (!normalized.EndsWith(Path.DirectorySeparatorChar))
        {
            normalized += Path.DirectorySeparatorChar;
        }

        try
        {
            return Path.GetPathRoot(Path.GetFullPath(normalized));
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveUserReviewReason(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".log" => "Large old log file",
            ".bak" => "Old backup file",
            ".exe" or ".msi" => "Old installer",
            _ => "Old archive or disk image"
        };
    }

    private static string? ResolveDriveReviewReason(string filePath, string extension)
    {
        if (InstallerAndArchiveExtensions.Contains(extension))
        {
            return ResolveUserReviewReason(extension);
        }

        if (LargeMediaExtensions.Contains(extension))
        {
            return "Large media or project file";
        }

        var fileName = Path.GetFileName(filePath);
        if (fileName.Contains("backup", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("copy", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("old", StringComparison.OrdinalIgnoreCase))
        {
            return "Possibly redundant large file";
        }

        return null;
    }

    private static bool IsCacheDirectoryName(string directoryName)
    {
        return directoryName.Equals("cache", StringComparison.OrdinalIgnoreCase) ||
               directoryName.Equals("caches", StringComparison.OrdinalIgnoreCase) ||
               directoryName.Equals("temp", StringComparison.OrdinalIgnoreCase) ||
               directoryName.Equals("tmp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldSkipDirectory(string directoryPath)
    {
        try
        {
            var directory = new DirectoryInfo(directoryPath);
            return SkippedDirectoryNames.Contains(directory.Name) ||
                   SystemPathExclusions.ShouldExcludeDirectory(directory);
        }
        catch
        {
            return true;
        }
    }

    private static bool IsRepositoryRoot(string directoryPath)
    {
        try
        {
            return Directory.Exists(Path.Combine(directoryPath, ".git"));
        }
        catch
        {
            return false;
        }
    }
}
