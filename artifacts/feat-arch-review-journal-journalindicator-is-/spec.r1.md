# Specification: Remove duplicate `JournalIndicator` from Domain layer

## Summary
Eliminate the duplicated `JournalIndicator` type from the Domain layer in favor of the existing `JournalIndicatorDto` in Application, and drop the no-op `TotalEntries => DirectEntries` computed property from both. This is a pure refactor — no behavior change for end users or persisted data — that restores Clean Architecture layering by removing a read-model masquerading as a domain entity.

## Background
The Journal module currently maintains two near-identical types representing the same per-product journal summary read model:

- `Anela.Heblo.Domain.Features.Journal.JournalIndicator`
- `Anela.Heblo.Application.Features.Journal.Contracts.JournalIndicatorDto`

`JournalIndicator` has no identity, no lifecycle, no behavior, and enforces no invariants. It is a query projection produced by `IJournalRepository.GetJournalIndicatorsAsync` and consumed by Application-layer handlers. Per `docs/architecture/development_guidelines.md`, the Domain layer should contain entities, aggregates, value objects, and repository interfaces — not query projections. Read models belong in Application/Contracts.

Both types also expose `TotalEntries => DirectEntries`, a computed property that adds no value over reading `DirectEntries` directly. The duplication has already drifted (an identical `// Within last 30 days` comment appears in both files), and any future field addition would require synchronizing both definitions.

Removing the Domain type and the no-op property reduces drift risk, clarifies the domain model, and aligns the codebase with its stated architectural guidelines.

## Functional Requirements

### FR-1: Remove `JournalIndicator` from the Domain layer
Delete `backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicator.cs`. No other Domain code should retain a reference to this type after the change.

**Acceptance criteria:**
- `JournalIndicator.cs` no longer exists under `Anela.Heblo.Domain`.
- `grep -r "JournalIndicator\b" backend/src/Anela.Heblo.Domain` returns no matches (the DTO lives in Application, not Domain).
- Solution builds cleanly (`dotnet build`).

### FR-2: Update `IJournalRepository.GetJournalIndicatorsAsync` return type
Change the repository interface so it no longer references the deleted Domain `JournalIndicator`. The repository contract must continue to express "per-product journal counts and last-entry metadata" without leaking Application-layer DTOs into the Domain interface.

**Chosen approach (primary):** Introduce a minimal Domain primitive — a `record struct` — that captures only what the repository inherently knows (raw counts and dates), and have the Application handler map this primitive into `JournalIndicatorDto`. This preserves the Domain → Application dependency direction.

Proposed Domain type:
```csharp
namespace Anela.Heblo.Domain.Features.Journal;

public readonly record struct JournalIndicatorSnapshot(
    int DirectEntries,
    DateTime? LastEntryDate,
    bool HasRecentEntries);
```

Updated signature:
```csharp
Task<Dictionary<string, JournalIndicatorSnapshot>> GetJournalIndicatorsAsync(
    IEnumerable<string> productCodes,
    CancellationToken cancellationToken = default);
```

The existing repository implementation is updated to populate `JournalIndicatorSnapshot` instead of `JournalIndicator`. Application-layer code (handlers/services that previously received `Dictionary<string, JournalIndicator>`) now receives `Dictionary<string, JournalIndicatorSnapshot>` and maps each entry into `JournalIndicatorDto` (copying `ProductCode` from the dictionary key).

**Acceptance criteria:**
- `IJournalRepository.GetJournalIndicatorsAsync` returns `Dictionary<string, JournalIndicatorSnapshot>` (or equivalent Domain-only type).
- No Application-layer type (`JournalIndicatorDto`, etc.) appears in any signature inside `Anela.Heblo.Domain`.
- All existing call sites compile and produce the same `JournalIndicatorDto` values they did before (modulo the removed `TotalEntries` property — see FR-3).
- Repository implementation in the Infrastructure/Persistence layer is updated accordingly.

### FR-3: Remove `TotalEntries` no-op property from `JournalIndicatorDto`
Delete the `public int TotalEntries => DirectEntries;` line from `JournalIndicatorDto`. Update every consumer (backend handlers, frontend TypeScript via regenerated OpenAPI client, any tests, any UI references) to read `DirectEntries` directly.

**Acceptance criteria:**
- `JournalIndicatorDto` no longer defines `TotalEntries`.
- `grep -r "TotalEntries" backend/` and `grep -r "totalEntries" frontend/src` return no matches outside auto-generated artifacts that will be regenerated.
- Regenerated TypeScript OpenAPI client no longer exposes `totalEntries` on the journal indicator type.
- Frontend builds (`npm run build`) and lints (`npm run lint`) cleanly after consumers are updated.
- Any UI that displayed `totalEntries` now uses `directEntries` and renders the same numeric value.

