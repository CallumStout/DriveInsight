using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveInsight.Services;
using DriveInsight.Utilities;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace DriveInsight.ViewModels;

public partial class StorageBreakdownPaneViewModel : ViewModelBase
{
    private static readonly string[] BreakdownColors =
    [
        "#1E63FF",
        "#31B57B",
        "#E3A12D",
        "#D94B73",
        "#6C7A90",
        "#9B6AD6"
    ];

    private readonly IStorageBreakdownScanner _scanner;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, CachedBreakdown> _results = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(2);
    private AsyncScanCancellation? _scanCancellation;
    private Task _activeLoad = Task.CompletedTask;
    private bool _isActive;
    private int _requestVersion;
    public Task Initialization { get; }

    private sealed record CachedBreakdown(StorageBreakdownScanResult Result, DateTimeOffset CompletedAt);

    public ObservableCollection<DriveInfo> Drives { get; } = [];
    public ObservableCollection<StorageBreakdownItemViewModel> BreakdownItems { get; } = [];
    public ObservableCollection<ISeries> Series { get; } = [];
    public IAsyncRelayCommand RefreshStorageBreakdownCommand { get; }
    public IAsyncRelayCommand? ExportCsvCommand { get; set; }

    [ObservableProperty]
    private DriveInfo? selectedDrive;

    [ObservableProperty]
    private bool isScanning;

