using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.RemoveItemFromBox;

public class RemoveItemFromBoxResponse : BaseResponse
{
    public TransportBoxDto? TransportBox { get; set; }
}