# Architecture Review: Remove Redundant Soft-Delete Predicates in `JournalRepository`

## Skip Design: true

Backend-only behavior-preserving refactor inside a single repository class. No UI, no API contract, no DTO, no schema, and no new endpoints. Pure code cleanup.

## Architectural Fit Assessment

The change strengthens alignment with the existing pattern, not weakens it. The codebase already commits to **EF Core global query filters as the canonical soft-delete enforcement mechanism**:

- `JournalEntryConfiguration.cs:53` registers `HasQueryFilter(x => !x.IsDeleted)` and a supporting composite index `(IsDeleted, EntryDate)` at line 62.
- The Marketing module follows the same pattern (`MarketingActionConfiguration.cs:89`) and importantly demonstrates the intended escape hatch: `MarketingActionRepository.cs:140` uses `IgnoreQueryFilters()` exactly where a bypass is genuinely needed.

The Journal repository currently violates this convention by duplicating the predicate in every method, which is the precise readability/maintenance trap called out in the brief. Removing the duplicates restores the invariant: **soft-delete enforcement lives in `IEntityTypeConfiguration<T>`; repositories trust the filter and call `IgnoreQueryFilters()` only when an admin path explicitly needs to opt out**.

Note: The same anti-pattern is present in `MarketingActionRepository.cs:24,49,124` (and possibly elsewhere). The spec correctly scopes this work to Journal only; flag the Marketing duplication in the PR description per the spec's "Out of Scope" rule but do not touch it here.

Integration points are minimal: `JournalRepository` is the sole consumer of `DbSet<JournalEntry>` for read paths; MediatR handlers depend only on `IJournalRepository`, whose public surface is unchanged.

## Proposed Architecture

### Component Overview

```
                                   ┌──────────────────────────────────┐
MediatR handlers                   │ JournalEntryConfiguration        │
       │                           │   • HasQueryFilter(!IsDeleted)   │ ◄── single source of truth
       │  IJournalRepository       │   • IX_IsDeleted_EntryDate index │
       ▼                           └────────────┬─────────────────────┘
┌────────────────────────┐                      │ applied by EF Core
│ JournalRepository      │                      │ to all DbSet<JournalEntry>
│                        │                      │ reads (incl. joins)
│  GetByIdAsync          │ ───────────► Context.Set<JournalEntry>()
│  GetEntriesAsync       │              (no explicit !IsDeleted)
│  SearchEntriesAsync    │
│  GetEntriesByProduct…  │
│  GetJournalIndicators… │ ───────────► JournalEntryProduct ⋈ JournalEntry
└────────────────────────┘                                  ▲
                                                            └── global filter
                                                                applied to join
                                                                source as well
```

The architectural shape does not change. The refactor only removes five predicate fragments. The "single source of truth for soft-delete" arrow is what gets re-established.

### Key Design Decisions

