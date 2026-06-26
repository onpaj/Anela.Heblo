using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.SetUserActive;

public class SetUserActiveRequest : IRequest<SetUserActiveResponse>
{
    public Guid UserId { get; set; }
    public bool IsActive { get; set; }
}
