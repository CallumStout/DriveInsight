using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DriveInsight.ViewModels;

public partial class DriveFolderRowViewModel : ViewModelBase
{
    public required string Name { get; init; }

    public required string FullPath { get; init; }

    public required string SizeText { get; init; }

    public required double UsagePercent { get; init; }

    public required string UsageBrush { get; init; }

    public required string IconPathData { get; init; }

    public required double IconContainerSize { get; init; }

    public required double IconSize { get; init; }

    public required string IconBackground { get; init; }

    public required string IconFill { get; init; }

    public required double TextSize { get; init; }

    public required double UsageBarHeight { get; init; }

    public required bool IsFolder { get; init; }

    public required int Depth { get; init; }

    public required Thickness NameIndent { get; init; }

    public required double RowOffsetX { get; init; }

    public bool IsChildRow { get; init; }

    public bool IsPlaceholder { get; init; }

    public ObservableCollection<DriveFolderRowViewModel> Children { get; } = [];

    [ObservableProperty]
    private bool isExpanded;

    [ObservableProperty]
    private bool hasLoadedChildren;
}
