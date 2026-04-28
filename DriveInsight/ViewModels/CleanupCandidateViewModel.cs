using CommunityToolkit.Mvvm.ComponentModel;
using DriveInsight.Services;
using DriveInsight.Utilities;

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
    public string SizeText => StorageFormatter.Format(Candidate.SizeBytes);

    [ObservableProperty]
    private bool isSelected;

}
