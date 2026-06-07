using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetGroupDetail;

public class GetGroupDetailRequest : IRequest<GetGroupDetailResponse>
{
    public Guid Id { get; set; }
}
