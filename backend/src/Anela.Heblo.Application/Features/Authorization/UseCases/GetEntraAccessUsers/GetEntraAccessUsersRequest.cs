using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetEntraAccessUsers;

public class GetEntraAccessUsersRequest : IRequest<GetEntraAccessUsersResponse> { }

public class EntraUserDto
{
    public string EntraObjectId { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
}

public class GetEntraAccessUsersResponse : BaseResponse
{
    public List<EntraUserDto> Users { get; set; } = new();
}
