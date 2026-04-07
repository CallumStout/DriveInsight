namespace DriveInsight.ViewModels;

public sealed class PaneItemViewModel : ViewModelBase, IPane
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string IconKey { get; init; }
    public required ViewModelBase Content { get; init; }
}
