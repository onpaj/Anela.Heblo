using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureStockTakingHistory;

public class GetManufactureStockTakingHistoryRequest : IRequest<GetManufactureStockTakingHistoryResponse>
{
    [Required]
    [StringLength(50, ErrorMessage = "Product code cannot exceed 50 characters")]
    public string ProductCode { get; set; } = null!;

    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; } = "date";
    public bool SortDescending { get; set; } = true;
}
