using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.Model;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Pricing.Services;

/// <summary>
/// The single place that knows the pricing rules. Pure: no clock, no repository, no I/O.
///
/// A margin is a function of price and cost, so it cannot be the durable representation of
/// an edit — editing M0 and then the price would leave a stored M0 stale. Every edit is
/// therefore normalised to the cost it implies, and margins are always derived.
/// </summary>
public class PricingSimulationCalculator : IPricingSimulationCalculator
{
    public PricingSimulationResult Calculate(
        IReadOnlyList<PricingBaselineRow> baseline,
        IReadOnlyList<PricingOverrideDto> overrides,
        PricingEditDto? edit)
    {
        var overrideByCode = new Dictionary<string, PricingOverrideDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var over in overrides)
        {
            if (!string.IsNullOrEmpty(over.ProductCode))
            {
                overrideByCode[over.ProductCode] = over;
            }
        }

        if (edit is not null)
        {
            var target = baseline.FirstOrDefault(b =>
                string.Equals(b.ProductCode, edit.ProductCode, StringComparison.OrdinalIgnoreCase));

            // An edit naming a product the baseline does not contain used to be dropped
            // while still returning success -- the client cleared the cell's error and
            // resynced it to the old value, so the user's edit just vanished. Reject it
            // so the cell can say why (stale filter, product gone from the catalogue).
            if (target is null)
            {
                throw new PricingEditException(
                    ErrorCodes.PricingProductNotInBaseline,
                    new Dictionary<string, string>
                    {
                        { "productCode", edit.ProductCode },
                        { "field", edit.Field.ToString() }
                    });
            }

            overrideByCode.TryGetValue(edit.ProductCode, out var existing);
            overrideByCode[edit.ProductCode] = ApplyEdit(target, existing, edit);
        }

        var rows = baseline
            .Select(b => BuildRow(b, overrideByCode.GetValueOrDefault(b.ProductCode)))
            .ToList();

