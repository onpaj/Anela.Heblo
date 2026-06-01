using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.AcceptStockUpOperation;

public class AcceptStockUpOperationResponse : BaseResponse
{
    public StockUpResultStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
}
