using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetStockTakingHistory;

public class GetStockTakingHistoryRequest : IRequest<GetStockTakingHistoryResponse>
{
    [StringLength(50, ErrorMessage = "Product code cannot exceed 50 characters")]
    [Required]
    public string ProductCode { get; set; }

    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; } = "date";
    public bool SortDescending { get; set; } = true; // Default to newest first
}