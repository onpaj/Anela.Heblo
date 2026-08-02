# Architecture review — Marketing `GetPagedAsync` IncludeDeleted flag is dead

## Verdict: approved, no changes required

The plan and design were checked against the actual source (not just against each
other). Every claim they make about surrounding code was verified directly:

- `MarketingActionConfiguration.cs:89` — `builder.HasQueryFilter(x => !x.IsDeleted)` confirmed as written.
- `MarketingActionRepository.cs:129` (`GetByOutlookEventIdsAsync`) — confirmed unconditional `.IgnoreQueryFilters()`, the reference pattern the fix mirrors.
- `MarketingActionQueryCriteria.cs` — `IncludeDeleted` is a plain `bool`, default `false`, exactly as described.
- `MarketingAction` domain constructor and `SoftDelete(userId, username, utcNow)` — the design's test-seeding code matches the real signatures parameter-for-parameter. No placeholder drift.
- `BaseRepository<TEntity, TKey>` — generic CRUD only, no query-filter handling, no unit-of-work surprises that would interact with `IgnoreQueryFilters()`.
- `GetMarketingActionsHandlerTests.cs` — mocks `IMarketingActionRepository` entirely, so it cannot observe this change; no regression risk there.
- Test location precedent — `backend/test/Anela.Heblo.Tests/Repositories/MeetingTranscriptRepositoryTests.cs` exists and matches the exact shape the design proposes (InMemory `ApplicationDbContext`, constructor-injected repository, `IDisposable`). The design's choice of `Repositories/` over the plan's tentative `Persistence/Marketing/` is the correct call — it's grounded in a real existing file, not invented.

## Alignment with existing patterns

The fix is a one-line-semantics change that makes `GetPagedAsync` consistent with
the codebase's own established convention: global query filter is the default,
`IgnoreQueryFilters()` is the explicit opt-out, used unconditionally at
`GetByOutlookEventIdsAsync:129` and now conditionally here. This is not a new
pattern — it's applying an existing one correctly. `JournalEntryConfiguration.cs:53`
has the identical `HasQueryFilter(x => !x.IsDeleted)` shape, confirming soft-delete
via global filter is a repo-wide convention, not a Marketing-module one-off.

One correction to the design doc's own framing: it states `GetByIdAsync` and
`GetForCalendarAsync` "rely on the global filter" — in fact both also carry a
redundant manual `!x.IsDeleted` predicate (`GetByIdAsync:24`, `GetForCalendarAsync:113`),
same as the `IncludeDeleted == false` branch being fixed here. This doesn't change
the design's conclusion (those methods are correctly out of scope — no finding was
raised against them and they have no include-deleted branch to be dead), but the
design's wording overstates how clean the "reference" methods are. Not worth a
revision cycle; noted for whoever reads this later.

## Proposed change

```csharp
if (criteria.IncludeDeleted)
{
    query = query.IgnoreQueryFilters();
}
else
{
    query = query.Where(x => !x.IsDeleted);
}
```

Ordering is correct: `IgnoreQueryFilters()` is applied as the first operation on
`query`, before the search/type/date/product filters are composed — it's a
queryable-wide switch, not a predicate that needs positioning relative to other
`Where` clauses, but doing it first keeps the branch self-contained and avoids any
future confusion if someone reorders the filters below it.

The `else` branch keeps the existing (redundant, harmless) manual `Where(x =>
!x.IsDeleted)` rather than dropping it to rely on the global filter alone. Correct
call for a surgical fix: touching that line would be an unrelated cleanup riding on
a bug-fix diff, and issue #2511 already established the redundancy is intentional
documentation-by-code elsewhere in this codebase, not a defect.

## Test design

InMemory-provider EF Core repository test is the right tool here — this is
specifically a global-query-filter interaction bug, which a mocked repository
(as `GetMarketingActionsHandlerTests` uses) structurally cannot catch. EF Core's
InMemory provider applies model-level `HasQueryFilter` the same way relational
providers do; this is standard, provider-agnostic EF Core behavior, not something
that needs a Postgres/Testcontainers dependency to verify.

The two tests (`IncludeDeletedTrue_ReturnsBothRows`,
`IncludeDeletedFalse_ReturnsOnlyNonDeleted`) map 1:1 to FR-1/FR-2's acceptance
criteria and are the minimum needed to prove the fix — no gold-plating.

One item flagged correctly by the design itself and worth restating so it isn't
missed during implementation: the test's `MarketingActionType.Campaign` placeholder
does not exist. The real enum (`MarketingActionType.cs`) is `SocialMedia = 0, Blog =
1, Newsletter = 2, PR = 3, Event = 4, Meeting = 99`. Any concrete member works for
this test (the enum value is irrelevant to the soft-delete behavior under test) —
use `MarketingActionType.Blog` for consistency with the existing handler test's
convention (`GetMarketingActionsHandlerTests.cs` uses `Blog` in its own fixtures).

## Risks

None beyond the trivial. `IgnoreQueryFilters()` only affects filter composition on
this one query, scoped to `IncludeDeleted == true` requests — the default (false)
path, which covers the calendar view and normal listing, is untouched. No schema,
API contract, or frontend change. No migration required.

## Prerequisites before implementation

None outstanding. Domain constructor/`SoftDelete` signatures, test file location,
and the `IgnoreQueryFilters()` reference pattern have all been verified against
current source in this review — implementation can proceed directly from the
design doc, substituting `MarketingActionType.Blog` for the `Campaign` placeholder.
