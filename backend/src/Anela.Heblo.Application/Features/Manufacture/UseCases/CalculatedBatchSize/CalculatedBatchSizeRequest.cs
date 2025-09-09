using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchBySize;

public class CalculatedBatchSizeRequest : IRequest<CalculatedBatchSizeResponse>
{
    public string ProductCode { get; set; } = null!;
    public double? DesiredBatchSize { get; set; }
}