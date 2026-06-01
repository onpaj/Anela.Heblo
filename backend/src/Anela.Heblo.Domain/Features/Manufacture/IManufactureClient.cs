namespace Anela.Heblo.Domain.Features.Manufacture;

public interface IManufactureClient
{
    Task<SubmitManufactureClientResponse> SubmitManufactureAsync(SubmitManufactureClientRequest request, CancellationToken cancellationToken = default);

    Task UpdateBoMIngredientAmountAsync(string productCode, string ingredientCode, double newAmount, CancellationToken cancellationToken = default);

    Task<ManufactureTemplate?> GetManufactureTemplateAsync(string id, CancellationToken cancellationToken = default);
    Task<List<ManufactureTemplate>> FindByIngredientAsync(string ingredientCode, CancellationToken cancellationToken = default);
    Task<List<ProductPart>> GetSetPartsAsync(string setProductCode, CancellationToken cancellationToken = default);

    Task<List<ManufactureErpDocumentItem>> GetErpDocumentItemsAsync(string documentCode, int? documentTypeId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the display order for BoM line items to Abra Flexi and invalidates the template cache.
    /// </summary>
    /// <param name="productCode">Product code whose BoM is being reordered (used for cache invalidation).</param>
    /// <param name="items">Pairs of (BoMItemId, Order) to set. BoMItemId is <see cref="Ingredient.TemplateId"/>.</param>
    [Obsolete("Use SetBomItemsOrderAndPhaseAsync which also persists the phase label. This method is kept for compatibility.")]
    Task SetBomItemsOrderAsync(
        string productCode,
        IEnumerable<(int BoMItemId, int Order)> items,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes display order AND phase label for BoM line items to Abra Flexi, then invalidates the template cache.
    /// </summary>
    /// <param name="productCode">Product code whose BoM is being saved (used for cache invalidation).</param>
    /// <param name="items">Triples of (BoMItemId, Order, PhaseLabel). BoMItemId is <see cref="Ingredient.TemplateId"/>. PhaseLabel null clears the field.</param>
    Task SetBomItemsOrderAndPhaseAsync(
        string productCode,
        IEnumerable<(int BoMItemId, int Order, string? PhaseLabel)> items,
        CancellationToken cancellationToken = default);
}