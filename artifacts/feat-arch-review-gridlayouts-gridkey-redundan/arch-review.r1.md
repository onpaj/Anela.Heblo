# Architecture Review: Remove Redundant GridKey from GridLayouts JSON Payload

## Skip Design: true

## Architectural Fit Assessment

The change is a surgical, internal refactor within a single vertical slice (`Features/GridLayouts/`) and fits cleanly into the existing architecture:

- **No layer boundary changes.** Domain (`GridLayout` entity, `IGridLayoutRepository`), Persistence (`GridLayoutRepository.UpsertAsync` takes `userId`, `gridKey`, `layoutJson` as separate parameters), and the API contract (`GridLayoutDto`) all remain intact.
- **No new module wiring.** No DI registration, no new repository methods, no new validators.
- **Repository contract already supports the shape.** `UpsertAsync(string userId, string gridKey, string layoutJson, ...)` keeps `GridKey` outside the JSON blob — the column has always been the authoritative source. The defect is purely in the application-layer serialization choice.
- **Read-path resilience is already covered.** `GetGridLayoutHandler` catches `JsonException` and returns `null`. `System.Text.Json` ignores unknown properties by default, which gives backward compatibility for free (legacy rows containing `gridKey`/`lastModified` keys deserialize cleanly into a slim record).
- **One subtle redundancy beyond the spec.** `GridLayoutDto.LastModified` is *also* overwritten on read (`GetGridLayoutHandler.cs:57`) and is serialized as dead `"lastModified":null` by the current Save handler. This is the same anti-pattern as `gridKey` — see *Specification Amendments*.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────┐
│  Anela.Heblo.Application/Features/GridLayouts/           │
│                                                          │
│  ┌────────────────────────────────────────────────────┐  │
│  │ Contracts/ (unchanged, API surface)                │  │
│  │   GridLayoutDto      { GridKey, Columns, ... }     │  │
│  │   GridColumnStateDto { Id, Order, Width, Hidden }  │  │
│  └────────────────────────────────────────────────────┘  │
│                                                          │
│  ┌────────────────────────────────────────────────────┐  │
│  │ GridLayoutPersistencePayload.cs  (NEW, internal)   │  │
│  │   internal sealed record with Columns only         │  │
│  │   — used by both handlers, never escapes the slice │  │
│  └─────────────────┬────────────────┬─────────────────┘  │
│                    │ Serialize      │ Deserialize        │
│                    ▼                ▼                    │
│  ┌──────────────────────┐  ┌──────────────────────────┐  │
│  │ UseCases/            │  │ UseCases/                │  │
│  │   SaveGridLayout/    │  │   GetGridLayout/         │  │
│  │   Handler            │  │   Handler                │  │
│  │   • build payload    │  │   • deserialize slim     │  │
│  │   • serialize        │  │   • assemble GridLayoutDto│ │
│  │   • UpsertAsync(...) │  │     from columns +       │  │
│  │                      │  │     entity.GridKey/      │  │
│  │                      │  │     LastModified         │  │
│  └──────────┬───────────┘  └────────────┬─────────────┘  │
└─────────────┼───────────────────────────┼────────────────┘
              ▼                           ▼
       IGridLayoutRepository  (unchanged: UpsertAsync / GetAsync)
              │
              ▼
       GridLayouts table: { UserId, GridKey, LayoutJson, LastModified }
