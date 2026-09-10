# Targeted Material-Name Lookup in GetConsumptionHistoryHandler Implementation Plan

**Goal:** Replace `GetConsumptionHistoryHandler`'s unconditional `GetAllAsync` full-table load of packing materials with a targeted `GetMaterialNamesByIdsAsync` lookup scoped to the distinct material ids present on the current page.

**Architecture:** Add one new method to `IPackingMaterialRepository` (`GetMaterialNamesByIdsAsync`), implement it in `PackingMaterialRepository` as a `WHERE Id IN (...)` query projected to `Id`/`Name` only, and swap the handler's `GetAllAsync` call for it. This mirrors the existing `GetRecentLogsForMaterialsAsync` sibling method almost verbatim (same empty-collection short-circuit, same "collect ids from a prior result, pass into a targeted batch call" idiom used by `GetPackingMaterialsListHandler`). No API, DTO, or schema changes.

**Tech Stack:** .NET 8, EF Core, xUnit

## How to run the tests in this plan

This repo's test project is `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`. Scope test runs with `--filter "FullyQualifiedName~<Name>"` (the convention used throughout this codebase — see `docs/testing/mcp-testing.md` and prior PackingMaterials work). Example used throughout this plan:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterials"
```

---

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

### task: add-query-count-test

Adds a query-count-style regression test proving `GetConsumptionHistoryHandler.Handle` never calls `GetAllAsync` and calls `GetMaterialNamesByIdsAsync` exactly once, with ids scoped to the current page. Written and run *before* the handler is changed, so it fails against today's `GetAllAsync`-based implementation — the "red" step for the handler swap done in the next task. Follows the same `CountingRepositoryWrapper`-around-a-real-`PackingMaterialRepository` pattern as `PackingMaterialsListQueryCountTests.cs`, in its own new file (that file is scoped and documented as covering `GetPackingMaterialsListHandler` specifically, so this test does not go there).

**Files:**
- Test (new): `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetConsumptionHistoryQueryCountTests.cs`

**Steps:**

1. Create `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetConsumptionHistoryQueryCountTests.cs` with the following content:

```csharp
// Note: Uses a CountingRepositoryWrapper instead of SQLite + DbCommandInterceptor for the
// same reason as PackingMaterialsListQueryCountTests.cs (ApplicationDbContext.OnModelCreating
// has PostgreSQL-specific column type annotations incompatible with SQLite's EnsureCreated()).
// The wrapper proves the handler calls GetAllAsync zero times and GetMaterialNamesByIdsAsync
// exactly once, with the correct page-scoped id set.

