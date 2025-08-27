namespace Anela.Heblo.Application.Features.Logistics.Transport.Contracts;

public class CreateNewTransportBoxResponse
{
    public bool Success { get; set; }
    public TransportBoxDto? TransportBox { get; set; }
    public string? ErrorMessage { get; set; }
}