using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Products.Model;
using Anela.Heblo.Domain.Features.Catalog.Eshop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi.Products;

/// <summary>
/// Read-only sweep of all Shoptet products via the REST API. Requests are strictly sequential
/// and spaced by <see cref="ShoptetProductClientOptions.MinRequestIntervalMs"/> to stay under the
/// Shoptet rate limit (the API token is shared with packing/expedition).
/// </summary>
public class ShoptetProductClient : IEshopProductClient
{
    private const string OperationName = "ShoptetProductClient.ListAsync";
    private const string OriginalImageCut = "orig";
    private const string VisibleProduct = "visible";
    private const int ItemsPerPage = 1000;
    private const decimal KilogramsToGrams = 1000m;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly ShoptetProductClientOptions _options;
    private readonly ILogger<ShoptetProductClient> _logger;
    private long? _lastRequestStartTimestamp;

    public ShoptetProductClient(
        HttpClient http,
        IOptions<ShoptetProductClientOptions> options,
        ILogger<ShoptetProductClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<EshopProduct>> ListAsync(CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        _lastRequestStartTimestamp = null;

        try
        {
            var imageBaseUrl = await GetImageBaseUrlAsync(ct);
            var flagTitles = await GetFlagTitlesAsync(ct);
            var flagsByProduct = await GetFlagsByProductAsync(flagTitles, ct);
            var products = await GetAllProductsAsync(ct);

            var result = new List<EshopProduct>();
            foreach (var product in products)
            {
                var detail = await GetAsync<ProductDetailResponse>(
                    $"/api/products/{Uri.EscapeDataString(product.Guid)}?include=images", ct);
                if (detail?.Data is null)
                {
                    throw new InvalidOperationException($"Shoptet product detail {product.Guid} has no data.");
                }

                var flags = flagsByProduct.TryGetValue(product.Guid, out var found)
                    ? found
                    : (IReadOnlyList<EshopProductFlag>)Array.Empty<EshopProductFlag>();
                result.AddRange(MapVariants(detail.Data, imageBaseUrl, flags));
            }

            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Operation {Operation} failed. ExceptionType={ExceptionType} Message={Message} StatusCode={StatusCode} ElapsedMs={ElapsedMs}",
                OperationName,
                ex.GetType().FullName,
                ex.Message,
                (int?)(ex as HttpRequestException)?.StatusCode,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private async Task<string> GetImageBaseUrlAsync(CancellationToken ct)
    {
        var cuts = await GetAsync<EshopImageCutsResponse>("/api/eshop?include=imageCuts", ct);
        var original = cuts?.Data?.ImageCuts.FirstOrDefault(c => c.Name == OriginalImageCut);
        if (string.IsNullOrEmpty(original?.UrlPath))
        {
            throw new InvalidOperationException("Shoptet image cut 'orig' with urlPath was not found.");
        }

        return original.UrlPath;
    }

    private async Task<Dictionary<string, string>> GetFlagTitlesAsync(CancellationToken ct)
    {
        var flags = await GetAsync<FlagListResponse>("/api/products/flags", ct);
        return (flags?.Data?.Flags ?? new List<FlagItem>())
            .GroupBy(f => f.Code)
            .ToDictionary(g => g.Key, g => g.First().Title);
    }

    // Flags come only from the ?flag= filter: it lists products whose flag is in effect now,
    // whereas the detail would also report expired (configured) flags.
    private async Task<Dictionary<string, IReadOnlyList<EshopProductFlag>>> GetFlagsByProductAsync(
        Dictionary<string, string> flagTitles, CancellationToken ct)
    {
        var byProduct = new Dictionary<string, List<EshopProductFlag>>();
        foreach (var (code, title) in flagTitles)
        {
            var products = await GetProductListAsync($"flag={Uri.EscapeDataString(code)}&", ct);
            foreach (var product in products)
            {
                if (!byProduct.TryGetValue(product.Guid, out var list))
                {
                    list = new List<EshopProductFlag>();
                    byProduct[product.Guid] = list;
                }

                list.Add(new EshopProductFlag { Code = code, Title = title });
            }
        }

        return byProduct.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<EshopProductFlag>)kv.Value);
    }

    private Task<List<ProductListItem>> GetAllProductsAsync(CancellationToken ct) =>
        GetProductListAsync("", ct);

    private async Task<List<ProductListItem>> GetProductListAsync(string queryPrefix, CancellationToken ct)
    {
        var items = new List<ProductListItem>();
        var pageCount = 1;
        for (var page = 1; page <= pageCount; page++)
        {
            var response = await GetAsync<ProductListResponse>(
                $"/api/products?{queryPrefix}itemsPerPage={ItemsPerPage}&page={page}", ct);
            if (response?.Data is null)
            {
                throw new InvalidOperationException($"Shoptet product list page {page} has no data.");
            }

            items.AddRange(response.Data.Products);
            pageCount = Math.Max(pageCount, response.Data.Paginator?.PageCount ?? 1);
        }

        return items;
    }

    private static IEnumerable<EshopProduct> MapVariants(
        ProductDetail detail, string imageBaseUrl, IReadOnlyList<EshopProductFlag> flags)
    {
        var images = detail.Images ?? new List<ProductImage>();
        var mainImageUrl = BuildImageUrl(imageBaseUrl, images.FirstOrDefault(i => i.IsMainImage));
        var isProductVisible = detail.Visibility == VisibleProduct;

        return detail.Variants.Select(variant => new EshopProduct
        {
            Code = variant.Code,
            ProductGuid = detail.Guid,
            Name = detail.Name,
            VariantName = variant.Name,
            IsVisible = isProductVisible && variant.Visible,
            ImageUrl = BuildImageUrl(imageBaseUrl, images.FirstOrDefault(i => !string.IsNullOrEmpty(variant.Image) && i.Name == variant.Image))
                       ?? mainImageUrl,
            DefaultImageUrl = mainImageUrl,
            WeightGrams = ParseKilogramsAsGrams(variant.Weight),
            Height = ParsePositive(variant.Height),
            Width = ParsePositive(variant.Width),
            Depth = ParsePositive(variant.Depth),
            AtypicalShipping = variant.AtypicalShipping,
            Flags = flags,
        });
    }

    private static string? BuildImageUrl(string baseUrl, ProductImage? image) =>
        string.IsNullOrEmpty(image?.SeoName) ? null : baseUrl + image.SeoName;

    // Computed in decimal so 1.005 kg is exactly 1005 g (no binary floating-point noise).
    private static double? ParseKilogramsAsGrams(string? value) =>
        TryParseDecimal(value, out var kg) && kg > 0 ? (double)(kg * KilogramsToGrams) : null;

    private static double? ParsePositive(string? value) =>
        TryParseDecimal(value, out var number) && number > 0 ? (double)number : null;

    private static bool TryParseDecimal(string? value, out decimal number) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out number);

    private async Task<T?> GetAsync<T>(string relativeUrl, CancellationToken ct)
    {
        await ThrottleAsync(ct);

        using var response = await _http.GetAsync(relativeUrl, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
    }

    // Enforces MinRequestIntervalMs between request STARTS. Requests are awaited one at a time.
    private async Task ThrottleAsync(CancellationToken ct)
    {
        if (_lastRequestStartTimestamp is { } last && _options.MinRequestIntervalMs > 0)
        {
            var remaining = TimeSpan.FromMilliseconds(_options.MinRequestIntervalMs) - Stopwatch.GetElapsedTime(last);
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, ct);
            }
        }

        _lastRequestStartTimestamp = Stopwatch.GetTimestamp();
    }
}
