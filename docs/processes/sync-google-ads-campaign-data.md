---
process: sync-google-ads-campaign-data
kind: sync
module: marketing-ads
summary: Google Ads read source for the MarketingAds backbone — account, campaign/ad group/keyword/negative/ad entities, daily facts, search terms and change history pulled over the Google Ads REST API v25.
owns:
  - backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Api/**
  - backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Reporting/**
  - backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsMarketingAdsServiceCollectionExtensions.cs
verified_at: "b77bcf0e3"
related: [sync-ad-platform-transactions]
---

# Google Ads → MarketingAds (read side)

## Purpose
Gives Heblo's marketing agents and Metabase the Google Ads data needed to *manage* the account,
not only report on it: what exists (campaigns, ad groups, keywords, negatives, ads), what it cost
and earned per day at each level, which search terms triggered ads, and every change made to the
account by Heblo, the agency, a person or Google's auto-applied recommendations.

## Trigger
No job of its own. `GoogleAdsReadSource` is one `IAdPlatformReadSource`; the core MarketingAds
jobs call it (daily facts at 05:30 Europe/Prague, change history hourly — spec section 5; those
jobs ship in the core PR and will get their own process docs). It exists only when
`AddGoogleAdsMarketingAds` registered it: `GoogleAds:CustomerId` numeric, OAuth client
id/secret and refresh token present and not placeholders (`AdSettingsGuard`), `LoginCustomerId`
empty or numeric. Otherwise the platform is silently absent and the core syncs the others.

## Data flow
1. OAuth refresh token → access token (`oauth2.googleapis.com/token`, cached until 2 min before expiry).
2. `POST googleads.googleapis.com/v25/customers/{CustomerId}/googleAds:search` per GAQL query
   (integration doc §4), all pages.
3. Rows mapped to the contract records (`AdAccountSnapshot`, `AdEntitySnapshot`,
   `AdDailyFactRow`, `AdSearchTermRow`, `AdChangeEventRow`); the core upserts them into the `ads` schema.

## Logic & formulas
- Cost = `metrics.cost_micros / 1 000 000`, account currency (CZK), net of VAT. Missing metrics = 0.
- Facts are returned for campaign, ad group, keyword (`keyword_view`) and ad (`ad_group_ad`)
  levels; summing across levels double-counts.
- Search terms: `EXACT`+`NEAR_EXACT` → Exact, `PHRASE`+`NEAR_PHRASE` → Phrase (summed), `BROAD` → Broad,
  `AI_MAX`/`PERFORMANCE_MAX` → no match type.
- External ids: integration doc §6.
- Change history: window clamped to now − 30 days + 1 h, `LIMIT 10000`, account-time → UTC,
  actor kind per integration doc §7; events older than the watermark are dropped.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `GoogleAds:CustomerId` | `XXX-XXX-XXXX` (placeholder) | client account id (KV `GoogleAds--CustomerId`) |
| `GoogleAds:LoginCustomerId` | empty | manager id when access is via MCC (KV `GoogleAds--LoginCustomerId`) |
| `GoogleAds:OAuth2ClientId` / `OAuth2ClientSecret` / `OAuth2RefreshToken` | placeholders | OAuth credentials (KV) |
| `GoogleAds:HebloUserEmail` | empty | Google user Heblo acts as; its API changes are `Heblo` (KV `GoogleAds--HebloUserEmail`) |
| `GoogleAds:ApiVersion` | `v25` | REST API version |

## Runtime facts
- Access spike 2026-10-07: the account holds 19 campaigns, 60 ad groups, 223 keywords, 940 negative
  keywords (373 ad-group + 567 campaign level) and 66 ads (all statuses).
- `change_event` over 29 days returned 11 events, all agency web-UI edits (`GOOGLE_ADS_WEB_CLIENT`).
- Heblo currently acts as `ondra@anela.cz` (owner decision); classification still separates Heblo from
  him because `Heblo` requires `client_type` `GOOGLE_ADS_API`, while his UI edits are `User`.
- Cloud project `heblo-493908` has Explorer access since 2026-10-07 (2 880 ops/day).
- The `GoogleAds--*` secrets (CustomerId, OAuth2ClientId/ClientSecret/RefreshToken, HebloUserEmail)
  are set in `kv-heblo-stg` and `kv-heblo-prod`.

## Known quirks
- Google omits zero-valued fields; a missing metric is 0, not an error.
- `change_event` cannot reach back more than 30 days; after a longer outage older changes are lost (warning logged).
- An ad-group criterion UPDATE/REMOVE event may lack `negative` and is then reported at level `Keyword`.
- Events in the repeated autumn DST hour are mapped to standard time and may be missed or appear up to 1 h late (Google timestamps carry no UTC offset; the repeated 02:00-03:00 hour on the last Sunday of October, once a year).
- Staging and production read the same live account (read-only; the Cloud project's quota is shared).
- Criterion change events with a non-keyword payload (location, language, device, audience...) are kept with a null entity reference; see integration doc §7.
- The developer token is not used; Google sunset it on 2026-09-09.

## Code entry points
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Reporting/GoogleAdsReadSource.cs` — the five contract methods
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Reporting/GoogleAdsQueries.cs` — every GAQL query
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Api/GoogleAdsRestClient.cs` — transport, paging, retries
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsMarketingAdsServiceCollectionExtensions.cs` — configuration gate
