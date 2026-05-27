using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Catalog;

public class ProductIngredientOrder : Entity<int>
{
    public string ParentProductCode { get; set; } = null!;
    public string IngredientProductCode { get; set; } = null!;
    public int SortOrder { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