```

### Key Design Decisions

#### Decision 1: Home for the slim persistence type

**Options considered:**
- (A) Anonymous type at each call site (`new { columns = request.Columns }`).
- (B) Two private records, one per handler.
- (C) One `internal sealed record` at `Features/GridLayouts/GridLayoutPersistencePayload.cs`.
- (D) Place it inside `Contracts/`.

**Chosen approach:** (C).

**Rationale:** (A) violates FR-4 — save and get drift independently. (B) is the same drift risk with extra ceremony. (D) is wrong — `Contracts/` is reserved for API-facing DTOs (development_guidelines.md: "DTOs are never shared or global … API project never defines or owns DTOs … each module owns its contract interfaces and DTOs"). The persistence shape is an internal storage detail, not a contract. (C) gives one definition shared by both `UseCases/` siblings, marked `internal` so it cannot leak to the API surface or other modules, and sits next to `GridLayoutsModule.cs` at the feature root — the same level developers already look for module-wide types.

#### Decision 2: Preserve the `"columns"` JSON property name

**Options considered:**
- (A) Rely on `System.Text.Json` default PascalCase output.
- (B) Apply `[JsonPropertyName("columns")]` on the slim record's property.
- (C) Pass `JsonSerializerOptions` with `PropertyNamingPolicy = CamelCase`.

**Chosen approach:** (B).

**Rationale:** The existing `GridLayoutDto` uses explicit `[JsonPropertyName]` attributes and `JsonSerializer.Serialize(payload)` is called with no options. Legacy rows therefore contain lowercase `"columns"`. If the new slim type emits PascalCase `"Columns"`, **legacy rows will fail to deserialize the columns array** (NFR-3 / FR-3 breaks). (B) is local, explicit, requires no new wiring, and mirrors the convention already used in `Contracts/`. (C) introduces options ceremony for a single property.

#### Decision 3: Keep the explicit `entity.GridKey`/`entity.LastModified` assignments on read

**Options considered:**
- (A) Build the response DTO via a constructor/factory from `(GridLayout entity, GridLayoutPersistencePayload payload)`.
- (B) Keep the existing imperative `dto.GridKey = entity.GridKey; dto.LastModified = entity.LastModified;` post-deserialization.

**Chosen approach:** (B).

**Rationale:** FR-2 explicitly preserves line 56 as the sole source of `GridKey`. The minimum-diff path is to change *what* is deserialized into `dto` (a slim shape mapped to `GridLayoutDto.Columns`), not to re-architect the response assembly. A two-line `var dto = new GridLayoutDto { Columns = payload.Columns }; dto.GridKey = entity.GridKey; dto.LastModified = entity.LastModified;` keeps the change surgical and the test signal stable.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/GridLayouts/
├── GridLayoutsModule.cs                         (unchanged)
├── GridLayoutPersistencePayload.cs              (NEW)
├── Contracts/
│   ├── GridLayoutDto.cs                         (unchanged — API contract)
│   └── GridColumnStateDto.cs                    (unchanged)
└── UseCases/
    ├── SaveGridLayout/SaveGridLayoutHandler.cs  (MODIFIED — serializes slim payload)
    ├── GetGridLayout/GetGridLayoutHandler.cs    (MODIFIED — deserializes slim payload)
    └── ResetGridLayout/                         (unchanged)
```

No persistence-layer changes. No domain-layer changes. No controller changes. No migration.

### Interfaces and Contracts

**New internal type** (`Features/GridLayouts/GridLayoutPersistencePayload.cs`):

```csharp
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Features.GridLayouts.Contracts;

namespace Anela.Heblo.Application.Features.GridLayouts;

internal sealed record GridLayoutPersistencePayload(
    [property: JsonPropertyName("columns")] List<GridColumnStateDto> Columns);
```

Constraints:
- `internal` — must not be reachable from `Anela.Heblo.API` or other features.
- `sealed record` — value semantics, no inheritance, prevents accidental subtyping for "versioning."
- The `[JsonPropertyName("columns")]` is **load-bearing** for backward compatibility (see Decision 2).
- Property type reuses `GridColumnStateDto` so a future change to column shape is still single-sourced.

**Public contracts (unchanged):**
- `GridLayoutDto` (`Contracts/GridLayoutDto.cs`) — keep all three properties (`GridKey`, `Columns`, `LastModified`) and their `[JsonPropertyName]` attributes. The DTO continues to flow over MediatR and HTTP unchanged.
- `IGridLayoutRepository` — unchanged signature; `UpsertAsync(userId, gridKey, layoutJson, ...)` already carries `gridKey` out-of-band.

### Data Flow

**Save (write path):**

```
SaveGridLayoutRequest { GridKey, Columns }
       │
       ▼
SaveGridLayoutHandler.Handle
   1. resolve userId via ICurrentUserService          (unchanged)
   2. payload = new GridLayoutPersistencePayload(request.Columns)   ← was: new GridLayoutDto { GridKey, Columns }
   3. json = JsonSerializer.Serialize(payload)
   4. repository.UpsertAsync(userId, request.GridKey, json, ct)     (unchanged)
       │
       ▼
GridLayouts row: { UserId, GridKey (column), LayoutJson = "{\"columns\":[...]}", LastModified }
```

**Read (read path):**

```
GetGridLayoutRequest { GridKey }
       │
       ▼
GetGridLayoutHandler.Handle
   1. resolve userId                                                (unchanged)
   2. entity = repository.GetAsync(userId, request.GridKey, ct)     (unchanged)
   3. if (entity is null) return null layout                        (unchanged)
   4. payload = JsonSerializer.Deserialize<GridLayoutPersistencePayload>(entity.LayoutJson)
      • legacy rows: extra "gridKey"/"lastModified" keys are ignored by default
      • malformed/empty/literal-null: existing catch + null-path branches still apply
   5. dto = new GridLayoutDto { Columns = payload.Columns ?? new() }
   6. dto.GridKey       = entity.GridKey
      dto.LastModified  = entity.LastModified
   7. return GetGridLayoutResponse { Layout = dto }
```

**Null-safety nuance:** `GridLayoutPersistencePayload.Columns` is a non-nullable list with a `record` positional parameter; if the JSON is `{}` (no `columns` key) the deserializer returns a record with `Columns = null!`. Defensive `payload.Columns ?? new List<GridColumnStateDto>()` keeps the existing empty-layout semantics (legacy `Handle_WhenLayoutJsonIsEmpty_*` test contract). Implementers must verify whether the existing test for `"null"` literal JSON (`Handle_WhenLayoutJsonIsLiteralNull_ReturnsNullLayoutAndDoesNotLog`) still produces a null `payload` — the spec says the response is `null layout`, which the existing `if (dto is null) return null` guard already handles. Keep that guard.

