# Architecture Review: Decouple OrgChart Adapter Deserialization from Application Contracts

## Skip Design: true

## Architectural Fit Assessment
This is a pure internal refactor of one Adapters-layer class (`OrgChartService`) with no change to `IOrgChartService`, no change to any Application contract, and no observable behavior change. It aligns cleanly with the project's Clean Architecture layering: the Adapters layer (`backend/src/Adapters/*`) exists specifically to isolate external I/O formats from the Application layer's contracts, and `Anela.Heblo.Application.Features.OrgChart.Contracts` types (`OrgChartResponse : BaseResponse`, `OrganizationDto`, `PositionDto`, `EmployeeDto`) are Application-owned API DTOs, not I/O models.

The precedent for this exact separation already exists in the codebase: `Anela.Heblo.Adapters.GoogleAds` defines `RawAccountBudget` (a `sealed record`, adapter-local, no Application-layer base type) as the shape returned by the external GAQL query, and `GoogleAdsTransactionSource` explicitly maps each `RawAccountBudget` field into the Application-owned `MarketingTransaction` before returning it. `OrgChartService` is the outlier — it is the only adapter in `backend/src/Adapters/` observed to deserialize directly into an Application response DTO that inherits `BaseResponse`. This spec brings `OrgChartService` in line with the `GoogleAds` precedent, no new pattern is being invented.

No architecture doc explicitly states "adapters must not deserialize into Application response types," but `docs/architecture/development_guidelines.md`'s Contracts/DTO rules ("Communication between modules exclusively through contracts") and the `GoogleAds` precedent make the intended convention unambiguous.

## Proposed Architecture

### Component Overview
```
┌─────────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Adapters.OrgChart  (Adapters layer, outer ring)          │
│                                                                       │
│  HTTP GET (OrgChartOptions.DataSourceUrl)                            │
│         │                                                            │
│         ▼                                                            │
│  JsonSerializer.Deserialize<OrgChartJsonModel>   ◄── NEW: adapter-   │
│         │                                            local I/O model │
│         ▼                                                            │
│  [null check] → InvalidOperationException                            │
│         │                                                            │
│         ▼                                                            │
│  explicit mapping (new private method)           ◄── NEW: adapter-   │
│         │                                            owned mapping   │
│         ▼                                                            │
│  OrgChartResponse { Organization = OrganizationDto {...} }            │
└─────────────────────────┬─────────────────────────────────────────────┘
                           │ (unchanged: IOrgChartService.GetOrganizationStructureAsync)
                           ▼
┌─────────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Application.Features.OrgChart  (Application layer)      │
│  GetOrganizationStructureHandler → OrgChartResponse (Contracts/)     │
└─────────────────────────────────────────────────────────────────────┘
```
Nothing crosses the Adapters→Application boundary except the final, already-existing `OrgChartResponse`. The new JSON model types and the mapping code are entirely contained in `Anela.Heblo.Adapters.OrgChart` and never referenced outside it.

### Key Design Decisions

#### Decision 1: Where the external-JSON model types live
**Options considered:**
1. Nested `private`/`internal` classes inside `OrgChartService.cs`.
2. A single new file `Models/OrgChartJsonModel.cs` in the Adapters project containing all four classes (as the finding/spec suggest).
3. One file per class under `Models/`.

**Chosen approach:** Option 2 — one file, `Models/OrgChartJsonModel.cs`, containing `OrgChartJsonModel`, `OrgChartJsonOrganization`, `OrgChartJsonPosition`, `OrgChartJsonEmployee` as `internal sealed class` types (`record` is fine too since these are pure I/O DTOs internal to the adapter, not the "DTOs are never records" Application-contract rule — see Amendment below).

**Rationale:** The four types are small, exist only to mirror one JSON document, and are never consumed outside this deserialization step — splitting them into four files adds navigation overhead with no benefit. `internal` visibility matches spec FR-1 exactly and needs no `InternalsVisibleTo`, since only `OrgChartServiceTests` exercises the public `IOrgChartService` surface (confirmed by reading the existing test file — it never touches the deserialization internals). This mirrors the single-file `RawAccountBudget.cs` precedent in `GoogleAds`, just with one file holding the whole (nested) shape instead of a flat record.

