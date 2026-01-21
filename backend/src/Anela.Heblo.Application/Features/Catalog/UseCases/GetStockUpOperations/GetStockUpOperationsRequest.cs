using Anela.Heblo.Domain.Features.Catalog.Stock;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetStockUpOperations;

public class GetStockUpOperationsRequest : IRequest<GetStockUpOperationsResponse>
{
    // Existing filters
    public string? State { get; set; } // Changed to string to support "Active" special value
    public int? PageSize { get; set; }
    public int? Page { get; set; }

    // NEW filters
    public StockUpSourceType? SourceType { get; set; }
    public int? SourceId { get; set; }
    public string? ProductCode { get; set; }
    public string? DocumentNumber { get; set; } // Partial match
    public DateTime? CreatedFrom { get; set; }
    public DateTime? CreatedTo { get; set; }

    // NEW sorting
    public string? SortBy { get; set; } // "id" | "createdAt" | "state" | "documentNumber" | "productCode"
    public bool SortDescending { get; set; } = true;
}
