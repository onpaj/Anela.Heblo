namespace Anela.Heblo.Application.Domain.Manufacture;

public interface IManufactureRepository
{
    Task<ManufactureTemplate> GetManufactureTemplateAsync(string id, CancellationToken cancellationToken = default);
    Task<List<ManufactureTemplate>> FindByIngredientAsync(string ingredientCode, CancellationToken cancellationToken = default);
}