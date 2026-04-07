using CommunityToolkit.Mvvm.ComponentModel;

namespace DriveInsight.ViewModels;

public partial class PaneItemViewModel : ViewModelBase, IPane
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string IconKey { get; init; }

    public required string IconPathData { get; init; }

    public required ViewModelBase Content { get; init; }

    [ObservableProperty]
    private bool isActive;
}
