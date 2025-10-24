using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.EnqueueStockTaking;

public class EnqueueStockTakingRequest : IRequest<EnqueueStockTakingResponse>
{
    public string ProductCode { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
    public bool SoftStockTaking { get; set; }
}