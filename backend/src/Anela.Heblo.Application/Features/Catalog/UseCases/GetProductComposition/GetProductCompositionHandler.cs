using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;

public class GetProductCompositionHandler : IRequestHandler<GetProductCompositionRequest, GetProductCompositionResponse>
{
    private readonly IManufactureClient _manufactureClient;

    public GetProductCompositionHandler(IManufactureClient manufactureClient)
    {
        _manufactureClient = manufactureClient;
    }

    public async Task<GetProductCompositionResponse> Handle(
        GetProductCompositionRequest request,
        CancellationToken cancellationToken)
    {
        var template = await _manufactureClient.GetManufactureTemplateAsync(
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
