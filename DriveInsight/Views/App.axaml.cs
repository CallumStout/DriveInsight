using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DriveInsight.Services;

namespace DriveInsight.Views;

public partial class App : Application
{
    internal static IAppThemeService ThemeService { get; } = new AppThemeService();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ThemeService.ApplySavedTheme(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