#### Decision 2: `record` vs `class` for the internal JSON models
**Options considered:**
1. Plain mutable `class` with `{ get; set; }` (matches spec's literal example and the existing `OrgChartResponse`/`*Dto` style).
2. `record`/`record class` with `{ get; set; }` or positional properties (matches the `GoogleAds` `RawAccountBudget` precedent).

**Chosen approach:** Plain `class` with settable properties, exactly as FR-1 specifies.

**Rationale:** `CLAUDE.md`'s "DTOs are classes, never C# records" rule is scoped to OpenAPI-serialized Application/API contracts (the generator mis-orders record constructor parameters); these adapter-internal JSON models are never surfaced through OpenAPI, so the rule's literal trigger doesn't apply, and `GoogleAds` already uses a `record` for the same kind of type. Even so, `System.Text.Json` deserializes mutable classes with `{ get; set; }` more predictably here (all properties are nullable/optional, no non-nullable constructor parameters to satisfy), and it matches the spec's own example and the existing Application-DTO style in this feature (`OrganizationDto`, `PositionDto`, `EmployeeDto` are all plain classes). Follow FR-1 literally: plain classes.

#### Decision 3: Where the mapping code lives
**Options considered:**
1. Inline in `GetOrganizationStructureAsync`, as one expression building nested object initializers (as sketched in the finding).
2. A small `private static` mapping method (or a few, one per level: `Organization` → `OrganizationDto`, `Position` → `PositionDto`, `Employee` → `EmployeeDto`) in `OrgChartService`.
3. A separate `OrgChartJsonMapper` static class.

**Chosen approach:** Option 2 — 3–4 small `private static` mapping methods inside `OrgChartService.cs` (e.g. `MapOrganization`, `MapPosition`, `MapEmployee`), composed via `.Select(...).ToList()` for the two list levels.

**Rationale:** The mapping is a pure, single-consumer transformation with no reuse elsewhere and no independent test requirement (FR-3's acceptance criteria only reference the four existing `OrgChartServiceTests`, no new mapper tests) — a separate class/file would be over-engineering for ~20 lines of field assignment. Small private static methods per level keep `GetOrganizationStructureAsync` readable (avoids one deeply-nested initializer) while staying in the file a maintainer already opens to understand this adapter. This is a style choice, not a boundary-relevant one; a developer preferring the single nested-initializer style from the finding may use it instead, as it satisfies FR-2/FR-3 equally — but keep it out of a new file/class per the reasoning above.

## Implementation Guidance

### Directory / Module Structure
```
backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/
├── Anela.Heblo.Adapters.OrgChart.csproj      (unchanged)
├── OrgChartAdapterServiceCollectionExtensions.cs (unchanged)
├── OrgChartService.cs                         (modified: deserialize target + mapping methods)
└── Models/
    └── OrgChartJsonModel.cs                   (NEW: 4 internal classes)
```
No new project references, no `.csproj` changes (confirmed: current `.csproj` only references `Anela.Heblo.Application` and two `Microsoft.Extensions.*` packages — nothing here requires more).

### Interfaces and Contracts
No public interface changes — `IOrgChartService.GetOrganizationStructureAsync(CancellationToken)` is untouched, per spec FR-2/Out-of-Scope.

New adapter-internal types (in `Models/OrgChartJsonModel.cs`, namespace `Anela.Heblo.Adapters.OrgChart.Models`):
```csharp
internal class OrgChartJsonModel
{
    public OrgChartJsonOrganization? Organization { get; set; }
}

internal class OrgChartJsonOrganization
{
    public string? Name { get; set; }
    public List<OrgChartJsonPosition>? Positions { get; set; }
}

internal class OrgChartJsonPosition
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public int? Level { get; set; }
    public string? ParentPositionId { get; set; }
    public string? Department { get; set; }
    public List<OrgChartJsonEmployee>? Employees { get; set; }
    public string? Url { get; set; }
}

internal class OrgChartJsonEmployee
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? StartDate { get; set; }
    public bool IsPrimary { get; set; }
    public string? Url { get; set; }
}
```
These types have zero dependency on `Anela.Heblo.Application.Shared.BaseResponse` or any Application contract — verified by design (they carry no `Success`/`ErrorCode`/`Params`), matching FR-1's acceptance criteria directly.

### Data Flow
1. `GetOrganizationStructureAsync` fetches the HTTP body exactly as today (unchanged lines 39–44 of `OrgChartService.cs`).
2. `JsonSerializer.Deserialize<OrgChartJsonModel>(content, JsonOptions)` replaces the current `Deserialize<OrgChartResponse>` call.
3. Null-check: `if (model == null) throw new InvalidOperationException("Failed to deserialize organizational structure");` — same message, same no-inner-exception shape as today, now checked against `OrgChartJsonModel` instead of `OrgChartResponse`.
4. Explicit mapping builds a new `OrgChartResponse` (default constructor → `Success = true`, `ErrorCode = null`, `Params = null`) with `Organization` populated field-by-field from the JSON model, applying the null-coalescing rules in FR-2.4 (`?? string.Empty` for strings, `?? new()` for lists, `Level` passed through as nullable `int?`).
5. The existing `LogInformation` call keeps reading from the final `OrgChartResponse` (`orgChart.Organization.Positions...`), not the intermediate `OrgChartJsonModel` — per FR-3, this must stay pointed at the mapped result so the log continues to reflect what's actually returned.
6. `catch (HttpRequestException)` and `catch (JsonException)` blocks are otherwise untouched; the `JsonException` catch now naturally covers a failure to deserialize into `OrgChartJsonModel` instead of `OrgChartResponse` — no code change needed there beyond the generic type argument on `Deserialize`.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Mapping introduces a subtle field-name mismatch (e.g. typo mapping `ParentPositionId`) that silently produces empty/wrong data with no compile error | Low | FR-2's acceptance criterion requires byte-for-byte equivalent output vs. today for the same payload; add/keep test coverage asserting the full object graph (name, positions, nested employees, in order) for a representative fixture payload — the four existing tests only cover error paths, not the happy-path field mapping, so this is a coverage gap worth closing during implementation even though the spec doesn't mandate a new test file. |
| Developer instinctively reaches for AutoMapper (listed as a "Recommended Tool" in `development_guidelines.md`) for this mapping, pulling a new dependency into a project that currently has none | Low | Do not add AutoMapper here — `Anela.Heblo.Adapters.OrgChart.csproj` has no AutoMapper reference today and the mapping is small/one-shot; hand-written mapping methods (Decision 3) are simpler, keep the adapter's dependency footprint unchanged, and match how `GoogleAdsTransactionSource` maps `RawAccountBudget` → `MarketingTransaction` (by hand, no AutoMapper). |
| `Level` (`int?` in both source and target) or `IsPrimary` (`bool`, non-nullable in both) accidentally gets wrapped in an extra `??`/cast that changes its nullability semantics | Low | Pass `Level` straight through (`Level = jsonPosition.Level`); do not add a `?? 0` default — the target `PositionDto.Level` is already `int?` with no default, so no coalescing is needed or wanted for this one field. |

## Specification Amendments
None required — the spec (`spec.r1.md`) is implementation-ready as written. Two clarifications for the developer, not changes to spec intent:
1. **Records vs. classes for the new JSON models**: use plain classes exactly as FR-1/the spec's own code sample shows (see Decision 2) — `CLAUDE.md`'s "DTOs are classes, never records" rule doesn't literally apply to these adapter-internal, non-OpenAPI types, but the spec's own example already uses classes, so there's no actual conflict to resolve.
2. **Test coverage gap**: FR-3's acceptance criteria only require the four existing (error-path) tests to keep passing unmodified. Recommend the implementer add one happy-path test asserting the full mapped `OrgChartResponse.Organization` graph against a small fixture JSON payload, to give FR-2's "identical output" acceptance criterion an actual automated check (currently it would only be verified by manual/visual code review). This is a recommendation, not a scope change — it doesn't conflict with "no new consumer of the adapter model" (FR-1) since the test would assert against `OrgChartResponse`, the existing public contract, not the new internal model.

## Prerequisites
None. No migrations, no config changes, no new infrastructure. This can start immediately: the target file (`OrgChartService.cs`), its test file (`OrgChartServiceTests.cs`), and the Application contracts it maps to are all already in place and unchanged by this work.
