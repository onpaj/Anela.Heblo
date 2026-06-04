```markdown
# Architecture Review: Extract Duplicated Journal Sorting Logic

## Skip Design: true

Backend-only refactor inside a single file. No UI components, screens, or visual changes.

## Architectural Fit Assessment

The proposal aligns perfectly with an **already-established pattern** in this codebase. `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/IssuedInvoiceRepository.cs:211` already uses the exact shape being proposed:

```csharp
private static IQueryable<IssuedInvoice> ApplySorting(
    IQueryable<IssuedInvoice> query, string? sortBy, bool sortDescending)
```

Other modules (`GetCatalogListHandler`, `GetProductMarginsHandler`, `GetProductMarginSummaryHandler`) also follow the `ApplySorting(...)` helper convention. Consolidating `JournalRepository` to match is a maintenance win and an inconsistency fix — no architectural risk.

Integration points are minimal: the change is fully encapsulated inside `JournalRepository`. `IJournalRepository` (the public contract) is unaffected, and there are no MediatR handlers or DI registrations to touch. The class already inherits from `BaseRepository<JournalEntry, int>`, but no abstraction at that level needs to change.

## Proposed Architecture

### Component Overview

```
JournalRepository (unchanged surface)
├── GetEntriesAsync(...)        ──┐
│   └── query = ApplySorting(...) ├──→ ApplySorting(IQueryable<JournalEntry>, string?, string)
├── SearchEntriesAsync(...)     ──┘     [private static, single source of truth]
├── GetEntriesByProductAsync(...)   [unchanged — has its own fixed ordering]
└── GetJournalIndicatorsAsync(...)  [unchanged]
```

`ApplySorting` is a pure expression-tree composition helper. It takes an `IQueryable<JournalEntry>`, appends an `OrderBy` / `OrderByDescending`, and returns the resulting `IQueryable<JournalEntry>`. No database I/O, no allocation surprises — EF Core sees the same expression tree it did before.

### Key Design Decisions

#### Decision 1: Location of the helper
**Options considered:**
- A. Private static method on `JournalRepository` itself (matches `IssuedInvoiceRepository`).
- B. Internal static utility class shared across repositories (e.g., `JournalQueryExtensions`).
- C. Generic sort-helper on `BaseRepository<,>` driven by a column registry.

**Chosen approach:** A — private static method on `JournalRepository`.

**Rationale:** The sort column set is feature-specific (`Title`, `CreatedAt`, `EntryDate` exist only on `JournalEntry`). The codebase already endorses approach A in `IssuedInvoiceRepository`. Approach B is YAGNI — there is no second consumer. Approach C would require reflection or strongly-typed selectors and pulls in complexity the spec explicitly avoids.

#### Decision 2: Preserve current (slightly quirky) semantics verbatim
**Options considered:**
- A. Preserve case-sensitive `"ASC"` check and silent fallback for unknown `sortBy`.
- B. Tighten contract — case-insensitive direction, validate `sortBy`, reject invalid values.

**Chosen approach:** A — exact behavioral preservation.

**Rationale:** Spec FR-3 mandates zero external behavior change. Tightening the contract is out of scope and would risk breaking frontend callers that currently rely on the lenient parsing. Any cleanup belongs in a follow-up.

#### Decision 3: Signature shape (`string` vs `bool` for direction)
**Options considered:**
- A. `(IQueryable<JournalEntry>, string?, string)` — mirror existing method signatures.
- B. `(IQueryable<JournalEntry>, string?, bool)` — match `IssuedInvoiceRepository.ApplySorting` shape.

**Chosen approach:** A.

**Rationale:** Callers in `JournalRepository` already accept `string sortDirection` from the public method signatures. Converting to `bool` at the helper boundary would mean adding `sortDirection == "ASC"` evaluation *outside* the helper — which re-introduces a different form of duplication and changes the semantics encoded in one place (any non-`"ASC"` value falls through to descending). Keeping the helper string-based isolates the direction comparison in one location.

## Implementation Guidance

### Directory / Module Structure

Single-file change. No new files.

```
backend/src/Anela.Heblo.Persistence/Catalog/Journal/
└── JournalRepository.cs        [modified — extract helper, replace 2 call sites]

backend/test/Anela.Heblo.Tests/Features/Journal/
└── JournalRepositoryIntegrationTests.cs   [modified — add sort matrix tests]
```

### Interfaces and Contracts

**Public contract:** `IJournalRepository` is unchanged. No PRs to client code, no OpenAPI regeneration.

**New private helper signature** (place near the bottom of `JournalRepository`, after `GetJournalIndicatorsAsync`, to keep public methods at the top):

```csharp
private static IQueryable<JournalEntry> ApplySorting(
    IQueryable<JournalEntry> query, string? sortBy, string sortDirection) =>
    sortBy?.ToLower() switch
    {
        "title" => sortDirection == "ASC"
            ? query.OrderBy(x => x.Title)
            : query.OrderByDescending(x => x.Title),
        "createdat" => sortDirection == "ASC"
            ? query.OrderBy(x => x.CreatedAt)
            : query.OrderByDescending(x => x.CreatedAt),
        _ => sortDirection == "ASC"
            ? query.OrderBy(x => x.EntryDate)
            : query.OrderByDescending(x => x.EntryDate)
        };
