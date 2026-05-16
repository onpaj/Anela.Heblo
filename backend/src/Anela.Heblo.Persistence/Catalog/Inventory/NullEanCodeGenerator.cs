using Anela.Heblo.Domain.Features.Catalog.Inventory;

namespace Anela.Heblo.Persistence.Catalog.Inventory;

/// <summary>
/// No-op implementation of IEanCodeGenerator used in InMemory/test environments where
/// a real NpgsqlDataSource is not available. Throws NotSupportedException if called at
/// runtime to surface configuration mistakes early.
/// </summary>
public class NullEanCodeGenerator : IEanCodeGenerator
{
    public Task<IReadOnlyList<string>> GenerateAsync(int count, CancellationToken ct)
    {
        throw new NotSupportedException(
            "EAN code generation requires a real PostgreSQL database. " +
            "NullEanCodeGenerator is registered only for InMemory environments.");
    }
}
