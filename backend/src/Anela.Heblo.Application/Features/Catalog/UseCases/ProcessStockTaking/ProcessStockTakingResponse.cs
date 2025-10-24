using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.ProcessStockTaking;

public class ProcessStockTakingResponse : BaseResponse
{
    public StockTakingResultDto Result { get; set; } = new();
}