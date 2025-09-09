using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchBySize;

public class CalculateBatchBySizeRequest : IRequest<CalculateBatchBySizeResponse>
{
    public string ProductCode { get; set; } = null!;
    public double DesiredBatchSize { get; set; }
}