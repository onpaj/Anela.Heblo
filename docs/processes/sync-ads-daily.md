---
process: sync-ads-daily
kind: sync
module: marketing-ads
summary: Daily pull of every registered ad platform's accounts, campaigns/ad groups/keywords/ads, cost and conversion facts (yesterday plus a 14-day lookback) and search terms into Heblo_V3.ads, read by AI marketing agents over MCP and by Metabase.
owns:
  - backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdsDailySyncService.cs
  - backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdAccountUpserter.cs
  - backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdEntitySyncStep.cs
  - backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdEntityUpserter.cs
  - backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdDailyFactUpserter.cs
  - backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdSearchTermUpserter.cs
  - backend/src/Anela.Heblo.Application/Features/MarketingAds/Infrastructure/Jobs/AdsDailySyncJob.cs
  - backend/src/Anela.Heblo.Application/Features/MarketingAds/Configuration/**
  - backend/src/Anela.Heblo.Persistence.Ads/Sql/**
  - backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdsBackfillService.cs
  - backend/src/Anela.Heblo.Application/Features/MarketingAds/Contracts/IAdPlatformRangeReadSource.cs
  - backend/tools/Anela.Heblo.AdsBackfill/**
verified_at: "5d9408774"
related: [sync-ads-change-history, sync-google-ads-campaign-data, sync-shoptet-orders]
---

# Ads daily sync → ads (agents + Metabase)

## Purpose
The measurement backbone of the marketing agents platform (spec `docs/superpowers/specs/2026-10-07-marketing-agents-platform-design.md`, section 5). AI agents read it over MCP to *manage* campaigns (which keyword wastes money, which search term needs a negative keyword); people read the month-grain views in Metabase (`ads.v_ads_campaign_monthly`, `ads.v_ads_blended_monthly`). The blended view answers "how much real Shoptet revenue per koruna of ad spend", because platform-reported conversions over-count.

## Trigger
- Hangfire recurring job `ads-daily-sync` (category Marketing), cron `30 5 * * *`, Europe/Prague. `[AutomaticRetry(Attempts = 0)]`, `[DisableConcurrentExecution(3600)]`. Manual trigger from Recurring Jobs.
- Always registered. It does nothing when disabled in Recurring Jobs (`IsJobEnabledAsync`), and logs "skipped" when the ads persistence is not registered (no `AdsDatabase:ConnectionString`) or no `IAdPlatformReadSource` is registered (each platform adapter registers one only when its own secrets are really configured; see `sync-google-ads-campaign-data`).

## Data flow
For every registered source, sequentially:
1. `GetAccountsAsync` → upsert `ads.ad_accounts` by (platform, external_id). New accounts get `is_managed` from `Ads:ManagedAccounts`; existing rows keep theirs.
2. For each **managed** account: `GetEntitiesAsync` → upsert `ads.ad_entities` by (account, level, external_id), parents linked. For sources **without** a change log (`Capabilities.ChangeLog` false) the entity upsert and the snapshot-diff `ads.ad_change_events` rows (`source = SnapshotDiff`) commit in one SaveChanges (EF implicit transaction): if the event write fails, the upsert rolls back and the next run diffs the same change again.
   If the Entities stream fails for an account, that account's DailyFacts and SearchTerms are skipped for the run (facts need the entity ids), so daily data for it stops until Entities succeeds.
3. For each date from yesterday−`FactLookbackDays` to yesterday (account time zone): `GetDailyFactsAsync` → replace that account's `ads.ad_daily_facts` rows for the date (update, insert, delete rows the platform no longer reports).
4. Same for `GetSearchTermsAsync` → `ads.ad_search_term_daily` (only sources with `Capabilities.SearchTerms`).
5. Each stream writes `ads.sync_state` (platform × account × stream `Entities` / `DailyFacts` / `SearchTerms`): status, last_success_at, last_error, watermark (= last synced date).

## Backfill (history before the 14-day window)
The daily job never reaches further back than yesterday − `FactLookbackDays`. Older history is loaded once, by a person, with `backend/tools/Anela.Heblo.AdsBackfill` (`AdsBackfillService`):

```
set -a; . ./ads-backfill.env; set +a   # chmod 600: AdsDatabase__ConnectionString, GoogleAds__CustomerId,
                                       # GoogleAds__OAuth2ClientId/ClientSecret/RefreshToken
dotnet run --project backend/tools/Anela.Heblo.AdsBackfill -- 2025-01-01 2026-09-30
```

- Same accounts/entities steps as the daily run, then one calendar month at a time: facts and search terms are fetched for the month (one call per level when the source implements `IAdPlatformRangeReadSource`, else day by day) and every day of the month goes through the same `ReplaceDayAsync` as the daily run. Re-running any range is safe.
- `to` must be before the account's today. The DailyFacts/SearchTerms `sync_state` rows are not touched; the entity step is a normal entity sync, so it updates the Entities row (and, for a source without a change log, writes snapshot-diff events) exactly as the daily run would.
- Rows for entities missing from the current entity listing are skipped and logged as `AdsBackfill.UnmappedRows` (warning).
- A failed month stops that account; the error names the date to resume from. Exit codes: 0 ok, 1 failures, 2 bad arguments/configuration, 130 Ctrl+C.
- Prerequisites: the ads migration and `ads_read_views.sql` are applied. Run outside 05:30–06:15 Prague (the daily job replaces the same days for the last 15) and off-hours (1-vCore Postgres shared with production).
- Change history is not backfilled: Google keeps 30 days; the change sync's first pull takes all of it (`Ads:ChangeSync:InitialLookbackDays` = 29).

## Logic & formulas
- Facts are stored at every level the platform reports. **Never sum across levels** — every query and view picks one level (views use Campaign).
- Cost is net of VAT in the account's currency. Blended views add only CZK accounts and set `has_non_czk_ad_cost` otherwise (no FX rates in `ads`).
- Blended revenue = Shoptet `order.price_without_vat / coalesce(exchange_rate, 1)` from `shoptet_raw."order"`, excluding `status_id = -4` (cancelled); e-shop revenue additionally excludes `cash_desk_order`. ROAS = revenue / cost; PNO % = cost × 100 / revenue.
- A failing source or stream is isolated: the others still run; the job then throws so Hangfire shows Failed with the (capped) error list. A failed save clears the shared `AdsDbContext` change tracker so it cannot poison the next stream.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `Ads:FactLookbackDays` | 14 | days before yesterday re-pulled daily (late conversions); 0..30 |
| `Ads:BatchSize` | 500 | rows per SaveChanges; 1..5000 |
| `Ads:ManagedAccounts` | [] | `Platform:externalId` keys of Anela's own accounts; empty = all discovered accounts managed |
| `Ads:DailySync:CronExpression` | `30 5 * * *` | seed cron (DB value wins after first run) |
| `Ads:DailySync:TimeZone` | `Europe/Prague` | cron time zone |
| `Ads:DailySync:RunTimeoutMinutes` | 45 | run is cancelled after this |
| `AdsDatabase:ConnectionString` | (KV `AdsDatabase--ConnectionString`) | gates the whole stack |

Invalid values throw at the start of each run (`AdsSyncOptions.EnsureValid`), never at startup.

## Runtime facts
- `AdsDatabase--ConnectionString` is set in `kv-heblo-stg` and `kv-heblo-prod` as a copy of `ConnectionStrings--Default` (same database, like `AnalyticsDatabase--` and `ShoptetOrdersSync--`) — 2026-10-14. The app reads it at startup, so the jobs stay "skipped" until a restart.
- `InitialAdsSchema` and `ads_read_views.sql` applied by hand to Heblo_TST and Heblo_V3 (the startup migration covers only `ApplicationDbContext`) — 2026-10-14.
- Backfilled 2025-01-01..2026-10-13 into Heblo_TST (13 min 18 s) and Heblo_V3 (11 min 26 s): 694 791 rows each, 0 errors, 0 unmapped; heblosql CPU peaked at 26 % with burst credits untouched. Heblo_V3 `ads` tables 146 MB — 2026-10-14.
- Backfill rehearsal against a local Postgres (live Google reads), 2025-01-01..2026-10-13: 3 min 42 s, 36 146 fact rows + 658 645 search-term rows, 0 unmapped, `ads` schema 134 MB (search terms 128 MB). Search terms peak at ~100k rows/month (Mar–Apr 2025); about 10 API operations per month — 2026-10-14.
- Google account `343-302-3676` (CZK, Europe/Prague): 19 campaigns, 60 ad groups, 223 keywords, 940 negative keywords, 66 ads (access spike, 2026-10-07, pre-deploy).

## Known quirks
- Rows for entities the entity listing did not return are skipped and logged as `AdsDailySync.UnmappedRows` (e.g. facts for a removed campaign the platform no longer lists).
- Entities that disappear from a listing are not marked removed; platforms normally keep listing them with status Removed.
- `Created` snapshot events are written only once the account has had a successful entity sync, so the first run does not flood the table.
- The entity save (upsert + snapshot-diff events, one SaveChanges) must commit within ONE database attempt. New entities, their parent links and their `Created` events go out in that one save — EF fixes up the temporary keys — so there are no intermediate batch flushes on this path (the entity upsert for sources with a change log still flushes every `BatchSize` rows). `AdsPersistenceModule` gives the ads pipeline a 2-minute per-attempt timeout (`AdsAttemptTimeout`); a slower attempt is retried from scratch, and if it keeps timing out the Entities stream fails with `TimeoutRejectedException` in `sync_state.last_error`.
- Blended ROAS/PNO are only trustworthy for months where ad cost is complete: revenue covers every day Shoptet has, ad cost only the days the sync pulled (the first run reaches back yesterday−14 days; failed days leave gaps). Compare `days_with_ad_cost` (monthly view) / `has_ad_cost_data` (daily view) with the month length; a short month overstates ROAS and understates PNO.
- Re-running `shoptet_raw_views.sql` does not affect the ads views (they read `shoptet_raw."order"`, not `order_fact`).

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Infrastructure/Jobs/AdsDailySyncJob.cs` — schedule, skip/fail semantics
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdsDailySyncService.cs` — orchestration
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdEntitySyncStep.cs` — entity upsert + snapshot diff in one SaveChanges
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Sync/AdDailyFactUpserter.cs` — replace-per-day semantics
- `backend/src/Anela.Heblo.Persistence.Ads/Sql/ads_read_views.sql` — Metabase views + grants
