using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder;

public class UpdateProductCompositionOrderRequest
    : IRequest<UpdateProductCompositionOrderResponse>
{
    [Required]
    public string ProductCode { get; set; } = string.Empty;

    public List<IngredientOrderItem> Order { get; set; } = new();
}

public class IngredientOrderItem
{
    [Required]
    public string IngredientProductCode { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public string? PhaseLabel { get; set; }
}
