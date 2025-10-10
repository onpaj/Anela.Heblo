using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.AddItemToBox;

public class AddItemToBoxResponse : BaseResponse
{
    public TransportBoxItemDto? Item { get; set; }
    public TransportBoxDto? TransportBox { get; set; }
}