### task: add-repository-method

Adds `GetMaterialNamesByIdsAsync` to `IPackingMaterialRepository` and implements it in `PackingMaterialRepository`. Because `IPackingMaterialRepository` is also implemented by two test-project classes (`MockPackingMaterialRepository` and the file-local `CountingRepositoryWrapper` inside `PackingMaterialsListQueryCountTests.cs`), both must gain the new member in this same task or the whole solution fails to build. A new test file proves the repository method's own contract (FR-1) directly against a real EF Core in-memory `ApplicationDbContext`.

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/PackingMaterials/IPackingMaterialRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialRepository.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsListQueryCountTests.cs`
- Test (new): `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialRepositoryGetMaterialNamesByIdsAsyncTests.cs`

**Steps:**

1. Create the new test file `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialRepositoryGetMaterialNamesByIdsAsyncTests.cs` with the following content. It will not compile yet, because `PackingMaterialRepository` has no `GetMaterialNamesByIdsAsync` member — this is the expected "red" state.

```csharp
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.PackingMaterials;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class PackingMaterialRepositoryGetMaterialNamesByIdsAsyncTests
{
    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"MaterialNameLookup_{Guid.NewGuid()}")
            .Options;
        var context = new ApplicationDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task ReturnsNamesOnlyForRequestedIds_AndOmitsUnmatchedIds()
    {
        // Arrange
        using var context = NewContext();
        var m1 = new Domain.Features.PackingMaterials.PackingMaterial("Tape", 1m, ConsumptionType.PerDay, 100m);
        var m2 = new Domain.Features.PackingMaterials.PackingMaterial("Box", 1m, ConsumptionType.PerDay, 100m);
        var m3 = new Domain.Features.PackingMaterials.PackingMaterial("Label", 1m, ConsumptionType.PerDay, 100m);
        await context.PackingMaterials.AddRangeAsync(m1, m2, m3);
        await context.SaveChangesAsync();

        var repository = new PackingMaterialRepository(context);

        // Act: request m1, m3, and one id that does not exist. m2 is deliberately omitted.
        var missingId = m1.Id + m2.Id + m3.Id + 1000;
        var result = await repository.GetMaterialNamesByIdsAsync(new[] { m1.Id, m3.Id, missingId }, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result[m1.Id].Should().Be("Tape");
        result[m3.Id].Should().Be("Label");
        result.Should().NotContainKey(m2.Id);
        result.Should().NotContainKey(missingId);
    }

    [Fact]
    public async Task DuplicateIds_ReturnEachMaterialOnlyOnce()
    {
        // Arrange
        using var context = NewContext();
        var m1 = new Domain.Features.PackingMaterials.PackingMaterial("Tape", 1m, ConsumptionType.PerDay, 100m);
        await context.PackingMaterials.AddRangeAsync(m1);
        await context.SaveChangesAsync();

        var repository = new PackingMaterialRepository(context);

        // Act
        var result = await repository.GetMaterialNamesByIdsAsync(new[] { m1.Id, m1.Id, m1.Id }, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result[m1.Id].Should().Be("Tape");
    }

    [Fact]
    public async Task EmptyIds_ReturnsEmptyDictionary_WithoutQueryingTheDatabase()
    {
        // Arrange: dispose the context up front. If the implementation does not short-circuit
        // on an empty id collection, touching the disposed context will throw.
        var context = NewContext();
        var repository = new PackingMaterialRepository(context);
        context.Dispose();

        // Act
        var result = await repository.GetMaterialNamesByIdsAsync(Array.Empty<int>(), CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}
```

2. Run the new test to confirm it fails to build (red):

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterialRepositoryGetMaterialNamesByIdsAsyncTests"
```

Expected: a compile error such as `'PackingMaterialRepository' does not implement interface member 'IPackingMaterialRepository.GetMaterialNamesByIdsAsync'` / `'PackingMaterialRepository' does not contain a definition for 'GetMaterialNamesByIdsAsync'`.

3. Add the new member to `IPackingMaterialRepository.cs`. Insert it directly after the `GetRecentLogsForMaterialsAsync` declaration:

```csharp
    /// <summary>
    /// Resolves display names for a set of packing materials by id. Ids with no matching
    /// material are simply absent from the result (no exception). When <paramref name="packingMaterialIds"/>
    /// is empty, returns an empty dictionary without executing a database query.
    /// </summary>
    /// <param name="packingMaterialIds">The packing material identifiers to resolve names for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary of <c>Id -> Name</c> for the ids that exist.</returns>
    Task<IReadOnlyDictionary<int, string>> GetMaterialNamesByIdsAsync(
        IEnumerable<int> packingMaterialIds,
        CancellationToken cancellationToken = default);
```

So the file's method ordering becomes: `GetRecentLogsAsync`, `GetRecentLogsForMaterialsAsync`, `GetMaterialNamesByIdsAsync` (new), `HasDailyProcessingBeenRunAsync`, ...

4. Implement the method in `PackingMaterialRepository.cs`. Insert it directly after `GetRecentLogsForMaterialsAsync`'s implementation (which ends at the closing brace before `HasDailyProcessingBeenRunAsync`):

```csharp
    public async Task<IReadOnlyDictionary<int, string>> GetMaterialNamesByIdsAsync(
        IEnumerable<int> packingMaterialIds,
        CancellationToken cancellationToken = default)
    {
        var ids = packingMaterialIds as IReadOnlyCollection<int> ?? packingMaterialIds.ToArray();
        if (ids.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        var rows = await DbSet
            .Where(m => ids.Contains(m.Id))
            .Select(m => new { m.Id, m.Name })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Id, r => r.Name);
    }
```

5. Add the matching implementation to `MockPackingMaterialRepository.cs`, so the test double stays a complete `IPackingMaterialRepository`. Insert it directly after the existing `GetRecentLogsForMaterialsAsync` method (after its closing brace, before `HasDailyProcessingBeenRunAsync`):

```csharp
    public Task<IReadOnlyDictionary<int, string>> GetMaterialNamesByIdsAsync(
        IEnumerable<int> packingMaterialIds,
        CancellationToken cancellationToken = default)
    {
        var ids = packingMaterialIds.ToHashSet();
        var dict = _materials
            .Where(m => ids.Contains(m.Id))
            .ToDictionary(m => m.Id, m => m.Name);
        return Task.FromResult<IReadOnlyDictionary<int, string>>(dict);
    }
```

6. Add a passthrough implementation to the file-local `CountingRepositoryWrapper` class inside `PackingMaterialsListQueryCountTests.cs`, so that existing file keeps compiling. Insert it in the "Delegate all other methods to inner repository" section, next to the `GetConsumptionHistoryAsync` delegate at the bottom of the class:

```csharp
        public Task<IReadOnlyDictionary<int, string>> GetMaterialNamesByIdsAsync(
            IEnumerable<int> packingMaterialIds,
            CancellationToken cancellationToken = default)
            => _inner.GetMaterialNamesByIdsAsync(packingMaterialIds, cancellationToken);
```

7. Run a full solution build to confirm every `IPackingMaterialRepository` implementer now compiles:

```bash
dotnet build
```

Expected: `Build succeeded. 0 Error(s)`.

8. Run the new test file to confirm it now passes (green):

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterialRepositoryGetMaterialNamesByIdsAsyncTests"
```

Expected: `Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3`.

9. Run the existing PackingMaterials suite to confirm no regressions from the mock/wrapper edits:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterials"
```

Expected: all tests pass, 0 failed.

10. Commit:

```bash
git add backend/src/Anela.Heblo.Domain/Features/PackingMaterials/IPackingMaterialRepository.cs backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialRepository.cs backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsListQueryCountTests.cs backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialRepositoryGetMaterialNamesByIdsAsyncTests.cs
git commit -m "#4027: Add targeted GetMaterialNamesByIdsAsync lookup to IPackingMaterialRepository"
```

---

