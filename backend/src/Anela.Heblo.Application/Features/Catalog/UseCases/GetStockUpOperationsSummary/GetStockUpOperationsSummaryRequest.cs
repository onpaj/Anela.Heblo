using Anela.Heblo.Domain.Features.Catalog.Stock;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetStockUpOperationsSummary;

public class GetStockUpOperationsSummaryRequest : IRequest<GetStockUpOperationsSummaryResponse>
{
    public StockUpSourceType? SourceType { get; set; }
}
