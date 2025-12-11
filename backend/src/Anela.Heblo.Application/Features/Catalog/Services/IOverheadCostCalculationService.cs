using Anela.Heblo.Domain.Features.Catalog.ValueObjects;

namespace Anela.Heblo.Application.Features.Catalog.Services;

public interface IOverheadCostCalculationService
{
    Task<Dictionary<string, List<MonthlyCost>>> GetCostsAsync(
        List<string>? productCodes = null,
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        CancellationToken cancellationToken = default);

    bool IsLoaded { get; }

    Task Reload();
}
