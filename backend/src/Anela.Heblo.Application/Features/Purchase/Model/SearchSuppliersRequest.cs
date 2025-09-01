using MediatR;
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Purchase.Model;

public class SearchSuppliersRequest : IRequest<SearchSuppliersResponse>
{
    [Required(ErrorMessage = "Search term is required")]
    [StringLength(100, ErrorMessage = "Search term cannot exceed 100 characters")]
    public string SearchTerm { get; set; } = null!;

    public int Limit { get; set; } = 0;
}