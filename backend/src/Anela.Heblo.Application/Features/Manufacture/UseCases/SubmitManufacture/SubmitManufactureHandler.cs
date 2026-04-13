using Anela.Heblo.Application.Features.Manufacture.ErrorFilters;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufacture;

public class SubmitManufactureHandler : IRequestHandler<SubmitManufactureRequest, SubmitManufactureResponse>
{
    private readonly IManufactureClient _manufactureClient;
    private readonly IManufactureOrderRepository _repository;
    private readonly IManufactureErrorTransformer _errorTransformer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SubmitManufactureHandler> _logger;

    public SubmitManufactureHandler(
        IManufactureClient manufactureClient,
        IManufactureOrderRepository repository,
        IManufactureErrorTransformer errorTransformer,
        TimeProvider timeProvider,
        ILogger<SubmitManufactureHandler> logger)
    {
        _manufactureClient = manufactureClient;
        _repository = repository;
        _errorTransformer = errorTransformer;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<SubmitManufactureResponse> Handle(
        SubmitManufactureRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var clientResponse = await _manufactureClient.SubmitManufactureAsync(
                request.ToClientRequest(), cancellationToken);

            _logger.LogInformation("Successfully created manufacture {ManufactureId} for order {ManufactureOrderId}",
                clientResponse.ManufactureId, request.ManufactureOrderNumber);

            await PersistDocCodesAsync(request.ManufactureOrderId, clientResponse, cancellationToken);

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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error creating manufacture for order {ManufactureOrderNumber}", request.ManufactureOrderNumber);
            return new SubmitManufactureResponse(ex)
            {
                UserMessage = _errorTransformer.Transform(ex)
            };
        }
    }

    private async Task PersistDocCodesAsync(
        int orderId,
        SubmitManufactureClientResponse clientResponse,
        CancellationToken cancellationToken)
    {
        if (orderId == 0)
            return;

        var order = await _repository.GetOrderByIdAsync(orderId, cancellationToken);
        if (order == null)
        {
            _logger.LogWarning("Cannot persist FlexiDoc codes: order {OrderId} not found", orderId);
            return;
        }

        var now = _timeProvider.GetUtcNow().DateTime;

        if (clientResponse.MaterialIssueForSemiProductDocCode != null)
        {
            order.DocMaterialIssueForSemiProduct = clientResponse.MaterialIssueForSemiProductDocCode;
            order.DocMaterialIssueForSemiProductDate = now;
        }

        if (clientResponse.SemiProductReceiptDocCode != null)
        {
            order.DocSemiProductReceipt = clientResponse.SemiProductReceiptDocCode;
            order.DocSemiProductReceiptDate = now;
        }

        if (clientResponse.SemiProductIssueForProductDocCode != null)
        {
            order.DocSemiProductIssueForProduct = clientResponse.SemiProductIssueForProductDocCode;
            order.DocSemiProductIssueForProductDate = now;
        }

        if (clientResponse.MaterialIssueForProductDocCode != null)
        {
            order.DocMaterialIssueForProduct = clientResponse.MaterialIssueForProductDocCode;
            order.DocMaterialIssueForProductDate = now;
        }

        if (clientResponse.ProductReceiptDocCode != null)
        {
            order.DocProductReceipt = clientResponse.ProductReceiptDocCode;
            order.DocProductReceiptDate = now;
        }

        await _repository.UpdateOrderAsync(order, cancellationToken);

        _logger.LogInformation("Persisted FlexiDoc codes for order {OrderId}", orderId);
    }
}