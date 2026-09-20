using CommunityToolkit.Mvvm.Input;
using DriveInsight.Utilities;

namespace DriveInsight.ViewModels;

public enum InsightKind
{
    Critical,
    Warning,
    Tip
}

public partial class InsightCardViewModel : ViewModelBase
{
    public required string Id { get; init; }
    public required InsightKind Kind { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public required string ActionText { get; init; }
    public IAsyncRelayCommand? ActionCommand { get; init; }
    public IRelayCommand? DismissCommand { get; set; }

    public bool IsCritical => Kind == InsightKind.Critical;
    public bool IsWarning => Kind == InsightKind.Warning;

    public string IconPathData => Kind switch
    {
        InsightKind.Critical => AppIcons.Warning,
        InsightKind.Warning => AppIcons.Info,
        _ => AppIcons.Spark
    };
}
