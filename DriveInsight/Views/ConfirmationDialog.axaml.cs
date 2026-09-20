using Avalonia.Controls;
using DriveInsight.Services;

namespace DriveInsight.Views;

public partial class ConfirmationDialog : Window
{
    public ConfirmationDialog()
    {
        InitializeComponent();
    }

    public ConfirmationDialog(
        string title,
        string message,
        string confirmText,
        string cancelText,
        ConfirmationKind kind = ConfirmationKind.Destructive)
        : this()
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText;
        ConfigureKind(kind);

        ConfirmButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
    }

    private void ConfigureKind(ConfirmationKind kind)
    {
        if (kind == ConfirmationKind.Info)
        {
            WarningBox.IsVisible = false;
            ConfirmButton.Classes.Remove("Danger");
            ConfirmButton.Classes.Add("Primary");
        }
        else
        {
            WarningBox.IsVisible = true;
            ConfirmButton.Classes.Remove("Primary");
            ConfirmButton.Classes.Add("Danger");
        }
    }
}
