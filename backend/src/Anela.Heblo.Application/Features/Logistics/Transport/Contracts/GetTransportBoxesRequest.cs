using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.Transport.Contracts;

public class GetTransportBoxesRequest : IRequest<GetTransportBoxesResponse>
{
    public int Skip { get; set; } = 0;
    public int Take { get; set; } = 50;
    public string? Code { get; set; }
    public string? State { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; }
}