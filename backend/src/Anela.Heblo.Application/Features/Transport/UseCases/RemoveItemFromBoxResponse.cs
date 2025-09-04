using Anela.Heblo.Application.Features.Transport.Contracts;

namespace Anela.Heblo.Application.Features.Transport.UseCases;

public class RemoveItemFromBoxResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public TransportBoxDto? TransportBox { get; set; }
}