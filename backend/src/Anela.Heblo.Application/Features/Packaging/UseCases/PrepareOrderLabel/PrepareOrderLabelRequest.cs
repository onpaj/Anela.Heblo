using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.PrepareOrderLabel;

public class PrepareOrderLabelRequest : IRequest<PrepareOrderLabelResponse>
{
    public string OrderCode { get; set; } = null!;
    public bool ForceRecreate { get; set; }
}
