using Avalonia;
using Avalonia.Controls;

namespace DriveInsight.Views.Controls;

public partial class AppIcon : UserControl
{
    public static readonly StyledProperty<string?> DataProperty =
        AvaloniaProperty.Register<AppIcon, string?>(nameof(Data));

    public string? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public AppIcon() => InitializeComponent();
}