    [ObservableProperty]
    private string status = "Select a drive";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalScannedText))]
    [NotifyPropertyChangedFor(nameof(HasBreakdown))]
    private long totalScannedBytes;

    public string TotalScannedText => StorageFormatter.Format(TotalScannedBytes, 1);
    public bool HasBreakdown => TotalScannedBytes > 0;

    public StorageBreakdownPaneViewModel(IStorageBreakdownScanner? scanner = null, TimeProvider? clock = null)
    {
        _scanner = scanner ?? new DriveScanner();
        _clock = clock ?? TimeProvider.System;
        RefreshStorageBreakdownCommand = new AsyncRelayCommand(() => StartLoad(forceRefresh: true), CanRefreshStorageBreakdown);
        Initialization = RefreshAvailableDrivesAsync();
    }

    public async Task RefreshAvailableDrivesAsync()
    {
        _results.Clear();
        var previouslySelectedName = SelectedDrive?.Name;
        var refreshed = await Task.Run(() => _scanner.GetReadyDrives().ToList());

        Drives.Clear();
        foreach (var drive in refreshed)
        {
            Drives.Add(drive);
        }

        SelectedDrive = Drives.FirstOrDefault(drive => drive.Name == previouslySelectedName) ?? Drives.FirstOrDefault();
        if (SelectedDrive is null)
        {
            ClearBreakdown("No ready drives found");
        }
    }

    public void SetActive(bool active) => _ = SetActiveAsync(active);

    public Task SetActiveAsync(bool active)
    {
        _isActive = active;
        if (!active)
        {
            StopCurrentScan();
            return Task.CompletedTask;
        }
        return IsScanning ? _activeLoad : StartLoad(forceRefresh: false);
    }

    partial void OnSelectedDriveChanged(DriveInfo? value)
    {
        StopCurrentScan();
        ClearBreakdown(value is null ? "Select a drive" : $"Ready to scan {value.Name.TrimEnd('\\')}");
        RefreshStorageBreakdownCommand.NotifyCanExecuteChanged();
        if (_isActive && value is not null)
            _ = StartLoad(forceRefresh: false);
    }

    partial void OnIsScanningChanged(bool value) => RefreshStorageBreakdownCommand.NotifyCanExecuteChanged();

    private bool CanRefreshStorageBreakdown() => _isActive && !IsScanning && SelectedDrive is not null;

    private Task StartLoad(bool forceRefresh)
    {
        _activeLoad = LoadSelectedDriveAsync(forceRefresh);
        return _activeLoad;
    }

    private void StopCurrentScan()
    {
        var cancellation = _scanCancellation;
        _scanCancellation = null;
        _requestVersion++;
        cancellation?.Cancel();
        IsScanning = false;
        // Each scan disposes its own CTS after its worker has stopped using it.
    }

    private async Task LoadSelectedDriveAsync(bool forceRefresh)
    {
        StopCurrentScan();
        var drive = SelectedDrive;
        if (drive is null || !_isActive) return;
        var driveName = drive.Name;
        if (forceRefresh) _results.Remove(driveName);
        else if (_results.TryGetValue(driveName, out var saved) && _clock.GetUtcNow() - saved.CompletedAt < CacheLifetime)
        {
            BuildBreakdown(saved.Result.Items);
            Status = $"{driveName.TrimEnd('\\')}: cached {saved.CompletedAt.ToLocalTime():HH:mm:ss}. " + CoverageStatus(saved.Result);
            return;
        }

        ClearBreakdown($"Scanning drive {driveName.TrimEnd('\\')}");
        await using var cancellation = new AsyncScanCancellation();
        _scanCancellation = cancellation;
        var version = _requestVersion;
        var ct = cancellation.Token;
        bool IsCurrent() => _isActive && version == _requestVersion && !ct.IsCancellationRequested &&
            string.Equals(SelectedDrive?.Name, driveName, StringComparison.OrdinalIgnoreCase);
        try
        {
            IsScanning = true;
            var result = await Task.Run(() => _scanner.GetStorageBreakdownScanAsync(drive, ct: ct), ct);
            if (!IsCurrent()) return;
            BuildBreakdown(result.Items);
            _results[driveName] = new CachedBreakdown(result, _clock.GetUtcNow());
            Status = $"{driveName.TrimEnd('\\')}: {result.FileCount:N0} files in {result.ElapsedSeconds:0.0}s. " + CoverageStatus(result);
        }
        catch (OperationCanceledException)
        {
            if (IsCurrent()) ClearBreakdown($"Scan cancelled for {driveName.TrimEnd('\\')}.");
        }
        catch
        {
            if (IsCurrent()) ClearBreakdown($"Could not scan drive {driveName.TrimEnd('\\')}. Refresh to retry.");
        }
        finally
        {
            if (ReferenceEquals(_scanCancellation, cancellation))
            {
                _scanCancellation = null;
                IsScanning = false;
            }
        }
    }

    private static string CoverageStatus(StorageBreakdownScanResult result)
    {
        var status = result.UnreadableDirectories > 0
            ? $"{result.UnreadableDirectories:N0} folders could not be fully read."
            : "Scan complete.";
        if (result.SkippedLinks > 0) status += $" {result.SkippedLinks:N0} directory links skipped.";
        return status;
    }

    private void BuildBreakdown(IReadOnlyCollection<StorageBreakdownItem> breakdown)
    {
        TotalScannedBytes = breakdown.Sum(category => category.Bytes);
        BreakdownItems.Clear();
        Series.Clear();

        var index = 0;
        foreach (var category in breakdown.Where(category => category.Bytes > 0))
        {
            var color = BreakdownColors[index % BreakdownColors.Length];
            BreakdownItems.Add(new StorageBreakdownItemViewModel
            {
                Name = category.Name,
                FullPath = category.FullPath,
                Bytes = category.Bytes,
                TotalBytes = TotalScannedBytes,
                Color = color
            });

            Series.Add(new PieSeries<double?>
            {
                Name = category.Name,
                Values = [category.Bytes],
                ToolTipLabelFormatter = _ => StorageFormatter.Format(category.Bytes, 1),
                Fill = new SolidColorPaint(SKColor.Parse(color)),
                Stroke = new SolidColorPaint(SKColor.Parse("#FCFDFE")) { StrokeThickness = 3 },
                HoverPushout = 0,
                InnerRadius = 96,
                IsHoverable = IsLargeEnoughForTooltip(category.Bytes),
                IsVisibleAtLegend = false,
                DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle
            });

            index++;
        }

        OnPropertyChanged(nameof(HasBreakdown));
        OnPropertyChanged(nameof(TotalScannedText));
    }

    private void ClearBreakdown(string status)
    {
        TotalScannedBytes = 0;
        BreakdownItems.Clear();
        Series.Clear();
        Status = status;
    }

    private bool IsLargeEnoughForTooltip(long bytes)
    {
        const long tenMegabytes = 10L * 1024L * 1024L;
        return bytes >= tenMegabytes && bytes >= TotalScannedBytes * 0.001;
    }
}
