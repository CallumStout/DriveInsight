using Avalonia.Controls;
using Avalonia.Interactivity;
using DriveInsight.ViewModels;

namespace DriveInsight.Views;

public partial class DrivesPaneView : UserControl
{
    public DrivesPaneView()
    {
        InitializeComponent();

        var tree = this.FindControl<TreeView>("FoldersTree");
        tree?.AddHandler(TreeViewItem.ExpandedEvent, OnFolderExpanded, RoutingStrategies.Bubble);
    }

    private async void OnFolderExpanded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DrivesPaneViewModel vm)
        {
            return;
        }

        if (e.Source is not TreeViewItem item || item.DataContext is not DriveFolderRowViewModel row || !row.IsFolder)
        {
            return;
        }

        await vm.ExpandFolderRowAsync(row);
    }

    private void OnFoldersTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is TreeView tree && tree.SelectedItem is not null)
        {
            tree.SelectedItem = null;
        }
    }
}
