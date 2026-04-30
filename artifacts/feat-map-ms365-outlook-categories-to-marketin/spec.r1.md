```markdown
# Specification: Map MS365 Outlook Categories to Marketing Calendar Categories

## Summary
Replace the brittle `Enum.TryParse<MarketingActionType>` lookup in the Outlook → Marketing Calendar import path with a configuration-driven, bidirectional mapping between Outlook category names and the app's `MarketingActionType` enum. The mapping is defined in `appsettings.json`, supports case-insensitive matching, and round-trips during outgoing sync so colors stay consistent in Outlook. Unmapped categories surface in the import response so administrators can extend the configuration.

## Background
The marketing calendar imports events from a Microsoft 365 Group calendar. Each Graph event carries one or more `categories` (named labels with preset colors managed via Outlook master categories). Today, `ImportFromOutlookHandler.BuildAction` calls `Enum.TryParse<MarketingActionType>(category)` against the **first** category and silently falls back to `General` when the name does not literally match an enum member. Real category names used by the marketing team — e.g. `"PR – léto"`, `"Sociální sítě"`, `"Email"` — therefore never resolve correctly, and every imported event collapses into `General`.

Symmetrically, when the app pushes an event to Outlook, `OutlookCalendarSyncService.BuildEventBody` writes the raw enum string (e.g. `"Campaign"`) as the Outlook category, which does not match any of the master categories the team actually uses, so colors are lost on the Outlook side.

The fix is purely application-side: a single `IMarketingCategoryMapper` reads two dictionaries from `MarketingCalendarOptions` and is consumed in both directions. No database migration, no Graph API additions, no admin UI.

## Functional Requirements

### FR-1: Configuration-driven incoming category mapping
Outlook category names defined in `appsettings.json` resolve to the matching `MarketingActionType` during import.

**Acceptance criteria:**
- `MarketingCalendarOptions.CategoryMappings` is a `Dictionary<string, MarketingActionType>` constructed with `StringComparer.OrdinalIgnoreCase`.
- Mapping is consulted for every event imported via `ImportFromOutlookHandler`.
- Matching is case-insensitive: `"sociální sítě"` matches a key `"Sociální sítě"`.
- When `CategoryMappings` is empty (default / migration state), no exception is thrown and import proceeds with every event mapped to `General`.

### FR-2: First-mapped-category-wins for multi-category events
When an Outlook event has multiple categories, the first one (in the order returned by Graph) that has an entry in `CategoryMappings` determines the `MarketingActionType`. Categories without a mapping are skipped, not failed.

**Acceptance criteria:**
- Given event categories `["Random tag", "PR – léto", "Sociální sítě"]` with mappings only for the latter two, the result is `Campaign` (the mapping for `"PR – léto"`).
- Given event categories `["Random tag", "Another"]` with no mappings, the result is `General`.
- The order of evaluation matches the order of `evt.Categories` from Graph.

### FR-3: Fallback to `General` for fully unmapped events
An event whose categories do not match any key in `CategoryMappings` (or that has zero categories) imports as `MarketingActionType.General` without failing.

**Acceptance criteria:**
- An event with no categories returns `MarketingActionType.General`.
- An event whose every category is absent from `CategoryMappings` returns `MarketingActionType.General`.
- The handler never throws because of an unrecognized category name.

### FR-4: Report unmapped categories in import response
Categories on imported events that could not be mapped are aggregated and returned to the caller, but **only for events that had no successful match at all**. Events that did successfully map (because at least one of their categories matched) do not contribute to the unmapped report.

**Acceptance criteria:**
- `ImportFromOutlookResponse.UnmappedCategories` is a `List<string>` populated with distinct category names (case-insensitive deduplication via `HashSet<string>(StringComparer.OrdinalIgnoreCase)`).
- An event with categories `["Random tag", "PR – léto"]` (where only `"PR – léto"` is mapped) does **not** add `"Random tag"` to the report.
- An event with categories `["Random tag", "Another"]` (no mappings) adds both names.
- Events with **zero** categories do not add empty strings to the report.
- The report aggregates across the entire import batch.
- The list is empty (not null) when every event mapped cleanly.

### FR-5: Configuration-driven outgoing category mapping
When the app pushes an event to Outlook, the canonical Outlook category name (per `OutgoingCategories`) is used instead of the raw enum `ToString()`.

**Acceptance criteria:**
- `MarketingCalendarOptions.OutgoingCategories` is a `Dictionary<MarketingActionType, string>`.
- `OutlookCalendarSyncService.BuildEventBody` writes `categories = new[] { mapper.MapToOutlookCategory(action.ActionType) }`.
- When the action type has an entry in `OutgoingCategories`, that value is written.
- When the action type has no entry, the value is `actionType.ToString()` (preserves current behavior for tests/dev environments without config).
- `NoOpOutlookCalendarSync` is unchanged.

### FR-6: Hot reload of mapping configuration
Operators can edit `appsettings.{Environment}.json` (or equivalent override) and have the mapping take effect without restarting the backend.

**Acceptance criteria:**
- `MarketingCategoryMapper` consumes `IOptionsMonitor<MarketingCalendarOptions>` and reads `CurrentValue` on each call.
- A change to `CategoryMappings` or `OutgoingCategories` at runtime is reflected on the next import / outgoing sync without process restart.

### FR-7: Round-trip configuration validation
Validation prevents the most common misconfiguration: a value in `OutgoingCategories` that has no matching key in `CategoryMappings`. This guarantees that a category name we push to Outlook will be re-imported as the same `MarketingActionType`.

**Acceptance criteria:**
- In `MarketingModule.Validate(...)`, every value in `OutgoingCategories` is checked to exist as a key in `CategoryMappings` (case-insensitive comparison).
- Validation passes when both dictionaries are empty.
- Validation passes when only `CategoryMappings` is populated.
- Validation fails with a clear error message when `OutgoingCategories` references a name not present in `CategoryMappings`.

### FR-8: Frontend display of unmapped categories
After running an import from Outlook, the import modal lists any unmapped category names so the administrator can extend the configuration.

**Acceptance criteria:**
- `ImportFromOutlookModal` renders an "Unmapped categories" panel only when `result.unmappedCategories?.length > 0`.
- Panel headline (Czech): `"Nemapované kategorie z Outlooku"`.
- Panel subtext (Czech): `"Tyto kategorie nebyly rozpoznány a události byly importovány jako Sociální sítě. Doplňte je do appsettings.json → MarketingCalendar.CategoryMappings."`.
- Each unmapped name is rendered as a small pill/badge.
- The panel appears after the existing `created` / `skipped` / `failed` summary.
- The OpenAPI-generated TypeScript client exposes `unmappedCategories` on the typed response.

## Non-Functional Requirements

### NFR-1: Performance
- Mapping is an O(1) dictionary lookup per category. No Graph calls are added.
- `MarketingCategoryMapper` is registered as a `Singleton` and depends only on `IOptionsMonitor`. No per-request allocation overhead beyond the result record.
- Import time for a typical batch (tens to low hundreds of events) is unchanged within measurement noise.

### NFR-2: Security
- No new endpoints, no new auth surface. The mapper is invoked only from already-authenticated handlers/services.
- Configuration is read from `appsettings.json`; no secrets are introduced. Existing secret-management practices for `MarketingCalendar:GroupId` are unchanged.

### NFR-3: Reliability / Backwards compatibility
- Empty/missing `CategoryMappings` and `OutgoingCategories` MUST behave identically to the current code path (every event imports as `General`; outgoing sync writes the enum `ToString()`). This protects existing test suites and dev environments that do not configure the dictionaries.
- No data migration is required for existing marketing actions whose `ActionType` was set to `General` by the old import logic.

### NFR-4: Maintainability
- Mapping logic lives in a single class with a single responsibility (`MarketingCategoryMapper`).
- Both incoming and outgoing flows go through the same abstraction (`IMarketingCategoryMapper`), preventing divergence.
- Configuration is the single source of truth — no hardcoded category strings in handlers or services.

### NFR-5: Testability
- The mapper is unit-testable in isolation via a stub `IOptionsMonitor<MarketingCalendarOptions>`.
- Handler and sync-service tests exercise mapper integration without requiring real Graph calls.

## Data Model

No persistent data model changes.

**Configuration model** (`MarketingCalendarOptions`):

| Field | Type | Notes |
|---|---|---|
| `GroupId` | `string` | Existing |
| `PushEnabled` | `bool` | Existing |
| `CategoryMappings` | `Dictionary<string, MarketingActionType>` | New. Constructed with `StringComparer.OrdinalIgnoreCase`. Empty default. |
| `OutgoingCategories` | `Dictionary<MarketingActionType, string>` | New. Empty default. |

**In-memory result type** (`MarketingCategoryMapper`):

```csharp
public sealed record CategoryMappingResult(
    MarketingActionType ActionType,
    string? MatchedCategory,
    IReadOnlyList<string> UnmappedCategories);