using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetConsumptionHistory;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.PackingMaterials;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class GetConsumptionHistoryQueryCountTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public GetConsumptionHistoryQueryCountTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"GetConsumptionHistory_QueryCount_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task Handle_NeverCallsGetAllAsync_AndCallsGetMaterialNamesByIdsAsyncExactlyOnceWithPageScopedIds()
    {
        // Arrange
        var m1 = new PackingMaterial("M1", 1m, ConsumptionType.PerDay, 100m);
        var m2 = new PackingMaterial("M2", 1m, ConsumptionType.PerDay, 100m);
        await _context.PackingMaterials.AddRangeAsync(m1, m2);
        await _context.SaveChangesAsync();

        await _context.Set<PackingMaterialConsumption>().AddRangeAsync(
            new PackingMaterialConsumption(m1.Id, new DateOnly(2026, 1, 10), ConsumptionType.PerOrder, 5m, "INV-1"),
            new PackingMaterialConsumption(m2.Id, new DateOnly(2026, 1, 11), ConsumptionType.PerOrder, 3m, "INV-2"));
        await _context.SaveChangesAsync();

        var countingRepository = new CountingRepositoryWrapper(new PackingMaterialRepository(_context));
        var handler = new GetConsumptionHistoryHandler(
            countingRepository,
            NullLogger<GetConsumptionHistoryHandler>.Instance);

        // Act
        var response = await handler.Handle(new GetConsumptionHistoryRequest(), CancellationToken.None);

        // Assert
        response.Items.Should().HaveCount(2);

        countingRepository.GetAllAsyncCallCount.Should().Be(0,
            "GetConsumptionHistoryHandler must never load the full packing-materials table");
        countingRepository.GetMaterialNamesByIdsAsyncCallCount.Should().Be(1,
            "material names must be resolved with a single targeted lookup per request");

        countingRepository.LastMaterialNamesByIdsAsyncIds.Should().NotBeNull();
        var expectedIds = response.Items.Select(i => i.PackingMaterialId).Distinct().ToHashSet();
        countingRepository.LastMaterialNamesByIdsAsyncIds!.ToHashSet().Should().BeSubsetOf(expectedIds);
        countingRepository.LastMaterialNamesByIdsAsyncIds!.Count.Should().BeLessThanOrEqualTo(expectedIds.Count);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>
    /// Wrapper around PackingMaterialRepository that tracks calls to GetAllAsync and
    /// GetMaterialNamesByIdsAsync to verify GetConsumptionHistoryHandler's data access pattern.
    /// </summary>
    private sealed class CountingRepositoryWrapper : IPackingMaterialRepository
    {
        private readonly PackingMaterialRepository _inner;

        public int GetAllAsyncCallCount { get; private set; }
        public int GetMaterialNamesByIdsAsyncCallCount { get; private set; }
        public IReadOnlyCollection<int>? LastMaterialNamesByIdsAsyncIds { get; private set; }

        public CountingRepositoryWrapper(PackingMaterialRepository inner)
        {
            _inner = inner;
        }

        public async Task<IEnumerable<PackingMaterial>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            GetAllAsyncCallCount++;
            return await _inner.GetAllAsync(cancellationToken);
        }

        public async Task<IReadOnlyDictionary<int, string>> GetMaterialNamesByIdsAsync(
            IEnumerable<int> packingMaterialIds,
            CancellationToken cancellationToken = default)
        {
            GetMaterialNamesByIdsAsyncCallCount++;
            var ids = packingMaterialIds as IReadOnlyCollection<int> ?? packingMaterialIds.ToArray();
            LastMaterialNamesByIdsAsyncIds = ids;
            return await _inner.GetMaterialNamesByIdsAsync(ids, cancellationToken);
        }

        // Delegate all other methods to inner repository
        public Task<PackingMaterial?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
            => _inner.GetByIdAsync(id, cancellationToken);

        public Task<IEnumerable<PackingMaterialLog>> GetRecentLogsAsync(int packingMaterialId, DateTime fromDate, CancellationToken cancellationToken = default)
            => _inner.GetRecentLogsAsync(packingMaterialId, fromDate, cancellationToken);

        public Task<IReadOnlyDictionary<int, IReadOnlyList<PackingMaterialLog>>> GetRecentLogsForMaterialsAsync(
            IEnumerable<int> packingMaterialIds,
            DateTime fromDate,
            CancellationToken cancellationToken = default)
            => _inner.GetRecentLogsForMaterialsAsync(packingMaterialIds, fromDate, cancellationToken);

        public Task<bool> HasDailyProcessingBeenRunAsync(DateOnly date, CancellationToken cancellationToken = default)
            => _inner.HasDailyProcessingBeenRunAsync(date, cancellationToken);

        public Task<bool> AddDailyRunAsync(PackingMaterialDailyRun run, CancellationToken cancellationToken = default)
            => _inner.AddDailyRunAsync(run, cancellationToken);

        public Task<IEnumerable<PackingMaterial>> GetAllWithAllocationsAsync(CancellationToken cancellationToken = default)
            => _inner.GetAllWithAllocationsAsync(cancellationToken);

        public Task<PackingMaterial?> GetByIdWithAllocationsAsync(int id, CancellationToken cancellationToken = default)
            => _inner.GetByIdWithAllocationsAsync(id, cancellationToken);

        public Task AddConsumptionRowsAsync(IEnumerable<PackingMaterialConsumption> rows, CancellationToken cancellationToken = default)
            => _inner.AddConsumptionRowsAsync(rows, cancellationToken);

        public Task<IEnumerable<PackingMaterialConsumption>> GetConsumptionsByDateAsync(DateOnly date, CancellationToken cancellationToken = default)
            => _inner.GetConsumptionsByDateAsync(date, cancellationToken);

        public Task<PackingMaterial> AddAsync(PackingMaterial entity, CancellationToken cancellationToken = default)
            => _inner.AddAsync(entity, cancellationToken);

        public Task<IEnumerable<PackingMaterial>> AddRangeAsync(IEnumerable<PackingMaterial> entities, CancellationToken cancellationToken = default)
            => _inner.AddRangeAsync(entities, cancellationToken);

        public Task UpdateAsync(PackingMaterial entity, CancellationToken cancellationToken = default)
            => _inner.UpdateAsync(entity, cancellationToken);

        public Task DeleteAsync(PackingMaterial entity, CancellationToken cancellationToken = default)
            => _inner.DeleteAsync(entity, cancellationToken);

        public Task DeleteAsync(int id, CancellationToken cancellationToken = default)
            => _inner.DeleteAsync(id, cancellationToken);

        public Task DeleteRangeAsync(IEnumerable<PackingMaterial> entities, CancellationToken cancellationToken = default)
            => _inner.DeleteRangeAsync(entities, cancellationToken);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _inner.SaveChangesAsync(cancellationToken);

        public Task<IEnumerable<PackingMaterial>> FindAsync(System.Linq.Expressions.Expression<System.Func<PackingMaterial, bool>> predicate, CancellationToken cancellationToken = default)
            => _inner.FindAsync(predicate, cancellationToken);

        public Task<PackingMaterial?> SingleOrDefaultAsync(System.Linq.Expressions.Expression<System.Func<PackingMaterial, bool>> predicate, CancellationToken cancellationToken = default)
            => _inner.SingleOrDefaultAsync(predicate, cancellationToken);

        public Task<bool> AnyAsync(System.Linq.Expressions.Expression<System.Func<PackingMaterial, bool>> predicate, CancellationToken cancellationToken = default)
            => _inner.AnyAsync(predicate, cancellationToken);

        public Task<int> CountAsync(System.Linq.Expressions.Expression<System.Func<PackingMaterial, bool>>? predicate = null, CancellationToken cancellationToken = default)
            => _inner.CountAsync(predicate, cancellationToken);

        public Task<(IReadOnlyList<MaterialConsumptionHistoryRecord> Items, int TotalCount)> GetConsumptionHistoryAsync(
            MaterialConsumptionHistoryFilter filter,
            int skip,
            int take,
            bool ascending,
            CancellationToken cancellationToken = default)
            => _inner.GetConsumptionHistoryAsync(filter, skip, take, ascending, cancellationToken);
    }
}
```

2. Run the new test to confirm it fails (red) against today's `GetAllAsync`-based handler:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConsumptionHistoryQueryCountTests"
```

