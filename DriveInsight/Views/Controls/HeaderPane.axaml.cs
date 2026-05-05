using Avalonia;
using Avalonia.Controls;
using System.Windows.Input;

namespace DriveInsight.Views.Controls;

public partial class HeaderPane : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<HeaderPane, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<object?> CenterContentProperty =
        AvaloniaProperty.Register<HeaderPane, object?>(nameof(CenterContent));

    public static readonly StyledProperty<object?> RightContentProperty =
        AvaloniaProperty.Register<HeaderPane, object?>(nameof(RightContent));

    public static readonly StyledProperty<ICommand?> RefreshCommandProperty =
        AvaloniaProperty.Register<HeaderPane, ICommand?>(nameof(RefreshCommand));

    public static readonly StyledProperty<ICommand?> UpdateCommandProperty =
        AvaloniaProperty.Register<HeaderPane, ICommand?>(nameof(UpdateCommand));

    public static readonly StyledProperty<bool> ShowRefreshButtonProperty =
        AvaloniaProperty.Register<HeaderPane, bool>(nameof(ShowRefreshButton), false);

    public static readonly StyledProperty<bool> ShowUpdateButtonProperty =
        AvaloniaProperty.Register<HeaderPane, bool>(nameof(ShowUpdateButton), false);

    public static readonly StyledProperty<bool> ShowUpdateStatusProperty =
        AvaloniaProperty.Register<HeaderPane, bool>(nameof(ShowUpdateStatus), false);

    public static readonly StyledProperty<string> RefreshToolTipProperty =
        AvaloniaProperty.Register<HeaderPane, string>(nameof(RefreshToolTip), "Refresh");

    public static readonly StyledProperty<string> UpdateToolTipProperty =
        AvaloniaProperty.Register<HeaderPane, string>(nameof(UpdateToolTip), "Update to latest release");

    public static readonly StyledProperty<string> UpdateStatusProperty =
        AvaloniaProperty.Register<HeaderPane, string>(nameof(UpdateStatus), string.Empty);

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? CenterContent
    {
        get => GetValue(CenterContentProperty);
        set => SetValue(CenterContentProperty, value);
    }

    public object? RightContent
    {
        get => GetValue(RightContentProperty);
        set => SetValue(RightContentProperty, value);
    }

    public ICommand? RefreshCommand
    {
        get => GetValue(RefreshCommandProperty);
        set => SetValue(RefreshCommandProperty, value);
    }

    public ICommand? UpdateCommand
    {
        get => GetValue(UpdateCommandProperty);
        set => SetValue(UpdateCommandProperty, value);
    }

    public bool ShowRefreshButton
    {
        get => GetValue(ShowRefreshButtonProperty);
        set => SetValue(ShowRefreshButtonProperty, value);
    }

    public bool ShowUpdateButton
    {
        get => GetValue(ShowUpdateButtonProperty);
        set => SetValue(ShowUpdateButtonProperty, value);
    }

    public bool ShowUpdateStatus
    {
        get => GetValue(ShowUpdateStatusProperty);
        set => SetValue(ShowUpdateStatusProperty, value);
    }

    public string RefreshToolTip
    {
        get => GetValue(RefreshToolTipProperty);
        set => SetValue(RefreshToolTipProperty, value);
    }

    public string UpdateToolTip
    {
        get => GetValue(UpdateToolTipProperty);
        set => SetValue(UpdateToolTipProperty, value);
    }

    public string UpdateStatus
    {
        get => GetValue(UpdateStatusProperty);
        set => SetValue(UpdateStatusProperty, value);
    }

    public HeaderPane()
    {
        InitializeComponent();
    }
}
