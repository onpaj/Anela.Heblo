using MediatR;

namespace Anela.Heblo.Application.Features.Transport.UseCases;

public class GetTransportBoxSummaryRequest : IRequest<GetTransportBoxSummaryResponse>
{
    public string? Code { get; set; }
    public string? ProductCode { get; set; }
}