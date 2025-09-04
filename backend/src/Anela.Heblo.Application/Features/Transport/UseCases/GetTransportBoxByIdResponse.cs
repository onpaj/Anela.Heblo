using Anela.Heblo.Application.Features.Transport.Contracts;

namespace Anela.Heblo.Application.Features.Transport.UseCases;

public class GetTransportBoxByIdResponse
{
    public TransportBoxDto? TransportBox { get; set; }
}