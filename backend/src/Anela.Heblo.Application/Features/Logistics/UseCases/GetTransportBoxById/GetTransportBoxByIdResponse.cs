using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxById;

public class GetTransportBoxByIdResponse : BaseResponse
{
    public TransportBoxDto? TransportBox { get; set; }
}