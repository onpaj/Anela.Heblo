using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Logistics.Transport;

public interface ITransportBoxRepository : IRepository<TransportBox, int>
{
    Task<(IList<TransportBox> items, int totalCount)> GetPagedListAsync(
        int skip,
        int take,
        string? code = null,
        TransportBoxState? state = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        string? sortBy = null,
        bool sortDescending = false,
        bool isActiveFilter = false);

    Task<TransportBox?> GetByIdWithDetailsAsync(int id);
}