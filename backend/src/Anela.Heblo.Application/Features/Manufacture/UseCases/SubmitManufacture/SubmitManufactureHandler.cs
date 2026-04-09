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
            var manufactureId = await _manufactureClient.SubmitManufactureAsync(
                request.ToClientRequest(), cancellationToken);

            _logger.LogInformation("Successfully created manufacture {ManufactureId} for order {ManufactureOrderNumber}",
                manufactureId, request.ManufactureOrderNumber);

            return new SubmitManufactureResponse
            {
                ManufactureId = manufactureId
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
}