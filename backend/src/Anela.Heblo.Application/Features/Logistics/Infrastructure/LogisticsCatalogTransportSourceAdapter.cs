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

    public async Task<Dictionary<string, int>> GetProductsInTransportAsync(CancellationToken cancellationToken)
    {
        var boxes = await _transportBoxRepository.FindAsync(
            TransportBox.IsInTransportPredicate,
            includeDetails: true,
            cancellationToken);

        return boxes
            .SelectMany(b => b.Items)
            .GroupBy(i => i.ProductCode)
            .ToDictionary(g => g.Key, g => (int)g.Sum(i => i.Amount));
    }

    public async Task<Dictionary<string, int>> GetProductsInReserveAsync(CancellationToken cancellationToken)
    {
        var boxes = await _transportBoxRepository.FindAsync(
            TransportBox.IsInReservePredicate,
            includeDetails: true,
            cancellationToken);

        return boxes
            .SelectMany(b => b.Items)
            .GroupBy(i => i.ProductCode)
            .ToDictionary(g => g.Key, g => (int)g.Sum(i => i.Amount));
    }

    public async Task<Dictionary<string, int>> GetProductsInQuarantineAsync(CancellationToken cancellationToken)
    {
        var boxes = await _transportBoxRepository.FindAsync(
            TransportBox.IsInQuarantinePredicate,
            includeDetails: true,
            cancellationToken);

        return boxes
            .SelectMany(b => b.Items)
            .GroupBy(i => i.ProductCode)
            .ToDictionary(g => g.Key, g => (int)g.Sum(i => i.Amount));
    }
}
