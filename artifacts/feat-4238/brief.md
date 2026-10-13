Found during code review of #4224 / #4237.

## Problem

`MarketingAction` has no all-day concept — only `StartDate` and nullable `EndDate`. So the Outlook sync is asymmetric:

- **Import** is *told*: it reads Graph's `isAllDay`.
- **Export** *guesses*: `OutlookCalendarSyncService.IsDateOnly` infers all-day from a midnight-to-midnight range.

These are not inverses, and the round trip corrupts data.

## Failure scenario

A genuinely **timed** Outlook event 18. 9. 00:00 → 19. 9. 00:00 with `isAllDay: false` imports through the timed branch to `EndDate = 19. 9. 00:00`. That row now satisfies `IsDateOnly` — both `TimeOfDay` are zero — so the next edit in Heblo pushes `isAllDay: true` and `end = 20. 9. 00:00`.

**A 24-hour timed event silently becomes a 3-day all-day event on the shared group calendar.**

Related: `IsDateOnly` also requires a non-null `EndDate`, so a midnight-start action with `EndDate == null` exports as a 1-hour meeting at midnight. Not reachable from the UI today (the end field is required) but the API contract allows it. What a null end *means* is bound up with the same decision.

## Suggested fix

Persist `IsAllDay` on `MarketingAction`, set it from `evt.IsAllDay` on import, read it on export. Needs a migration (manual in this repo).

Short of that, at minimum add an import → export round-trip test — the existing `CreateEventAsync_ThenImportingWhatWasSent_ReproducesTheOriginalDates` only walks export → import, so the asymmetry has no coverage at all in the direction where it bites.

Context: `docs/integrations/microsoft-graph-calendar.md` ("Known gaps").