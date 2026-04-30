# Architecture Review: Map MS365 Outlook Categories to Marketing Calendar Categories

## Architectural Fit Assessment

The feature aligns cleanly with the existing Clean Architecture + Vertical Slice organization of the Marketing module. It introduces a single new application-layer service (`IMarketingCategoryMapper`) inside the existing `Features/Marketing/Services` slice, reuses the established `IOptionsMonitor<MarketingCalendarOptions>` pattern already in `OutlookCalendarSyncService`, and extends an existing MediatR handler/response pair without changing the request envelope or controller surface.

**Integration points:**
- **Inbound**: `ImportFromOutlookHandler.BuildAction` (Application layer) — replaces inline `Enum.TryParse` with mapper call.
- **Outbound**: `OutlookCalendarSyncService.BuildEventBody` — replaces `ToString()` with mapper call.
- **DI**: `MarketingModule` — adds one `Singleton` registration alongside `IOutlookCalendarSync`, respects the existing mock-auth conditional gate.
- **Contract**: `ImportFromOutlookResponse` gains one nullable-safe field that flows through the OpenAPI-generated TypeScript client to `ImportFromOutlookModal`.

No layering violations, no new cross-feature dependencies, no Graph API additions. The change is contained inside the Marketing vertical slice and is fully reversible by emptying the configuration dictionaries.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│ API Layer (Anela.Heblo.API)                                         │
│   MarketingController ──► MediatR                                   │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────────────┐
│ Application Layer (Anela.Heblo.Application/Features/Marketing)      │
│                                                                     │
│   UseCases/ImportFromOutlook/                                       │
│     ImportFromOutlookHandler ──┐                                    │
│                                │                                    │
│   Services/                    ├──► IMarketingCategoryMapper        │
│     OutlookCalendarSyncService ┘     (Singleton)                    │
│                                       │                             │
│                                       └──► IOptionsMonitor<         │
│                                              MarketingCalendarOptions>
│                                                  │                  │
│   Configuration/                                 │                  │
│     MarketingCalendarOptions ◄───────────────────┘                  │
│       + CategoryMappings (string → MarketingActionType, OIC)        │
│       + OutgoingCategories (MarketingActionType → string)           │
│                                                                     │
│   MarketingModule.Validate()  ◄── round-trip consistency check      │
└─────────────────────────────────────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────────────┐
│ Frontend (frontend/src/components/marketing/detail)                 │
│   ImportFromOutlookModal                                            │
│     └─ "Nemapované kategorie z Outlooku" panel                      │
│        (renders only when unmappedCategories.length > 0)            │
└─────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Single mapper service for both directions
**Options considered:**
- (a) Two separate services (`IIncomingCategoryMapper`, `IOutgoingCategoryMapper`).
- (b) One bidirectional `IMarketingCategoryMapper`.
- (c) Static helper class.

**Chosen approach:** (b) — a single `IMarketingCategoryMapper` interface with two methods.

**Rationale:** Both directions read from the same `MarketingCalendarOptions` and share the round-trip invariant enforced by `MarketingModule.Validate`. Splitting into two services would create two consumers of the same options and two registration points, doubling the surface area for divergence. A static helper would prevent mocking in handler tests and force re-reading `IOptionsMonitor` plumbing into call sites. Single instance + DI wins on testability and cohesion.

#### Decision 2: Singleton lifetime with `IOptionsMonitor`
**Options considered:**
- (a) `Scoped` with `IOptionsSnapshot<T>`.
- (b) `Transient` with `IOptions<T>`.
- (c) `Singleton` with `IOptionsMonitor<T>`.

**Chosen approach:** (c).

**Rationale:** The mapper has no per-request state, no DbContext or HTTP-client dependencies, and requires hot-reload (FR-6). `IOptionsMonitor.CurrentValue` is the only options accessor that survives in a singleton and reflects file-watcher reloads. This matches the existing `OutlookCalendarSyncService` registration pattern in the same module, so reviewers see one consistent shape.

#### Decision 3: Unmapped-category collection lives on the handler, not on the mapper
**Options considered:**
- (a) Mapper aggregates a stateful unmapped set across calls.
- (b) Mapper is stateless; handler accumulates via `HashSet<string>(StringComparer.OrdinalIgnoreCase)`.

