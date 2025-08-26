namespace Anela.Heblo.Application.Features.Logistics.Transport.Contracts;

public class ConfirmTransitResponse
{
    public bool Success { get; set; }
    public TransportBoxDto? TransportBox { get; set; }
    public string? ErrorMessage { get; set; }
}