Expected: **1 failed**. The failure is on the `GetAllAsyncCallCount.Should().Be(0, ...)` assertion — FluentAssertions reports something like `Expected countingRepository.GetAllAsyncCallCount to be 0 because GetConsumptionHistoryHandler must never load the full packing-materials table, but found 1.`

3. Commit the failing test as the documented "red" checkpoint for the handler swap done in the next task:

```bash
git add backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetConsumptionHistoryQueryCountTests.cs
git commit -m "#4027: Add failing query-count test for GetConsumptionHistoryHandler's material-name lookup"
```

---

### task: wire-handler-to-targeted-lookup

Replaces `GetConsumptionHistoryHandler.Handle`'s `GetAllAsync` call with the new page-scoped `GetMaterialNamesByIdsAsync` lookup, turning the previous task's test green and completing FR-2.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetConsumptionHistory/GetConsumptionHistoryHandler.cs`

**Steps:**

1. In `GetConsumptionHistoryHandler.cs`, replace:

```csharp
        var materialNames = (await _repository.GetAllAsync(cancellationToken))
            .ToDictionary(m => m.Id, m => m.Name);
```

with:

```csharp
        var materialIds = records.Select(r => r.PackingMaterialId).Distinct();
        var materialNames = await _repository.GetMaterialNamesByIdsAsync(materialIds, cancellationToken);
```

The surrounding `Handle` method (after the edit) reads:

```csharp
        var (records, totalCount) = await _repository.GetConsumptionHistoryAsync(
            filter, skip, pageSize, ascending: !request.SortDescending, cancellationToken);

        var materialIds = records.Select(r => r.PackingMaterialId).Distinct();
        var materialNames = await _repository.GetMaterialNamesByIdsAsync(materialIds, cancellationToken);

        var items = records.Select(r => MapToDto(r, materialNames)).ToList();
```

No other line in the file changes.

2. Run the query-count test added in the previous task and confirm it now passes (green):

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConsumptionHistoryQueryCountTests"
```

Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1`.

3. Run the existing handler test suite to confirm all four pre-existing tests still pass unchanged:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConsumptionHistoryHandlerTests"
```

