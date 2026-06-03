using Anela.Heblo.Application.Common.TimePeriods;
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchPlan;

public class CalculateBatchPlanHandler : IRequestHandler<CalculateBatchPlanRequest, CalculateBatchPlanResponse>
{
    private const int DefaultFallbackDays = 30;

    private readonly IBatchPlanningService _batchPlanningService;
    private readonly IManufactureCatalogSource _catalogSource;
    private readonly IManufactureClient _manufactureClient;
    private readonly ITimePeriodResolver _timePeriodResolver;

    public CalculateBatchPlanHandler(
        IBatchPlanningService batchPlanningService,
        IManufactureCatalogSource catalogSource,
        IManufactureClient manufactureClient,
        ITimePeriodResolver timePeriodResolver)
    {
        _batchPlanningService = batchPlanningService;
        _catalogSource = catalogSource;
        _manufactureClient = manufactureClient;
        _timePeriodResolver = timePeriodResolver;
    }

    public async Task<CalculateBatchPlanResponse> Handle(CalculateBatchPlanRequest request, CancellationToken cancellationToken)
    {
        // Can calculate even for product
        var product = await _catalogSource.GetByIdAsync(request.ProductCode, cancellationToken);
        if (product?.Type == ProductType.Product)
        {
            var manufactureTemplate = await _manufactureClient.GetManufactureTemplateAsync(request.ProductCode, cancellationToken);
            request.ManufactureType = manufactureTemplate.ManufactureType;
            if (request.ManufactureType == ManufactureType.MultiPhase)
            {
                // For multi-phase, transform to semiproduct code as before
                request.ProductCode = manufactureTemplate.Ingredients
                    .FirstOrDefault(w => w.ProductType == ProductType.SemiProduct)?.ProductCode ?? request.ProductCode;
            }
        }

        var salesRanges = ResolveSalesRanges(request);
        return await _batchPlanningService.CalculateBatchPlan(request, salesRanges, cancellationToken);
    }

    private IReadOnlyList<DateRange> ResolveSalesRanges(CalculateBatchPlanRequest request)
    {
        if (request.TimePeriod.HasValue)
        {
            return _timePeriodResolver.Resolve(request.TimePeriod.Value, request.FromDate, request.ToDate);
        }

        var endDate = request.ToDate ?? DateTime.Now;
        var startDate = request.FromDate ?? endDate.AddDays(-DefaultFallbackDays);
        return new[] { new DateRange(startDate, endDate) };
    }
}
