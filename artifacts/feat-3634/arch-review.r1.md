# Architecture Review: Eliminate N+1 Queries in RecurringJobConfiguration Seeding

## Skip Design: true

Backend-only performance fix. No UI components, screens, layouts, or visual decisions. There is no user-facing surface — the method runs once at application startup within a DI scope.

## Architectural Fit Assessment

The change fits the existing architecture cleanly and requires no structural work.

- **Scope is a single method body.** Only `RecurringJobConfigurationRepository.SeedDefaultConfigurationsAsync` (lines 40–62) changes. The interface `IRecurringJobConfigurationRepository`, the call site in `ServiceCollectionExtensions.SeedRecurringJobConfigurationsAsync`, the entity, and the DbContext are all untouched.
- **Repository pattern is respected.** The repository already owns direct `ApplicationDbContext` access (ADR-001, single shared context; ADR-002, generic repository extended per feature). Replacing a per-item `FirstOrDefaultAsync` loop with a single projection query stays entirely within the repository's existing responsibility — no leakage into the Application or API layers.
- **DI bindings unaffected.** ADR-004 (repository DI bindings live in the owning module) is not touched; no registration changes.
- **Tests already pin the contract.** `RecurringJobConfigurationRepositoryTests` asserts the two behaviors that must be preserved (9 rows from empty; 9 rows with no duplicate when one pre-exists). These are the acceptance gate and must pass unmodified.

The only real decision is a library-version constraint, addressed below.

## Proposed Architecture

### Component Overview

```
Application startup
        │
        ▼
ServiceCollectionExtensions.SeedRecurringJobConfigurationsAsync   (unchanged call site)
        │  resolves all IRecurringJob from DI, passes to ↓
        ▼
RecurringJobConfigurationRepository.SeedDefaultConfigurationsAsync   (← the only change)
        │
        ├── (1) project default configs from job metadata      [in-memory, unchanged]
        ├── (2) ONE read: SELECT JobName FROM RecurringJobConfigurations → HashSet<string>
        ├── (3) foreach missing config: AddAsync + add name to the set  [FR-4 in-set guard]
        └── (4) ONE SaveChangesAsync
        │
        ▼
ApplicationDbContext → RecurringJobConfigurations table
```

Read round-trips drop from N to 1; the single write is preserved. Total round-trips: N+1 → 2.

### Key Design Decisions

#### Decision 1: Materialize existing names with `ToListAsync` + `new HashSet<string>`, NOT `ToHashSetAsync`

**Options considered:**
- (A) `ToHashSetAsync(cancellationToken)` as written in the spec's proposed code.
- (B) `.Select(c => c.JobName).ToListAsync(cancellationToken)` then `new HashSet<string>(list)`.

**Chosen approach:** Option B.

**Rationale:** This is the concern the spec explicitly flagged for verification, and it is real. The project references `Microsoft.EntityFrameworkCore` **8.0.8** (`backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj`). The `ToHashSetAsync` extension method was **not added until EF Core 9.0** — it does not exist in EF Core 8.x. Writing the spec's literal snippet would fail to compile (`dotnet build`). Implementers MUST use the `ToListAsync` + `HashSet` fallback. Do not attempt to upgrade EF Core to obtain `ToHashSetAsync`; a package bump for one convenience method is unjustified risk far outside this fix's scope.

Concretely:

```csharp
var existingNames = new HashSet<string>(
    await _context.RecurringJobConfigurations
        .Select(c => c.JobName)
        .ToListAsync(cancellationToken));
```

#### Decision 2: Project `JobName` only, not full entities

**Options considered:** Load full `RecurringJobConfiguration` entities vs. project just `JobName`.

**Chosen approach:** Project `JobName` (`Select(c => c.JobName)`).

**Rationale:** Only the business key is needed to decide insert-or-skip. Projecting a single column avoids materializing/tracking entities the method never mutates, and keeps the read cheap. Matches FR-1's acceptance criterion.

#### Decision 3: Guard in-set duplicates by mutating the same `HashSet` (FR-4)

**Options considered:** Rely only on the DB-loaded set vs. also add each newly-queued name back into the set.