```

`MarketingActionType` enum is unchanged: `General`, `Promotion`, `Launch`, `Campaign`, `Event`, `Other`.

## API / Interface Design

### New service contract

```csharp
public interface IMarketingCategoryMapper
{
    CategoryMappingResult MapToActionType(IReadOnlyList<string> outlookCategories);
    string MapToOutlookCategory(MarketingActionType actionType);
}
```

Registered in `MarketingModule` as `Singleton`, alongside the existing `IOutlookCalendarSync` registration. Available in both real and mock auth modes.

### Modified contract: `ImportFromOutlookResponse`

Add:
```csharp
public List<string> UnmappedCategories { get; set; } = new();
```

The OpenAPI-generated TypeScript client (`frontend/src/api/generated/api-client.ts`) is regenerated as part of the standard build step so the new field is typed end-to-end.

### Modified handler: `ImportFromOutlookHandler.BuildAction`

Replace the inline `Enum.TryParse` block (~line 146-149) with:
```csharp
var mapping = _mapper.MapToActionType(evt.Categories ?? Array.Empty<string>());
var actionType = mapping.ActionType;
```

The handler instance accumulates unmapped names into a `HashSet<string>(StringComparer.OrdinalIgnoreCase)` across all events in the batch. Only events with a non-empty category list and `MatchedCategory == null` contribute to the set. The set is materialized into `response.UnmappedCategories` before returning.

### Modified service: `OutlookCalendarSyncService.BuildEventBody`

Replace (~line 156-182):
```csharp
categories = new[] { action.ActionType.ToString() }
```
with:
```csharp
categories = new[] { _mapper.MapToOutlookCategory(action.ActionType) }
```

### Configuration shape (template in `appsettings.json`)

```json
"MarketingCalendar": {
  "GroupId": "...",
  "PushEnabled": true,
  "CategoryMappings": {
    "Sociální sítě": "General",
    "Událost": "Promotion",
    "Email": "Launch",
    "PR – léto": "Campaign",
    "PR – zima": "Campaign",
    "Fotografie": "Event"
  },
  "OutgoingCategories": {
    "General": "Sociální sítě",
    "Promotion": "Událost",
    "Launch": "Email",
    "Campaign": "PR – léto",
    "Event": "Fotografie",
    "Other": "Ostatní"
  }
}
```

Real values are filled per environment (`appsettings.Development.json`, environment-specific overrides, or user secrets).

### Frontend UI flow

1. User opens **Marketing Calendar → Import z Outlooku**.
2. User picks a date range and runs import (existing flow).
3. The response now includes `unmappedCategories: string[]`.
4. After the existing `created` / `skipped` / `failed` summary, the modal renders an "Unmapped categories" panel when the list is non-empty:
   - Headline: `"Nemapované kategorie z Outlooku"`.
   - Subtext explaining that these were imported as `General` and where to add them.
   - Each unmapped name as a small pill.

## Dependencies

**Existing infrastructure (reused, unchanged):**
- `IOptionsMonitor<MarketingCalendarOptions>` (already used in `OutlookCalendarSyncService`).
- Conditional DI gate for mock auth in `MarketingModule.cs` (lines 29-38).
- Microsoft Graph token + HTTP client setup (no new Graph calls; the `categories` field is already returned by the existing `$select` in `ListEventsAsync`).
- `MarketingActionType` enum (unchanged).
- `BaseResponse` + `ErrorCodes` pattern in `ImportFromOutlookHandler` (extended, not replaced).
- Frontend `ACTION_TYPE_COLORS` map (unchanged).

**Build pipeline:**
- OpenAPI client regeneration (`frontend/src/api/generated/api-client.ts`) — see `docs/development/api-client-generation.md`.

**No new external dependencies. No new NuGet or npm packages.**

## Out of Scope

- Reading `/users/{owner}/outlook/masterCategories` from Microsoft Graph (would only be needed for showing color swatches in an admin UI; not required for the static-config approach).
- Replacing `MarketingActionType` enum with a DB-backed table.
- Admin UI for editing `CategoryMappings` / `OutgoingCategories` (config-file only).
- Changing the frontend `ACTION_TYPE_COLORS` map. Colors stay app-defined; mapping only decides which app-side bucket an Outlook event lands in.
- Migrating existing rows whose `ActionType` was wrongly set to `General` due to the old `Enum.TryParse` behavior. If correction is wanted later, it is a manual one-off, not part of this feature.
- Surfacing matched categories per event in the response (only **unmapped** names are reported).
- Multi-language UI strings for the unmapped-categories panel (Czech only, matching existing modal copy).

## Open Questions

1. **Location of real configuration values.** The brief lists `appsettings.Development.json` *or* `secrets.json` ("if treated as env-specific"). Decision needed before staging/prod rollout: are category mappings considered non-secret config (commit to `appsettings.{Environment}.json`) or environment-specific overrides held in user/Azure secrets? Assumption applied: commit to env-specific `appsettings.*.json`; revisit if the marketing team treats category names as sensitive.
2. **Handling of `Other`.** The example config maps `Other → "Ostatní"` outgoing, but no incoming key `"Ostatní"` is shown. Per FR-7, validation will fail unless `"Ostatní"` is also added to `CategoryMappings` (e.g. `"Ostatní": "Other"`). The example block in the brief should be updated for consistency before it lands in `appsettings.json` as a template. Assumption applied: include `"Ostatní": "Other"` in the template `CategoryMappings`.
3. **Duplicate canonical names in `OutgoingCategories`.** Multiple action types could map to the same Outlook name (e.g. `Campaign → "PR – léto"` and `Event → "PR – léto"`). This is allowed by the data model but creates a non-injective round-trip (importing `"PR – léto"` will always pick whichever single `MarketingActionType` is in `CategoryMappings`). Should validation warn when `OutgoingCategories` is non-injective? Assumption applied: no warning — this is an admin's choice; document the round-trip implication in code comments.
4. **Logging of unmapped categories.** Should the backend also log unmapped names at `Information` or `Warning` level when an import runs, in addition to returning them in the response? Assumption applied: log at `Information` level, batch-aggregated, once per import (avoid per-event noise).
5. **Dry-run behavior.** The verification plan mentions running an import in dry-run mode. Confirm that dry-run still populates `UnmappedCategories` (so admins can audit the mapping without writing). Assumption applied: yes — `UnmappedCategories` is populated identically in dry-run and real runs because mapping happens before persistence.
```