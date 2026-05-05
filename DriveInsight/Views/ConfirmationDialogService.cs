using System.Threading.Tasks;
using Avalonia.Controls;
using DriveInsight.Services;

namespace DriveInsight.Views;

public sealed class ConfirmationDialogService(Window owner) : IConfirmationDialogService
{
    public async Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText,
        string cancelText,
        ConfirmationKind kind = ConfirmationKind.Destructive)
    {
        var dialog = new ConfirmationDialog(title, message, confirmText, cancelText, kind);
        return await OwnerOverlayDialog.ShowAsync<bool>(owner, dialog);
    }
}
