using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;

public class GetProductCompositionHandler
    : IRequestHandler<GetProductCompositionRequest, GetProductCompositionResponse>
{
    private readonly IManufactureClient _manufactureClient;
    private readonly IProductIngredientOrderRepository _orderRepository;

    public GetProductCompositionHandler(
        IManufactureClient manufactureClient,
        IProductIngredientOrderRepository orderRepository)
    {
        _manufactureClient = manufactureClient;
        _orderRepository = orderRepository;
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

        var savedOrders = await _orderRepository.ListByParentAsync(
            request.ProductCode,
            cancellationToken);

        var orderByCode = savedOrders.ToDictionary(
            x => x.IngredientProductCode,
            x => x.SortOrder);

        var sorted = template.Ingredients
            .Select(i => new
            {
                Ingredient = i,
                Rank = orderByCode.TryGetValue(i.ProductCode, out var s) ? s : int.MaxValue
            })
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Ingredient.ProductName)
            .Select((x, index) => new IngredientDto
            {
                ProductCode = x.Ingredient.ProductCode,
                ProductName = x.Ingredient.ProductName,
                Amount = x.Ingredient.Amount,
                Unit = "g",
                Order = index + 1
            })
            .ToList();

        return new GetProductCompositionResponse
        {
            Ingredients = sorted
        };
    }
}
