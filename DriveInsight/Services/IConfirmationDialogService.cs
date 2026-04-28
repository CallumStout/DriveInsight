using System.Threading.Tasks;

namespace DriveInsight.Services;

public interface IConfirmationDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText);
}
