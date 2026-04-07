using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DriveInsight.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public ObservableCollection<PaneItemViewModel> Panes { get; }

    [ObservableProperty]
    private PaneItemViewModel? selectedPane;

    public MainWindowViewModel()
    {
        var dashboardPane = new PaneItemViewModel
        {
            Id = "dashboard",
            Title = "Dashboard",
            IconKey = "Home",
            Content = new DashboardPaneViewModel()
        };
        
        var drivesPane = new PaneItemViewModel
        {
            Id = "drives",
            Title = "Drives",
            IconKey = "Drive",
            Content = new DrivesPaneViewModel()
        };

        Panes = [dashboardPane, drivesPane];
        SelectedPane = dashboardPane;
    }
}
