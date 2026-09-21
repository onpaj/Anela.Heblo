# Microsoft Graph — group calendar (marketing calendar sync)

Findings from the Outlook group-calendar sync (`OutlookCalendarSyncService`,
`OutlookEventImportMapper`). There is no Graph sandbox for this tenant — every call hits the
live group calendar — so verify changes against staging before relying on them.

## `dateTimeTimeZone`: the value and its zone are separate fields

Graph's `start` / `end` are [`dateTimeTimeZone`](https://learn.microsoft.com/en-us/graph/api/resources/datetimetimezone)
resources:

```json
"start": { "dateTime": "2026-09-18T00:00:00.0000000", "timeZone": "UTC" }
```

`dateTime` is a **zone-less wall-clock string** — no `Z`, no offset. The zone lives in the
sibling `timeZone` field.

**Consequence for writes.** Serializing a `DateTime` with `ToString("O")` appends a `Z` for
a UTC value. Pairing that `Z` with `timeZone: "Europe/Prague"` states two different instants
at once. Heblo stores marketing action dates as UTC, so the sync declares `timeZone: "UTC"`
and formats with `yyyy-MM-ddTHH:mm:ss.fffffff` (no designator). A value whose `Kind` is not
already UTC is *converted*, never relabelled.

**Consequence for reads.** Parsing a zone-less string yields `DateTimeKind.Unspecified`, not
`Utc`. `OutlookEventDto.StartUtc`/`EndUtc` designate such a value UTC and convert anything
that carries an offset.

## Reads must pin the response time zone

Graph returns event times in the **mailbox's default time zone** unless the request asks
otherwise. Since the DTO designates a zone-less value as UTC, an unpinned default would make
local digits be read as UTC — shifting every event by the offset, and (because `IsDateOnly`
tests for midnight) silently disqualifying all-day events from the all-day export path.

`ListEventsAsync` and `GetEventAsync` therefore send:

```
Prefer: outlook.timezone="UTC"
```

## All-day events

- **`end` is exclusive.** An all-day event covering 18.–20. 9. is reported as
  `start = 18. 9. 00:00`, `end = 21. 9. 00:00`. Heblo's `EndDate` is **inclusive** (the
  calendar renders `StartDate..EndDate` as a closed interval), so the import subtracts a day
  and the export adds one.
- **`isAllDay` must be selected explicitly.** It is not returned by default; without it in
  `$select` the flag reads `false` and every all-day event is stored a day too long.
- **Midnight is required in the declared zone.** Graph rejects an all-day event whose start
  or end is not midnight *in the zone the payload declares*. `00:00Z` labelled
  `Europe/Prague` reads as 02:00 local and is refused — another reason the payload declares
  `UTC`.

## Known gaps

- `MarketingAction` has **no persisted all-day flag**. The export infers it from a
  midnight-to-midnight range (`IsDateOnly`), so a genuinely timed event that happens to run
  midnight-to-midnight round-trips back out as all-day. See the follow-up issue.
- `lastModifiedDateTime` is not selected and not compared, so the hourly sync has no
  conflict detection — Graph always wins.
