namespace DriveInsight.ViewModels;

public interface IPane
{
    string Id { get; }
    string Title { get; }
    string IconKey { get; }
    ViewModelBase Content { get; }
}
