using System.Text.Json;
using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.Model;
using Anela.Heblo.Application.Features.Pricing.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Pricing;
using MediatR;

namespace Anela.Heblo.Application.Features.Pricing.UseCases.UpdatePricingScenarioProducts;

public class UpdatePricingScenarioProductsHandler
    : IRequestHandler<UpdatePricingScenarioProductsRequest, UpdatePricingScenarioProductsResponse>
{
    private readonly IPricingScenarioRepository _repository;
    private readonly IPricingBaselineBuilder _baselineBuilder;
    private readonly IPricingSimulationCalculator _calculator;
    private readonly TimeProvider _timeProvider;

    public UpdatePricingScenarioProductsHandler(
        IPricingScenarioRepository repository,
        IPricingBaselineBuilder baselineBuilder,
        IPricingSimulationCalculator calculator,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _baselineBuilder = baselineBuilder;
        _calculator = calculator;
        _timeProvider = timeProvider;
    }

    public async Task<UpdatePricingScenarioProductsResponse> Handle(
        UpdatePricingScenarioProductsRequest request, CancellationToken cancellationToken)
    {
        var scenario = await _repository.GetByIdAsync(request.ScenarioId, cancellationToken);
        if (scenario is null)
        {
            return new UpdatePricingScenarioProductsResponse(ErrorCodes.PricingScenarioNotFound, new Dictionary<string, string>
            {
                { "scenarioId", request.ScenarioId.ToString() }
            });
        }

        var name = request.Name ?? scenario.Name;
        if (request.Name is not null &&
            await _repository.ExistsByNameAsync(name, scenario.Id, cancellationToken))
        {
            return new UpdatePricingScenarioProductsResponse(ErrorCodes.PricingScenarioNameConflict, new Dictionary<string, string>
            {
                { "name", name }
            });
        }

        var removals = new HashSet<string>(request.RemoveProductCodes, StringComparer.OrdinalIgnoreCase);
        var keptItems = scenario.Items.Where(i => !removals.Contains(i.ProductCode)).ToList();
        var unmatchedRemovals = request.RemoveProductCodes
            .Where(code => !scenario.Items.Any(i => string.Equals(i.ProductCode, code, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // The scenario's own filter, so an edit resolves against the same product set it was built on.
        var filter = JsonSerializer.Deserialize<PricingFilterDto>(scenario.FilterJson) ?? new PricingFilterDto();
        var baseline = await _baselineBuilder.BuildAsync(filter, cancellationToken);

        var (result, failure) = ApplyEdits(baseline, keptItems.Select(ToOverride).ToList(), request.Edits);
        if (failure is not null)
        {
            // All-or-nothing: nothing has been written yet.
            return failure;
        }

        var updated = new PricingScenario
        {
            Id = scenario.Id,
            Name = name,
            Description = ResolveDescription(request.Description, scenario.Description),
            ModifiedAt = _timeProvider.GetUtcNow().UtcDateTime,
            FilterJson = scenario.FilterJson,
            Items = BuildItems(result!.Overrides, keptItems, request.Edits, baseline)
        };

        await _repository.UpdateAsync(updated, cancellationToken);

        return new UpdatePricingScenarioProductsResponse
        {
            Scenario = new PricingScenarioSummaryDto
            {
                Id = updated.Id,
                Name = updated.Name,
                Description = updated.Description,
                CreatedBy = scenario.CreatedBy,
                CreatedAt = scenario.CreatedAt,
                ModifiedAt = updated.ModifiedAt,
                EditedProductCount = updated.Items.Count
            },
            Rows = result.Rows.ToList(),
            Totals = result.Totals,
            Overrides = result.Overrides.ToList(),
            UnmatchedRemovals = unmatchedRemovals
        };
    }

    /// <summary>
    /// Runs the edits through the calculator one gesture at a time, each on top of the overrides
    /// the previous one produced -- exactly what the grid does cell by cell. On the first
    /// impossible edit it returns the error response instead, naming the edit's position.
    /// </summary>
    private (PricingSimulationResult? Result, UpdatePricingScenarioProductsResponse? Failure) ApplyEdits(
        IReadOnlyList<PricingBaselineRow> baseline,
        IReadOnlyList<PricingOverrideDto> overrides,
        IReadOnlyList<PricingEditDto> edits)
    {
        var result = _calculator.Calculate(baseline, overrides, edit: null);

        for (var i = 0; i < edits.Count; i++)
        {
            try
            {
                result = _calculator.Calculate(baseline, result.Overrides, edits[i]);
            }
            catch (PricingEditException ex)
            {
                var parameters = new Dictionary<string, string>(ex.Parameters)
                {
                    ["editNumber"] = (i + 1).ToString()
                };
                return (null, new UpdatePricingScenarioProductsResponse(ex.ErrorCode, parameters));
            }
        }

        return (result, null);
    }

    /// <summary>
    /// Products an edit touched get a fresh baseline snapshot; untouched ones keep the snapshot
    /// they were saved with, so BaselineDrifted keeps reporting catalog moves for them.
    /// </summary>
    private static List<PricingScenarioItem> BuildItems(
        IReadOnlyList<PricingOverrideDto> overrides,
        IReadOnlyList<PricingScenarioItem> keptItems,
        IReadOnlyList<PricingEditDto> edits,
        IReadOnlyList<PricingBaselineRow> baseline)
    {
        var editedCodes = new HashSet<string>(edits.Select(e => e.ProductCode), StringComparer.OrdinalIgnoreCase);
        var keptByCode = keptItems.ToDictionary(i => i.ProductCode, StringComparer.OrdinalIgnoreCase);
        var baselineByCode = baseline.ToDictionary(b => b.ProductCode, StringComparer.OrdinalIgnoreCase);

        return overrides.Select(o =>
        {
            var previous = !editedCodes.Contains(o.ProductCode) ? keptByCode.GetValueOrDefault(o.ProductCode) : null;
            var current = baselineByCode.GetValueOrDefault(o.ProductCode);

            return new PricingScenarioItem
            {
                ProductCode = o.ProductCode,
                Price = o.Price,
                MaterialCost = o.MaterialCost,
                ManufacturingCost = o.ManufacturingCost,
                ForecastQuantity = o.ForecastQuantity,

                BaselinePrice = previous?.BaselinePrice ?? current?.Price ?? 0m,
                BaselineMaterialCost = previous?.BaselineMaterialCost ?? current?.MaterialCost ?? 0m,
                BaselineManufacturingCost = previous?.BaselineManufacturingCost ?? current?.ManufacturingCost ?? 0m,
                BaselineQuantity = previous?.BaselineQuantity ?? current?.Quantity ?? 0d
            };
        }).ToList();
    }

    private static PricingOverrideDto ToOverride(PricingScenarioItem item) => new()
    {
        ProductCode = item.ProductCode,
        Price = item.Price,
        MaterialCost = item.MaterialCost,
        ManufacturingCost = item.ManufacturingCost,
        ForecastQuantity = item.ForecastQuantity
    };

    private static string? ResolveDescription(string? requested, string? current) => requested switch
    {
        null => current,
        "" => null,
        _ => requested
    };

}
