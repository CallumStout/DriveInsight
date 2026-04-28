using CommunityToolkit.Mvvm.Input;

namespace DriveInsight.ViewModels;

public enum InsightKind
{
    Critical,
    Warning,
    Tip
}

public partial class InsightCardViewModel : ViewModelBase
{
    public required InsightKind Kind { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public required string ActionText { get; init; }
    public IAsyncRelayCommand? ActionCommand { get; init; }

    public string AccentColor => Kind switch
    {
        InsightKind.Critical => "#D93636",
        InsightKind.Warning => "#D1791A",
        _ => "#1E63FF"
    };

    public string Icon => Kind switch
    {
        InsightKind.Critical => "\u25B2",
        InsightKind.Warning => "\u25A0",
        _ => "\u25CF"
    };
}