**Chosen approach:** (b).

**Rationale:** A singleton mapper with mutable state would leak data between concurrent imports and require synchronization. Per-call `CategoryMappingResult` is immutable; the handler — already scoped per request — owns the aggregation. This also keeps the FR-4 rule ("only contribute when the whole event had no match") in the use-case layer where it semantically belongs, not buried in a generic mapper.

#### Decision 4: Validation in `MarketingModule.Validate`, not via `IValidateOptions<T>`
**Options considered:**
- (a) Implement `IValidateOptions<MarketingCalendarOptions>` with `ValidateOnStart()`.
- (b) Inline check in the existing `MarketingModule.Validate(...)` hook.

**Chosen approach:** (b).

**Rationale:** The brief explicitly references `MarketingModule.Validate(...)`, indicating a startup-time validation pattern already exists in the module. Adding a new `IValidateOptions` implementation introduces a parallel mechanism for the same concern. Stay with the established pattern; throw a clear `InvalidOperationException` (or whatever the module currently throws) listing offending values.

#### Decision 5: Outgoing fallback to `actionType.ToString()` rather than throwing
**Options considered:**
- (a) Throw when `OutgoingCategories` lacks a key.
- (b) Fall back to `actionType.ToString()`.

**Chosen approach:** (b).

**Rationale:** NFR-3 requires backwards compatibility for empty-config dev/test environments. Throwing would force every test fixture and `appsettings.Development.json` to be updated immediately and would couple production correctness to test setup. The fallback preserves today's behavior exactly while still letting validation (Decision 4) catch the more dangerous case (round-trip break).

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/Marketing/
├── Configuration/
│   └── MarketingCalendarOptions.cs          [MODIFY: add CategoryMappings, OutgoingCategories]
├── Contracts/
│   └── ImportFromOutlookResponse.cs         [MODIFY: add UnmappedCategories]
├── Services/
│   ├── IMarketingCategoryMapper.cs          [NEW]
│   ├── MarketingCategoryMapper.cs           [NEW]
│   └── OutlookCalendarSyncService.cs        [MODIFY: inject mapper, use in BuildEventBody]
├── UseCases/ImportFromOutlook/
│   └── ImportFromOutlookHandler.cs          [MODIFY: inject mapper, replace Enum.TryParse, aggregate unmapped]
└── MarketingModule.cs                       [MODIFY: register mapper Singleton, extend Validate()]

backend/src/Anela.Heblo.API/
└── appsettings.json                         [MODIFY: add MarketingCalendar.CategoryMappings/OutgoingCategories template]

backend/test/Anela.Heblo.Tests/Features/Marketing/
├── Services/
│   ├── MarketingCategoryMapperTests.cs      [NEW]
│   └── OutlookCalendarSyncServiceTests.cs   [MODIFY: outgoing canonical name]
└── UseCases/ImportFromOutlook/
    └── ImportFromOutlookHandlerTests.cs     [MODIFY: mapping + UnmappedCategories]

frontend/src/components/marketing/detail/
└── ImportFromOutlookModal.tsx               [MODIFY: render unmapped panel]

frontend/src/api/generated/
└── api-client.ts                            [REGENERATED by build step]
```

`IMarketingCategoryMapper` and its implementation MUST live in `Features/Marketing/Services/` to match the existing `IOutlookCalendarSync` placement. Do not create a new `Mapping/` subfolder.

### Interfaces and Contracts

**Service contract** (final shape — do not vary):
```csharp
public interface IMarketingCategoryMapper
{
    CategoryMappingResult MapToActionType(IReadOnlyList<string> outlookCategories);
    string MapToOutlookCategory(MarketingActionType actionType);
}

public sealed record CategoryMappingResult(
    MarketingActionType ActionType,
    string? MatchedCategory,
    IReadOnlyList<string> UnmappedCategories);
```

**Configuration contract** — `MarketingCalendarOptions`:
```csharp
public Dictionary<string, MarketingActionType> CategoryMappings { get; init; }
    = new(StringComparer.OrdinalIgnoreCase);

public Dictionary<MarketingActionType, string> OutgoingCategories { get; init; }
    = new();
