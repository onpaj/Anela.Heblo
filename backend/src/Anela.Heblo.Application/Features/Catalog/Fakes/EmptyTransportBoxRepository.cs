using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Application.Features.Catalog.Fakes;

public class EmptyTransportBoxRepository : EmptyRepository<TransportBox, int>, ITransportBoxRepository
{
    public Task<(IList<TransportBox> items, int totalCount)> GetPagedListAsync(
        int skip,
        int take,
        string? code = null,
        TransportBoxState? state = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        string? sortBy = null,
        bool sortDescending = false,
        bool isActiveFilter = false)
    {
        return Task.FromResult<(IList<TransportBox> items, int totalCount)>((new List<TransportBox>(), 0));
    }

    public Task<TransportBox?> GetByIdWithDetailsAsync(int id)
    {
        return Task.FromResult<TransportBox?>(null);
    }
}