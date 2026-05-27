namespace Anela.Heblo.Domain.Features.Catalog;

public interface IProductIngredientOrderRepository
{
    Task<List<ProductIngredientOrder>> ListByParentAsync(
        string parentProductCode,
        CancellationToken cancellationToken = default);

    Task<ProductIngredientOrder> CreateAsync(
        ProductIngredientOrder entity,
        CancellationToken cancellationToken = default);

    Task<ProductIngredientOrder> UpdateAsync(
        ProductIngredientOrder entity,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
