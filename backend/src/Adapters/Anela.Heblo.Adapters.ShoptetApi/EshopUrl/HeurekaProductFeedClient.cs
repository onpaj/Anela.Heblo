using System.Xml.Linq;
using Anela.Heblo.Domain.Features.Catalog.EshopUrl;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi.EshopUrl;

public class HeurekaProductFeedClient : IProductEshopUrlClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<HeurekaFeedOptions> _options;

    public HeurekaProductFeedClient(HttpClient httpClient, IOptions<HeurekaFeedOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<IEnumerable<ProductEshopUrl>> GetAllAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(_options.Value.ProductFeedUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var doc = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);

        return doc.Root!
            .Elements("SHOPITEM")
            .Select(item => new ProductEshopUrl
            {
                ProductCode = item.Element("ITEM_ID")?.Value ?? string.Empty,
                Url = item.Element("URL")?.Value ?? string.Empty
            })
            .Where(p => !string.IsNullOrEmpty(p.ProductCode))
            .ToList();
    }
}