### Test Plan (concrete)

Tighten the existing `SaveGridLayoutHandlerTests` and add the legacy-format read test required by FR-3:

1. **`SaveGridLayoutHandlerTests.Handle_CallsUpsertWithSerializedColumns`** (modify): assert `parsed.ContainsKey("columns") && !parsed.ContainsKey("gridKey") && !parsed.ContainsKey("lastModified")`. The current test only asserts the positive — it will pass against the buggy code and against the fix. The negative assertions are what enforce FR-1.
2. **`SaveGridLayoutHandlerTests`** (new): assert that two columns round-trip through `JsonSerializer.Deserialize<GridLayoutPersistencePayload>` with the captured JSON and produce identical `Id/Order/Width/Hidden` values — pins the on-disk shape and the slim record to each other.
3. **`GetGridLayoutHandlerTests.Handle_WhenSavedLayoutExists_ReturnsDeserializedDto`** (already uses the slim shape — unchanged).
4. **`GetGridLayoutHandlerTests`** (new, per FR-3): feed a legacy payload `{"gridKey":"old-key","columns":[{...}],"lastModified":"2025-01-01T..."}` and assert the response has `GridKey = entity.GridKey` (not `"old-key"`), `LastModified = entity.LastModified`, and the columns array recovered intact.
5. **`GridLayoutRepositoryTranslationTests`** (review-only): no changes; the repo contract did not change.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| New slim type emits PascalCase `"Columns"`, breaking deserialization of legacy rows that contain lowercase `"columns"` | High | Apply `[JsonPropertyName("columns")]` on the record property (Decision 2). Cover with the legacy-format read test (FR-3). |
| Existing `SaveGridLayoutHandlerTests` only asserts presence of `columns` and would pass against the buggy implementation | Medium | Tighten the assertion to also reject `gridKey` and `lastModified` keys (FR-1 acceptance criterion). |
| Slim record's positional `Columns` could deserialize as `null` for `{}` payloads, regressing the empty-layout contract | Medium | Guard with `payload.Columns ?? new List<GridColumnStateDto>()` when building the response DTO. Add a unit test for `{}` JSON. |
| Persistence type leaks into the API contract through a future refactor | Low | Mark the type `internal sealed`; no other module can reference it. Reinforced by Contracts/ ownership rules (development_guidelines.md). |
| Future column-shape change desynchronizes save vs. read | Low | Single shared type (FR-4). The slim record references `GridColumnStateDto` directly, so column-field additions propagate atomically. |
| `LastModified` remains as dead `null` in newly written rows (out-of-scope for spec but same anti-pattern) | Low | See *Specification Amendments* — recommend folding into this change. |

## Specification Amendments

**Amendment 1 (recommended): also exclude `LastModified` from the persisted JSON.**

`GridLayoutDto.LastModified` is `DateTime?`, the Save handler never sets it (so it serializes as `"lastModified":null`), and the Get handler unconditionally overwrites it from `entity.LastModified` at `GetGridLayoutHandler.cs:57` — the exact same dead-data pattern the spec flags for `GridKey`. Leaving it behind reproduces the spec's stated risk ("a future migration that touches one location without the other could produce silent divergence") for the same field on the next day-of-work. Since the slim payload exists *because* the JSON should carry only column data, excluding `LastModified` is zero marginal cost and closes the same hole. The spec's "Open Questions: None" is overoptimistic here.

If the project owner prefers to keep this strictly scoped (gridKey only) per the brief's title, the slim record is still future-proof — but the architecture review should not silently accept the half-finished cleanup. Decision required from the owner.

**Amendment 2 (minor, tightens FR-1 acceptance):** Add to FR-1 acceptance criteria: *"The persisted `LayoutJson` must not contain `gridKey` or `lastModified` keys; the save handler test must assert their absence, not just the presence of `columns`."* This pins the test to the intent.

**Amendment 3 (clarifies FR-3 boundary):** Add to FR-3 acceptance: *"Empty-object JSON (`{}`) and absent-columns payloads must produce a `GridLayoutDto` with an empty `Columns` list, matching prior behavior."* The current spec covers legacy rows with extra keys but not legacy rows with missing keys; the null-handling guard described in *Data Flow* needs an explicit acceptance criterion.

## Prerequisites

None. The change has:

- **No migrations** — schema unchanged.
- **No data backfill** — `System.Text.Json` ignores unknown properties; legacy rows read transparently.
- **No new packages or DI registrations** — only `System.Text.Json` and existing handler dependencies.
- **No configuration changes** — no `JsonSerializerOptions` plumbing.
- **No coordinated rollout** — read-old/write-new is the entire compatibility story; no two-phase deploy needed.

Implementation can begin immediately on a single PR.