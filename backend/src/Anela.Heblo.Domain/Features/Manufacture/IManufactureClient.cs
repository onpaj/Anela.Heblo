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
    Task SetBomItemsOrderAsync(
        string productCode,
        IEnumerable<(int BoMItemId, int Order)> items,
        CancellationToken cancellationToken = default);
}