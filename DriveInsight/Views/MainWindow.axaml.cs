using Avalonia.Controls;
using Avalonia.Threading;
using System.Collections.Generic;
using DriveInsight.Services;
using DriveInsight.ViewModels;

namespace DriveInsight.Views;

public partial class MainWindow : Window
{
    private readonly Dictionary<ViewModelBase, Control> _pages = [];
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel(
            new ConfirmationDialogService(this),
            new CleanupReviewDialogService(this),
            new ExportService(this));
        DataContext = _viewModel;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.SelectedPane)) QueueSelectedPage();
        };
        QueueSelectedPage();
    }

    private void QueueSelectedPage()
    {
        var selected = _viewModel.SelectedPane?.Content;
        if (selected is null) return;
        // Finish the navigation event first; create each expensive chart/grid once.
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(selected, _viewModel.SelectedPane?.Content)) return;
            if (!_pages.TryGetValue(selected, out var page))
            {
                page = selected switch
                {
                    DashboardPaneViewModel => new DashBoardPaneView(),
                    DrivesPaneViewModel => new DrivesPaneView(),
                    StorageBreakdownPaneViewModel => new StorageBreakdownPaneView(),
                    _ => new ContentControl()
                };
                page.DataContext = selected;
                _pages.Add(selected, page);
                PaneHost.Children.Add(page);
            }
            foreach (var cached in _pages.Values) cached.IsVisible = ReferenceEquals(cached, page);
        }, DispatcherPriority.Loaded);
    }
}
