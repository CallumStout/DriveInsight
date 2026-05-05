using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using DriveInsight.Views;

namespace DriveInsight.Services;

public sealed class CleanupReviewDialogService(Window owner) : ICleanupReviewDialogService
{
    public async Task<IReadOnlyList<CleanupCandidate>> ReviewAsync(string driveName, IReadOnlyList<CleanupCandidate> candidates)
    {
        var dialog = new CleanupReviewDialog(driveName, candidates);
        return await OwnerOverlayDialog.ShowAsync<IReadOnlyList<CleanupCandidate>>(owner, dialog) ?? [];
    }
}
