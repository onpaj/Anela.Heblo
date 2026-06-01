using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using System.Linq.Expressions;

namespace Anela.Heblo.Application.Features.Logistics.Infrastructure;

internal sealed class LogisticsCatalogTransportSourceAdapter : ICatalogTransportSource
{
    private readonly ITransportBoxRepository _transportBoxRepository;

    public LogisticsCatalogTransportSourceAdapter(ITransportBoxRepository transportBoxRepository)
    {
        _transportBoxRepository = transportBoxRepository;
    }

    public Task<Dictionary<string, int>> GetProductsInTransportAsync(CancellationToken cancellationToken) =>
        AggregateByProductCodeAsync(TransportBox.IsInTransportPredicate, cancellationToken);

    public Task<Dictionary<string, int>> GetProductsInReserveAsync(CancellationToken cancellationToken) =>
        AggregateByProductCodeAsync(TransportBox.IsInReservePredicate, cancellationToken);

    public Task<Dictionary<string, int>> GetProductsInQuarantineAsync(CancellationToken cancellationToken) =>
        AggregateByProductCodeAsync(TransportBox.IsInQuarantinePredicate, cancellationToken);

    private async Task<Dictionary<string, int>> AggregateByProductCodeAsync(
        Expression<Func<TransportBox, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var boxes = await _transportBoxRepository.FindAsync(predicate, includeDetails: true, cancellationToken: cancellationToken);
        return boxes.SelectMany(s => s.Items)
            .GroupBy(g => g.ProductCode)
            .ToDictionary(k => k.Key, v => v.Sum(s => (int)s.Amount));
    }
}
