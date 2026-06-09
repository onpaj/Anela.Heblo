using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetPackingUsers;

public class GetPackingUsersResponse : BaseResponse
{
    public List<PackingUserDto> Users { get; set; } = new();

    public GetPackingUsersResponse() { }
    public GetPackingUsersResponse(ErrorCodes errorCode) : base(errorCode) { }
}

public class PackingUserDto
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = null!;
}
