---
process: module-marketing-ads
kind: module
module: marketing-ads
summary: Heblo's ad-platform backbone for AI marketing agents — daily Google Ads performance today (Meta and Sklik planned) and change history in the ads schema, read by agents over MCP and by people in Metabase.
owns: []
verified_at: "1ef9ed875"
related: [sync-ads-daily, sync-ads-change-history, sync-google-ads-campaign-data, sync-shoptet-orders]
---

# Marketing Ads (Reklamní kampaně)

## Purpose
"Heblo is the eyes and the hands, agents are the brain" (handoff `docs/handoff/marketing-agents-platform.md`). This module holds campaign-level data at management granularity for the ad platforms and records every account change, so agents can propose changes and people can see what anyone did. Proposals/approvals (C3), identity and MCP tools (C4) and UI (C5) build on it.

## Users & screens
No Heblo page yet. Agents and the chief of marketing via MCP tools (C4: accounts, performance, search terms, blended performance, change history — backed by this module's MediatR handlers in `UseCases/`). People via Metabase views `ads.v_ads_campaign_monthly`, `ads.v_ads_blended_monthly`, `ads.v_ads_change_events`.

## Processes
- `sync-ads-daily` — accounts, entities, daily facts, search terms; 05:30 Europe/Prague daily.
- `sync-ads-change-history` — platform change logs / snapshot diffs, Heblo vs out-of-band; hourly at :15.
- `sync-google-ads-campaign-data` — the Google Ads read source both jobs call; no job of its own.

## Data owned
- `ads.ad_accounts` — one ad account per platform; `is_managed` = Anela's own.
- `ads.ad_entities` — campaign / ad group / keyword / negative keyword / ad, current state.
- `ads.ad_daily_facts` — metrics per entity per day, every reported level.
- `ads.ad_search_term_daily` — search terms per ad group per day.
- `ads.ad_change_events` — every observed account change with origin.
- `ads.sync_state` — health per platform × account × stream.
- Views `ads.v_ads_*` (`backend/src/Anela.Heblo.Persistence.Ads/Sql/ads_read_views.sql`).

## External systems
Google Ads REST API (read only, via `IAdPlatformReadSource` in `Adapters.GoogleAds`); Meta and Sklik adapters are planned. Reads `shoptet_raw."order"` for the blended views.

## Dependencies
Reads `shoptet_raw` (Shoptet orders mirror, `sync-shoptet-orders`). Platform adapters plug in read sources; C3 will plug in `IAdExecutionLookup`. Metabase reads the views through `metabase_ro`.

## Known quirks
- Platform-reported conversions over-count; use the blended view for "is advertising paying off".
- Non-CZK ad accounts are excluded from blended cost (flagged, not converted).
- Rollout is manual: migrations are applied by hand, `ads_read_views.sql` is run with `psql`, and `AdsDatabase--ConnectionString` does not exist in either Key Vault yet, so ads persistence is unwired in every environment and the jobs log "skipped" (2026-10-12).

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/MarketingAdsModule.cs` — wiring and the "not configured" fallbacks
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Reporting/AdsReadRepository.cs` — agent-facing queries
