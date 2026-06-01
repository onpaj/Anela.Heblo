using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxes;

public class GetTransportBoxesResponse : BaseResponse
{
    public IList<TransportBoxDto> Items { get; set; } = new List<TransportBoxDto>();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}