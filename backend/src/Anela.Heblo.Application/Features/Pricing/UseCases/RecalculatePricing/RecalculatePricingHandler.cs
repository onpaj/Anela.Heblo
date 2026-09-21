using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.Services;
using MediatR;

namespace Anela.Heblo.Application.Features.Pricing.UseCases.RecalculatePricing;

public class RecalculatePricingHandler
    : IRequestHandler<RecalculatePricingRequest, RecalculatePricingResponse>
{
    private readonly IPricingBaselineBuilder _baselineBuilder;
    private readonly IPricingSimulationCalculator _calculator;

    public RecalculatePricingHandler(
        IPricingBaselineBuilder baselineBuilder,
        IPricingSimulationCalculator calculator)
    {
        _baselineBuilder = baselineBuilder;
        _calculator = calculator;
    }

    public async Task<RecalculatePricingResponse> Handle(
        RecalculatePricingRequest request, CancellationToken cancellationToken)
    {
        var filter = new PricingFilterDto
        {
            ProductCode = request.ProductCode,
            ProductName = request.ProductName,
            ProductType = request.ProductType
        };

        var baseline = await _baselineBuilder.BuildAsync(filter, cancellationToken);

        try
        {
            var result = _calculator.Calculate(baseline, request.Overrides, request.Edit);

            return new RecalculatePricingResponse
            {
                Rows = result.Rows.ToList(),
                Totals = result.Totals,
                Overrides = result.Overrides.ToList()
            };
        }
        catch (PricingEditException ex)
        {
            // An impossible edit is a user error, not a fault: the UI keeps the prior cell
            // value and shows the message inline.
            return new RecalculatePricingResponse(ex.ErrorCode, ex.Parameters);
        }
    }
}
