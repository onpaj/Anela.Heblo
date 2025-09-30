using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufacture;

public class SubmitManufactureHandler : IRequestHandler<SubmitManufactureRequest, SubmitManufactureResponse>
{
    private readonly IManufactureOrderRepository _manufactureOrderRepository;
    private readonly IManufactureClient _manufactureClient;
    private readonly ILogger<SubmitManufactureHandler> _logger;

    public SubmitManufactureHandler(
        IManufactureOrderRepository manufactureOrderRepository,
        IManufactureClient manufactureClient,
        ILogger<SubmitManufactureHandler> logger)
    {
        _manufactureOrderRepository = manufactureOrderRepository;
        _manufactureClient = manufactureClient;
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
            };

            var manufactureId = await _manufactureClient.SubmitManufactureAsync(clientRequest, cancellationToken);

            _logger.LogInformation("Successfully created manufacture {ManufactureId} for order {ManufactureOrderId}",
                manufactureId, request.ManufactureOrderNumber);

            return new SubmitManufactureResponse
            {
                ManufactureId = manufactureId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating manufacture for order {ManufactureOrderId}", request.ManufactureOrderNumber);
            return new SubmitManufactureResponse(ex);
        }
    }
}