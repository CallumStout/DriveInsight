using Avalonia.Controls;
using DriveInsight.ViewModels;

namespace DriveInsight.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }
}
