# Design: Map MS365 Outlook Categories to Marketing Calendar Categories

## UX/UI Design

### Import Modal — Unmapped Categories Panel

Panel appears below the existing created / skipped / failed summary row and renders only when `unmappedCategories.length > 0`.

```
┌──────────────────────────────────────────────────────────┐
│  Import z Outlooku                                    [×] │
├──────────────────────────────────────────────────────────┤
│  Výsledek importu                                        │
│  ┌──────────┬────────────┬──────────┐                    │
│  │ Vytvořeno│  Přeskočeno│  Chyba   │                    │
│  │    12    │      3     │    0     │                    │
│  └──────────┴────────────┴──────────┘                    │
│                                                          │
│  ┌──────────────────────────────────────────────────┐    │
│  │ ⚠ Nemapované kategorie z Outlooku               │    │
│  │                                                  │    │
│  │ Tyto kategorie nebyly rozpoznány a události byly │    │
│  │ importovány jako výchozí kategorii (General).    │    │
│  │ Doplňte je do appsettings.json →                │    │
│  │ MarketingCalendar.CategoryMappings.              │    │
│  │                                                  │    │
│  │  [PR – jaro]  [Wellness kampaň]  [Video]         │    │
│  └──────────────────────────────────────────────────┘    │
│                                           [Zavřít]       │
└──────────────────────────────────────────────────────────┘
```

**Panel behaviour:**
- Hidden when `unmappedCategories` is empty or undefined.
- Warning-level visual treatment (amber border / light amber background) consistent with the existing design system. Pills are read-only — no click, no dismiss, no copy affordance beyond the user selecting text.
- Panel is not independently dismissible; it stays visible until the modal closes so the admin can copy the names.

**Component hierarchy:**

```
ImportFromOutlookModal
├── ImportSummaryRow              (existing: created / skipped / failed)
└── UnmappedCategoriesPanel       (new, conditional)
    ├── Panel heading + subtext
    └── CategoryPill[]             (one per name)
```

`UnmappedCategoriesPanel` and `CategoryPill` are local to the modal folder. They are not shared components — they have no use outside this feature.

---

## Component Design

### `IMarketingCategoryMapper` + `MarketingCategoryMapper`
**Location:** `Application/Features/Marketing/Services/`

Single-responsibility service for bidirectional category resolution. Registered as **Singleton** via `MarketingModule`.

**Responsibilities:**
- `MapToActionType(IReadOnlyList<string> outlookCategories)` — iterates in order, skips null/whitespace, returns the first match from an internal case-insensitive snapshot. Returns `General` with `MatchedCategory = null` when nothing matches. `UnmappedCategories` on the result contains all non-whitespace entries that had no match (used by the handler to feed the batch report).
- `MapToOutlookCategory(MarketingActionType actionType)` — looks up in `OutgoingCategories`; falls back to `actionType.ToString()` when absent.

**Hot reload:** Constructor eagerly builds a `Dictionary<string, MarketingActionType>(StringComparer.OrdinalIgnoreCase)` snapshot from `IOptionsMonitor<MarketingCalendarOptions>.CurrentValue`. `OnChange` rebuilds the snapshot; on rebuild failure the prior snapshot is retained and a `Warning` is logged. The snapshot is stored as `volatile` to avoid a torn read across the rebuild.

The implementation **never indexes `options.CategoryMappings` directly** because the options binder may not preserve the `OrdinalIgnoreCase` comparer across reloads. The snapshot copy is the sole lookup target.

---

### `MarketingCalendarOptions` (modified)
**Location:** `Application/Features/Marketing/Configuration/`

Two new properties appended to the existing class:

```csharp
public Dictionary<string, MarketingActionType> CategoryMappings { get; init; }
    = new(StringComparer.OrdinalIgnoreCase);

public Dictionary<MarketingActionType, string> OutgoingCategories { get; init; }
    = new();
```

Both default to empty — preserves existing behaviour when config is absent (NFR-3).

---

### `MarketingModule` (modified)

Two additions:

1. **DI registration** — `services.AddSingleton<IMarketingCategoryMapper, MarketingCategoryMapper>()` inside the same conditional DI gate already used for `IOutlookCalendarSync`.