**Chosen approach:** After each `AddAsync`, add the name to `existingNames`.

**Rationale:** Zero-cost defense against a discovered-job collection containing two jobs with the same `JobName` not yet in the DB — prevents queuing two rows with the same key in one `SaveChangesAsync`. Duplicate job names are not expected in practice (jobs are discovered from distinct types), but the guard makes the method robust and satisfies FR-4 without extra data structures.

## Implementation Guidance

### Directory / Module Structure

No new files, no new types, no moved code.

- **Modify only:** `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs` — the body of `SeedDefaultConfigurationsAsync` (lines 52–61). The `Microsoft.EntityFrameworkCore` using directive is already present (line 2); no new imports needed.
- **Do not modify:** the interface, the call site, the entity, the DbContext, or the test file.

### Interfaces and Contracts

Unchanged (FR-5). Public signature stays:

```csharp
Task SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default);
```

`cancellationToken` must be threaded to both the read (`ToListAsync`) and the write (`SaveChangesAsync`).

### Data Flow

For the two use cases the tests pin:

1. **Empty table, 9 jobs:** read returns empty set → all 9 filtered configs are `AddAsync`'d (each name added to the set as it goes) → one `SaveChangesAsync` → 9 rows.
2. **Table already has `purchase-price-recalculation`, 9 jobs:** read returns `{ "purchase-price-recalculation" }` → 8 missing configs added → one `SaveChangesAsync` → 9 rows total, single `purchase-price-recalculation` row.

Reference target shape (compile-safe for EF Core 8):

```csharp
var defaultConfigurations = jobs.Select(job => new RecurringJobConfiguration(
    job.Metadata.JobName,
    job.Metadata.DisplayName,
    job.Metadata.Description,
    job.Metadata.CronExpression,
    job.Metadata.DefaultIsEnabled,
    "System"
)).ToArray();

var existingNames = new HashSet<string>(
    await _context.RecurringJobConfigurations
        .Select(c => c.JobName)
        .ToListAsync(cancellationToken));

foreach (var config in defaultConfigurations.Where(c => !existingNames.Contains(c.JobName)))
{
    await _context.RecurringJobConfigurations.AddAsync(config, cancellationToken);
    existingNames.Add(config.JobName);
}

await _context.SaveChangesAsync(cancellationToken);
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `ToHashSetAsync` used verbatim from spec → build failure on EF Core 8.0.8 | Medium | Use `ToListAsync` + `new HashSet<string>(...)` (Decision 1). This is the single thing that can go wrong; call it out to implementers. |
| Behavior regression (duplicate insert or missing insert) | Medium | Existing tests `SeedDefaultConfigurationsAsync_WhenEmpty_*` and `_WhenConfigurationsExist_DoesNotDuplicate` must pass unmodified; they are the acceptance gate. |
| In-memory provider case-sensitivity of `HashSet` vs. DB collation differs | Low | Tests run on the EF InMemory provider; job names are lowercase-hyphenated constants, so ordinal `HashSet` comparison matches both providers. No `StringComparer` change needed; do not add one (out of scope, would alter semantics). |
| Concurrent multi-instance startup duplicate-key race | Low | Pre-existing under the old code too; explicitly out of scope (NFR-3). Do not add locking/upsert here. |

## Specification Amendments

1. **Amend the API/Interface Design snippet (spec lines 89–91 and the note at line 103).** The proposed code uses `ToHashSetAsync`, which does not exist in EF Core 8.0.8 (this project's version). Replace with the `ToListAsync` + `new HashSet<string>(...)` form shown in Decision 1 / Data Flow. The spec's note already anticipated this ("if not, use `.ToListAsync` ... `new HashSet<string>`"); this review upgrades that conditional to a firm requirement — the fallback is mandatory, not conditional.
2. **No other amendments.** FR-1 through FR-5, NFRs, Data Model, Out of Scope, and the "no schema change" stance are all sound and require no revision.

## Prerequisites

None. No migrations, no config, no infrastructure, no new packages. The change is self-contained in one method body and validated by the existing test suite plus `dotnet build` and `dotnet format`.