```

Call site replacement at `JournalRepository.cs:44–55` and `:135–146`:

```csharp
// Sorting
query = ApplySorting(query, sortBy, sortDirection);
```

### Data Flow

For both `GetEntriesAsync` and `SearchEntriesAsync`:

```
Controller / MediatR Handler
   │ (sortBy: string, sortDirection: string)
   ▼
JournalRepository.GetEntriesAsync | SearchEntriesAsync
   │ build IQueryable: Include → Where(!IsDeleted) → [filters in Search only]
   ▼
ApplySorting(query, sortBy, sortDirection)
   │ appends OrderBy / OrderByDescending to expression tree
   ▼
Skip / Take / CountAsync / ToListAsync
   │ EF Core translates the composed expression tree to a single SQL query
   ▼
PagedResult<JournalEntry>
```

The expression tree appended by `ApplySorting` is structurally identical to today's inline `switch`. EF Core's translator has no way to observe that the `OrderBy(...)` call came from a static method instead of from inline code — the resulting SQL is byte-identical.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| EF Core translation difference between inline and helper-returned `IQueryable` | LOW | Method returns plain `IQueryable<JournalEntry>` (no closure capture, no boxing). Verified by analogous `IssuedInvoiceRepository.ApplySorting` which translates correctly. Integration tests against in-memory provider will catch any ordering regression. |
| `query.ToQueryString()` verification (NFR-1) not supported by in-memory provider | LOW | The in-memory provider does not generate SQL, so `ToQueryString()` returns a non-SQL string there. SQL parity is implicit from expression-tree identity; do not block on a literal SQL diff. See Spec Amendment 1. |
| Future contributor moves helper to `BaseRepository<,>` and breaks other modules | LOW | Keep helper `private static` (not `protected`). Document via the existing pattern in `IssuedInvoiceRepository` — both stay symmetric. |
| Test additions accidentally rely on in-memory provider ordering quirks (e.g. `null` ordering for `Title`) | MEDIUM | Use seed data where `Title` is always non-null, or explicitly test the null-Title case as a known divergence between in-memory and PostgreSQL. Prefer non-null `Title` for sort assertions to keep tests provider-agnostic. |
| Whitespace / placement drift causes spurious diffs | LOW | Place `ApplySorting` at the bottom of the class (after `GetJournalIndicatorsAsync`) to minimize churn in the upper methods. `dotnet format` after the change. |

## Specification Amendments

1. **NFR-1 (Performance) — relax the `ToQueryString()` verification requirement.**
   The current `JournalRepositoryIntegrationTests` uses `UseInMemoryDatabase` (see `JournalRepositoryIntegrationTests.cs:21`). The in-memory provider does not produce relational SQL, so a literal SQL-diff via `ToQueryString()` is not meaningful. Acceptable substitute: assert ordering correctness against the in-memory provider for every `(sortBy, sortDirection)` combination and rely on expression-tree equivalence for SQL parity. If a relational SQL parity assertion is required, it should run against a Postgres test container (out of scope for this refactor).

2. **FR-4 (Test coverage) — explicitly note current state.**
   `JournalRepositoryIntegrationTests.cs` currently has **no** tests for `GetEntriesAsync` or `SearchEntriesAsync` sort behavior. The eight `(sortBy × sortDirection)` combinations called out in FR-1 (`"title"`, `"createdat"`, `null`, unknown × `"ASC"`, `"DESC"`, unknown-direction) need to be **added**, not just verified — there is nothing to update. Plan for ~16 new test cases total (8 per method), or use `[Theory]` with `[InlineData]` to keep the file readable.

3. **Helper placement — clarify class layout.**
   Place `ApplySorting` after `GetJournalIndicatorsAsync` (i.e., at the bottom of the class). This keeps the public method block intact and matches the convention used in `IssuedInvoiceRepository.cs:211`.

## Prerequisites

None. The change is self-contained:

- No database migrations.
- No new NuGet packages.
- No DI registration changes.
- No configuration or Key Vault entries.
- No OpenAPI / TypeScript client regeneration (interface unchanged).
- No frontend changes.

Implementation can begin immediately. Validation gate per CLAUDE.md: `dotnet build` + `dotnet format` + `dotnet test` on `Anela.Heblo.Tests` (specifically the Journal test class).
```