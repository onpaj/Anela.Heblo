using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetUserEffectivePermissions;

public class GetUserEffectivePermissionsRequest : IRequest<GetUserEffectivePermissionsResponse>
{
    public Guid UserId { get; set; }
}
