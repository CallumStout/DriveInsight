using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
            IconPathData = "M2,2 H10 V10 H2 Z M14,2 H22 V10 H14 Z M2,14 H10 V22 H2 Z M14,14 H22 V22 H14 Z",
            Content = new DashboardPaneViewModel()
        };
        
        var drivesPane = new PaneItemViewModel
        {
            Id = "drives",
            Title = "Drives",
            IconKey = "Drive",
            IconPathData = "M3,4 H21 V8 H3 Z M3,10 H21 V14 H3 Z M3,16 H21 V20 H3 Z",
            Content = new DrivesPaneViewModel()
        };

        Panes = [dashboardPane, drivesPane];
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
