# Map MS365 Outlook Categories to Marketing Calendar Categories

## Context

The marketing calendar imports events from a Microsoft 365 Group calendar. MS365 events carry one or more **categories** (named labels with preset colors managed via Outlook master categories). Today, `ImportFromOutlookHandler.BuildAction` parses the **first** category by attempting `Enum.TryParse<MarketingActionType>(category)` — so any real Outlook category name like `"PR – léto"` or `"Sociální sítě"` silently falls back to `General`. The 6 supported categories (`General/Promotion/Launch/Campaign/Event/Other`) are also colored only on the frontend.

Goal: make Outlook category names (defined by the marketing team in Outlook) drive the `MarketingActionType` chosen during import, and round-trip the same names back when we push events to Outlook so colors stay consistent.

Decisions captured (from clarifying questions):
- **Storage**: `appsettings.json` dictionary (no DB migration, no admin UI).
- **Unknown category on import**: fallback to `General` AND surface unmapped category names in the import response.
- **Multi-category event**: first mapped category wins (skip categories with no mapping).
- **Outgoing sync**: reverse-map — push the canonical Outlook category name for each `MarketingActionType` instead of the raw enum string.

## Implementation Plan

### 1. Extend configuration

**File**: `backend/src/Anela.Heblo.Application/Features/Marketing/Configuration/MarketingCalendarOptions.cs`

Add two dictionaries (both optional; empty defaults preserve current behavior):

```csharp
// Outlook category name (case-insensitive) → our action type
public Dictionary<string, MarketingActionType> CategoryMappings { get; init; } = new(StringComparer.OrdinalIgnoreCase);

// Our action type → canonical Outlook category name used when pushing events
public Dictionary<MarketingActionType, string> OutgoingCategories { get; init; } = new();
```

Add validation in `MarketingModule.cs` `Validate(...)` to ensure every value in `OutgoingCategories` has a matching key in `CategoryMappings` (so round-trip stays consistent). Validation must NOT fail when both dictionaries are empty — that is the current/migration state.

Example config block (developer documents this in `appsettings.json`; the team fills the values in their environment-specific file):

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

### 2. Add the mapper service

**New files**:
- `backend/src/Anela.Heblo.Application/Features/Marketing/Services/IMarketingCategoryMapper.cs`
- `backend/src/Anela.Heblo.Application/Features/Marketing/Services/MarketingCategoryMapper.cs`

```csharp
public interface IMarketingCategoryMapper
{
    // Returns mapped type + the matched Outlook category name (or null + the unmapped names)
    CategoryMappingResult MapToActionType(IReadOnlyList<string> outlookCategories);

    // Returns canonical Outlook name for outgoing sync; falls back to actionType.ToString()
    string MapToOutlookCategory(MarketingActionType actionType);
}

public sealed record CategoryMappingResult(
    MarketingActionType ActionType,
    string? MatchedCategory,
    IReadOnlyList<string> UnmappedCategories);
```

Implementation rules:
- Walk `outlookCategories` in order; first one whose name matches a key in `CategoryMappings` (case-insensitive — `Dictionary<string, ...>(StringComparer.OrdinalIgnoreCase)`) wins.
- If none match, return `MarketingActionType.General`, `MatchedCategory = null`, `UnmappedCategories = original list` so the caller can report them.
- `MapToOutlookCategory`: lookup in `OutgoingCategories`; if missing, return `actionType.ToString()` (current behavior — keeps tests/no-config dev environments working).
- Use `IOptionsMonitor<MarketingCalendarOptions>` so config changes are picked up without restart.

### 3. Wire DI

**File**: `backend/src/Anela.Heblo.Application/Features/Marketing/MarketingModule.cs`

Register `IMarketingCategoryMapper → MarketingCategoryMapper` as `Singleton` (depends only on `IOptionsMonitor`). Place registration alongside the existing `IOutlookCalendarSync` block. Mapper must be available in both real and mock auth modes.

### 4. Use mapper on import

