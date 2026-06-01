using Anela.Heblo.Domain.Features.DataQuality;
using MediatR;

namespace Anela.Heblo.Application.Features.DataQuality.UseCases.RunDqt;

public class RunDqtRequest : IRequest<RunDqtResponse>
{
    public DqtTestType TestType { get; set; } = DqtTestType.IssuedInvoiceComparison;
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
}
