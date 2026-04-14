using Anela.Heblo.Domain.Features.Catalog.Products;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
using System.Net;

namespace Anela.Heblo.Adapters.Flexi.Products;

public class FlexiProductClient : IProductWeightClient
{
    private readonly IBoMClient _bomClient;
    private readonly IPriceListClient _priceListClient;
    private readonly ILogger<FlexiProductClient> _logger;

    public FlexiProductClient(
        IBoMClient bomClient,
        IPriceListClient priceListClient,
        ILogger<FlexiProductClient> logger)
    {
        _bomClient = bomClient;
        _priceListClient = priceListClient;
        _logger = logger;
    }

    public async Task<double?> RefreshProductWeight(string productCode, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting weight recalculation for product {ProductCode}", productCode);

        // 1. Get BOM data for the product
        double? weight;
        try
        {
            weight = (await _bomClient.GetBomWeight(productCode, cancellationToken))?.NetWeight;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotImplemented)
        {
            _logger.LogError(ex,
                "FlexiBee kusovnik returned 501 NotImplemented while fetching BoM weight — " +
                "endpoint may be disabled or unsupported on this instance. ProductCode: {ProductCode}",
                productCode);
            throw;
        }

        if (weight == null)
        {
            _logger.LogWarning("No BOM data found for product {ProductCode}", productCode);
            return null;
        }

        var request = new PriceListFlexiDto()
        {
            ProductCode = productCode,
            Weight = weight.Value,
        };

        var result = await _priceListClient.SaveAsync(request, cancellationToken);
        if (!result.IsSuccess)
        {
            _logger.LogError("Failed to update product Weight: {ErrorMessage}", result.GetErrorMessage());
            throw new InvalidOperationException($"Failed to update product Weight: {result.GetErrorMessage()}");
        }

        return request.Weight;
    }
}