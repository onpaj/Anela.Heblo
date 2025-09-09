namespace Anela.Heblo.Domain.Features.Manufacture;

public interface IManufactureRepository
{
    Task<ManufactureTemplate> GetManufactureTemplateAsync(string id, CancellationToken cancellationToken = default);
    Task<List<ManufactureTemplate>> FindByIngredientAsync(string ingredientCode, CancellationToken cancellationToken = default);
    Task<List<ProductPart>> GetSetParts(string setProductCode, CancellationToken cancellationToken = default);
}