### FR-4: Update tests
All affected backend unit/integration tests and any frontend tests that reference `JournalIndicator`, `JournalIndicatorDto.TotalEntries`, or `totalEntries` must be updated to use the new shapes. Tests must continue to assert the same observable behavior (counts, dates, recent-entries flag) post-refactor.

**Acceptance criteria:**
- All tests touched by the change pass.
- No test continues to reference the deleted `JournalIndicator` Domain type or the deleted `TotalEntries`/`totalEntries` property.
- Test coverage of the affected handlers and repository methods is not reduced.

## Non-Functional Requirements

### NFR-1: Performance
No performance regression. The change is purely a type-level rename/relocation with a small in-memory mapping step in the Application layer (dictionary projection). The mapping is O(n) over the same dictionary already returned today.

### NFR-2: Security
No security impact. No authentication, authorization, data-sensitivity, or input-validation surface changes. No new external inputs.

### NFR-3: Maintainability
- The Domain layer must contain no read-model/query-projection types after this change in the Journal module.
- A single source of truth (`JournalIndicatorDto`) for the journal indicator shape exposed beyond the repository boundary.
- The change must not introduce new duplication or new no-op computed properties.

### NFR-4: Backward compatibility
- This refactor changes the public API response shape by removing `totalEntries`. Because the field's value was always identical to `directEntries`, frontend consumers can migrate to `directEntries` with no semantic change. There is no API-versioning requirement for this project (single deployed app, solo developer); the change is applied uniformly to backend, generated client, and frontend in a single PR.

## Data Model
No database schema, persisted entity, or migration changes. The refactor operates entirely on in-memory query-projection types.

Entity relationships (unchanged):
- Journal entries are persisted per product.
- The repository aggregates them into a per-product summary (count, last date, recent-flag) when requested by Application handlers.

Type relationships (after refactor):
- Domain: `JournalIndicatorSnapshot` (record struct) — fields: `DirectEntries`, `LastEntryDate`, `HasRecentEntries`.
- Application: `JournalIndicatorDto` (class) — fields: `ProductCode`, `DirectEntries`, `LastEntryDate`, `HasRecentEntries`. (No `TotalEntries`.)
- Mapping: Application handler converts `Dictionary<string, JournalIndicatorSnapshot>` → `IEnumerable<JournalIndicatorDto>` or `Dictionary<string, JournalIndicatorDto>` as needed, copying the dictionary key into `ProductCode`.

## API / Interface Design

### Repository interface (Domain)
Before:
```csharp
Task<Dictionary<string, JournalIndicator>> GetJournalIndicatorsAsync(
    IEnumerable<string> productCodes, CancellationToken ct = default);
```
After:
```csharp
Task<Dictionary<string, JournalIndicatorSnapshot>> GetJournalIndicatorsAsync(
    IEnumerable<string> productCodes, CancellationToken ct = default);
```

### Application DTO
Before:
```csharp
public class JournalIndicatorDto
{
    public string ProductCode { get; set; } = null!;
    public int DirectEntries { get; set; }
    public int TotalEntries => DirectEntries;
    public DateTime? LastEntryDate { get; set; }
    public bool HasRecentEntries { get; set; }   // Within last 30 days
}
```
After:
```csharp
public class JournalIndicatorDto
{
    public string ProductCode { get; set; } = null!;
    public int DirectEntries { get; set; }
    public DateTime? LastEntryDate { get; set; }
    public bool HasRecentEntries { get; set; }   // Within last 30 days
}
```

### HTTP API
- All endpoints that return `JournalIndicatorDto` (directly or as a nested field on another response DTO) lose the `totalEntries` property in their JSON response shape. No URL, method, or other field changes.
- OpenAPI document and generated TypeScript client are regenerated as part of the build (per `docs/development/api-client-generation.md`).

### Frontend
- Every TypeScript reference to `.totalEntries` on a journal indicator is replaced with `.directEntries`.
- No new UI; no layout changes; no copy changes.

## Dependencies
- Internal: `IJournalRepository` and its concrete implementation; Application handlers in `Anela.Heblo.Application/Features/Journal`; any controller exposing journal indicators; frontend `frontend/src` consumers of journal indicator data; OpenAPI codegen pipeline (auto-run on backend build).
- External: None. No NuGet package, npm package, environment variable, secret, or Azure resource changes.

## Out of Scope
- Renaming, restructuring, or moving any other types in the Journal module.
- Splitting or relocating `IJournalRepository` itself (e.g., moving it from Domain to Application).
- Any feature changes to journal entry CRUD, calculations, or business rules.
- Performance optimization of `GetJournalIndicatorsAsync`.
- Adding new fields, filters, or query parameters to the indicator API.
- Database schema, migrations, or persistence-layer refactors.
- Sweep of other potential Domain-layer read models outside the Journal module (a separate arch-review item, not this refactor).
- Adding deprecation aliases or back-compat shims for `TotalEntries`/`totalEntries` — they are removed outright.

## Open Questions
None.

## Status: COMPLETE