using Anela.Heblo.Application.Features.Transport.Contracts;

namespace Anela.Heblo.Application.Features.Transport.UseCases;

public class CreateNewTransportBoxResponse
{
    public bool Success { get; set; }
    public TransportBoxDto? TransportBox { get; set; }
    public string? ErrorMessage { get; set; }
}