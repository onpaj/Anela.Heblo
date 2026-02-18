using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Xcc;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchPlan;

public class CalculateBatchPlanHandler : IRequestHandler<CalculateBatchPlanRequest, CalculateBatchPlanResponse>
{
    private readonly IBatchPlanningService _batchPlanningService;
    private readonly ICatalogRepository _catalogRepository;
    private readonly IManufactureClient _manufactureClient
        ;

    public CalculateBatchPlanHandler(IBatchPlanningService batchPlanningService, ICatalogRepository catalogRepository, IManufactureClient manufactureClient)
    {
        _batchPlanningService = batchPlanningService;
        _catalogRepository = catalogRepository;
        _manufactureClient = manufactureClient;
    }

    public async Task<CalculateBatchPlanResponse> Handle(CalculateBatchPlanRequest request, CancellationToken cancellationToken)
    {
        // Can calculate even for product
        var product = await _catalogRepository.GetByIdAsync(request.ProductCode, cancellationToken);
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

        return await _batchPlanningService.CalculateBatchPlan(request, cancellationToken);
    }
}