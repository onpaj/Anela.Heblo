namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

public interface IEanCodeGenerator
{
    Task<IReadOnlyList<string>> GenerateAsync(int count, CancellationToken ct);
}
