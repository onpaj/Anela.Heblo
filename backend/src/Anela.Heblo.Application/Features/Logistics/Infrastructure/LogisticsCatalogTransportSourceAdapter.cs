using System.Linq.Expressions;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Logistics.Transport;

namespace Anela.Heblo.Application.Features.Logistics.Infrastructure;

internal sealed class LogisticsCatalogTransportSourceAdapter : ICatalogTransportSource
{
    private readonly ITransportBoxRepository _transportBoxRepository;

    public LogisticsCatalogTransportSourceAdapter(ITransportBoxRepository transportBoxRepository)
    {
        _transportBoxRepository = transportBoxRepository;
    }

    public Task<Dictionary<string, int>> GetProductsInTransportAsync(CancellationToken cancellationToken) =>
        GetProductAmountsByPredicateAsync(TransportBox.IsInTransportPredicate, cancellationToken);

    public Task<Dictionary<string, int>> GetProductsInReserveAsync(CancellationToken cancellationToken) =>
        GetProductAmountsByPredicateAsync(TransportBox.IsInReservePredicate, cancellationToken);

    public Task<Dictionary<string, int>> GetProductsInQuarantineAsync(CancellationToken cancellationToken) =>
        GetProductAmountsByPredicateAsync(TransportBox.IsInQuarantinePredicate, cancellationToken);

    private async Task<Dictionary<string, int>> GetProductAmountsByPredicateAsync(
        Expression<Func<TransportBox, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var boxes = await _transportBoxRepository.FindAsync(predicate, includeDetails: true, cancellationToken);
        // Cast to int: Logistics reports whole units; fractional amounts are a Manufacture concern.
        return boxes
            .SelectMany(b => b.Items)
            .GroupBy(i => i.ProductCode)
            .ToDictionary(g => g.Key, g => (int)g.Sum(i => i.Amount));
    }
}
