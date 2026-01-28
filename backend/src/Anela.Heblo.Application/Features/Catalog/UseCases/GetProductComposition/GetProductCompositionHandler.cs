using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;

public class GetProductCompositionHandler : IRequestHandler<GetProductCompositionRequest, GetProductCompositionResponse>
{
    private readonly IManufactureRepository _manufactureRepository;

    public GetProductCompositionHandler(IManufactureRepository manufactureRepository)
    {
        _manufactureRepository = manufactureRepository;
    }

    public async Task<GetProductCompositionResponse> Handle(
        GetProductCompositionRequest request,
        CancellationToken cancellationToken)
    {
        var template = await _manufactureRepository.GetManufactureTemplateAsync(
            request.ProductCode,
            cancellationToken);

        if (template == null)
        {
            return new GetProductCompositionResponse
            {
                Ingredients = new List<IngredientDto>()
            };
        }

        var ingredients = template.Ingredients
            .Select(i => new IngredientDto
            {
                ProductCode = i.ProductCode,
                ProductName = i.ProductName,
                Amount = i.Amount,
                Unit = "g" // TODO: Determine unit from product type or configuration
            })
            .ToList();

        return new GetProductCompositionResponse
        {
            Ingredients = ingredients
        };
    }
}
