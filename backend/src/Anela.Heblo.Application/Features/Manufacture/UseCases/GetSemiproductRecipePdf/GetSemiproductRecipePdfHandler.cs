using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetSemiproductRecipePdf;

public class GetSemiproductRecipePdfHandler : IRequestHandler<GetSemiproductRecipePdfRequest, GetSemiproductRecipePdfResponse>
{
    private readonly IManufactureClient _manufactureClient;
    private readonly ISemiproductRecipeRenderer _renderer;

    public GetSemiproductRecipePdfHandler(IManufactureClient manufactureClient, ISemiproductRecipeRenderer renderer)
    {
        _manufactureClient = manufactureClient;
        _renderer = renderer;
    }

    public async Task<GetSemiproductRecipePdfResponse> Handle(
        GetSemiproductRecipePdfRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var template = await _manufactureClient.GetManufactureTemplateAsync(request.ProductCode, cancellationToken);

            if (template == null)
            {
                return new GetSemiproductRecipePdfResponse(ErrorCodes.ManufactureTemplateNotFound,
                    new Dictionary<string, string> { { "ProductCode", request.ProductCode } });
            }

            var totalOriginal = template.Ingredients.Sum(i => i.OriginalAmount);

            var ingredientLines = template.Ingredients.Select(i => new SemiproductRecipeIngredientLine
            {
                ProductCode = i.ProductCode,
                ProductName = i.ProductName,
                AmountFullBatch = i.OriginalAmount,
                AmountHalfBatch = i.OriginalAmount / 2,
                Percentage = totalOriginal > 0 ? i.OriginalAmount / totalOriginal * 100 : 0,
            }).ToList();

            var data = new SemiproductRecipeData
            {
                ProductCode = template.ProductCode,
                ProductName = template.ProductName,
                BatchSize = template.OriginalAmount,
                Ingredients = ingredientLines,
            };

            var pdfBytes = _renderer.Render(data);

            return new GetSemiproductRecipePdfResponse
            {
                PdfBytes = pdfBytes,
                FileName = $"receptura-{request.ProductCode}.pdf",
            };
        }
        catch (Exception ex)
        {
            return new GetSemiproductRecipePdfResponse(ErrorCodes.Exception,
                new Dictionary<string, string> { { "Message", ex.Message } });
        }
    }
}
