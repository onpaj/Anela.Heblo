using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetBatchTemplate;

public class GetBatchTemplateHandler : IRequestHandler<GetBatchTemplateRequest, GetBatchTemplateResponse>
{
    private readonly IManufactureRepository _manufactureRepository;

    public GetBatchTemplateHandler(IManufactureRepository manufactureRepository)
    {
        _manufactureRepository = manufactureRepository;
    }

    public async Task<GetBatchTemplateResponse> Handle(GetBatchTemplateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var template = await _manufactureRepository.GetManufactureTemplateAsync(request.ProductCode, cancellationToken);

            if (template == null)
            {
                return new GetBatchTemplateResponse(ErrorCodes.ManufactureTemplateNotFound,
                    new Dictionary<string, string> { { "ProductCode", request.ProductCode } });
            }

            return new GetBatchTemplateResponse
            {
                Success = true,
                ProductCode = template.ProductCode,
                ProductName = template.ProductName,
                BatchSize = template.BatchSize,
                Ingredients = template.Ingredients.Select(i => new BatchIngredientDto
                {
                    ProductCode = i.ProductCode,
                    ProductName = i.ProductName,
                    Amount = i.Amount,
                    Price = i.Price
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            return new GetBatchTemplateResponse(ErrorCodes.Exception,
                new Dictionary<string, string> { { "Message", ex.Message } });
        }
    }
}