using Anela.Heblo.Domain.Features.Catalog.Stock;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetStockUpOperations;

public class GetStockUpOperationsRequest : IRequest<GetStockUpOperationsResponse>
{
    public StockUpOperationState? State { get; set; }
    public int? PageSize { get; set; }
    public int? Page { get; set; }
}
