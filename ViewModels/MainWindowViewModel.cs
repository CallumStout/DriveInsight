using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveInsight.Models;
using DriveInsight.Services;

namespace DriveInsight.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly DriveScanner _scanner = new();

    public ObservableCollection<DriveInfo> Drives { get; } = [];
    public ObservableCollection<FolderStat> TopFolders { get; } = [];

    [ObservableProperty] private DriveInfo? selectedDrive;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string status = "Ready";

    public MainWindowViewModel()
    {
        foreach (var d in _scanner.GetReadyDrives()) Drives.Add(d);
    }

    [RelayCommand]
    private async Task ScanSelectedDrive()
    {
        if (SelectedDrive is null) return;

        IsBusy = true;
        Status = $"Scanning {SelectedDrive.Name}...";
        TopFolders.Clear();

        var top = await _scanner.GetTopFoldersAsync(SelectedDrive.RootDirectory.FullName);

        foreach (var item in top) TopFolders.Add(item);

        Status = $"Done. {TopFolders.Count} folders loaded.";
        IsBusy = false;
    }
}