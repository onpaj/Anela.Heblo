using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.CreateGroup;

public class CreateGroupResponse : BaseResponse
{
    public Guid Id { get; set; }
    public CreateGroupResponse() { }
    public CreateGroupResponse(ErrorCodes errorCode) : base(errorCode) { }
}
