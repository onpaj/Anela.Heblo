using Anela.Heblo.Domain.Features.Catalog.ValueObjects;

namespace Anela.Heblo.Domain.Features.Catalog.CostProviders;

public interface ICostProvider
{
    Task<Dictionary<string, List<MonthlyCost>>> GetCostsAsync(
        List<string>? productCodes = null,
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        CancellationToken cancellationToken = default);

    Task RefreshAsync(CancellationToken ct = default);
}