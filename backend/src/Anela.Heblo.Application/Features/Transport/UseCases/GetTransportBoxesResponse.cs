using Anela.Heblo.Application.Features.Transport.Contracts;

namespace Anela.Heblo.Application.Features.Transport.UseCases;

public class GetTransportBoxesResponse
{
    public IList<TransportBoxDto> Items { get; set; } = new List<TransportBoxDto>();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}