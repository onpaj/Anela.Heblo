using Anela.Heblo.Application.Features.Transport.UseCases.GetTransportBoxById;

namespace Anela.Heblo.Application.Features.Transport.UseCases.UpdateTransportBoxDescription;

public class UpdateTransportBoxDescriptionResponse
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, string>? Params { get; set; }
    public GetTransportBoxByIdResponse? UpdatedBox { get; set; }
}