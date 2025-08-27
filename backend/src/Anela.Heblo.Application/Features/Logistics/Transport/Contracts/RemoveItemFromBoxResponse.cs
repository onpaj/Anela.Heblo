namespace Anela.Heblo.Application.Features.Logistics.Transport.Contracts;

public class RemoveItemFromBoxResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public TransportBoxDto? TransportBox { get; set; }
}