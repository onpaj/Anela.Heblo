using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Anela.Heblo.Domain.Features.Photobank
{
    public interface IPhotobankAutoTagRepository
    {
        Task<List<PhotoAutoTagCandidate>> GetPhotosPendingAutoTagAsync(int pageSize, int offset, CancellationToken cancellationToken);
        Task StampAutoTaggedAtAsync(IReadOnlyList<int> photoIds, DateTime timestamp, CancellationToken cancellationToken);
        Task ResetAutoTaggedAtAsync(IReadOnlyList<int> photoIds, CancellationToken cancellationToken);
        Task<List<PhotoAutoTagCandidate>> GetPhotoRuleCandidatesPageAsync(int pageSize, int offset, CancellationToken cancellationToken);

        Task SaveChangesAsync(CancellationToken cancellationToken);
    }
}
