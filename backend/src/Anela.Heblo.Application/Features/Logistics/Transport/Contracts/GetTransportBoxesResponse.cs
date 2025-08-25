namespace Anela.Heblo.Application.Features.Logistics.Transport.Contracts;

public class GetTransportBoxesResponse
{
    public IList<TransportBoxDto> Items { get; set; } = new List<TransportBoxDto>();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}