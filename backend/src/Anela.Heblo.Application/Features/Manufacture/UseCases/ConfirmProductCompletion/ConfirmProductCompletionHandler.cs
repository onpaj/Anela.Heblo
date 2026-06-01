using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Features.Manufacture.Services.Workflows;
using Anela.Heblo.Application.Shared;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.ConfirmProductCompletion;

public class ConfirmProductCompletionHandler
    : IRequestHandler<ConfirmProductCompletionRequest, ConfirmProductCompletionResponse>
{
    private const string UnexpectedErrorMessage = "Došlo k neočekávané chybě při dokončení výroby produktů";

    private readonly IConfirmProductCompletionWorkflow _workflow;
    private readonly IMapper _mapper;
    private readonly ILogger<ConfirmProductCompletionHandler> _logger;

    public ConfirmProductCompletionHandler(
        IConfirmProductCompletionWorkflow workflow,
        IMapper mapper,
        ILogger<ConfirmProductCompletionHandler> logger)
    {
        _workflow = workflow;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<ConfirmProductCompletionResponse> Handle(
        ConfirmProductCompletionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var productActualQuantities = request.Products.ToDictionary(p => p.Id, p => p.ActualQuantity);

            var result = await _workflow.ExecuteAsync(
                request.Id,
                productActualQuantities,
                request.OverrideConfirmed,
                request.ChangeReason,
                cancellationToken);

            if (result.RequiresConfirmation)
            {
                if (result.Distribution is null)
                {
                    throw new InvalidOperationException("Distribution cannot be null when mapping to DTO");
                }

                return new ConfirmProductCompletionResponse
                {
                    RequiresConfirmation = true,
                    Distribution = _mapper.Map<ResidueDistributionDto>(result.Distribution),
                };
            }

            if (result.Success)
            {
                return new ConfirmProductCompletionResponse();
            }

            _logger.LogWarning(
                "ConfirmProductCompletion failed for order {OrderId}: {Message}",
                request.Id, result.ErrorMessage);

            return new ConfirmProductCompletionResponse(ErrorCodes.InvalidOperation)
            {
                Message = result.ErrorMessage,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error confirming product completion for order {OrderId}", request.Id);
            return new ConfirmProductCompletionResponse(ErrorCodes.InternalServerError)
            {
                Message = UnexpectedErrorMessage,
            };
        }
    }
}
