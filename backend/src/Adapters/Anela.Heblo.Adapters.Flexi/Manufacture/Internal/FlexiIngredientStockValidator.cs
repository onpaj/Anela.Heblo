using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal sealed class FlexiIngredientStockValidator : IFlexiIngredientStockValidator
{
    private readonly IErpStockClient _stockClient;
    private readonly TimeProvider _timeProvider;

    public FlexiIngredientStockValidator(IErpStockClient stockClient, TimeProvider timeProvider)
    {
        _stockClient = stockClient ?? throw new ArgumentNullException(nameof(stockClient));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task ValidateAsync(
        Dictionary<string, IngredientRequirement> ingredientRequirements,
        CancellationToken cancellationToken)
    {
        var stockDate = _timeProvider.GetLocalNow().DateTime;
        var insufficientIngredients = new List<string>();

        foreach (var (ingredientCode, requirement) in ingredientRequirements)
        {
            int warehouseId = FlexiWarehouseResolver.ForProductType(requirement.ProductType);

            var stockItems = await _stockClient.StockToDateAsync(stockDate, warehouseId, cancellationToken);
            var ingredientStock = stockItems.FirstOrDefault(s => s.ProductCode == ingredientCode);
            var availableStock = ingredientStock != null ? (double)ingredientStock.Stock : 0;

            if (availableStock < requirement.RequiredAmount)
            {
                insufficientIngredients.Add(
                    $"{requirement.ProductName} ({ingredientCode}): Required {requirement.RequiredAmount:F2}, Available {availableStock:F2}"
                );
            }
        }

        if (insufficientIngredients.Any())
        {
            throw new FlexiManufactureException(
                FlexiManufactureOperationKind.StockValidation,
                $"Insufficient stock for ingredients: {string.Join("; ", insufficientIngredients)}");
        }
    }
}
