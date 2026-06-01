namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

public interface IMaterialContainerCodeGenerator
{
    Task<IReadOnlyList<string>> GenerateAsync(int count, CancellationToken ct);
}
