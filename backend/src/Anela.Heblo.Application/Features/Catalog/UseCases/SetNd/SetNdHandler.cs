using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.SetNd;

public class SetNdHandler : IRequestHandler<SetNdRequest, SetNdResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly ICatalogNdWriter _ndWriter;
    private readonly ILogger<SetNdHandler> _logger;

    public SetNdHandler(
        ICatalogRepository catalogRepository,
        ICatalogNdWriter ndWriter,
        ILogger<SetNdHandler> logger)
    {
        _catalogRepository = catalogRepository;
        _ndWriter = ndWriter;
        _logger = logger;
    }

    public async Task<SetNdResponse> Handle(SetNdRequest request, CancellationToken cancellationToken)
    {
        var catalogItem = await _catalogRepository.SingleOrDefaultAsync(
            x => x.ProductCode == request.ProductCode,
            cancellationToken);

        if (catalogItem == null)
        {
            _logger.LogWarning("Cannot set ND flag, catalog product not found. ProductCode: {ProductCode}", request.ProductCode);
            return new SetNdResponse(
                ErrorCodes.ProductNotFound,
                new Dictionary<string, string> { { "productCode", request.ProductCode } });
        }

        try
        {
            await _ndWriter.SetAsync(request.ProductCode, request.IsNd, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to write ND flag to the ERP. ProductCode: {ProductCode}, IsNd: {IsNd}",
                request.ProductCode, request.IsNd);
            return new SetNdResponse(
                ErrorCodes.NdWriteFailed,
                new Dictionary<string, string> { { "productCode", request.ProductCode } });
        }

        await ApplyToCatalogCacheAsync(request.ProductCode, request.IsNd, cancellationToken);

        _logger.LogInformation("ND flag for {ProductCode} set to {IsNd}", request.ProductCode, request.IsNd);

        return new SetNdResponse
        {
            ProductCode = request.ProductCode,
            IsNd = request.IsNd
        };
    }

    /// <summary>
    /// The flag is already stored in the ERP at this point, so a failed cache patch must not fail the
    /// request; the hourly attributes refresh picks the change up. Patching the single product (instead
    /// of waiting for the debounced catalog-wide merge) is what lets the UI refetch see the new value.
    /// </summary>
    private async Task ApplyToCatalogCacheAsync(string productCode, bool isNd, CancellationToken cancellationToken)
    {
        try
        {
            await _catalogRepository.ApplyNdFlagAsync(productCode, isNd);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "ND flag for {ProductCode} was saved, but the catalog cache patch failed; the hourly refresh will catch up",
                productCode);
        }
    }
}
