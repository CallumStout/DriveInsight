using Avalonia.Controls;
using Avalonia.Interactivity;
using DriveInsight.Models;
using DriveInsight.ViewModels;

namespace DriveInsight.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();

        var tree = this.FindControl<TreeView>("FoldersTree");
        tree?.AddHandler(TreeViewItem.ExpandedEvent, OnTreeItemExpanded, RoutingStrategies.Bubble);
    }

    private async void OnTreeItemExpanded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (e.Source is not TreeViewItem item) return;
        if (item.DataContext is not FileSystemNode node) return;

        await vm.EnsureChildrenLoadedAsync(node);
    }
}
