using System.Text.Json;
using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Pricing;
using MediatR;

namespace Anela.Heblo.Application.Features.Pricing.UseCases.GetPricingScenario;

public class GetPricingScenarioHandler
    : IRequestHandler<GetPricingScenarioRequest, GetPricingScenarioResponse>
{
    private readonly IPricingScenarioRepository _repository;
    private readonly IPricingBaselineBuilder _baselineBuilder;
    private readonly IPricingSimulationCalculator _calculator;

    public GetPricingScenarioHandler(
        IPricingScenarioRepository repository,
        IPricingBaselineBuilder baselineBuilder,
        IPricingSimulationCalculator calculator)
    {
        _repository = repository;
        _baselineBuilder = baselineBuilder;
        _calculator = calculator;
    }

    public async Task<GetPricingScenarioResponse> Handle(
        GetPricingScenarioRequest request, CancellationToken cancellationToken)
    {
        var scenario = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (scenario is null)
        {
            return new GetPricingScenarioResponse(ErrorCodes.PricingScenarioNotFound, new Dictionary<string, string>
            {
                { "scenarioId", request.Id.ToString() }
            });
        }

        var overrides = scenario.Items.Select(i => new PricingOverrideDto
        {
            ProductCode = i.ProductCode,
            Price = i.Price,
            MaterialCost = i.MaterialCost,
            ManufacturingCost = i.ManufacturingCost,
            ForecastQuantity = i.ForecastQuantity
        }).ToList();

        var filter = JsonSerializer.Deserialize<PricingFilterDto>(scenario.FilterJson) ?? new PricingFilterDto();

        var baseline = await _baselineBuilder.BuildAsync(filter, cancellationToken);

        var result = _calculator.Calculate(baseline, overrides, edit: null);

        var snapshotByCode = scenario.Items.ToDictionary(i => i.ProductCode, StringComparer.OrdinalIgnoreCase);
        foreach (var row in result.Rows)
        {
            if (!snapshotByCode.TryGetValue(row.ProductCode, out var snapshot)) continue;

            row.BaselineDrifted =
                snapshot.BaselinePrice != row.BaselinePrice ||
                snapshot.BaselineMaterialCost != row.BaselineMaterialCost ||
                snapshot.BaselineManufacturingCost != row.BaselineManufacturingCost;
        }

        return new GetPricingScenarioResponse
        {
            Scenario = new PricingScenarioSummaryDto
            {
                Id = scenario.Id,
                Name = scenario.Name,
                Description = scenario.Description,
                CreatedBy = scenario.CreatedBy,
                CreatedAt = scenario.CreatedAt,
                ModifiedAt = scenario.ModifiedAt,
                EditedProductCount = scenario.Items.Count
            },
            Rows = result.Rows.ToList(),
            Totals = result.Totals,
            Overrides = overrides
        };
    }
}
