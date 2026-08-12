using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.GetMindMapDetail;

public class GetMindMapDetailRequest : IRequest<GetMindMapDetailResponse>
{
    public Guid Id { get; set; }
}
