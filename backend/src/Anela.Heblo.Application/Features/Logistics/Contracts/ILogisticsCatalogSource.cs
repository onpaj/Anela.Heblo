using Anela.Heblo.Application.Features.Logistics.Contracts.Models;

namespace Anela.Heblo.Application.Features.Logistics.Contracts;

public interface ILogisticsCatalogSource
{
    Task<IReadOnlyList<LogisticsGiftPackageItem>> GetGiftPackageSetsAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);

    Task<LogisticsGiftPackageItem?> GetGiftPackageAsync(
        string code, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);

    Task<LogisticsCatalogItem?> GetCatalogItemAsync(
        string code, CancellationToken cancellationToken);
}
