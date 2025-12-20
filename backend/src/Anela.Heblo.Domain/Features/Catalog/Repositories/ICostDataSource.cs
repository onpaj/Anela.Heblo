using Anela.Heblo.Domain.Features.Catalog.ValueObjects;

namespace Anela.Heblo.Domain.Features.Catalog.Repositories;

public interface ICostDataSource
{
    Task<Dictionary<string, List<MonthlyCost>>> GetCostsAsync(
        List<string>? productCodes = null,
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        CancellationToken cancellationToken = default);
}