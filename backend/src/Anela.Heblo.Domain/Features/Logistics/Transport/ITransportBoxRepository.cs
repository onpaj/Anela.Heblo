using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Logistics.Transport;

public interface ITransportBoxRepository : IRepository<TransportBox, int>
{
    Task<(IList<TransportBox> items, int totalCount)> GetPagedListAsync(
        int skip,
        int take,
        string? code = null,
        TransportBoxState? state = null,
        string? productCode = null,
        string? sortBy = null,
        bool sortDescending = false,
        bool isActiveFilter = false);

    Task<TransportBox?> GetByIdWithDetailsAsync(int id);

    Task<bool> IsBoxCodeActiveAsync(string boxCode);

    Task<TransportBox?> GetByCodeAsync(string boxCode);

    Task<IEnumerable<TransportBox>> FindAsync(
        System.Linq.Expressions.Expression<Func<TransportBox, bool>> predicate,
        bool includeDetails = false,
        CancellationToken cancellationToken = default);
}