using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Anela.Heblo.Domain.Features.Photobank
{
    public interface IPhotobankRootRepository
    {
        Task<List<PhotobankIndexRoot>> GetRootsAsync(CancellationToken cancellationToken);
        Task<PhotobankIndexRoot> AddRootAsync(PhotobankIndexRoot root, CancellationToken cancellationToken);
        Task<bool> DeleteRootAsync(int id, CancellationToken cancellationToken);
        Task<List<PhotobankIndexRoot>> GetActiveRootsWithDriveAsync(CancellationToken cancellationToken);

        Task SaveChangesAsync(CancellationToken cancellationToken);
    }
}
