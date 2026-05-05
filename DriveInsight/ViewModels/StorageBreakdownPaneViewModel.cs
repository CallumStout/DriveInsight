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

    private readonly DriveScanner _scanner = new();
    private CancellationTokenSource? _scanCancellation;

    public ObservableCollection<DriveInfo> Drives { get; } = [];
    public ObservableCollection<StorageBreakdownItemViewModel> BreakdownItems { get; } = [];
    public ObservableCollection<ISeries> Series { get; } = [];
    public IAsyncRelayCommand RefreshStorageBreakdownCommand { get; }

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

    public StorageBreakdownPaneViewModel()
    {
        RefreshStorageBreakdownCommand = new AsyncRelayCommand(RefreshStorageBreakdownAsync, CanRefreshStorageBreakdown);
        RefreshAvailableDrives();
    }

    public void RefreshAvailableDrives()
    {
        var previouslySelectedName = SelectedDrive?.Name;
        var refreshed = _scanner.GetReadyDrives().ToList();

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

    partial void OnSelectedDriveChanged(DriveInfo? value)
    {
        RefreshStorageBreakdownCommand.NotifyCanExecuteChanged();
        if (value is not null)
        {
            _ = RefreshStorageBreakdownAsync();
        }
    }

    partial void OnIsScanningChanged(bool value)
    {
        RefreshStorageBreakdownCommand.NotifyCanExecuteChanged();
    }

    private bool CanRefreshStorageBreakdown()
    {
        return !IsScanning && SelectedDrive is not null;
    }

    private async Task RefreshStorageBreakdownAsync()
    {
        if (SelectedDrive is null || IsScanning)
        {
            return;
        }

        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();
        var ct = _scanCancellation.Token;

        try
        {
            IsScanning = true;
            Status = $"Scanning drive {SelectedDrive.Name.TrimEnd('\\')}";
            var breakdown = await _scanner.GetStorageBreakdownAsync(SelectedDrive, ct: ct);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            BuildBreakdown(breakdown);
            Status = HasBreakdown
                ? $"Scanned {SelectedDrive.Name.TrimEnd('\\')}"
                : $"No scannable files found on {SelectedDrive.Name.TrimEnd('\\')}";
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            ClearBreakdown($"Could not scan drive {SelectedDrive.Name.TrimEnd('\\')}");
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void BuildBreakdown(IReadOnlyCollection<StorageBreakdownItem> breakdown)
    {
        TotalScannedBytes = breakdown.Sum(category => category.Bytes);
        BreakdownItems.Clear();
        Series.Clear();

        if (TotalScannedBytes > 0)
        {
            Series.Add(new PieSeries<double?>
            {
                Name = "Used Space",
                Values = [TotalScannedBytes, null],
                ToolTipLabelFormatter = _ => StorageFormatter.Format(TotalScannedBytes, 1),
                Fill = new SolidColorPaint(SKColor.Parse("#DCE6F7")),
                Stroke = new SolidColorPaint(SKColor.Parse("#FCFDFE")) { StrokeThickness = 3 },
                HoverPushout = 0,
                IsVisibleAtLegend = false
            });
        }

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
                Values = [null, category.Bytes],
                ToolTipLabelFormatter = _ => StorageFormatter.Format(category.Bytes, 1),
                Fill = new SolidColorPaint(SKColor.Parse(color)),
                Stroke = new SolidColorPaint(SKColor.Parse("#FCFDFE")) { StrokeThickness = 3 },
                HoverPushout = 0,
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
}
