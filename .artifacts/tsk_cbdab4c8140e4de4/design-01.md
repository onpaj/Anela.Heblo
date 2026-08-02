# Design — Marketing `GetPagedAsync` IncludeDeleted flag is dead

No UI section: this is a backend-only query-layer fix. The frontend (`useMarketingCalendar.ts`) already sends `includeDeleted` correctly today; nothing about its request shape, hook signature, or component tree changes.

## Component design

### `MarketingActionRepository.GetPagedAsync` (backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs:27-103)

**Current responsibility:** build a filtered, paged `IQueryable<MarketingAction>` from `MarketingActionQueryCriteria` and materialize a `PagedResult<MarketingAction>`.

**Boundary change:** the method gains one additional responsibility it was supposed to have already — explicitly choosing whether the global soft-delete filter applies, instead of leaving that decision to EF Core's implicit filter application. This mirrors the existing split in the same class: `GetByIdAsync`/`GetForCalendarAsync` never see deleted rows (rely on the global filter, or duplicate it, no override needed); `GetByOutlookEventIdsAsync` always bypasses it (`IgnoreQueryFilters()` unconditionally, since sync-dedup must see deleted imports); `GetPagedAsync` is the only method that needs to switch between the two based on caller input, so it's the only one that needs a branch on `IgnoreQueryFilters()`.

**Interface (unchanged):**
```csharp
Task<PagedResult<MarketingAction>> GetPagedAsync(
    MarketingActionQueryCriteria criteria,
    CancellationToken cancellationToken = default)
```
No signature change. `IMarketingActionRepository`, `GetMarketingActionsHandler`, `GetMarketingActionsRequest`, and the frontend hook are all untouched — the fix is entirely inside the method body.

**Internal change** — replace the query construction at lines 31-39:

```csharp
var query = Context.Set<MarketingAction>()
    .Include(x => x.ProductAssociations)
    .Include(x => x.FolderLinks)
    .AsQueryable();

if (criteria.IncludeDeleted)
{
    query = query.IgnoreQueryFilters();
}
else
{
    query = query.Where(x => !x.IsDeleted);
}
```

Rationale for keeping the `else` branch's explicit `Where(x => !x.IsDeleted)` rather than dropping it and relying on the global filter alone: it's a no-op change in behavior (the global filter already enforces this), it keeps the diff minimal and symmetric (one `if`/`else`, each branch stating its own intent), and it matches the plan's default (open question resolved: leave in place, don't touch the false branch's mechanism). `IgnoreQueryFilters()` is a queryable-wide switch — once called, no model-level filter applies to the rest of the query, so it must come before the other `Where` clauses are composed; placing it as the very first operation on `query` (before search/type/date/product filters) guarantees this regardless of clause order below it.

No other method in the class changes. `GetByIdAsync`, `GetForCalendarAsync`, `GetByOutlookEventIdsAsync` are explicitly out of scope per the plan and receive no edits.

### Test component: `MarketingActionRepositoryGetPagedTests`

New test class, `backend/test/Anela.Heblo.Tests/Repositories/MarketingActionRepositoryGetPagedTests.cs`, following the `MeetingTranscriptRepositoryTests` shape (same directory — that's where repository tests against `ApplicationDbContext` already live, not the `Persistence/Marketing` path the plan tentatively suggested):

```csharp
public class MarketingActionRepositoryGetPagedTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly MarketingActionRepository _repository;

    public MarketingActionRepositoryGetPagedTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        _repository = new MarketingActionRepository(_context, NullLogger<MarketingActionRepository>.Instance);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task GetPagedAsync_IncludeDeletedTrue_ReturnsBothRows()
    {
        await SeedActionAsync(deleted: false);
        await SeedActionAsync(deleted: true);

        var result = await _repository.GetPagedAsync(
            new MarketingActionQueryCriteria { IncludeDeleted = true });

        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPagedAsync_IncludeDeletedFalse_ReturnsOnlyNonDeleted()
    {
        await SeedActionAsync(deleted: false);
        await SeedActionAsync(deleted: true);

        var result = await _repository.GetPagedAsync(
            new MarketingActionQueryCriteria { IncludeDeleted = false });

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(x => !x.IsDeleted);
    }

    private async Task<MarketingAction> SeedActionAsync(bool deleted)
    {
        var action = new MarketingAction(
            title: $"Action {Guid.NewGuid():N}",
            description: null,
            actionType: MarketingActionType.Campaign, // or whichever enum member exists
            startDate: DateTime.UtcNow,
            endDate: null,
            createdByUserId: "seed-user",
            createdByUsername: "Seeder",
            utcNow: DateTime.UtcNow);

        if (deleted)
        {
            action.SoftDelete("seed-user", "Seeder", DateTime.UtcNow);
        }

        _context.Set<MarketingAction>().Add(action);
        await _context.SaveChangesAsync();
        return action;
    }
}
```

Notes for whoever implements this:
- Constructor takes `MarketingActionRepository(ApplicationDbContext, ILogger<MarketingActionRepository>)` — pass `NullLogger<MarketingActionRepository>.Instance` (from `Microsoft.Extensions.Logging.Abstractions`), the `_logger` field is currently unused inside `GetPagedAsync` so no log assertions are needed.
- `MarketingAction` has no public parameterless constructor usable from tests (the private one is EF-only) — must go through the domain constructor + `SoftDelete()`, exactly as shown, not object-initializer syntax.
- Check the actual `MarketingActionType` enum for a valid member name before writing the test; the design above uses `Campaign` as a placeholder.
- The two tests are the FR-1/FR-2 acceptance criteria from the plan verbatim: seed one non-deleted + one soft-deleted row, assert `TotalCount == 2` when `IncludeDeleted = true` and `TotalCount == 1` when `false`.
- To satisfy the plan's red/green check (step 2), run this test file against the pre-fix repository once (temporarily revert the `if`/`else` swap) to confirm `GetPagedAsync_IncludeDeletedTrue_ReturnsBothRows` fails there, then reapply the fix and confirm both pass.

## Data schemas

No schema, request, or response shape changes:
- `MarketingActionQueryCriteria.IncludeDeleted` (bool) — already exists, unchanged.
- `GetMarketingActionsRequest.IncludeDeleted` — already exists, unchanged.
- `MarketingActionConfiguration.HasQueryFilter(x => !x.IsDeleted)` — unchanged; the fix works *with* the global filter (via `IgnoreQueryFilters()`), not around it.
- `PagedResult<MarketingAction>` response shape — unchanged; this fix only affects which rows populate `Items`/`TotalCount` when `IncludeDeleted = true`, not the shape.
- No new migration required.

## Scope confirmation

Single production file touched: `MarketingActionRepository.cs`, lines 31-39 only. One new test file. No frontend changes, no API contract changes, no other repository methods touched — consistent with the plan's in-scope/out-of-scope split.
