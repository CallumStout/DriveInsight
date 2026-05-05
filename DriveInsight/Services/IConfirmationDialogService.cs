using System.Threading.Tasks;

namespace DriveInsight.Services;

public enum ConfirmationKind
{
    Destructive,
    Info
}

public interface IConfirmationDialogService
{
    Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText,
        string cancelText,
        ConfirmationKind kind = ConfirmationKind.Destructive);
}
