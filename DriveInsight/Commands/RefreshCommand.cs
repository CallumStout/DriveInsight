using CommunityToolkit.Mvvm.Input;
using DriveInsight.ViewModels;

namespace DriveInsight.Commands;

public static class RefreshCommand
{
    public static IAsyncRelayCommand Create(DashboardPaneViewModel dashboard)
    {
        return new AsyncRelayCommand(
            async () =>
            {
                if (dashboard.IsRefreshing)
                {
                    return;
                }

                try
                {
                    dashboard.IsRefreshing = true;
                    dashboard.LoadDriveCapacity();
                    await dashboard.LoadBiggestFilesAndInsightsAsync();
                    await dashboard.RefreshLinkedPanesAsync();
                }
                finally
                {
                    dashboard.IsRefreshing = false;
                }
            },
            () => !dashboard.IsRefreshing);
    }
}