#### Decision 1: Trust the global query filter; do not defense-in-depth duplicate it
**Options considered:**
1. Keep duplicate predicates as "belt and braces" defense.
2. Remove all duplicate predicates and rely on the global filter (spec's choice).
3. Remove the global filter and keep predicates explicit per query.

**Chosen approach:** Option 2.

**Rationale:** Option 1 is the current state and the source of the three problems in the brief (readability, maintenance trap, silent inconsistency). Option 3 spreads soft-delete enforcement across N query sites — every new query becomes a place to forget the guard, which is exactly the failure mode global filters were designed to prevent, and it would also break the join-side enforcement that `GetJournalIndicatorsAsync` quietly depends on. Option 2 makes `JournalEntryConfiguration` the single, auditable enforcement point and aligns Journal with the Marketing module's escape-hatch pattern (`IgnoreQueryFilters()` per-query when admin access is intentionally needed).

#### Decision 2: Do not introduce an explicit "include soft-deleted" repository overload
**Options considered:**
1. Add `bool includeDeleted = false` parameters now for future admin paths.
2. Leave the interface unchanged; introduce `IgnoreQueryFilters()` only when an admin path actually exists.

**Chosen approach:** Option 2.

**Rationale:** YAGNI. No admin path exists, the spec explicitly lists it as out of scope, and a premature `includeDeleted` flag would just smuggle the same anti-pattern back into the repository under a different name. When (if) an admin query path is added, it should be a separate, intentionally-named method on `IJournalRepository` (e.g., `GetByIdIncludingDeletedAsync`) that uses `IgnoreQueryFilters()` — following the Marketing precedent.

#### Decision 3: Keep the existing composite index `(IsDeleted, EntryDate)`
**Options considered:**
1. Drop the index since explicit predicates are gone.
2. Keep the index unchanged.

**Chosen approach:** Option 2.

**Rationale:** The predicate EF Core appends from the global filter still references `IsDeleted`; the index continues to serve it. Touching the index would expand the change beyond the surgical scope and require a migration for no functional gain. The spec correctly notes this in its "Out of Scope" section.

## Implementation Guidance

### Directory / Module Structure

No new files. All edits stay in:

```
backend/src/Anela.Heblo.Persistence/Catalog/Journal/
└── JournalRepository.cs   (5 surgical edits)
```

Tests:

```
backend/test/Anela.Heblo.Tests/Features/Journal/
└── JournalRepositoryIntegrationTests.cs   (additive only — see below)
```

### Interfaces and Contracts

`IJournalRepository` is **unchanged**. No new methods, no new parameters, no signature changes. Public behavior contract: every read method continues to return only non-soft-deleted entries.

### Data Flow

For each affected method, the post-refactor data flow is:

1. Caller invokes repository method.
2. Repository builds LINQ against `Context.Set<JournalEntry>()` (and `Context.Set<JournalEntryProduct>()` for indicators), with no explicit `!IsDeleted` predicate in repository code.
3. EF Core's model-level translator appends `!IsDeleted` to the SQL for every reference to `JournalEntry` — including the join source in `GetJournalIndicatorsAsync`.
4. Generated SQL contains exactly one `IsDeleted` predicate per `JournalEntry` reference. Spot-check via `Microsoft.EntityFrameworkCore` logging on `LogLevel.Information` in one test (optional, per FR-6).

The five edit sites and their final form:

| Site | Current | Required |
|------|---------|----------|
| `JournalRepository.cs:26` | `FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)` | `FirstOrDefaultAsync(x => x.Id == id, ct)` |
| `JournalRepository.cs:40` | `.Where(x => !x.IsDeleted)` | (line deleted) |
| `JournalRepository.cs:90` | `.Where(x => !x.IsDeleted)` | (line deleted) |
| `JournalRepository.cs:172` | `.Where(x => !x.IsDeleted && (x.ProductAssociations.Any(pa => productCode.StartsWith(pa.ProductCodePrefix))))` | `.Where(x => x.ProductAssociations.Any(pa => productCode.StartsWith(pa.ProductCodePrefix)))` |
| `JournalRepository.cs:188` | `.Join(Context.Set<JournalEntry>().Where(je => !je.IsDeleted), …)` | `.Join(Context.Set<JournalEntry>(), …)` |

Also remove the now-orphan `.AsQueryable()` at line 41 and line 91 only if no longer needed for the subsequent reassignment compile (it is needed because `Include` returns `IIncludableQueryable<...>` and the `switch` reassigns to `IQueryable<JournalEntry>` — leave `.AsQueryable()` in place to preserve the existing pattern; surgical change rule).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Global filter assumption breaks if someone later calls `IgnoreQueryFilters()` on these methods without re-adding a predicate. | Low | This is the *intended* future behavior. Document the convention in the PR description: "Soft-delete is enforced by the `IEntityTypeConfiguration` filter. To include deleted rows, add a new repository method using `IgnoreQueryFilters()` — do not modify these." |
| EF Core in-memory provider used in `JournalRepositoryIntegrationTests` may not apply global query filters identically to PostgreSQL in edge cases. | Low | EF Core in-memory **does** honor `HasQueryFilter` — existing tests already rely on this implicitly. Add one focused test per affected method that inserts a soft-deleted entry and asserts it is excluded, to lock in the contract. |
| `GetJournalIndicatorsAsync` join-side filter regression — risk that the global filter is *not* applied through `Context.Set<JournalEntry>()` in the join projection. | Low | EF Core applies global filters to any `DbSet<T>` reference, including join sources. Verify with one test: insert a soft-deleted `JournalEntry` with a matching `JournalEntryProduct` and assert it does not appear in the indicator count. |
| Reviewer mistakes the diff for a behavior change. | Low | PR description should explicitly state: "Behavior-preserving. The global filter at `JournalEntryConfiguration.cs:53` already enforces `!IsDeleted` on every query; these `.Where` clauses produced duplicated SQL." Link the spec. |
| The `JournalEntryProduct` join in `GetJournalIndicatorsAsync` could in theory orphan to a hard-deleted (not soft-deleted) `JournalEntry` and produce a `null` join match. | Low | Out of scope — same risk exists today; cascade delete is configured at `JournalEntryConfiguration.cs:69`. Do not address here. |

## Specification Amendments

The spec is well-scoped and complete. Two small clarifications worth adding before implementation:

1. **FR-6 verification hardening.** The spec's "spot-check by enabling EF Core query logging" is good guidance but optional. Strengthen the acceptance bar by **adding one targeted unit test per affected method** that inserts a soft-deleted entry and asserts exclusion. This is a small additive change to `JournalRepositoryIntegrationTests.cs` (the existing test class already uses the in-memory provider and has no soft-delete coverage today). This converts FR-6 from "reviewer inspects" to "test enforces" without expanding scope. Five small `[Fact]` methods, no new test infrastructure.

2. **PR description note for the parallel Marketing-module finding.** The spec's Out of Scope correctly defers a wider audit. Add an explicit instruction in the PR description: "The same pattern exists in `MarketingActionRepository.cs:24,49,124`. Not fixed in this PR per scope; track separately." This satisfies the spec's "Mention in PR description if encountered" without expanding the diff.

No other amendments. The functional requirements, acceptance criteria, and risk framing in the spec are accurate.

## Prerequisites

None. The work can begin immediately:

- No migrations (schema and indexes unchanged).
- No configuration changes.
- No infrastructure changes.
- No new packages.
- No dependent in-flight features.
- Existing test infrastructure (`ApplicationDbContext` with in-memory provider, FluentAssertions, xUnit, Moq) is sufficient for the additive soft-delete exclusion tests.

Validation gates per `CLAUDE.md`: `dotnet build`, `dotnet format`, `dotnet test` for the touched test project. No frontend, no E2E.