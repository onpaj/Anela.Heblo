using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.Transport.Contracts;

public class GetTransportBoxSummaryRequest : IRequest<GetTransportBoxSummaryResponse>
{
    public string? Code { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}