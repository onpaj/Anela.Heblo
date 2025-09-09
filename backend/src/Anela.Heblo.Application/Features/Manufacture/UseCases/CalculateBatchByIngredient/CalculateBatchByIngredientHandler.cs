using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchBySize;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchByIngredient;

public class CalculateBatchByIngredientHandler : IRequestHandler<CalculateBatchByIngredientRequest, CalculateBatchByIngredientResponse>
{
    private readonly IManufactureRepository _manufactureRepository;

    public CalculateBatchByIngredientHandler(IManufactureRepository manufactureRepository)
    {
        _manufactureRepository = manufactureRepository;
    }

    public async Task<CalculateBatchByIngredientResponse> Handle(CalculateBatchByIngredientRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var template = await _manufactureRepository.GetManufactureTemplateAsync(request.ProductCode, cancellationToken);

            if (template == null)
            {
                return new CalculateBatchByIngredientResponse(ErrorCodes.ManufactureTemplateNotFound,
                    new Dictionary<string, string> { { "ProductCode", request.ProductCode } });
            }

            var targetIngredient = template.Ingredients.FirstOrDefault(i => i.ProductCode == request.IngredientCode);
            if (targetIngredient == null)
            {
                return new CalculateBatchByIngredientResponse(ErrorCodes.IngredientNotFoundInTemplate,
                    new Dictionary<string, string> { { "IngredientCode", request.IngredientCode } });
            }

            if (targetIngredient.Amount <= 0)
            {
                return new CalculateBatchByIngredientResponse(ErrorCodes.InvalidIngredientAmount,
                    new Dictionary<string, string> { { "Amount", targetIngredient.Amount.ToString() } });
            }

            var scaleFactor = request.DesiredIngredientAmount / targetIngredient.Amount;
            var newBatchSize = template.OriginalAmount * scaleFactor;

            return new CalculateBatchByIngredientResponse
            {
                Success = true,
                ProductCode = template.ProductCode,
                ProductName = template.ProductName,
                OriginalBatchSize = template.OriginalAmount,
                NewBatchSize = Math.Round(newBatchSize, 2),
                ScaleFactor = scaleFactor,
                ScaledIngredientCode = targetIngredient.ProductCode,
                ScaledIngredientName = targetIngredient.ProductName,
                ScaledIngredientOriginalAmount = targetIngredient.Amount,
                ScaledIngredientNewAmount = request.DesiredIngredientAmount,
                Ingredients = template.Ingredients.Select(i => new CalculatedIngredientDto
                {
                    ProductCode = i.ProductCode,
                    ProductName = i.ProductName,
                    OriginalAmount = i.Amount,
                    CalculatedAmount = Math.Round(i.Amount * scaleFactor, 2),
                    Price = i.Price
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            return new CalculateBatchByIngredientResponse(ErrorCodes.Exception,
                new Dictionary<string, string> { { "Message", ex.Message } });
        }
    }
}