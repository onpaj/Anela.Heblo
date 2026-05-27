using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Features.Manufacture.Services.Workflows;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.ConfirmSemiProductManufacture;

public class ConfirmSemiProductManufactureHandler
    : IRequestHandler<ConfirmSemiProductManufactureRequest, ConfirmSemiProductManufactureResponse>
{
    private const string UnexpectedErrorMessage = "Došlo k neočekávané chybě při potvrzení výroby polotovaru";

    private readonly IConfirmSemiProductManufactureWorkflow _workflow;
    private readonly ILogger<ConfirmSemiProductManufactureHandler> _logger;

    public ConfirmSemiProductManufactureHandler(
        IConfirmSemiProductManufactureWorkflow workflow,
        ILogger<ConfirmSemiProductManufactureHandler> logger)
    {
        _workflow = workflow;
        _logger = logger;
    }

    public async Task<ConfirmSemiProductManufactureResponse> Handle(
        ConfirmSemiProductManufactureRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _workflow.ExecuteAsync(
                request.Id,
                request.ActualQuantity,
                request.ChangeReason,
                cancellationToken);

            if (result.Success)
            {
                return new ConfirmSemiProductManufactureResponse
                {
                    Message = result.Message,
                };
            }

            var errorCode = result.ErrorCode ?? ErrorCodes.InvalidOperation;
            _logger.LogWarning(
                "ConfirmSemiProductManufacture failed for order {OrderId}: {ErrorCode} — {Message}",
                request.Id, errorCode, result.Message);

            return new ConfirmSemiProductManufactureResponse(errorCode)
            {
                Message = result.Message,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error confirming semi-product manufacture for order {OrderId}", request.Id);
            return new ConfirmSemiProductManufactureResponse(ErrorCodes.InternalServerError)
            {
                Message = UnexpectedErrorMessage,
            };
        }
    }
}