Expected: `Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4` — covering `Handle_ResolvesMaterialName_AndUnionsSources`, `Handle_ClampsPageSizeToMaximum`, `Handle_ConsumptionOnlyFilter_ExcludesLogs`, and `Handle_UnknownMaterial_FallsBackToPlaceholderName` (the last one specifically re-confirms the `"Neznámý"` fallback for an id with no resolvable name still works).

4. Run the full PackingMaterials test suite and a full build as a final regression check:

```bash
dotnet build
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterials"
```

Expected: build succeeds with 0 errors; all PackingMaterials tests pass, 0 failed.

5. Commit:

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetConsumptionHistory/GetConsumptionHistoryHandler.cs
git commit -m "#4027: Resolve material names from the current page instead of GetAllAsync in GetConsumptionHistoryHandler"
```

---

## Self-Review

**FR-1 coverage** (add `GetMaterialNamesByIdsAsync` to `IPackingMaterialRepository`, `WHERE Id IN (...)` implementation, empty-collection short-circuit, missing-id absence, deduplication) — covered by `add-repository-method`: interface signature added verbatim, `PackingMaterialRepository` implementation added verbatim (server-side `Select` + in-memory `ToDictionary`, per the arch review's EF Core translation guidance), and `PackingMaterialRepositoryGetMaterialNamesByIdsAsyncTests.cs` directly exercises all four acceptance criteria (targeted subset lookup, duplicate-id dedup, missing-id absence, empty-input-no-query via a disposed-context probe).

**FR-2 coverage** (handler drops `GetAllAsync`, calls `GetMaterialNamesByIdsAsync` exactly once with page-scoped distinct ids, empty-`records` path needs no full-table fallback, response shape/`"Neznámý"` fallback unchanged) — covered by `wire-handler-to-targeted-lookup`'s handler edit plus the `add-query-count-test` task's assertions (`GetAllAsyncCallCount == 0`, `GetMaterialNamesByIdsAsyncCallCount == 1`, ids are a subset of the page's distinct `PackingMaterialId`s). The empty-`records` path is structurally guaranteed (`records.Select(...).Distinct()` on an empty list yields an empty enumerable, which FR-1's own empty-collection test proves short-circuits with no DB round trip) and does not need a separate handler-level test. The unchanged `"Neznámý"` fallback and response shape are reconfirmed by re-running all four pre-existing `GetConsumptionHistoryHandlerTests.cs` tests unchanged in `wire-handler-to-targeted-lookup` step 3.

**FR-3 coverage** (update `MockPackingMaterialRepository`, add a query-count test proving zero `GetAllAsync` calls / exactly one `GetMaterialNamesByIdsAsync` call with page-scoped ids, all four existing handler tests keep passing, `dotnet build` and the PackingMaterials suite pass) — the mock update is `add-repository-method` step 5; the query-count test is the whole `add-query-count-test` task; the four existing handler tests are reconfirmed in `wire-handler-to-targeted-lookup` step 3; `dotnet build` and the full PackingMaterials suite are run in `wire-handler-to-targeted-lookup` step 4.

**Placeholder scan:** no "TBD", "similar to Task N", or unresolved references found — every code block is complete and self-contained, and every task repeats the exact code it needs rather than pointing at another task.

**Type/method-name consistency:** `GetMaterialNamesByIdsAsync(IEnumerable<int> packingMaterialIds, CancellationToken cancellationToken = default) : Task<IReadOnlyDictionary<int, string>>` is spelled identically across the interface (task 1 step 3), the `PackingMaterialRepository` implementation (task 1 step 4), `MockPackingMaterialRepository` (task 1 step 5), the existing `PackingMaterialsListQueryCountTests.cs` wrapper passthrough (task 1 step 6), the new `PackingMaterialRepositoryGetMaterialNamesByIdsAsyncTests.cs` (task 1 step 1), the new `GetConsumptionHistoryQueryCountTests.cs` wrapper (task 2 step 1), and the handler call site (task 3 step 1). `GetAllAsync` naming and the `"Neznámý"` fallback string are unchanged from the current codebase throughout.

An additional build hazard not explicitly called out in the source artifacts, but confirmed by direct inspection of the codebase before writing this plan, is folded into `add-repository-method`: the file-local `CountingRepositoryWrapper` inside the pre-existing `PackingMaterialsListQueryCountTests.cs` also implements `IPackingMaterialRepository` in full and would fail to compile the moment the interface gains a new member. Step 6 of `add-repository-method` adds the required passthrough there so the whole solution stays buildable after every task's commit.
