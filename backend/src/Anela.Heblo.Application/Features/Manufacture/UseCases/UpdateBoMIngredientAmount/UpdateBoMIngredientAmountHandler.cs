using Anela.Heblo.Application.Features.Manufacture.ErrorFilters;
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateBoMIngredientAmount;

public class UpdateBoMIngredientAmountHandler
    : IRequestHandler<UpdateBoMIngredientAmountRequest, UpdateBoMIngredientAmountResponse>
{
    private readonly IManufactureClient _manufactureClient;
    private readonly IManufactureErrorTransformer _errorTransformer;
    private readonly ILogger<UpdateBoMIngredientAmountHandler> _logger;

    public UpdateBoMIngredientAmountHandler(
        IManufactureClient manufactureClient,
        IManufactureErrorTransformer errorTransformer,
        ILogger<UpdateBoMIngredientAmountHandler> logger)
    {
        _manufactureClient = manufactureClient ?? throw new ArgumentNullException(nameof(manufactureClient));
        _errorTransformer = errorTransformer ?? throw new ArgumentNullException(nameof(errorTransformer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<UpdateBoMIngredientAmountResponse> Handle(
        UpdateBoMIngredientAmountRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _manufactureClient.UpdateBoMIngredientAmountAsync(
                request.ProductCode,
                request.IngredientCode,
                request.NewAmount,
                cancellationToken);

            _logger.LogInformation(
                "Updated BoM ingredient amount: product {ProductCode} ingredient {IngredientCode} = {NewAmount}",
                request.ProductCode, request.IngredientCode, request.NewAmount);

            return new UpdateBoMIngredientAmountResponse();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Failed to update BoM ingredient amount: product {ProductCode} ingredient {IngredientCode}",
                request.ProductCode, request.IngredientCode);

            return new UpdateBoMIngredientAmountResponse(ex)
            {
                UserMessage = _errorTransformer.Transform(ex)
            };
        }
    }
}
