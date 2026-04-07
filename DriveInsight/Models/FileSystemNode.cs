using System.Collections.ObjectModel;

namespace DriveInsight.Models;

public sealed class FileSystemNode
{
    public string Name { get; init; } = "";

    public string FullPath { get; init; } = "";

    public bool IsFolder { get; init; }

    public long Bytes { get; init; }

    public string SizeText { get; init; } = "";

    public bool HasLoadedChildren { get; set; }

    public bool IsPlaceholder { get; init; }

    public ObservableCollection<FileSystemNode> Children { get; } = [];
}
