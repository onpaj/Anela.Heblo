using Anela.Heblo.Domain.Features.Logistics;
using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.SetCarrierLabelOffset;

public class SetCarrierLabelOffsetRequest : IRequest<SetCarrierLabelOffsetResponse>
{
    public Carriers Carrier { get; set; }
    public int OffsetXMm { get; set; }
    public int OffsetYMm { get; set; }
}