```

The `OrdinalIgnoreCase` comparer MUST be set in the property initializer; .NET options binding constructs a fresh `Dictionary<,>` per options reload, and without an initializer the comparer falls back to default (case-sensitive), silently breaking FR-1.

> **Spec amendment** — see [Specification Amendments](#specification-amendments) below: `IOptionsMonitor.CurrentValue` returns whatever `Dictionary<,>` the binder produced, so the mapper MUST defensively look up keys using a case-insensitive lookup pattern (e.g., wrap or rebuild on options change). The simplest correct implementation: subscribe to `OnChange` and rebuild a private `IReadOnlyDictionary<string, MarketingActionType>` with the right comparer; expose the rebuilt snapshot via a volatile field.

**Response contract** — `ImportFromOutlookResponse`:
```csharp
public List<string> UnmappedCategories { get; set; } = new();
```
Initialize to empty list (not `null`) so the OpenAPI schema marks it required-non-nullable and the TS client types it as `string[]` rather than `string[] | undefined`.

**Frontend type** — after OpenAPI regen, `ImportFromOutlookResponse.unmappedCategories: string[]`. Do not hand-edit `api-client.ts`.

### Data Flow

**Incoming import (FR-1 through FR-4):**
```
Graph $select=categories
  → ImportFromOutlookHandler.HandleAsync(batch)
    ├─ HashSet<string>(OrdinalIgnoreCase) unmapped = new()
    └─ for each evt:
       ├─ result = _mapper.MapToActionType(evt.Categories ?? [])
       ├─ action.ActionType = result.ActionType
       └─ if (evt.Categories?.Count > 0 && result.MatchedCategory is null)
              foreach (n in result.UnmappedCategories) unmapped.Add(n)
  → response.UnmappedCategories = unmapped.ToList()
  → return BaseResponse<ImportFromOutlookResponse>
  → OpenAPI client → ImportFromOutlookModal renders panel
```

**Outgoing sync (FR-5):**
```
MarketingAction created/updated
  → OutlookCalendarSyncService.PushEventAsync(action)
    └─ BuildEventBody:
       categories = new[] { _mapper.MapToOutlookCategory(action.ActionType) }
  → Graph PATCH/POST event
```

**Hot reload (FR-6):**
```
appsettings.{Environment}.json change
  → ChangeToken fires
  → IOptionsMonitor<MarketingCalendarOptions>.OnChange
  → MarketingCategoryMapper rebuilds internal case-insensitive dictionary snapshot
  → next Map* call sees new mapping; in-flight calls see prior snapshot (safe)
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Options binder constructs `Dictionary<string, MarketingActionType>` without `OrdinalIgnoreCase` comparer, silently breaking case-insensitive match (FR-1) | **High** | Mapper rebuilds an internal snapshot with the correct comparer on every `OnChange`; never indexes `options.CategoryMappings` directly. Add a unit test that binds raw `IConfiguration` and asserts case-insensitive resolution end-to-end. |
| Round-trip non-injectivity (`Campaign → "PR – léto"` and `Event → "PR – léto"`) leads to silent type changes after import-export-import | Medium | Documented as accepted (Open Question 3). Add code comment in `MarketingCategoryMapper` near `MapToOutlookCategory` and a doc-comment on `OutgoingCategories`. Consider an `Information` log when validation detects duplicate values, listing them. |
| Singleton mapper holds stale options snapshot if `OnChange` callback throws | Medium | Wrap rebuild logic in try/catch; on failure, keep prior snapshot and log at `Warning`. Rebuild eagerly in constructor so first call never sees `null`. |
| `evt.Categories` from Graph contains empty strings or whitespace, polluting unmapped report | Low | Filter `string.IsNullOrWhiteSpace` in the mapper before lookup. Spec amendment below. |
| Validation message in `MarketingModule.Validate` is too vague, admin can't fix the config | Low | Validation error MUST list every offending `OutgoingCategories[key] = value` pair with the specific missing `CategoryMappings` key. |
| OpenAPI client regen forgotten in PR; frontend `unmappedCategories` is `any` or missing | Low | CI build runs `dotnet build` (which triggers regen per `docs/development/api-client-generation.md`); reviewer checklist mentions verifying `api-client.ts` diff. |
| Czech subtext in modal references "Sociální sítě" as the fallback, but actual fallback is `MarketingActionType.General` whose canonical name is admin-configurable | Low | Subtext should say "jako General" or "jako výchozí kategorii" rather than hardcoding "Sociální sítě". See spec amendment. |
| `dotnet format` failure on Allman braces in new files blocks CI | Low | Run `dotnet format` locally before commit (project rule). |

