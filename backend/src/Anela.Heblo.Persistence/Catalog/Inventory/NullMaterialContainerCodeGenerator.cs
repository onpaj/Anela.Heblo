using Anela.Heblo.Domain.Features.Catalog.Inventory;

namespace Anela.Heblo.Persistence.Catalog.Inventory;

/// <summary>
/// No-op implementation of IMaterialContainerCodeGenerator used in InMemory/test environments where
/// a real NpgsqlDataSource is not available. Throws NotSupportedException if called at
/// runtime to surface configuration mistakes early.
/// </summary>
public class NullMaterialContainerCodeGenerator : IMaterialContainerCodeGenerator
{
    public Task<IReadOnlyList<string>> GenerateAsync(int count, CancellationToken ct)
    {
        throw new NotSupportedException(
            "Material container code generation requires a real PostgreSQL database. " +
            "NullMaterialContainerCodeGenerator is registered only for InMemory environments.");
    }
}
