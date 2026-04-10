using Anela.Heblo.Application.Features.Manufacture.ErrorFilters;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufacture;

public class SubmitManufactureHandler : IRequestHandler<SubmitManufactureRequest, SubmitManufactureResponse>
{
    private readonly IManufactureClient _manufactureClient;
    private readonly IManufactureErrorTransformer _errorTransformer;
    private readonly ILogger<SubmitManufactureHandler> _logger;

    public SubmitManufactureHandler(
        IManufactureClient manufactureClient,
        IManufactureErrorTransformer errorTransformer,
        ILogger<SubmitManufactureHandler> logger)
    {
        _manufactureClient = manufactureClient;
        _errorTransformer = errorTransformer;
        _logger = logger;
    }

    public async Task<SubmitManufactureResponse> Handle(
        SubmitManufactureRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var clientRequest = new SubmitManufactureClientRequest
            {
                ManufactureOrderCode = request.ManufactureOrderNumber,
                ManufactureInternalNumber = request.ManufactureInternalNumber,
                Date = request.Date,
                CreatedBy = request.CreatedBy,
                ManufactureType = request.ManufactureType,
                Items = request.Items.Select(item => new SubmitManufactureClientItem
                {
                    ProductCode = item.ProductCode,
                    Amount = item.Amount,
                    ProductName = item.Name,
                }).ToList(),
                LotNumber = request.LotNumber,
                ExpirationDate = request.ExpirationDate,
                ResidueDistribution = request.ResidueDistribution,
            };

            var clientResponse = await _manufactureClient.SubmitManufactureAsync(clientRequest, cancellationToken);

            _logger.LogInformation("Successfully created manufacture {ManufactureId} for order {ManufactureOrderId}",
                clientResponse.ManufactureId, request.ManufactureOrderNumber);

            return new SubmitManufactureResponse
            {
                ManufactureId = clientResponse.ManufactureId,
                MaterialIssueForSemiProductDocCode = clientResponse.MaterialIssueForSemiProductDocCode,
                SemiProductReceiptDocCode = clientResponse.SemiProductReceiptDocCode,
                SemiProductIssueForProductDocCode = clientResponse.SemiProductIssueForProductDocCode,
                MaterialIssueForProductDocCode = clientResponse.MaterialIssueForProductDocCode,
                ProductReceiptDocCode = clientResponse.ProductReceiptDocCode,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating manufacture for order {ManufactureOrderId}", request.ManufactureOrderNumber);
            return new SubmitManufactureResponse(ex)
            {
                UserMessage = _errorTransformer.Transform(ex)
            };
        }
    }
}