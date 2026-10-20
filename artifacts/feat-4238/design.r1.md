# Design: Persist an IsAllDay flag on MarketingAction

This is a backend-only correctness fix (arch review: Skip Design = true — no new or changed UI component). No UX/UI section is included per the designer's own rule: this feature has no user-facing component.

## Component Design

### `MarketingAction` (Domain) — `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs`

- New property: `public bool IsAllDay { get; private set; }`.
- Constructor gains a required `bool isAllDay` parameter (positioned after `endDate`), set unconditionally — there is no valid "unknown" state for this flag once construction completes.
- `UpdateDetails(...)` gains the same required `bool isAllDay` parameter, in the same position, set unconditionally on every call — an update always re-establishes the full date/all-day identity, exactly as it already does for `startDate`/`endDate`.
- `Reschedule(...)` is **unchanged** — no new parameter, `IsAllDay` is not touched by a reschedule (architect Decision 2). Responsibility: a reschedule changes *when*, never *what kind of* time range.
- New public static helper: `public static bool ComputeIsAllDay(DateTime startDate, DateTime? endDate)` — pure function, no side effects, reproduces exactly the deleted `OutlookCalendarSyncService.IsDateOnly` rule (`endDate is not null && startDate.TimeOfDay == TimeSpan.Zero && endDate.Value.TimeOfDay == TimeSpan.Zero`). This is the single place that rule now lives; nothing else recomputes it.

Responsibility boundary: `MarketingAction` owns *storing* `IsAllDay` and owns the *manual-path default rule* (`ComputeIsAllDay`), since that rule is a pure fact about a date range, not an Outlook-specific concern. It does not own *deciding* which source of truth (Graph vs. computed default) to use for a given call — that decision belongs to each caller (import mapper vs. handler), matching how those same callers already decide `startDate`/`endDate` values today.

### `OutlookEventImportMapper` (Application) — `backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookEventImportMapper.cs`

- `BuildAction`: passes `evt.IsAllDay` (Graph's own value) as the constructor's `isAllDay` argument. No new parsing/inference logic — the value is already present on `OutlookEventDto` and already used by `ParseEndDate`.
- `ApplyChanges`: passes `evt.IsAllDay` as `UpdateDetails`'s `isAllDay` argument.
- `HasChanges`: comparison gains one more term — `|| existing.IsAllDay != evt.IsAllDay` — alongside the existing `Title`/`Description`/`StartDate`/`EndDate`/`ActionType` comparisons. Responsibility unchanged: this method's contract is "does re-importing this event require writing anything," and `IsAllDay` is now one more field that can independently change on the Graph side (a user could toggle "All day" in Outlook without moving the event's dates at all).

Responsibility boundary: this class is the only place that reads `OutlookEventDto.IsAllDay` as authoritative input. It does not compute or guess — it forwards.

### `OutlookCalendarSyncService` (Adapters) — `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookCalendarSyncService.cs`

- `BuildEventBody`: replaces `var isAllDay = IsDateOnly(action);` with `var isAllDay = action.IsAllDay;`. `BuildGraphEnd(action, isAllDay)` call is unchanged (same signature, now fed the real flag instead of a guess).
- `IsDateOnly` private static method: **deleted**. Its only caller is the line above; no other reference exists in this file or elsewhere in the codebase (confirmed by architect's repo search — flag during implementation if a new caller is somehow found, per spec FR-3).

Responsibility boundary: this class no longer makes *any* decision about all-day-ness — it purely transports whatever `MarketingAction.IsAllDay` already says. This is the direct fix for the bug: the export side stops having an opinion of its own.

### `CreateMarketingActionHandler` / `UpdateMarketingActionHandler` (Application, UseCases)

- Both compute `var isAllDay = MarketingAction.ComputeIsAllDay(request.StartDate, request.EndDate);` immediately before constructing/calling `UpdateDetails`, and pass it through.
- No change to `CreateMarketingActionRequest` / `UpdateMarketingActionRequest` — they carry no `IsAllDay` field (NFR-2: no API/DTO surface change).

Responsibility boundary: these handlers are the "manual origin" source of truth for `IsAllDay` — since there is no UI input for it, they fall back to the same date-shape rule the system has always (implicitly) used, but now decide it once at write time instead of leaving it to be re-derived on every future export.

### `MoveMarketingActionHandler` (Application, UseCases)

- **Unchanged.** Calls `action.Reschedule(...)` exactly as today; `Reschedule`'s unchanged signature means there is nothing new for this handler to supply.

### `MarketingActionConfiguration` (Persistence)

- Adds `builder.Property(x => x.IsAllDay).IsRequired();` alongside the other simple scalar property mappings in this file (grouped near `StartDate`/`EndDate` for readability, matching the file's existing property-then-index-then-navigation ordering). No new index — `IsAllDay` is not filtered/sorted on anywhere in current query code (`MarketingActionQueryCriteria`, repository methods).

## Data Schemas

### Domain / EF entity change

| Property | Type | Nullable | Default (new rows) | Backfill rule (existing rows) |
|---|---|---|---|---|
| `MarketingAction.IsAllDay` | `bool` | No | Set explicitly by every constructor/`UpdateDetails` call (import: `evt.IsAllDay`; manual: `ComputeIsAllDay(...)`) | `ComputeIsAllDay(StartDate, EndDate)` applied once, in the migration's `Up()`, to all pre-existing rows |

### Migration shape (illustrative — exact EF-generated scaffolding to be produced by `dotnet ef migrations add` at implementation time, per architect Prerequisites)

```csharp
public partial class AddIsAllDayToMarketingAction : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsAllDay",
            schema: "public",
            table: "MarketingActions",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        // Backfill: reproduce the legacy IsDateOnly guess as a one-time value so no
        // existing row's live export behavior changes at the moment this migration runs.
        // Rows with an active OutlookEventId self-correct on their next import cycle.
        migrationBuilder.Sql(@"
            UPDATE ""public"".""MarketingActions""
            SET ""IsAllDay"" = TRUE
            WHERE ""EndDate"" IS NOT NULL
              AND date_trunc('day', ""StartDate"") = ""StartDate""
              AND date_trunc('day', ""EndDate"") = ""EndDate"";
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IsAllDay",
            schema: "public",
            table: "MarketingActions");
    }
}
```

No `ApplicationDbContextModelSnapshot.cs` hand-editing — this file is regenerated automatically by `dotnet ef migrations add`.

### API / DTO shapes

No change. `MarketingActionDto`, `CreateMarketingActionRequest`, `UpdateMarketingActionRequest`, `MarketingActionCalendarDto` are all unaffected (spec NFR-2, spec Open Question 3 left unresolved/out of scope).

### Graph payload shape (export)

No structural change to `BuildEventBody`'s output JSON shape — `isAllDay` was already a field in the outbound payload; only the *source* of its value changes (from `IsDateOnly(action)` to `action.IsAllDay`).

### Graph payload shape (import)

No change. `OutlookEventDto.IsAllDay` already exists and is already fetched via `EventSelect`; only a new *consumer* of it is added (the constructor/`UpdateDetails` call, in addition to the existing `ParseEndDate` consumer).
