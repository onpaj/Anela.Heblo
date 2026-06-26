using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.SetUserCanPack;

public class SetUserCanPackRequest : IRequest<SetUserCanPackResponse>
{
    public Guid UserId { get; set; }
    public bool CanPack { get; set; }
}
