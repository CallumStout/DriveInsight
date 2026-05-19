using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveInsight.Services;

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
            IconPathData = "M2,2 H10 V10 H2 Z M14,2 H22 V10 H14 Z M2,14 H10 V22 H2 Z M14,14 H22 V22 H14 Z",
            Content = dashboardPaneContent = new DashboardPaneViewModel(
                () =>
                {
                    drivesPaneContent.RefreshAvailableDrives();
                    return Task.CompletedTask;
                },
                confirmationDialog,
                cleanupReviewDialog)
        };
        
        var drivesPane = new PaneItemViewModel
        {
            Id = "drives",
            Title = "Drives",
            IconKey = "Drive",
            IconPathData = "M3,4 H21 V8 H3 Z M3,10 H21 V14 H3 Z M3,16 H21 V20 H3 Z",
            Content = drivesPaneContent
        };

        var storageBreakdownPane = new PaneItemViewModel
        {
            Id = "breakdown",
            Title = "Storage Breakdown",
            IconKey = "Storage",
            IconPathData = "M12,2 A10,10 0 1 0 22,12 H12 Z M14,2.2 V10 H21.8 A10,10 0 0 0 14,2.2 Z",
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
        }
    }

}
