using System.Collections.Generic;
using System.Threading.Tasks;

namespace DriveInsight.Services;

public sealed class NullDialogService : IConfirmationDialogService, ICleanupReviewDialogService
{
    public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText)
    {
        return Task.FromResult(false);
    }

    public Task<IReadOnlyList<CleanupCandidate>> ReviewAsync(string driveName, IReadOnlyList<CleanupCandidate> candidates)
    {
        return Task.FromResult<IReadOnlyList<CleanupCandidate>>([]);
    }
}
