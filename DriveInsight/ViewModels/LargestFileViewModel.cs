using DriveInsight.Utilities;

namespace DriveInsight.ViewModels;

public partial class LargestFileViewModel : ViewModelBase
{
    public required string Name { get; init; }
    public required string FilePath { get; init; }
    public required long SizeBytes { get; init; }
    public required string Category { get; init; }

    public string SizeText => StorageFormatter.Format(SizeBytes);
}