2. **Startup validation** — iterates `OutgoingCategories.Values`, trims each value, and asserts presence in `CategoryMappings` using `StringComparer.OrdinalIgnoreCase`. Error message names every offending pair: `"OutgoingCategories[Campaign] = 'PR – léto' has no matching key in CategoryMappings."` Validation passes when both dictionaries are empty.

---

### `ImportFromOutlookHandler` (modified)
**Location:** `Application/Features/Marketing/UseCases/ImportFromOutlook/`

`BuildAction` replaces the `Enum.TryParse` block:

```csharp
var mapping = _mapper.MapToActionType(evt.Categories ?? Array.Empty<string>());
action.ActionType = mapping.ActionType;
```

Handler maintains `HashSet<string>(StringComparer.OrdinalIgnoreCase) unmappedAccumulator` across the batch loop. Per event: if the event had at least one non-whitespace category **and** `mapping.MatchedCategory is null`, all names in `mapping.UnmappedCategories` are added to the accumulator. After the loop: `response.UnmappedCategories = unmappedAccumulator.ToList()`. One batch-aggregated `Information` log is emitted when the accumulator is non-empty.

`IMarketingCategoryMapper` is constructor-injected.

---

### `OutlookCalendarSyncService` (modified)
**Location:** `Application/Features/Marketing/Services/`

`BuildEventBody` replaces the inline `ToString()` call:

```csharp
categories = new[] { _mapper.MapToOutlookCategory(action.ActionType) }
```

`IMarketingCategoryMapper` is constructor-injected alongside existing dependencies. `NoOpOutlookCalendarSync` is unchanged.

---

### `ImportFromOutlookResponse` (modified)
**Location:** `Application/Features/Marketing/Contracts/`

```csharp
public List<string> UnmappedCategories { get; set; } = new();
```

Initialized to empty list (not `null`) so the OpenAPI schema emits a required non-nullable `string[]` in the TypeScript client. No manual edits to `api-client.ts` — regenerated by `dotnet build`.

---

### `ImportFromOutlookModal` (modified)
**Location:** `frontend/src/components/marketing/detail/`

Reads `result.unmappedCategories` from the generated typed response. Renders `UnmappedCategoriesPanel` when `(result.unmappedCategories?.length ?? 0) > 0`. No other modal behaviour changes.

---

## Data Schemas

### Configuration — `appsettings.json` template

```json
"MarketingCalendar": {
  "GroupId": "...",
  "PushEnabled": true,
  "CategoryMappings": {
    "Sociální sítě": "General",
    "Ostatní":        "Other",
    "Událost":        "Promotion",
    "Email":          "Launch",
    "PR – léto":      "Campaign",
    "PR – zima":      "Campaign",
    "Fotografie":     "Event"
  },
  "OutgoingCategories": {
    "General":   "Sociální sítě",
    "Other":     "Ostatní",
    "Promotion": "Událost",
    "Launch":    "Email",
    "Campaign":  "PR – léto",
    "Event":     "Fotografie"
  }
}
```

`"Ostatní": "Other"` is present in `CategoryMappings` so the template passes `Validate` (resolves Open Question 2). Real values are populated per environment in `appsettings.{Environment}.json`.

---

### In-memory result type

```csharp
public sealed record CategoryMappingResult(
    MarketingActionType ActionType,
    string? MatchedCategory,
    IReadOnlyList<string> UnmappedCategories);
```

`UnmappedCategories` on this record is scoped to the **single `MapToActionType` call** (one event). The handler's `HashSet` accumulator is the batch-level aggregation point and enforces the FR-4 rule (only events with zero matches contribute).

---

### API response — `ImportFromOutlookResponse`

| Field | Type | OpenAPI | Notes |
|---|---|---|---|
| `Created` | `int` | `integer` | Existing |
| `Skipped` | `int` | `integer` | Existing |
| `Failed` | `int` | `integer` | Existing |
| `UnmappedCategories` | `List<string>` | `array / string[]` required, non-nullable | New. Empty list when all events mapped. |

---

### TypeScript component contract

```typescript
// Generated — do not hand-edit
interface ImportFromOutlookResponse {
  created: number;
  skipped: number;
  failed: number;
  unmappedCategories: string[];   // always present; empty when all events mapped
}

// Local component — not exported outside the marketing feature
interface UnmappedCategoriesPanelProps {
  categories: string[];   // non-empty; parent guards rendering
}
```