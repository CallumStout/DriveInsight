using Avalonia;
using DriveInsight.Services;
using DriveInsight.Views;
using System;
using Velopack;

namespace DriveInsight;

class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (ElevatedDeepScanRunner.IsDeepScanCommand(args) ||
            ElevatedDeepScanRunner.IsDeepChildrenCommand(args) ||
            ElevatedDeepScanRunner.IsDeepHelperCommand(args))
        {
            return ElevatedDeepScanRunner.RunAsync(args).GetAwaiter().GetResult();
        }

        VelopackApp.Build().Run();

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        return 0;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