## Specification Amendments

1. **Comparer preservation across options reloads.** FR-1 requires `StringComparer.OrdinalIgnoreCase`, but `Microsoft.Extensions.Options` binding does not preserve dictionary comparers across rebinds. Amend FR-1 acceptance criteria: *"The mapper implementation MUST guarantee case-insensitive lookup regardless of the comparer attached to the dictionary instance returned by `IOptionsMonitor.CurrentValue`."* Implementation: rebuild a private `Dictionary<string, MarketingActionType>(StringComparer.OrdinalIgnoreCase)` snapshot on each `OnChange`.

2. **Whitespace and empty-string filtering.** Add to FR-2: *"Categories that are null, empty, or whitespace are skipped during mapping and are NOT contributed to `UnmappedCategories`."* This prevents Graph quirks (trailing spaces, empty entries) from creating noise in the admin report.

3. **Modal subtext should not hardcode "Sociální sítě".** FR-8 specifies the subtext `"Tyto kategorie nebyly rozpoznány a události byly importovány jako Sociální sítě..."` — but `Sociální sítě` is only the fallback if `General` happens to be configured that way. Amend to: `"Tyto kategorie nebyly rozpoznány a události byly importovány jako výchozí kategorie (General). Doplňte je do appsettings.json → MarketingCalendar.CategoryMappings."`

4. **Validation must be case-insensitive.** FR-7 says "case-insensitive comparison" for the round-trip check. Amend acceptance to be explicit: *"Validation iterates `OutgoingCategories.Values` and asserts each value is contained in `CategoryMappings.Keys` using `StringComparer.OrdinalIgnoreCase`. Trailing/leading whitespace is trimmed before comparison."*

5. **Logging cardinality.** Open Question 4 settles on `Information` log per import. Amend NFR-1: *"At most one batch-aggregated `Information` log is emitted per import run when `UnmappedCategories` is non-empty; no per-event logging."*

6. **Template config consistency.** Open Question 2 — the `appsettings.json` template MUST include `"Ostatní": "Other"` in `CategoryMappings` so the example passes `Validate`. Add to spec under "Configuration shape".

7. **Test for binder behavior.** Add to NFR-5 test list: *"A test that loads a `MarketingCalendarOptions` instance via `ConfigurationBuilder.AddJsonStream` (not direct construction) and asserts the mapper still resolves keys case-insensitively. This test guards against the binder-comparer regression in Risk #1."*

## Prerequisites

1. **Confirm `MarketingModule.Validate` exists and is invoked at startup.** The brief and spec assume a `Validate(...)` method on `MarketingModule`. Before implementation begins, locate it and confirm whether it currently throws on misconfig or just logs. If the method does not exist, the implementer must either add it (registered via `IStartupFilter` or `ValidateOnStart()`) or implement `IValidateOptions<MarketingCalendarOptions>` — pick whichever matches the patterns used elsewhere in `Anela.Heblo.Application`.

2. **Confirm `appsettings.Development.json` policy.** Open Question 1 — decide whether category names go in committed `appsettings.{Environment}.json` or in user-secrets / Azure App Config. Default to committed env-specific file; mark as a non-blocking decision but record it before merging the PR so docs stay accurate.

3. **OpenAPI regen toolchain available.** Verify `dotnet build` triggers TypeScript client regen on the implementer's machine and in CI per `docs/development/api-client-generation.md`. No new tooling needed, but a stale generator will silently produce an `unmappedCategories: any` field.

4. **No DB migration, no new infrastructure, no new secrets.** Explicitly confirmed — implementation can proceed without DBA, DevOps, or security review beyond standard PR review.

5. **Real Outlook category names from the marketing team.** Smoke testing (verification step 4 in the brief) requires at least two real category names from the M365 Group calendar. Obtain these before merging so `appsettings.Development.json` is populated with values that actually appear in events.