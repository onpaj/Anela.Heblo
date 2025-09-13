namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class BatchDistributionCalculator : IBatchDistributionCalculator
{
    public void OptimizeBatch(ProductBatch batch, bool minimizeResidue = true)
    {
        var variants = batch.ValidVariants;
        double totalWeight = batch.TotalWeight;

        // Binary search for max days
        double low = 0;
        double high = 1000; // arbitrární horní hranice
        double bestDays = 0;

        while (high - low > 0.1)
        {
            double mid = (low + high) / 2;

            double requiredWeight = 0;
            foreach (var v in variants)
            {
                double needed = Math.Max(mid * v.DailySales - v.CurrentStock, 0);
                requiredWeight += Math.Ceiling(needed) * v.Weight;
            }

            if (requiredWeight <= totalWeight)
            {
                bestDays = mid;
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        // Přepočítej finální výrobu
        foreach (var v in variants)
        {
            double needed = Math.Max(bestDays * v.DailySales - v.CurrentStock, 0);
            v.SuggestedAmount = (int)Math.Floor(needed);
        }

        if (minimizeResidue)
        {
            var totalUsedWeight = variants.Select(s => s.SuggestedAmount * s.Weight).Sum();
            var remaining = batch.TotalWeight - totalUsedWeight;
            DistributeRemainingWeight(batch.Variants, remaining);
        }
    }

    private static void DistributeRemainingWeight(IList<ProductVariant> variants, double remainingWeight)
    {
        if (remainingWeight <= 0)
            return;

        // Seřaď varianty podle objemu vzestupně (menší balení mají šanci víc využít zbytek)
        var sortedVariants = variants.OrderByDescending(v => v.Weight).ToList();

        foreach (var variant in sortedVariants)
        {
            int additional = (int)(remainingWeight / variant.Weight);
            if (additional > 0)
            {
                variant.SuggestedAmount += additional;
                remainingWeight -= additional * variant.Weight;
            }

            if (remainingWeight < sortedVariants.Min(v => v.Weight))
                break; // už nelze naplnit žádné další balení
        }
    }
}