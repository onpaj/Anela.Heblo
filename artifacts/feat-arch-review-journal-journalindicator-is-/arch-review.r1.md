# Architecture Review: Remove duplicate `JournalIndicator` from Domain layer

## Skip Design: true

Backend-only refactor. No UI components, screens, layouts, or visual design decisions are introduced or changed. The frontend `src/` tree contains zero references to `JournalIndicator`, `journalIndicator`, `totalEntries`, `directEntries`, or `hasRecentEntries` (verified by grep), so there is also no UX surface to update.

## Architectural Fit Assessment

The refactor aligns with the project's stated layering rules in `docs/architecture/development_guidelines.md`:

- **Read models / query projections belong in `Application/Features/<Module>/Contracts/`**, not in `Domain`.
- **Domain layer owns entities, aggregates, value objects, repository interfaces** — nothing else.
- **DTOs are module-owned, never shared, never in API/Xcc** — `JournalIndicatorDto` is correctly located.

Today the Journal module violates rule #1: `JournalIndicator` is a property bag with no identity, no invariants, no behavior, no lifecycle — it is a query projection masquerading as a domain entity, sitting next to genuine domain types (`JournalEntry`, `JournalEntryProduct`, `JournalEntryTag`).

**Critical finding from active code exploration**: `JournalIndicatorDto` has **zero consumers** in the entire codebase. There is no handler, controller, MediatR request/response, mapping, or frontend reference. `JournalIndicator` (Domain) is consumed only by:
- `IJournalRepository.GetJournalIndicatorsAsync` signature (Domain)
- `JournalRepository.GetJournalIndicatorsAsync` implementation (Persistence)
- Three integration tests in `JournalRepositoryIntegrationTests.cs` (lines 196–297)

No HTTP API today returns a journal indicator. The OpenAPI document does not contain `JournalIndicatorDto`. The TypeScript client will not change on regeneration. This invalidates several acceptance criteria in the spec (see **Specification Amendments**) and changes the risk profile of the change.

**Main integration points**:
1. Domain → Persistence: the repository interface and its implementation.
2. Domain → Tests: three integration tests directly use the Domain type.
3. Application → (no one): the DTO exists as a dead contract.

The refactor's main integration concern is therefore much narrower than the spec implies: it is a four-file change touching one Domain type, one Domain interface, one Persistence implementation, and one test file. There is no live API surface to migrate and no frontend to update.

## Proposed Architecture

### Component Overview

```
Before:
  Anela.Heblo.Domain.Features.Journal
    ├── JournalEntry, JournalEntryProduct, JournalEntryTag … (real domain)
    ├── IJournalRepository.GetJournalIndicatorsAsync → Dictionary<string, JournalIndicator>
    └── JournalIndicator  ← read-model masquerading as domain type, with no-op TotalEntries

  Anela.Heblo.Application.Features.Journal.Contracts
    └── JournalIndicatorDto  ← unreferenced anywhere; mirrors Domain type byte-for-byte

After (spec's chosen approach, primitive in Domain):
  Anela.Heblo.Domain.Features.Journal
    ├── JournalEntry, JournalEntryProduct, JournalEntryTag … (unchanged)
    ├── IJournalRepository.GetJournalIndicatorsAsync → Dictionary<string, JournalIndicatorSnapshot>
    └── JournalIndicatorSnapshot  ← readonly record struct: DirectEntries, LastEntryDate, HasRecentEntries
                                    (no ProductCode — that's the dictionary key)

  Anela.Heblo.Application.Features.Journal.Contracts
    └── JournalIndicatorDto  ← TotalEntries removed; remains the future Application-layer
                               read-model shape if/when a consumer is introduced

  Persistence.Catalog.Journal.JournalRepository
    └── Constructs JournalIndicatorSnapshot directly; no per-entry ProductCode field.
        Aggregation logic and recent-entries calculation unchanged.
```

### Key Design Decisions

#### Decision 1: How to keep `IJournalRepository.GetJournalIndicatorsAsync` purely Domain-typed

