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
