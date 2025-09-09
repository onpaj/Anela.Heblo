using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetBatchTemplate;

public class GetBatchTemplateRequest : IRequest<GetBatchTemplateResponse>
{
    public string ProductCode { get; set; } = null!;
}