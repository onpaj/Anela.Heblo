namespace Anela.Heblo.Domain.Features.Manufacture;

public interface IManufactureClient
{
    Task<string> SubmitManufactureAsync(SubmitManufactureClientRequest request, CancellationToken cancellationToken = default);

    Task UpdateBoMIngredientAmountAsync(string productCode, string ingredientCode, double newAmount, CancellationToken cancellationToken = default);

    Task<ManufactureTemplate> GetManufactureTemplateAsync(string id, CancellationToken cancellationToken = default);
    Task<List<ManufactureTemplate>> FindByIngredientAsync(string ingredientCode, CancellationToken cancellationToken = default);
    Task<List<ProductPart>> GetSetPartsAsync(string setProductCode, CancellationToken cancellationToken = default);
}