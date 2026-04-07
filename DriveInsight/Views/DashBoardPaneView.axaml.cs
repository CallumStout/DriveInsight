using Avalonia.Controls;
using Avalonia.Interactivity;
using DriveInsight.Models;
using DriveInsight.ViewModels;

namespace DriveInsight.Views;

public partial class DashBoardPaneView : UserControl
{
    public DashBoardPaneView()
    {
        InitializeComponent();

        var tree = this.FindControl<TreeView>("FoldersTree");
        tree?.AddHandler(TreeViewItem.ExpandedEvent, OnTreeItemExpanded, RoutingStrategies.Bubble);
    }

    private async void OnTreeItemExpanded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DashboardPaneViewModel vm)
        {
            return;
        }

        if (e.Source is not TreeViewItem item || item.DataContext is not FileSystemNode node)
        {
            return;
        }

        await vm.EnsureChildrenLoadedAsync(node);
    }
}
