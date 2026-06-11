using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.UpdateUser;

public class UpdateUserRequest : IRequest<UpdateUserResponse>
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool CanPack { get; set; }
}
