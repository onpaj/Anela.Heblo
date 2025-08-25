namespace Anela.Heblo.Application.Features.Logistics.Transport.Contracts;

public class ChangeTransportBoxStateResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public GetTransportBoxByIdResponse? UpdatedBox { get; set; }
}