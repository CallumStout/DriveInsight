using System.Collections.Generic;
using System.Threading.Tasks;

namespace DriveInsight.Services;

public interface ICleanupReviewDialogService
{
    Task<IReadOnlyList<CleanupCandidate>> ReviewAsync(string driveName, IReadOnlyList<CleanupCandidate> candidates);
}
