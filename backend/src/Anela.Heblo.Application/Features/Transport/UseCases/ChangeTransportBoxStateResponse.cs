namespace Anela.Heblo.Application.Features.Transport.UseCases;

public class ChangeTransportBoxStateResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public GetTransportBoxByIdResponse? UpdatedBox { get; set; }
}