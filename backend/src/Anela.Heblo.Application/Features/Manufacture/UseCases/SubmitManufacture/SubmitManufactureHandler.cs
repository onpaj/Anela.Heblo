using Anela.Heblo.Application.Features.Manufacture.Infrastructure.Exceptions;
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
        catch (ConsumptionMovementFailedException ex)
        {
            _logger.LogError(ex, "Consumption movement failed for manufacture order {ManufactureOrderId}. FlexiBee error: {FlexiBeeError}",
                request.ManufactureOrderNumber, ex.FlexiBeeErrorMessage);

            return new SubmitManufactureResponse(
                ex.ErrorCode,
                new Dictionary<string, string>
                {
                    { "ManufactureOrderCode", ex.ManufactureOrderCode ?? request.ManufactureOrderNumber },
                    { "FlexiBeeError", ex.FlexiBeeErrorMessage ?? "Unknown error" },
                    { "ErrorMessage", ex.Message }
                });
        }
        catch (ProductionMovementFailedException ex)
        {
            _logger.LogError(ex, "Production movement failed for manufacture order {ManufactureOrderId}. FlexiBee error: {FlexiBeeError}. Consumption movement ID: {ConsumptionMovement}. MANUAL ROLLBACK REQUIRED.",
                request.ManufactureOrderNumber, ex.FlexiBeeErrorMessage, ex.ConsumptionMovementReference);

            return new SubmitManufactureResponse(
                ex.ErrorCode,
                new Dictionary<string, string>
                {
                    { "ManufactureOrderCode", ex.ManufactureOrderCode ?? request.ManufactureOrderNumber },
                    { "FlexiBeeError", ex.FlexiBeeErrorMessage ?? "Unknown error" },
                    { "ConsumptionMovementId", ex.ConsumptionMovementReference },
                    { "RollbackInstructions", $"PARTIAL SUCCESS: Consumption movement {ex.ConsumptionMovementReference} was created but production movement failed. Manual rollback required in FlexiBee." },
                    { "ErrorMessage", ex.Message }
                });
        }
        catch (ManufactureSubmissionFailedException ex)
        {
            _logger.LogError(ex, "Manufacture submission validation failed for order {ManufactureOrderId}",
                request.ManufactureOrderNumber);

            return new SubmitManufactureResponse(
                ex.ErrorCode,
                new Dictionary<string, string>
                {
                    { "ManufactureOrderCode", ex.ManufactureOrderCode ?? request.ManufactureOrderNumber },
                    { "ErrorMessage", ex.Message }
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating manufacture for order {ManufactureOrderId}", request.ManufactureOrderNumber);
            return new SubmitManufactureResponse(ex);
        }
    }
}