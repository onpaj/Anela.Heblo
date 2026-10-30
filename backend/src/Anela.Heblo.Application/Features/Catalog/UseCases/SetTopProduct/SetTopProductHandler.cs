using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.SetTopProduct;

public class SetTopProductHandler : IRequestHandler<SetTopProductRequest, SetTopProductResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly ICatalogTopProductWriter _topProductWriter;
    private readonly ILogger<SetTopProductHandler> _logger;

    public SetTopProductHandler(
        ICatalogRepository catalogRepository,
        ICatalogTopProductWriter topProductWriter,
        ILogger<SetTopProductHandler> logger)
    {
        _catalogRepository = catalogRepository;
        _topProductWriter = topProductWriter;
        _logger = logger;
    }

    public async Task<SetTopProductResponse> Handle(SetTopProductRequest request, CancellationToken cancellationToken)
    {
        var catalogItem = await _catalogRepository.SingleOrDefaultAsync(
            x => x.ProductCode == request.ProductCode,
            cancellationToken);

        if (catalogItem == null)
        {
            _logger.LogWarning("Cannot set top product flag, catalog product not found. ProductCode: {ProductCode}", request.ProductCode);
            return new SetTopProductResponse(
                ErrorCodes.ProductNotFound,
                new Dictionary<string, string> { { "productCode", request.ProductCode } });
        }

        try
        {
            await _topProductWriter.SetAsync(request.ProductCode, request.IsTopProduct, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to write top product flag to the ERP. ProductCode: {ProductCode}, IsTopProduct: {IsTopProduct}",
                request.ProductCode, request.IsTopProduct);
            return new SetTopProductResponse(
                ErrorCodes.TopProductWriteFailed,
                new Dictionary<string, string> { { "productCode", request.ProductCode } });
        }

        await RefreshAttributesAfterWriteAsync(request.ProductCode, cancellationToken);

        _logger.LogInformation("Top product flag for {ProductCode} set to {IsTopProduct}", request.ProductCode, request.IsTopProduct);

        return new SetTopProductResponse
        {
            ProductCode = request.ProductCode,
            IsTopProduct = request.IsTopProduct
        };
    }

    /// <summary>
    /// The flag is already stored in the ERP at this point, so a failed refresh must not fail the request;
    /// the hourly attributes refresh picks the change up.
    /// </summary>
    private async Task RefreshAttributesAfterWriteAsync(string productCode, CancellationToken cancellationToken)
    {
        try
        {
            await _catalogRepository.RefreshAttributesData(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Top product flag for {ProductCode} was saved, but the catalog attributes refresh failed; the hourly refresh will catch up",
                productCode);
        }
    }
}
