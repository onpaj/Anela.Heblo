namespace Anela.Heblo.Application.Features.Logistics.Transport.Contracts;

public class AddItemToBoxResponse
{
    public bool Success { get; set; }
    public TransportBoxItemDto? Item { get; set; }
    public TransportBoxDto? TransportBox { get; set; }
    public string? ErrorMessage { get; set; }
}