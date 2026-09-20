using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveInsight.Services;
using DriveInsight.Utilities;

namespace DriveInsight.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public ObservableCollection<PaneItemViewModel> Panes { get; }

    [ObservableProperty]
    private PaneItemViewModel? selectedPane;

    public MainWindowViewModel(
        IConfirmationDialogService? confirmationDialog = null,
        ICleanupReviewDialogService? cleanupReviewDialog = null,
        IExportService? exportService = null)
    {
        var nullDialog = new NullDialogService();
        confirmationDialog ??= nullDialog;
        cleanupReviewDialog ??= nullDialog;
        exportService ??= new NullExportService();
        var drivesPaneContent = new DrivesPaneViewModel();
        var storagePaneContent = new StorageBreakdownPaneViewModel();
        DashboardPaneViewModel? dashboardPaneContent = null;

        var dashboardPane = new PaneItemViewModel
        {
            Id = "dashboard",
            Title = "Dashboard",
            IconKey = "Home",
            IconPathData = AppIcons.Dashboard,
            Content = dashboardPaneContent = new DashboardPaneViewModel(
                () => drivesPaneContent.RefreshAvailableDrivesAsync(),
                confirmationDialog,
                cleanupReviewDialog)
        };
        
        var drivesPane = new PaneItemViewModel
        {
            Id = "drives",
            Title = "Drives",
            IconKey = "Drive",
            IconPathData = AppIcons.Drive,
            Content = drivesPaneContent
        };

        var storageBreakdownPane = new PaneItemViewModel
        {
            Id = "breakdown",
            Title = "Storage Breakdown",
            IconKey = "Storage",
            IconPathData = AppIcons.Storage,
            Content = storagePaneContent
        };

        drivesPaneContent.SetDashboardRefresh(() => ((DashboardPaneViewModel)dashboardPane.Content).RefreshDashboardCommand.ExecuteAsync(null));

        var exportCsvCommand = new AsyncRelayCommand(
            () => exportService.ExportDriveInsightCsvAsync(dashboardPaneContent, drivesPaneContent, storagePaneContent));

        dashboardPaneContent.ExportCsvCommand = exportCsvCommand;
        drivesPaneContent.ExportCsvCommand = exportCsvCommand;
        storagePaneContent.ExportCsvCommand = exportCsvCommand;

        Panes = [dashboardPane, drivesPane, storageBreakdownPane];
        SelectedPane = dashboardPane;
    }

    [RelayCommand]
    private void SelectPane(PaneItemViewModel? pane)
    {
        if (pane is null || ReferenceEquals(SelectedPane, pane))
        {
            return;
        }

        SelectedPane = pane;
    }

    partial void OnSelectedPaneChanged(PaneItemViewModel? value)
    {
        foreach (var pane in Panes)
        {
            pane.IsActive = ReferenceEquals(pane, value);
            if (pane.Content is StorageBreakdownPaneViewModel storage)
            {
                if (!pane.IsActive) storage.SetActive(false);
                else Dispatcher.UIThread.Post(() =>
                {
                    if (pane.IsActive) storage.SetActive(true);
                }, DispatcherPriority.Background);
            }
        }
    }

}
