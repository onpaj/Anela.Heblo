using Anela.Heblo.Application.Features.Transport.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Transport.UseCases;

public class RemoveItemFromBoxResponse : BaseResponse
{
    public string? ErrorMessage { get; set; }
    public TransportBoxDto? TransportBox { get; set; }
}