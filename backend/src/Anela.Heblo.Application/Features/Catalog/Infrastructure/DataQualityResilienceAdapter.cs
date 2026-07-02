using Anela.Heblo.Application.Features.DataQuality.Contracts;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class DataQualityResilienceAdapter : IDqtResilienceService
{
    private readonly ICatalogResilienceService _resilienceService;

    public DataQualityResilienceAdapter(ICatalogResilienceService resilienceService)
    {
        _resilienceService = resilienceService;
    }

    public Task<T> ExecuteWithResilienceAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default) =>
        _resilienceService.ExecuteWithResilienceAsync(operation, operationName, cancellationToken);
}
