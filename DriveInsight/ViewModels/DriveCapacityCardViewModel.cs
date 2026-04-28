using DriveInsight.Utilities;

namespace DriveInsight.ViewModels;

public partial class DriveCapacityCardViewModel : ViewModelBase
{
    public required string DriveName { get; init; }
    public required long UsedBytes { get; init; }
    public required long TotalBytes { get; init; }
    public required double UsedPercent { get; init; }
    public required string ProgressBrush { get; init; }

    public string UsageText => $"{StorageFormatter.Format(UsedBytes)} of {StorageFormatter.Format(TotalBytes)}";
    public string PercentageText => $"{UsedPercent:0}%";
}
