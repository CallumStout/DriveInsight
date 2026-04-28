using CommunityToolkit.Mvvm.ComponentModel;
using DriveInsight.Services;

namespace DriveInsight.ViewModels;

public partial class CleanupCandidateViewModel : ViewModelBase
{
    public CleanupCandidateViewModel(CleanupCandidate candidate)
    {
        Candidate = candidate;
        IsSelected = candidate.IsSelectedByDefault;
    }

    public CleanupCandidate Candidate { get; }

    public string Name => Candidate.Name;
    public string FullPath => Candidate.FullPath;
    public string Reason => Candidate.Reason;
    public string Risk => Candidate.Risk;
    public string SizeText => FormatStorage(Candidate.SizeBytes, 1);

    [ObservableProperty]
    private bool isSelected;

    private static string FormatStorage(long bytes, int decimals)
    {
        const double scale = 1024d;
        if (bytes < scale)
        {
            return $"{bytes} B";
        }

        var units = new[] { "KB", "MB", "GB", "TB", "PB" };
        var value = bytes / scale;
        var unitIndex = 0;

        while (value >= scale && unitIndex < units.Length - 1)
        {
            value /= scale;
            unitIndex++;
        }

        return $"{value.ToString($"F{decimals}")} {units[unitIndex]}";
    }
}
