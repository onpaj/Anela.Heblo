using Anela.Heblo.Application.Features.Authorization.UseCases;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetGroupDetail;

public class GetGroupDetailResponse : BaseResponse
{
    public GroupDetailDto? Group { get; set; }
    public GetGroupDetailResponse() { }
    public GetGroupDetailResponse(ErrorCodes errorCode) : base(errorCode) { }
}
