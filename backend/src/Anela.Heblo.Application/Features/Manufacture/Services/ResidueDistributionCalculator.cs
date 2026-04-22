using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class ResidueDistributionCalculator : IResidueDistributionCalculator
{
    private readonly IManufactureClient _manufactureClient;
    private readonly ICatalogRepository _catalogRepository;

    public ResidueDistributionCalculator(IManufactureClient manufactureClient, ICatalogRepository catalogRepository)
    {
        _manufactureClient = manufactureClient;
        _catalogRepository = catalogRepository;
    }

    public async Task<ResidueDistribution> CalculateAsync(UpdateManufactureOrderDto order, CancellationToken cancellationToken = default)
    {
        if (order.SemiProduct is null)
            return new ResidueDistribution { IsWithinAllowedThreshold = true };

        if (order.Products.All(p => p.ProductCode == order.SemiProduct.ProductCode))
            return new ResidueDistribution { IsWithinAllowedThreshold = true };

        var actualSemiProduct = order.SemiProduct.ActualQuantity ?? order.SemiProduct.PlannedQuantity;

        // Subtract grams that exit as direct semiproduct output (virtual row where ProductCode == SemiProduct.ProductCode)
        var directRowGrams = order.Products
            .Where(p => p.ProductCode == order.SemiProduct.ProductCode)
            .Sum(p => p.ActualQuantity ?? p.PlannedQuantity);
        var effectiveActual = actualSemiProduct - directRowGrams;

        var productData = await BuildProductDataAsync(order, cancellationToken);
        if (productData.Count == 0)
            return new ResidueDistribution { IsWithinAllowedThreshold = true };

        var totalTheoretical = productData.Sum(p => p.TheoreticalConsumption);

        var semiProductCatalog = await _catalogRepository.GetByIdAsync(order.SemiProduct.ProductCode, cancellationToken);
        var allowedResiduePercentage = semiProductCatalog?.Properties.AllowedResiduePercentage ?? 0;

        var difference = effectiveActual - totalTheoretical;
        var differencePercentage = totalTheoretical > 0
            ? (double)Math.Abs(difference) / (double)totalTheoretical * 100
            : 0;
        var isWithinThreshold = differencePercentage <= allowedResiduePercentage;

        var distributions = ComputeDistributions(productData, effectiveActual, totalTheoretical);

        return new ResidueDistribution
        {
            ActualSemiProductQuantity = effectiveActual,
            TheoreticalConsumption = totalTheoretical,
            Difference = difference,
            DifferencePercentage = differencePercentage,
            IsWithinAllowedThreshold = isWithinThreshold,
            AllowedResiduePercentage = allowedResiduePercentage,
            Products = distributions
        };
    }

    private async Task<List<ProductCalculationData>> BuildProductDataAsync(
        UpdateManufactureOrderDto order,
        CancellationToken cancellationToken)
    {
        var result = new List<ProductCalculationData>();

        foreach (var product in order.Products)
        {
            // Skip direct semiproduct output rows — they have no manufacture template
            if (product.ProductCode == order.SemiProduct?.ProductCode)
                continue;

            var actualPieces = product.ActualQuantity ?? product.PlannedQuantity;
            if (actualPieces <= 0)
                continue;

            var template = await _manufactureClient.GetManufactureTemplateAsync(product.ProductCode, cancellationToken);
            var semiProductIngredient = template.Ingredients
                .FirstOrDefault(i => i.ProductType == ProductType.SemiProduct);

            if (semiProductIngredient is null)
                continue;

            var theoreticalGramsPerUnit = (decimal)semiProductIngredient.Amount;
            var theoreticalConsumption = theoreticalGramsPerUnit * actualPieces;

            result.Add(new ProductCalculationData(
                product.ProductCode,
                product.ProductName,
                actualPieces,
                theoreticalGramsPerUnit,
                theoreticalConsumption));
        }

        return result;
    }

    private static List<ProductConsumptionDistribution> ComputeDistributions(
        List<ProductCalculationData> productData,
        decimal actualSemiProduct,
        decimal totalTheoretical)
    {
        var distributions = productData
            .Select(p =>
            {
                var proportionRatio = totalTheoretical > 0
                    ? (double)p.TheoreticalConsumption / (double)totalTheoretical
                    : 0;

                var adjustedConsumption = Math.Round(actualSemiProduct * (decimal)proportionRatio, 4);

                return new ProductConsumptionDistribution
                {
                    ProductCode = p.ProductCode,
                    ProductName = p.ProductName,
                    ActualPieces = p.ActualPieces,
                    TheoreticalGramsPerUnit = p.TheoreticalGramsPerUnit,
                    TheoreticalConsumption = p.TheoreticalConsumption,
                    AdjustedConsumption = adjustedConsumption,
                    ProportionRatio = proportionRatio
                };
            })
            .ToList();

        FixRoundingRemainder(distributions, actualSemiProduct);

        foreach (var d in distributions)
        {
            d.AdjustedGramsPerUnit = d.ActualPieces > 0
                ? d.AdjustedConsumption / d.ActualPieces
                : 0;
        }

        return distributions;
    }

    private static void FixRoundingRemainder(
        List<ProductConsumptionDistribution> distributions,
        decimal actualSemiProduct)
    {
        var sum = distributions.Sum(d => d.AdjustedConsumption);
        var remainder = actualSemiProduct - sum;

        if (remainder == 0)
            return;

        var largest = distributions.OrderByDescending(d => d.AdjustedConsumption).First();
        largest.AdjustedConsumption += remainder;
    }

    private record ProductCalculationData(
        string ProductCode,
        string ProductName,
        decimal ActualPieces,
        decimal TheoreticalGramsPerUnit,
        decimal TheoreticalConsumption);
}
