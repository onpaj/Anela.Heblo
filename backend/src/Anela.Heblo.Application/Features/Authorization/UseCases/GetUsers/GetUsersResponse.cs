using Anela.Heblo.Application.Features.Authorization.UseCases;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetUsers;

public class GetUsersResponse : BaseResponse
{
    public List<AppUserDto> Users { get; set; } = new();
}
