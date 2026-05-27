using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetSemiproductRecipePdf;

public class GetSemiproductRecipePdfRequest : IRequest<GetSemiproductRecipePdfResponse>
{
    public string ProductCode { get; set; } = string.Empty;
    public double? BatchSize { get; set; }
}
