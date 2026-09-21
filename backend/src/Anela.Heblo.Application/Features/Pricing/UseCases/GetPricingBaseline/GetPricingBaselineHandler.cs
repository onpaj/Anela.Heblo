using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.Services;
using MediatR;

namespace Anela.Heblo.Application.Features.Pricing.UseCases.GetPricingBaseline;

public class GetPricingBaselineHandler
    : IRequestHandler<GetPricingBaselineRequest, GetPricingBaselineResponse>
{
    private readonly IPricingBaselineBuilder _baselineBuilder;
    private readonly IPricingSimulationCalculator _calculator;

    public GetPricingBaselineHandler(
        IPricingBaselineBuilder baselineBuilder,
        IPricingSimulationCalculator calculator)
    {
        _baselineBuilder = baselineBuilder;
        _calculator = calculator;
    }

    public async Task<GetPricingBaselineResponse> Handle(
        GetPricingBaselineRequest request, CancellationToken cancellationToken)
    {
        var filter = new PricingFilterDto
        {
            ProductCode = request.ProductCode,
            ProductName = request.ProductName,
            ProductType = request.ProductType
        };

        var baseline = await _baselineBuilder.BuildAsync(filter, cancellationToken);

        // No overrides and no edit: the untouched starting state.
        var result = _calculator.Calculate(baseline, Array.Empty<PricingOverrideDto>(), edit: null);

        return new GetPricingBaselineResponse
        {
            Rows = result.Rows.ToList(),
            Totals = result.Totals
        };
    }
}