        return new PricingSimulationResult(
            rows,
            BuildTotals(rows),
            overrideByCode.Values.ToList());
    }

    /// <summary>
    /// Translates one user gesture into the normalised override for that row: price,
    /// material cost, manufacturing cost and forecast quantity — the independent variables.
    /// </summary>
    private static PricingOverrideDto ApplyEdit(
        PricingBaselineRow baseline, PricingOverrideDto? existing, PricingEditDto edit)
    {
        // The override stays SPARSE: a field is pinned only once an edit actually
        // determines it, and anything still null keeps riding the live baseline via
        // BuildRow's `over?.X ?? baseline.X`. Densifying it here (writing all four
        // fields back on every edit) silently froze the untouched ones -- editing only
        // the forecast quantity pinned that row's price and costs too, so a later
        // catalogue price change never reached the row even though the scenario
        // correctly flagged BaselineDrifted.
        var price = existing?.Price;
        var material = existing?.MaterialCost;
        var manufacturing = existing?.ManufacturingCost;
        var quantity = existing?.ForecastQuantity;

        // Derivations read the row's current EFFECTIVE state (pinned value if there is
        // one, else the live baseline) without pinning anything by the act of reading.
        var effectivePrice = price ?? baseline.Price;
        var effectiveMaterial = material ?? baseline.MaterialCost;

        switch (edit.Field)
        {
            case PricingEditField.Price:
                price = edit.Value;
                break;

            // The cost cells are the plainest edits there are: they pin exactly the
            // independent variable the user typed. M0/M1 then fall out of BuildRow's
            // derivation, which is what keeps Cena / Materiál / Výroba / M0 / M1
            // readable as one consistent row whichever of them was edited.
            case PricingEditField.MaterialCost:
                material = edit.Value;
                break;

            case PricingEditField.ManufacturingCost:
                manufacturing = edit.Value;
                break;

            case PricingEditField.M0Amount:
                // M0 = P - Cm  =>  Cm = P - M0. Manufacturing is untouched, so M1 shifts with M0.
                material = effectivePrice - edit.Value;
                break;

            case PricingEditField.M0Percentage:
                material = effectivePrice - (effectivePrice * edit.Value / 100m);
                break;

            case PricingEditField.M1Amount:
                // M1 = P - Cm - Cf  =>  Cf = (P - Cm) - M1. Material is untouched, so M0 holds.
                manufacturing = (effectivePrice - effectiveMaterial) - edit.Value;
                break;

            case PricingEditField.M1Percentage:
                manufacturing = (effectivePrice - effectiveMaterial) - (effectivePrice * edit.Value / 100m);
                break;

            case PricingEditField.ForecastQuantity:
                quantity = (double)edit.Value;
                break;
        }

        // Validation needs the concrete row the edit implies, so resolve against the
        // baseline here -- without writing those fallbacks back into the override.
        Validate(
            edit,
            price ?? baseline.Price,
            material ?? baseline.MaterialCost,
            manufacturing ?? baseline.ManufacturingCost,
            quantity ?? baseline.Quantity);

        return new PricingOverrideDto
        {
            ProductCode = baseline.ProductCode,
            Price = price,
            MaterialCost = material,
            ManufacturingCost = manufacturing,
            ForecastQuantity = quantity
        };
    }

    /// <summary>
    /// An edit is valid exactly when the row it implies is physically possible: a positive
    /// price and non-negative costs. Negative margins are deliberately permitted — showing
    /// that a product loses money at a given price is one of the things this tool is for.
    /// </summary>
    private static void Validate(
        PricingEditDto edit, decimal price, decimal material, decimal manufacturing, double quantity)
    {
        var context = new Dictionary<string, string>
        {
            { "productCode", edit.ProductCode },
            { "field", edit.Field.ToString() },
            { "value", edit.Value.ToString("0.##") }
        };

        if (price <= 0m)
            throw new PricingEditException(ErrorCodes.PricingInvalidPrice, context);

        if (material < 0m)
            throw new PricingEditException(ErrorCodes.PricingNegativeMaterialCost, context);

        if (manufacturing < 0m)
            throw new PricingEditException(ErrorCodes.PricingNegativeManufacturingCost, context);

        if (quantity < 0d)
            throw new PricingEditException(ErrorCodes.PricingNegativeQuantity, context);
    }

    private static PricingRowDto BuildRow(PricingBaselineRow baseline, PricingOverrideDto? over)
    {
        var price = over?.Price ?? baseline.Price;
        var material = over?.MaterialCost ?? baseline.MaterialCost;
        var manufacturing = over?.ManufacturingCost ?? baseline.ManufacturingCost;
        var quantity = over?.ForecastQuantity ?? baseline.Quantity;

        var m0 = price - material;
        var m1 = price - material - manufacturing;

        // Same M0/M1 formula, applied to the untouched baseline inputs -- the single
        // place that knows this rule (see the class doc comment) also owns the
        // "before" side, so the screen and the exported ceník can never disagree.
        var baselineM0 = baseline.Price - baseline.MaterialCost;
        var baselineM1 = baselineM0 - baseline.ManufacturingCost;
        var baselineM0Percentage = Percentage(baselineM0, baseline.Price);
        var baselineM1Percentage = Percentage(baselineM1, baseline.Price);

        return new PricingRowDto
        {
            ProductCode = baseline.ProductCode,
            ProductName = baseline.ProductName,

            BaselinePrice = baseline.Price,
            BaselineMaterialCost = baseline.MaterialCost,
            BaselineManufacturingCost = baseline.ManufacturingCost,
            BaselineQuantity = baseline.Quantity,

            Price = price,
            MaterialCost = material,
            ManufacturingCost = manufacturing,
            ForecastQuantity = quantity,

            M0Amount = m0,
            M0Percentage = Percentage(m0, price),
            M1Amount = m1,
            M1Percentage = Percentage(m1, price),
            BaselineM0Amount = baselineM0,
            BaselineM1Amount = baselineM1,
            BaselineM0Percentage = baselineM0Percentage,
            BaselineM1Percentage = baselineM1Percentage,

            IsEdited = over is not null,
            IsExcluded = !baseline.HasData
        };
    }

    private static PricingTotalsDto BuildTotals(IReadOnlyList<PricingRowDto> rows)
    {
        var counted = rows.Where(r => !r.IsExcluded).ToList();

        var revenueBefore = counted.Sum(r => r.BaselinePrice * (decimal)r.BaselineQuantity);
        var revenueAfter = counted.Sum(r => r.Price * (decimal)r.ForecastQuantity);

        var m0Before = counted.Sum(r => (r.BaselinePrice - r.BaselineMaterialCost) * (decimal)r.BaselineQuantity);
        var m0After = counted.Sum(r => r.M0Amount * (decimal)r.ForecastQuantity);

        var m1Before = counted.Sum(r =>
            (r.BaselinePrice - r.BaselineMaterialCost - r.BaselineManufacturingCost) * (decimal)r.BaselineQuantity);
        var m1After = counted.Sum(r => r.M1Amount * (decimal)r.ForecastQuantity);

        return new PricingTotalsDto
        {
            RevenueBefore = revenueBefore,
            RevenueAfter = revenueAfter,
            RevenueDelta = revenueAfter - revenueBefore,
            RevenueDeltaPercentage = Percentage(revenueAfter - revenueBefore, revenueBefore),

            M0Before = m0Before,
            M0After = m0After,
            M0Delta = m0After - m0Before,
            M0DeltaPercentage = Percentage(m0After - m0Before, m0Before),

            M1Before = m1Before,
            M1After = m1After,
            M1Delta = m1After - m1Before,
            M1DeltaPercentage = Percentage(m1After - m1Before, m1Before),

            // Counted over the same population as every other total (`counted`, i.e.
            // rows with data). Counting edits on excluded rows here read "1 produkt
            // upraven" next to six deltas of zero, since an excluded row contributes
            // to none of them.
            EditedProductCount = counted.Count(r => r.IsEdited),
            ExcludedProductCount = rows.Count(r => r.IsExcluded)
        };
    }

    // A percentage of zero is reported as zero rather than infinity or NaN: a total that
    // starts at zero has no meaningful percentage change, and the UI must not print "∞".
    private static decimal Percentage(decimal part, decimal whole)
        => whole == 0m ? 0m : part / whole * 100m;
}
