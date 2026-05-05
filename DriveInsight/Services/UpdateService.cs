using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace DriveInsight.Services;

public enum UpdateCheckResult
{
    UpdateStarted,
    Cancelled,
    Failed
}

public sealed class AvailableUpdate(UpdateManager manager, UpdateInfo update)
{
    public UpdateManager Manager { get; } = manager;
    public UpdateInfo Update { get; } = update;
}

public static class UpdateService
{
    private const string RepositoryUrl = "https://github.com/CallumStout/DriveInsight";

    public static async Task<AvailableUpdate?> CheckForAvailableUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var manager = CreateUpdateManager();

            if (!manager.IsInstalled)
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var update = await manager.CheckForUpdatesAsync();
            return update is null ? null : new AvailableUpdate(manager, update);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update check failed: {ex}");
            return null;
        }
    }

    public static async Task<UpdateCheckResult> DownloadUpdateAndRestartAsync(
        AvailableUpdate availableUpdate,
        Func<Task<bool>> confirmUpdateAsync,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var shouldUpdate = await confirmUpdateAsync();
            if (!shouldUpdate)
            {
                return UpdateCheckResult.Cancelled;
            }

            cancellationToken.ThrowIfCancellationRequested();

            await availableUpdate.Manager.DownloadUpdatesAsync(availableUpdate.Update);
            availableUpdate.Manager.ApplyUpdatesAndRestart(availableUpdate.Update);
            return UpdateCheckResult.UpdateStarted;
        }
        catch (OperationCanceledException)
        {
            return UpdateCheckResult.Cancelled;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update check failed: {ex}");
            return UpdateCheckResult.Failed;
        }
    }

    private static UpdateManager CreateUpdateManager()
    {
        var source = new GithubSource(
            RepositoryUrl,
            accessToken: null,
            prerelease: false);

        return new UpdateManager(source);
    }
}
