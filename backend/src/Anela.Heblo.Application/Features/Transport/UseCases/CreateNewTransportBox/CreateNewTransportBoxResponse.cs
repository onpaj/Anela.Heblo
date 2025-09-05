using Anela.Heblo.Application.Features.Transport.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Transport.UseCases;

public class CreateNewTransportBoxResponse : BaseResponse
{
    public TransportBoxDto? TransportBox { get; set; }
}