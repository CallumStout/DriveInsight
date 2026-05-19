using System;
using System.IO;
using Avalonia;
using Avalonia.Styling;

namespace DriveInsight.Services;

public interface IAppThemeService
{
    bool IsDarkMode { get; }

    void ApplySavedTheme(Application application);

    void SetDarkMode(bool isDarkMode);
}

public sealed class AppThemeService : IAppThemeService
{
    private const string SettingsFileName = "theme.txt";
    private readonly string settingsFilePath;

    public AppThemeService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        settingsFilePath = Path.Combine(appData, "DriveInsight", SettingsFileName);
        IsDarkMode = ReadSavedTheme();
    }

    public bool IsDarkMode { get; private set; }

    public void ApplySavedTheme(Application application)
    {
        application.RequestedThemeVariant = IsDarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    public void SetDarkMode(bool isDarkMode)
    {
        if (IsDarkMode == isDarkMode)
        {
            return;
        }

        IsDarkMode = isDarkMode;
        if (Application.Current is { } application)
        {
            application.RequestedThemeVariant = isDarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        SaveTheme(isDarkMode);
    }

    private bool ReadSavedTheme()
    {
        try
        {
            return File.Exists(settingsFilePath)
                && string.Equals(File.ReadAllText(settingsFilePath).Trim(), "Dark", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void SaveTheme(bool isDarkMode)
    {
        try
        {
            var directory = Path.GetDirectoryName(settingsFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(settingsFilePath, isDarkMode ? "Dark" : "Light");
        }
        catch
        {
            // Theme switching should never fail because preference persistence is unavailable.
        }
    }
}
