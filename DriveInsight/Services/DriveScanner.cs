using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DriveInsight.Models;

namespace DriveInsight.Services;

public sealed class DriveScanner
{
    public IEnumerable<DriveInfo> GetReadyDrives() => DriveInfo.GetDrives().Where(d => d.IsReady);

    public async Task<List<FolderStat>> GetTopFoldersAsync(string rootPath, int top = 20, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var result = new List<FolderStat>();
            var root = new DirectoryInfo(rootPath);

            foreach (var dir in root.EnumerateDirectories())
            {
                ct.ThrowIfCancellationRequested();
                long size = SafeDirSize(dir);
                result.Add(new FolderStat
                {
                    Name = dir.Name,
                    FullPath = dir.FullName,
                    Bytes = size
                });
            }

            return result
                .OrderByDescending(x => x.Bytes)
                .Take(top)
                .ToList();
        }, ct);
    }

    public async Task<List<FileSystemEntry>> GetImmediateChildrenAsync(string folderPath, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var result = new List<FileSystemEntry>();
            var dir = new DirectoryInfo(folderPath);

            try
            {
                foreach (var childDir in dir.EnumerateDirectories())
                {
                    ct.ThrowIfCancellationRequested();
                    result.Add(new FileSystemEntry
                    {
                        Name = childDir.Name,
                        FullPath = childDir.FullName,
                        IsFolder = true
                    });
                }
            }
            catch { }

            try
            {
                foreach (var childFile in dir.EnumerateFiles())
                {
                    ct.ThrowIfCancellationRequested();
                    long size = 0;
                    try { size = childFile.Length; } catch { }

                    result.Add(new FileSystemEntry
                    {
                        Name = childFile.Name,
                        FullPath = childFile.FullName,
                        Bytes = size,
                        IsFolder = false
                    });
                }
            }
            catch { }

            return result
                .OrderByDescending(x => x.IsFolder)
                .ThenBy(x => x.Name)
                .ToList();
        }, ct);
    }

    public async Task<long> GetFolderSizeAsync(string folderPath, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var dir = new DirectoryInfo(folderPath);
            return SafeDirSize(dir);
        }, ct);
    }

    private static long SafeDirSize(DirectoryInfo dir)
    {
        long total = 0;
        try
        {
            foreach (var f in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try { total += f.Length; } catch { }
            }
        }
        catch { }

        return total;
    }
}
