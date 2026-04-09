using Anela.Heblo.Domain.Features.Catalog.Lots;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal sealed class FefoConsumptionAllocator : IFefoConsumptionAllocator
{
    public const double AllocationEpsilon = 0.001;

    public List<ConsumptionItem> Allocate(
        Dictionary<string, IngredientRequirement> ingredientRequirements,
        Dictionary<string, List<CatalogLot>> ingredientLots,
        string sourceProductCode)
    {
        var consumptionItems = new List<ConsumptionItem>();

        foreach (var (ingredientCode, requirement) in ingredientRequirements)
        {
            if (!requirement.HasLots)
            {
                // For items without lots, create a single consumption item with full amount
                consumptionItems.Add(new ConsumptionItem
                {
                    ProductCode = ingredientCode,
                    ProductName = requirement.ProductName,
                    ProductType = requirement.ProductType,
                    LotNumber = null,
                    Expiration = null,
                    Amount = requirement.RequiredAmount,
                    SourceProductCode = sourceProductCode
                });
                continue;
            }

            // For items with lots, use FEFO allocation
            var lots = ingredientLots[ingredientCode];
            var sortedLots = lots
                .OrderBy(l => l.Expiration ?? DateOnly.MaxValue)
                .ThenBy(s => s.Id)
                .ToList();
            double remainingToAllocate = requirement.RequiredAmount;

            foreach (var lot in sortedLots)
            {
                if (remainingToAllocate <= 0)
                {
                    break;
                }

                var amountFromThisLot = Math.Min(remainingToAllocate, (double)lot.Amount);

                consumptionItems.Add(new ConsumptionItem
                {
                    ProductCode = ingredientCode,
                    ProductName = requirement.ProductName,
                    ProductType = requirement.ProductType,
                    LotNumber = lot.Lot,
                    Expiration = lot.Expiration,
                    Amount = amountFromThisLot,
                    SourceProductCode = sourceProductCode
                });

                remainingToAllocate -= amountFromThisLot;
            }

            if (remainingToAllocate > AllocationEpsilon)
            {
                throw new FlexiManufactureException(
                    FlexiManufactureOperationKind.Allocation,
                    $"Cannot allocate full amount for ingredient {requirement.ProductCode}: {remainingToAllocate:F3} remaining");
            }
        }

        return consumptionItems;
    }
}
