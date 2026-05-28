using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Npgsql;

namespace Anela.Heblo.Persistence.Catalog.Inventory;

public class MaterialContainerCodeGenerator : IMaterialContainerCodeGenerator
{
    private const string SequenceName = "ean_internal_seq";
    private readonly NpgsqlDataSource _dataSource;

    public MaterialContainerCodeGenerator(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyList<string>> GenerateAsync(int count, CancellationToken ct)
    {
        if (count <= 0) return Array.Empty<string>();

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        // SequenceName is a private compile-time constant — not user-controlled; no injection risk
        await using var cmd = new NpgsqlCommand(
            $"SELECT nextval('{SequenceName}') FROM generate_series(1, @count)",
            conn);
        cmd.Parameters.AddWithValue("count", count);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var codes = new List<string>(count);
        while (await reader.ReadAsync(ct))
        {
            var seq = reader.GetInt64(0);
            codes.Add($"INT-{seq:D8}");
        }
        return codes;
    }
}
