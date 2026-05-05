using DriveInsight.Utilities;

namespace DriveInsight.ViewModels;

public partial class StorageBreakdownItemViewModel : ViewModelBase
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required long Bytes { get; init; }
    public required long TotalBytes { get; init; }
    public required string Color { get; init; }

    public string SizeText => StorageFormatter.Format(Bytes, 1);
    public double Percent => TotalBytes > 0 ? Bytes / (double)TotalBytes * 100d : 0d;
    public string PercentText => $"{Percent:0.#}%";
    public bool HasPath => !string.IsNullOrWhiteSpace(FullPath);
}
