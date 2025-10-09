using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.CreateNewTransportBox;

public class CreateNewTransportBoxResponse : BaseResponse
{
    public TransportBoxDto? TransportBox { get; set; }
}