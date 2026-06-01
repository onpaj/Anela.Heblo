using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxSummary;

public class GetTransportBoxSummaryRequest : IRequest<GetTransportBoxSummaryResponse>
{
    public string? Code { get; set; }
    public string? ProductCode { get; set; }
}