using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchBySize;

public class CalculatedBatchSizeHandler : IRequestHandler<CalculatedBatchSizeRequest, CalculatedBatchSizeResponse>
{
    private readonly IManufactureRepository _manufactureRepository;
    private readonly ICatalogRepository _catalogRepository;

    public CalculatedBatchSizeHandler(IManufactureRepository manufactureRepository, ICatalogRepository catalogRepository)
    {
        _manufactureRepository = manufactureRepository;
        _catalogRepository = catalogRepository;
    }

    public async Task<CalculatedBatchSizeResponse> Handle(CalculatedBatchSizeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var template = await _manufactureRepository.GetManufactureTemplateAsync(request.ProductCode, cancellationToken);
            var product = await _catalogRepository.GetByIdAsync(request.ProductCode, cancellationToken);
            
            if (template == null)
            {
                return new CalculatedBatchSizeResponse(ErrorCodes.ManufactureTemplateNotFound,
                    new Dictionary<string, string> { { "ProductCode", request.ProductCode } });
            }
            
            if (product == null)
            {
                return new CalculatedBatchSizeResponse(ErrorCodes.ProductNotFound,
                    new Dictionary<string, string> { { "ProductCode", request.ProductCode } });
            }

            template.BatchSize = product.MinimalManufactureQuantity;

            if (template.OriginalAmount <= 0)
            {
                return new CalculatedBatchSizeResponse(ErrorCodes.InvalidBatchSize,
                    new Dictionary<string, string> { { "BatchSize", template.BatchSize.ToString() } });
            }

            if(request.DesiredBatchSize == null) // Use MMQ instead
                request.DesiredBatchSize = product.MinimalManufactureQuantity;
            
            double scaleFactor = (request.DesiredBatchSize / template.OriginalAmount).Value;

            return new CalculatedBatchSizeResponse
            {
                Success = true,
                ProductCode = template.ProductCode,
                ProductName = template.ProductName,
                OriginalBatchSize = template.OriginalAmount,
                NewBatchSize = request.DesiredBatchSize.Value,
                ScaleFactor = scaleFactor,
                Ingredients = template.Ingredients.Select(i => new CalculatedIngredientDto
                {
                    ProductCode = i.ProductCode,
                    ProductName = i.ProductName,
                    OriginalAmount = i.Amount,
                    CalculatedAmount = Math.Round(i.Amount * scaleFactor, 2),
                    Price = i.Price
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            return new CalculatedBatchSizeResponse(ErrorCodes.Exception,
                new Dictionary<string, string> { { "Message", ex.Message } });
        }
    }
}