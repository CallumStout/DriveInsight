using Avalonia;
using Avalonia.Controls;
using System.Windows.Input;

namespace DriveInsight.Views;

public partial class PaneRadioButton : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<PaneRadioButton, string?>(nameof(Text));

    public static readonly StyledProperty<string?> IconDataProperty =
        AvaloniaProperty.Register<PaneRadioButton, string?>(nameof(IconData));

    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<PaneRadioButton, bool>(nameof(IsChecked));

    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<PaneRadioButton, ICommand?>(nameof(Command));

    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<PaneRadioButton, object?>(nameof(CommandParameter));

    public PaneRadioButton()
    {
        InitializeComponent();
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public bool IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }
}
