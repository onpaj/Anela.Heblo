using Anela.Heblo.Application.Features.Catalog.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;

public class ProductEnrichmentCache : IProductEnrichmentCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<KnowledgeBaseOptions> _options;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private IReadOnlyDictionary<string, ProductEnrichmentEntry> _cache =
        new Dictionary<string, ProductEnrichmentEntry>();
    private DateTime _lastLoaded = DateTime.MinValue;

    public ProductEnrichmentCache(
        IServiceScopeFactory scopeFactory,
        IOptions<KnowledgeBaseOptions> options)
    {
        _scopeFactory = scopeFactory;
        _options = options;
    }

    public async Task<IReadOnlyDictionary<string, ProductEnrichmentEntry>> GetProductLookupAsync(
        CancellationToken ct = default)
    {
        var ttl = TimeSpan.FromMinutes(_options.Value.ProductEnrichmentCacheTtlMinutes);

        if (DateTime.UtcNow - _lastLoaded < ttl)
            return _cache;

        await _lock.WaitAsync(ct);
        try
        {
            if (DateTime.UtcNow - _lastLoaded < ttl)
                return _cache;

            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IProductCatalogQueryService>();

            var products = await service.GetActiveProductsAsync(ct);

            _cache = products.ToDictionary(
                p => p.ProductCode,
                p => new ProductEnrichmentEntry
                {
                    ProductCode = p.ProductCode,
                    ProductName = p.ProductName,
                    Url = p.Url
                });

            _lastLoaded = DateTime.UtcNow;
            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }
}
