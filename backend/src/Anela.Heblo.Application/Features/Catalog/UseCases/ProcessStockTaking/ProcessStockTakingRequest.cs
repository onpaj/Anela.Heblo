using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.ProcessStockTaking;

public class ProcessStockTakingRequest : IRequest<ProcessStockTakingResponse>
{
    public string ProductCode { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
    public bool SoftStockTaking { get; set; } = false;
}