**Options considered:**
- **(A) Introduce a minimal Domain primitive** (`JournalIndicatorSnapshot` record struct) and map to `JournalIndicatorDto` in Application. *(spec's chosen approach)*
- **(B) Return `Dictionary<string, JournalIndicatorDto>`** directly from the repository interface — violates Domain → Application dependency direction. The brief lists it as a fallback only "if the repo interface must remain purely domain-typed."
- **(C) Relocate the indicator query out of `IJournalRepository`** into a dedicated Application-layer read-model query (e.g., `IJournalIndicatorQueryService`). The repository would then own only `JournalEntry` CRUD, and the projection would live alongside its DTO.
- **(D) Delete the indicator surface entirely** — it currently has no live consumer outside tests. (Listed in the prior plan `2026-05-12-remove-journal-family-entries.md` §Alternatives.)

**Chosen approach: (A) — `JournalIndicatorSnapshot` record struct in Domain.**

**Rationale:**
- Smallest, lowest-risk change that fixes the stated smell (Domain owns a query projection masquerading as a domain entity).
- Preserves the dependency direction: Domain references nothing from Application.
- Aligns with `csharp-coding-style.md`: prefer `record struct` for immutable, value-like models.
- (C) is architecturally cleaner but is explicitly **Out of Scope** in the spec ("Splitting or relocating `IJournalRepository` itself"). Flagged as a follow-up.
- (D) is the most YAGNI-pure option, but the spec also rules it out implicitly by treating `JournalIndicatorDto` as a kept type. Flagged as a follow-up — see **Specification Amendments**.
- (B) is rejected by the spec's own NFR (Domain must not reference Application).

#### Decision 2: Shape of `JournalIndicatorSnapshot`

**Chosen:** `readonly record struct JournalIndicatorSnapshot(int DirectEntries, DateTime? LastEntryDate, bool HasRecentEntries);` — **no `ProductCode` field**.

**Rationale:**
- `ProductCode` is already the dictionary key in `Dictionary<string, JournalIndicatorSnapshot>`. Storing it again on each value duplicates state and creates a desynchronization risk (key vs. value drift).
- This is a clear improvement over the current `JournalIndicator`, which carries `ProductCode` redundantly with its dictionary key in `JournalRepository.GetJournalIndicatorsAsync`.
- Application-layer mapping (when a future consumer adds one) takes the `ProductCode` from the dictionary key.

`readonly record struct` (not `record class`) is chosen because the type is small (≤16 bytes payload), value semantics fit a read-only projection, and it avoids heap allocations.

#### Decision 3: What to do with the dead `JournalIndicatorDto`

**Chosen (per spec):** keep `JournalIndicatorDto` and remove only its `TotalEntries` no-op.

**Recommendation (amend the spec):** **delete `JournalIndicatorDto` outright**. It has zero references in code today. Keeping a "future single source of truth" violates the same YAGNI principle the spec invokes to delete `TotalEntries`. See **Specification Amendments §1**.

## Implementation Guidance

### Directory / Module Structure

| File | Action |
|------|--------|
| `backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicator.cs` | **Delete** |
| `backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicatorSnapshot.cs` | **Create** — record struct, three fields, no `ProductCode` |
| `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs` | **Modify** — return type → `Dictionary<string, JournalIndicatorSnapshot>` |
| `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` | **Modify** — build snapshots instead of `JournalIndicator` instances |
| `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalIndicatorDto.cs` | **Modify** — remove `TotalEntries` (or **delete** if §1 amendment accepted) |
| `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs` | **Modify** — drop three `TotalEntries` assertions; switch to `JournalIndicatorSnapshot` |

No new folders. No `Module.cs` change. No DI registration change.

### Interfaces and Contracts

```csharp
// backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicatorSnapshot.cs
namespace Anela.Heblo.Domain.Features.Journal;

public readonly record struct JournalIndicatorSnapshot(
    int DirectEntries,
    DateTime? LastEntryDate,
    bool HasRecentEntries);
```

```csharp
// IJournalRepository.cs — signature only
Task<Dictionary<string, JournalIndicatorSnapshot>> GetJournalIndicatorsAsync(
    IEnumerable<string> productCodes,
    CancellationToken cancellationToken = default);
```

`JournalIndicatorDto` remains a `class` (not a `record`) per the project's mandatory rule: **DTOs are classes, never C# records** (OpenAPI generator constraint). The Domain `JournalIndicatorSnapshot` is a `record struct` because it never crosses the OpenAPI boundary — it lives entirely server-side.

### Data Flow

```
GetJournalIndicatorsAsync(productCodes, ct)
    ↓
JournalRepository (Persistence):
    1. Initialize accumulators for each product code (default: 0, null, false)
    2. EF query: GROUP BY product code → (Count, Max(EntryDate))
    3. Compute HasRecentEntries: LastEntryDate >= Today - 30 days
    4. Materialize: Dictionary<string, JournalIndicatorSnapshot>
       (snapshots constructed at the end, not mutated — record struct is immutable)
    ↓
Returned to caller (today: only tests; future: any Application handler may map to DTO)
```

The current implementation **mutates** `JournalIndicator` instances after construction. With a `readonly record struct`, the implementation must construct each snapshot as a single expression — a small but real rewrite of the loop bodies. See **Risks §1**.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Persistence currently mutates `JournalIndicator` after construction. Switching to `readonly record struct` forces a build-once pattern (accumulator dictionary projected to snapshots at the end). | MEDIUM | Aggregation is ~30 lines. Land with the existing three integration tests, which pin observable behavior regardless of internal mutability. |
| Spec assumes an Application-layer mapping handler exists. It does not — the only caller of `GetJournalIndicatorsAsync` is the test suite. | HIGH | Amend spec (§1). Either accept that no production mapping exists (the only consumer to migrate is tests), or delete `JournalIndicatorDto`. Do **not** invent a handler that has no caller. |
| Spec claims the HTTP API response shape changes. No controller exposes a journal indicator today; no API shape changes. | LOW | Drop FR-3 acceptance criteria about regenerated TS client and frontend updates (see §2). Vacuously true. |
| Three integration tests assert `indicator.TotalEntries.Should().Be(…)`. They break on both type change and removed property. | LOW | Replace the three assertions with equivalents on `DirectEntries`. Update type references to `JournalIndicatorSnapshot`. |
| `JournalIndicatorSnapshot` is a Domain "value object lookalike" — a future maintainer might add behavior and unintentionally re-create the problem this refactor exists to fix. | LOW | Keep as `readonly record struct` with three primitive fields. One-line XML comment clarifying it is a repository-level projection, not a domain concept. |
| Renaming a public Domain symbol could conflict with in-progress branches. | LOW | Solo developer + AI-assisted review. Spec is COMPLETE; brief filed by daily arch-review routine. Verify branch list before merge. |

## Specification Amendments

### §1: `JournalIndicatorDto` should be deleted, not preserved

`JournalIndicatorDto` has zero references in the codebase (verified by grep — only its own declaration matches in source, with other matches in brief/spec/prior-plan markdown). FR-3 currently keeps it for "single source of truth"; NFR-3 invokes the same goal. Both are moot — there is no consumer beyond the repository boundary.

**Recommended amendment**: replace FR-3 with **"Delete `JournalIndicatorDto.cs` entirely."** Applying YAGNI consistently — the spec deletes `JournalIndicator` for being a dead/duplicate type while preserving `JournalIndicatorDto`, which has no consumers at all. That is inconsistent. Reintroduce the DTO when a real consumer arrives.

**Decision required from spec author** before implementation. Either choice is implementable.

### §2: FR-3 frontend / OpenAPI acceptance criteria are vacuous

Because no controller returns `JournalIndicatorDto`, the OpenAPI document and TypeScript client do not contain it. These acceptance criteria from FR-3 are true by absence and cannot be meaningfully verified:

- "Regenerated TypeScript OpenAPI client no longer exposes `totalEntries`."
- "Any UI that displayed `totalEntries` now uses `directEntries`."
- "`grep -r "totalEntries" frontend/src` returns no matches…"

**Recommended amendment**: replace with a single negative check — "`grep -ri 'totalEntries\|TotalEntries' frontend/ backend/src` returns zero matches in source code."

### §3: `JournalIndicatorSnapshot` should not carry `ProductCode`

Confirming the spec's example is correct — `ProductCode` is the dictionary key and must not be duplicated on the value type. Calling out so the implementer doesn't accidentally re-add it when porting fields from `JournalIndicator`.

### §4: Out-of-scope items are correctly scoped, but flag one follow-up

The spec correctly defers (a) moving `IJournalRepository` itself out of Domain and (b) a broader sweep of Domain read-model violations.

**Suggested follow-up arch-review item** (file separately, not in this PR): `GetJournalIndicatorsAsync` doesn't belong on `IJournalRepository`. The interface inherits `IRepository<JournalEntry, int>` and otherwise contains CRUD/query on `JournalEntry`. The indicator query is a per-product aggregate read model returning no `JournalEntry`. A future refactor should relocate it to an Application-layer query interface (e.g., `IJournalIndicatorQueryService`), eliminating the need for a Domain primitive entirely because that interface could legally return `JournalIndicatorDto` directly.

## Prerequisites

None. Pure refactor:

- No database migration.
- No configuration / appsettings / Key Vault / feature flag change.
- No DI registration change.
- No NuGet / npm package change.
- No OpenAPI / TS client breaking change (DTO is not exposed).
- No infrastructure / Azure resource change.

Single PR, single commit if desired. Local validation: `dotnet build`, `dotnet format`, `dotnet test --filter "FullyQualifiedName~JournalRepositoryIntegrationTests"`, `dotnet test`, `npm run build` + `npm run lint` (expected: no diff to generated client).