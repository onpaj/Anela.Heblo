using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.CreateLocalUser;

public class CreateLocalUserRequest : IRequest<CreateLocalUserResponse>
{
    public string DisplayName { get; set; } = null!;
}
