using Anela.Heblo.Application.Features.Transport.Contracts;

namespace Anela.Heblo.Application.Features.Transport.UseCases;

public class AddItemToBoxResponse
{
    public bool Success { get; set; }
    public TransportBoxItemDto? Item { get; set; }
    public TransportBoxDto? TransportBox { get; set; }
    public string? ErrorMessage { get; set; }
}