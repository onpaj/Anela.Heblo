---
process: sync-ads-change-history
kind: sync
module: marketing-ads
summary: Hourly import of every change made to Anela's ad accounts — from each platform's change history, or by diffing entity snapshots where a platform has none — into Heblo_V3.ads.ad_change_events, each flagged as made by Heblo or out-of-band.
owns:
  - backend/src/Anela.Heblo.Application/Features/MarketingAds/Infrastructure/Jobs/AdsChangeSyncJob.cs
  - backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdsChangeSyncService.cs
  - backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdChangeOriginMatcher.cs
  - backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdChangeEventWriter.cs
  - backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdChangeEventCandidate.cs
  - backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdChangeValueTokens.cs
verified_at: "5d9408774"
related: [sync-ads-daily, sync-google-ads-campaign-data]
---

# Ads change history → ads.ad_change_events

## Purpose
Observability over who changed what in the ad accounts — Heblo (approved proposals), the agency, a person in the platform UI, or a platform's auto-applied recommendation. Spec success criterion 2: every change appears within a day, flagged *Heblo* or *out-of-band*. Read by agents (MCP `GetAdChangeHistory`, C4) and in Metabase (`ads.v_ads_change_events`).

## Trigger
Hangfire recurring job `ads-change-sync` (category Marketing), cron `15 * * * *` (hourly at :15), Europe/Prague, `[AutomaticRetry(Attempts = 0)]`, `[DisableConcurrentExecution(1800)]`. Same skip rules as `ads-daily-sync`. Hangfire runs one worker, so the run is kept short (`Ads:ChangeSync:RunTimeoutMinutes` = 15).

## Data flow
For every registered source and managed account:
- **Source with a change log** (`Capabilities.ChangeLog`, Google Ads): `GetChangeEventsAsync(since)` where `since` = the stored `ChangeEvents` watermark, passed raw and inclusive; first run: now − `InitialLookbackDays`. C2 never clamps it — the source owns its history limit (Google: 30 days − 1 h, DST-aware) and logs a warning when it clamps. Rows are inserted once per (account, source, external_event_id); repeats are dropped by the writer and the unique key. The watermark becomes the newest `OccurredAt` returned; an empty pull leaves it unchanged. `sync_state` stream `ChangeEvents`.
- **Source without one**: re-list entities and diff name/status/attributes against `ads.ad_entities` (`source = SnapshotDiff`, `actor_kind = Unknown`, one event per changed field); the entity upsert and the event write are staged and committed with one SaveChanges (EF implicit transaction), so a failed write rolls the upsert back and the next run re-detects the change. `Created` events appear only after the account's first successful entity sync. `sync_state` stream `Entities`.

Events carry `entity_level` and the raw `entity_external_ref` (the platform's own id); `entity_id` links to `ads.ad_entities` only when the id resolves unambiguously.

## Logic & formulas
Origin (`AdChangeOriginMatcher`, pure). A change is `Heblo` only when all hold:
1. `ActorKind` is `Heblo` or `Unknown` — `User` and `PlatformAutomation` actors are always `OutOfBand`;
2. an action returned by `IAdExecutionLookup` (C3's execution log) targets the same entity — same level and id, or the executed `PlatformResourceId` equals / ends with `/{entity id}`;
3. it is the same change — PauseAd ↔ the new value contains "Paused" (a revert: "Enabled"); AddNegativeKeyword ↔ created resource id or the keyword text appears in the new value;
4. it was executed within ±`Ads:OutOfBandMatchWindowMinutes` (120) of the change; the closest execution wins.

Otherwise `OutOfBand`. Until C3 ships, `IAdExecutionLookup` is a no-op and every change is OutOfBand. Heblo's identity on Google is `GoogleAds:HebloUserEmail`; until it is set Heblo's own API writes arrive as `ActorKind.User` → OutOfBand (a visible false alarm, not a silent miss). Values that are not JSON are stored as JSON strings; timestamps are stored in UTC.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `Ads:ChangeSync:CronExpression` | `15 * * * *` | seed cron (DB value wins after first run) |
| `Ads:ChangeSync:TimeZone` | `Europe/Prague` | cron time zone |
| `Ads:ChangeSync:RunTimeoutMinutes` | 15 | run is cancelled after this |
| `Ads:ChangeSync:InitialLookbackDays` | 29 | first pull reach (the source clamps to its own limit); must be >= 1. 29 = everything Google keeps (30 days) with a day of slack; older changes cannot be backfilled |
| `Ads:OutOfBandMatchWindowMinutes` | 120 | ± window for matching executions |
| `GoogleAds:HebloUserEmail` | empty (KV `GoogleAds--HebloUserEmail`) | Google user Heblo acts as; read by the Google source, not by this job |

## Runtime facts
- Google account `343-302-3676`: 11 change events in 29 days, all agency web-UI edits (spike 2026-10-07, pre-deploy).
- `AdsDatabase--ConnectionString` set and the `ads` schema created in both environments on 2026-10-14; no change event is stored until the apps restart and the job first runs (its first pull reaches back 29 days).

## Known quirks
- An account with no change for more than 30 days makes Google log its clamp warning every hour: the watermark does not advance on an empty pull, so `since` stays older than the limit until the next change.
- Large bursts: Google cuts a pull at 10 000 rows; the watermark is the newest event returned, so the next run continues from there (inclusive `since` + dedupe by `ExternalEventId` makes the overlap harmless).
- Watermark uses `Max(OccurredAt)`, not the last row, because only Google promises ascending order.
- The matcher compares string values inside the JSON, so a platform that reports a pause without "Paused"/"PAUSED" in the new value cannot be matched to Heblo.
- Snapshot diffs see only the net state between two runs: pause-then-re-enable within one hour is invisible on sources without a change log; disappearing entities are not detected.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdsChangeSyncService.cs` — orchestration and watermark rule
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdChangeOriginMatcher.cs` — Heblo vs out-of-band rules
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdChangeEventWriter.cs` — dedupe and insert
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Contracts/IAdExecutionLookup.cs` — port C3 implements
