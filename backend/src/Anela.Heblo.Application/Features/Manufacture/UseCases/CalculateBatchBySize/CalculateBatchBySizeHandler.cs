using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchBySize;

public class CalculateBatchBySizeHandler : IRequestHandler<CalculateBatchBySizeRequest, CalculateBatchBySizeResponse>
{
    private readonly IManufactureRepository _manufactureRepository;

    public CalculateBatchBySizeHandler(IManufactureRepository manufactureRepository)
    {
        _manufactureRepository = manufactureRepository;
    }

    public async Task<CalculateBatchBySizeResponse> Handle(CalculateBatchBySizeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var template = await _manufactureRepository.GetManufactureTemplateAsync(request.ProductCode, cancellationToken);

            if (template == null)
            {
                return new CalculateBatchBySizeResponse(ErrorCodes.ManufactureTemplateNotFound,
                    new Dictionary<string, string> { { "ProductCode", request.ProductCode } });
            }

            if (template.BatchSize <= 0)
            {
                return new CalculateBatchBySizeResponse(ErrorCodes.InvalidBatchSize,
                    new Dictionary<string, string> { { "BatchSize", template.BatchSize.ToString() } });
            }

            var scaleFactor = request.DesiredBatchSize / template.BatchSize;

            return new CalculateBatchBySizeResponse
            {
                Success = true,
                ProductCode = template.ProductCode,
                ProductName = template.ProductName,
                OriginalBatchSize = template.BatchSize,
                NewBatchSize = request.DesiredBatchSize,
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
            return new CalculateBatchBySizeResponse(ErrorCodes.Exception,
                new Dictionary<string, string> { { "Message", ex.Message } });
        }
    }
}