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

    /// <summary>
    /// True when any box currently occupies <paramref name="boxCode"/> — i.e. holds it in a
    /// state for which <see cref="TransportBoxStateRules.OccupiesCode"/> is true. Matching is
    /// case-insensitive. The name predates the rule; "active" here means "occupying the code",
    /// which includes Error and Quarantine.
    /// </summary>
    Task<bool> IsBoxCodeActiveAsync(string boxCode);

    Task<TransportBox?> GetByCodeAsync(string boxCode);

    Task<IEnumerable<TransportBox>> FindAsync(
        System.Linq.Expressions.Expression<Func<TransportBox, bool>> predicate,
        bool includeDetails = false,
        CancellationToken cancellationToken = default);

    Task<IList<TransportBox>> GetReceivedBoxesAsync(CancellationToken cancellationToken = default);

    Task<Dictionary<TransportBoxState, int>> GetStateSummaryAsync(
        string? code = null,
        string? productCode = null,
        CancellationToken cancellationToken = default);
}