**File**: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/ImportFromOutlook/ImportFromOutlookHandler.cs`

- Inject `IMarketingCategoryMapper`.
- Replace the inline `Enum.TryParse` block in `BuildAction` (~line 146-149) with:
  ```csharp
  var mapping = _mapper.MapToActionType(evt.Categories ?? Array.Empty<string>());
  var actionType = mapping.ActionType;
  // accumulate mapping.UnmappedCategories into a HashSet<string> on the handler instance
  ```
- Aggregate unmapped names across all imported events into a `HashSet<string>(StringComparer.OrdinalIgnoreCase)` and pass them into the response.
- Only collect unmapped names when there is **at least one** category on the event (events with zero categories shouldn't pollute the report).

### 5. Surface unmapped categories in the response

**File**: `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/ImportFromOutlookResponse.cs`

Add:
```csharp
public List<string> UnmappedCategories { get; set; } = new();
```

The frontend modal will display these so the admin knows which Outlook category names need to be added to `CategoryMappings`.

### 6. Use mapper on outgoing sync

**File**: `backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookCalendarSyncService.cs`

- Inject `IMarketingCategoryMapper`.
- In `BuildEventBody` (~line 156-182), replace:
  ```csharp
  categories = new[] { action.ActionType.ToString() }
  ```
  with:
  ```csharp
  categories = new[] { _mapper.MapToOutlookCategory(action.ActionType) }
  ```
- `NoOpOutlookCalendarSync` needs no changes.

### 7. Frontend — show unmapped categories in import result

**File**: `frontend/src/components/marketing/detail/ImportFromOutlookModal.tsx`

After `created`/`skipped`/`failed` summary, render an "Unmapped categories" panel when `result.unmappedCategories?.length > 0`:
- Headline: `"Nemapované kategorie z Outlooku"`.
- Subtext: `"Tyto kategorie nebyly rozpoznány a události byly importovány jako Sociální sítě. Doplňte je do appsettings.json → MarketingCalendar.CategoryMappings."`.
- List the names as small pills.

Run the OpenAPI client regen so `unmappedCategories` is available on the typed response (the project's standard build step regenerates `frontend/src/api/generated/api-client.ts` — see `docs/development/api-client-generation.md`).

### 8. Tests

**New file**: `backend/test/Anela.Heblo.Tests/Features/Marketing/Services/MarketingCategoryMapperTests.cs`
Cases:
- Maps a single matching category → correct `ActionType`.
- Case-insensitive match (`"sociální sítě"` matches `"Sociální sítě"`).
- Multi-category: first mapped wins; unmapped earlier names are reported as unmapped only when no mapping was found at all? — Decision: only report unmapped when the **entire event** had no match. (Cleaner for the admin report; an event with `["Random tag", "PR – léto"]` shouldn't surface `"Random tag"` as a problem because the event was successfully mapped.)
- Empty categories → `General`, no unmapped names.
- All unmapped → `General` + all names reported as unmapped.
- `MapToOutlookCategory` returns canonical name when present, else `actionType.ToString()`.

**Update**: `backend/test/Anela.Heblo.Tests/Features/Marketing/UseCases/ImportFromOutlook/ImportFromOutlookHandlerTests.cs` (or wherever the handler tests live — locate via grep on `ImportFromOutlookHandler`).
- Add a test that imports events with custom Outlook category names and verifies they map per the configured dictionary.
- Add a test that asserts `response.UnmappedCategories` is populated for events whose categories aren't in the dictionary.

**Update**: `OutlookCalendarSyncService` tests (locate via grep `OutlookCalendarSyncServiceTests`) — assert outgoing event body uses the canonical Outlook name from config.

## Critical files

- `backend/src/Anela.Heblo.Application/Features/Marketing/Configuration/MarketingCalendarOptions.cs`
- `backend/src/Anela.Heblo.Application/Features/Marketing/MarketingModule.cs`
- `backend/src/Anela.Heblo.Application/Features/Marketing/Services/IMarketingCategoryMapper.cs` *(new)*
- `backend/src/Anela.Heblo.Application/Features/Marketing/Services/MarketingCategoryMapper.cs` *(new)*
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/ImportFromOutlook/ImportFromOutlookHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/ImportFromOutlookResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookCalendarSyncService.cs`
- `frontend/src/components/marketing/detail/ImportFromOutlookModal.tsx`
- `backend/appsettings.json` (template entries; real values per environment)

## Reuse from existing code

- `IOptionsMonitor<MarketingCalendarOptions>` pattern — already used in `OutlookCalendarSyncService.cs`.
- Conditional DI gate for mock auth — `MarketingModule.cs:29-38`.
- Graph token + HTTP client setup — already wired; **no Graph calls are added by this feature** (we only consume the `categories` field already returned by `ListEventsAsync` via `$select`).
- `MarketingActionType` enum — unchanged; keep as the canonical app-side category set.
- Existing handler-level `BaseResponse` + `ErrorCodes` pattern in `ImportFromOutlookHandler` — extend, don't replace.

## Out of scope

- Reading `/users/{owner}/outlook/masterCategories` (would only be needed if we wanted to show colors in admin UI — not required for static-config approach).
- Replacing `MarketingActionType` enum with a DB-backed table.
- Changing the frontend `ACTION_TYPE_COLORS` map — colors stay app-defined; mapping just decides which app-side bucket an Outlook event lands in.
- Migrating existing rows whose `ActionType` was wrongly set to `General` due to the old `Enum.TryParse` behavior — manual one-off if needed.

## Verification

1. **Unit tests**: `dotnet test --filter "FullyQualifiedName~Marketing"` — all green, including new mapper tests and updated handler tests.
2. **Build**: `dotnet build` succeeds; `npm run build` (frontend) succeeds with regenerated client.
3. **Local config**: add `CategoryMappings` + `OutgoingCategories` to `backend/src/Anela.Heblo.API/appsettings.Development.json` (or `secrets.json` if treated as env-specific) with at least 2 real Outlook category names from the team's group calendar.
4. **Import smoke test**:
   - Start backend + frontend locally.
   - Open Marketing Calendar → "Import z Outlooku" → choose a date range covering events with known categories → run import (non-dry-run on a throwaway window first, or dry-run to inspect mapping).
   - Verify imported actions have the expected `ActionType` (color in calendar matches the category they had in Outlook).
   - Verify the modal shows any unmapped category names.
5. **Outgoing smoke test**:
   - Create a new marketing action via the UI (e.g. ActionType = `Campaign`).
   - Confirm the event appears in the M365 Group calendar with the canonical Outlook category (e.g. `"PR – léto"`) and that color, not the literal string `"Campaign"`.
6. **Edit + sync**: edit the action, change its `ActionType` to `Launch`, save. Confirm Outlook event's category updates to the canonical `Launch` name (e.g. `"Email"`).
