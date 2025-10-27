using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Domain.Features.Manufacture;

public interface IManufactureRepository
{
    Task<ManufactureTemplate> GetManufactureTemplateAsync(string id, CancellationToken cancellationToken = default);
    Task<List<ManufactureTemplate>> FindByIngredientAsync(string ingredientCode, CancellationToken cancellationToken = default);
    Task<List<ProductPart>> GetSetPartsAsync(string setProductCode, CancellationToken cancellationToken = default);
}