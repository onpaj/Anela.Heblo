using Anela.Heblo.Application.Features.Transport.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Transport.UseCases;

public class AddItemToBoxResponse : BaseResponse
{
    public TransportBoxItemDto? Item { get; set; }
    public TransportBoxDto? TransportBox { get; set; }
    public string? ErrorMessage { get; set; }
}