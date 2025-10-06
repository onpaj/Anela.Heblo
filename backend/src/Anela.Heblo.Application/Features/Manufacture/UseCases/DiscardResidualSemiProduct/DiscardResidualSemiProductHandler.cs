using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.DiscardResidualSemiProduct;

public class DiscardResidualSemiProductHandler : IRequestHandler<DiscardResidualSemiProductRequest, DiscardResidualSemiProductResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly IManufactureClient _manufactureClient;
    private readonly ILogger<DiscardResidualSemiProductHandler> _logger;

    public DiscardResidualSemiProductHandler(
        ICatalogRepository catalogRepository,
        IManufactureClient manufactureClient,
        ILogger<DiscardResidualSemiProductHandler> logger)
    {
        _catalogRepository = catalogRepository;
        _manufactureClient = manufactureClient;
        _logger = logger;
    }

    public async Task<DiscardResidualSemiProductResponse> Handle(
        DiscardResidualSemiProductRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting discard residual semi-product process for manufacture order {ManufactureOrderCode}, semi-product {SemiProductCode}",
                request.ManufactureOrderCode, request.ProductCode);

            var product = await _catalogRepository.GetByIdAsync(request.ProductCode);
            if (product == null)
            {
                return new DiscardResidualSemiProductResponse(ErrorCodes.ProductNotFound);
            }

            var clientRequest = new Domain.Features.Manufacture.DiscardResidualSemiProductRequest
            {
                ManufactureOrderCode = request.ManufactureOrderCode,
                ProductCode = request.ProductCode,
                ProductName = request.ProductName ?? product.ProductName ?? string.Empty,
                CompletionDate = request.CompletionDate,
                CompletedBy = request.CompletedBy,
                MaxAutoDiscardQuantity = request.BatchSize * (product.Properties.AllowedResiduePercentage / 100.0),
            };

            var result = await _manufactureClient.DiscardResidualSemiProductAsync(clientRequest, cancellationToken);

            if (result.Success)
            {
                _logger.LogInformation("Successfully processed discard for manufacture order {ManufactureOrderCode}. Found: {QuantityFound}, Discarded: {QuantityDiscarded}, Manual approval required: {RequiresManualApproval}",
                    request.ManufactureOrderCode, result.QuantityFound, result.QuantityDiscarded, result.RequiresManualApproval);
            }
            else
            {
                _logger.LogWarning("Discard process completed with errors for manufacture order {ManufactureOrderCode}: {ErrorMessage}",
                    request.ManufactureOrderCode, result.ErrorMessage ?? result.Details);
            }

            return new DiscardResidualSemiProductResponse
            {
                Success = result.Success,
                QuantityFound = result.QuantityFound,
                QuantityDiscarded = result.QuantityDiscarded,
                RequiresManualApproval = result.RequiresManualApproval,
                StockMovementReference = result.StockMovementReference,
                Details = result.Details
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during discard residual semi-product process for manufacture order {ManufactureOrderCode}",
                request.ManufactureOrderCode);
            return new DiscardResidualSemiProductResponse(ex);
        }
    }
}