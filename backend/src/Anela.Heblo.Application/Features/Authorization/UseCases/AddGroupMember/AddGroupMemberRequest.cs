using Anela.Heblo.Application.Features.Authorization.UseCases;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.AddGroupMember;

public class AddGroupMemberRequest : IRequest<AddGroupMemberResponse>
{
    public Guid GroupId { get; set; }
    public string EntraObjectId { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
}

public class AddGroupMemberResponse : BaseResponse
{
    public AppUserDto? User { get; set; }

    public AddGroupMemberResponse() { }
    public AddGroupMemberResponse(ErrorCodes errorCode) : base(errorCode) { }
}
