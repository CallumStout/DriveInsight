using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using DriveInsight.ViewModels;

namespace DriveInsight.Views;

public partial class DrivesPaneView : UserControl
{
    public DrivesPaneView()
    {
        InitializeComponent();
    }

    private async void OnFolderChevronClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DrivesPaneViewModel vm)
        {
            return;
        }

        if (sender is not ToggleButton toggle || toggle.DataContext is not DriveFolderRowViewModel row)
        {
            return;
        }

        row.IsExpanded = !row.IsExpanded;
        if (row.IsExpanded)
        {
            await vm.ExpandFolderRowAsync(row);
        }
    }
}
