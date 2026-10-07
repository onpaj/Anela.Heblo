# Google Ads API — Integration Findings

> **Living document.** Every new finding about the Google Ads API MUST be added here before code relies on it.
> Reference: https://developers.google.com/google-ads/api/docs/start · Field reference: https://developers.google.com/google-ads/api/fields/v25/overview
> Enum/proto source of truth: https://github.com/googleapis/googleapis/tree/master/google/ads/googleads/v25

## 1. Overview

Two independent code paths in `Anela.Heblo.Adapters.GoogleAds`:

| Path | Transport | Purpose | State |
|---|---|---|---|
| `SdkAccountBudgetFetcher` → `GoogleAdsInvoiceImportJob` | `Google.Ads.GoogleAds` SDK 21.1.0, API **V18** | billing (`account_budget`) | dead: V18 is sunset, job disabled in prod, `GoogleAds--DeveloperToken` still absent (see §11) |
| `GoogleAdsReadSource`, `GoogleAdsActionExecutor` (`Api/`, `Reporting/`, `Execution/`) | REST over `HttpClient`, API **v25** | MarketingAds backbone: entities, daily facts, search terms, change history, v1 actions | this document |

SDK 21.1.0 contains only V16–V18. The current SDK (27.x) supports v22–v25 and no longer has V18, so upgrading it means rewriting the billing fetcher. The marketing code therefore uses REST and leaves the SDK alone.

## 2. Authentication and access

- OAuth 2.0 refresh-token flow. `POST https://oauth2.googleapis.com/token` with `grant_type=refresh_token`, `client_id`, `client_secret`, `refresh_token` → `access_token` (≈ 3 600 s). Heblo caches it until 2 minutes before expiry.
- Scope: `https://www.googleapis.com/auth/adwords`. The refresh token belongs to one Google user; Heblo acts with that user's rights (setting `GoogleAds:HebloUserEmail`).
- **Current identity (decision 2026-10-07):** Heblo acts as `ondra@anela.cz`, whose role on the client account is **ADMIN**. Least privilege would be a dedicated user with **Standard** access (e.g. a `heblo-ads@` mailbox) so Heblo's changes are also distinguishable by `change_event.user_email`; that remains the recommendation. Until then the actor classification (§7) still separates Heblo from Ondrej's manual edits, because Heblo is recognised only when `client_type = GOOGLE_ADS_API`; his edits in the Google Ads UI arrive as `GOOGLE_ADS_WEB_CLIENT` and are classified `User`.
- Cloud project: `heblo-493908`. OAuth client "Heblo Google Ads" (type Desktop app). Consent screen user type **Internal** (only anela.cz users can consent; no 7-day refresh-token expiry).
- The OAuth consent screen must be **In production** or **Internal**. In *Testing* status Google expires refresh tokens after 7 days → `invalid_grant`.
- **Developer tokens were sunset on 2026-09-09.** Access levels now belong to the Google Cloud project that owns the OAuth client (Cloud Console → Google Ads API Overview). The `developer-token` header is optional and ignored by v25; Google plans to reject it in a future major version, so Heblo's REST code does not send it.
- Access levels: Test (test accounts only), **Explorer** (production, 2 880 ops/day), Basic (15 000 ops/day), Standard (unlimited). Heblo needs Explorer or higher (≈ 150 calls/day). Project `heblo-493908` had Test access; Explorer was granted on 2026-10-07 (one click, no form) and production calls succeeded the same afternoon.
- `login-customer-id` header: only when the user reaches the account through a manager (MCC). Setting `GoogleAds:LoginCustomerId`; empty = header omitted. `GoogleAds:CustomerId` must be the **client** account; the read source refuses a manager account. Anela has no manager account linked, so no `LoginCustomerId` is set.
- **Known quirk when (re)doing the consent.** The anela.cz Workspace disallows passkey-only sign-in, yet Google demands passkey re-authentication for the `adwords` scope. The consent then loops to the passkey settings page ("the administrator has not allowed signing in with passkeys"). It looped from an automation-driven browser and was completed by Ondrej in a normal Chrome window (exact sign-in path not recorded). Expect the same for any new user or whoever redoes the consent; do it by hand in a regular browser.
- Secrets live in Key Vault (`kv-heblo-stg`, `kv-heblo-prod`): `GoogleAds--CustomerId`, `GoogleAds--OAuth2ClientId`, `GoogleAds--OAuth2ClientSecret`, `GoogleAds--OAuth2RefreshToken`, `GoogleAds--HebloUserEmail`. All five were set in both vaults on 2026-10-07; there is no `GoogleAds--LoginCustomerId`.

## 3. Endpoints used

| Method | Path | Used by |
|---|---|---|
| POST | `/v25/customers/{customerId}/googleAds:search` body `{"query": "...", "pageToken": "..."}` | read source, executor `ReadCurrent` |
| POST | `/v25/customers/{customerId}/campaignCriteria:mutate` | add / remove campaign negative keyword |
| POST | `/v25/customers/{customerId}/adGroupCriteria:mutate` | add / remove ad-group negative keyword |
| POST | `/v25/customers/{customerId}/adGroupAds:mutate` | pause / re-enable an ad |

- `search` pages are fixed at 10 000 rows; page size cannot be set (removed in v17). Follow `nextPageToken`. Heblo caps at 100 pages.
- Mutate body: `{"operations": [...], "partialFailure": false, "validateOnly": <bool>}`. Response: `{"results": [{"resourceName": "..."}]}`; with `validateOnly: true` the body is `{}` and nothing changes.
- Update operations carry a field mask as a string: `{"updateMask": "status", "update": {"resourceName": "...", "status": "PAUSED"}}`.
- Remove operations: `{"remove": "customers/{id}/campaignCriteria/{campaignId}~{criterionId}"}`.

## 4. GAQL queries Heblo runs

| Query name (code) | Resource | Notes |
|---|---|---|
| `customer` | `customer` | id, descriptive name, currency, time zone, manager flag |
| `campaigns` | `campaign` + `campaign_budget.amount_micros` | all statuses |
| `ad_groups` | `ad_group` | all statuses |
| `keywords` | `ad_group_criterion` `type = 'KEYWORD' AND negative = FALSE` | |
| `ad_group_negative_keywords` | `ad_group_criterion` `type = 'KEYWORD' AND negative = TRUE` | |
| `campaign_negative_keywords` | `campaign_criterion` `type = 'KEYWORD' AND negative = TRUE` | |
| `ads` | `ad_group_ad` | |
| `campaign_facts`, `ad_group_facts`, `keyword_facts` (`keyword_view`), `ad_facts` | with `segments.date = 'yyyy-MM-dd'` | impressions, clicks, cost_micros, conversions, conversions_value |
| `search_terms` | `search_term_view` + `segments.search_term_match_type` | excludes Performance Max |
| `change_events` | `change_event` | see §7 |
| `executor_*` | `campaign`, `ad_group`, `campaign_criterion`, `ad_group_criterion`, `ad_group_ad` filtered by numeric id | executor `ReadCurrent` |

Entity queries do **not** filter `REMOVED`: daily facts in the 14-day lookback can belong to an entity removed since, and the core needs that entity to attach the facts to.

**Never interpolate user/agent text into GAQL.** Only numeric ids (validated by `GoogleAdsIds`) and dates are interpolated; keyword text is compared in C#.

## 5. Response format quirks (proto3 JSON)

- Field names are lowerCamelCase: `ad_group_criterion.criterion_id` → `adGroupCriterion.criterionId`, `metrics.cost_micros` → `metrics.costMicros`.
- int64 fields (ids, `impressions`, `clicks`, `costMicros`, `amountMicros`) are JSON **strings**; doubles (`conversions`, `conversionsValue`) are numbers.
- **Fields at their default value are omitted**: a row with 0 clicks has no `clicks` key; `manager: false` is absent. Missing metric = 0.
- Money is in micros of the account currency: `costMicros / 1 000 000`. Cost is net of VAT.
- Enums are strings (`"ENABLED"`, `"PAUSED"`, `"REMOVED"`, `"EXACT"`, …).

## 6. External-id conventions in Heblo

| Level | `ExternalId` | Example |
|---|---|---|
| Campaign | `{campaignId}` | `111` |
| AdGroup | `{adGroupId}` | `221` |
| Keyword | `{adGroupId}~{criterionId}` | `221~331` |
| NegativeKeyword (ad group) | `adGroupCriteria/{adGroupId}~{criterionId}` | `adGroupCriteria/221~341` |
| NegativeKeyword (campaign) | `campaignCriteria/{campaignId}~{criterionId}` | `campaignCriteria/111~351` |
| Ad | `{adGroupId}~{adId}` (the `ad_group_ad` key) | `221~441` |

Resource name = `customers/{customerId}/{collection}/{id}` (`campaigns`, `adGroups`, `adGroupCriteria`, `campaignCriteria`, `adGroupAds`).

## 7. change_event

- Must filter `change_event.change_date_time` to a window that starts **at most 30 days back**, and must have `LIMIT` (≤ 10 000). Heblo clamps older watermarks to now − 30 days + 1 h and logs a warning; older history is unrecoverable.
- `change_date_time` is in the **account time zone** (`customer.time_zone`, Europe/Prague), format `yyyy-MM-dd HH:mm:ss[.ffffff]` (timestamps come back with microseconds, e.g. `2026-09-21 14:11:00.459519`; second-precision bounds are accepted). Heblo converts both ways.
- Events in the repeated autumn DST hour are mapped to standard time (Google timestamps carry no offset), so they may be missed or appear up to 1 h late (window: the repeated 02:00-03:00 hour on the last Sunday of October, once a year).
- AD-resource change events (`changeResourceType` AD, resource `customers/{id}/ads/{adId}`, e.g. RSA headline edits) carry no ad group in the resource name, so they are kept with entity level/id null (only `adGroupAds/...` events map to an Ad entity).
- `resource_name` = `customers/{id}/changeEvents/{timestampMicros}~{commandIndex}~{mutateIndex}`; the part after `changeEvents/` is Heblo's `ExternalEventId`.
- `old_resource` / `new_resource` hold only the changed fields of the resource (`{"adGroupAd": {"status": "PAUSED"}}`). An ad-group criterion UPDATE/REMOVE may therefore lack `negative`, and is then reported as level `Keyword` even when it is a negative.
- `campaign_criterion` / `ad_group_criterion` also hold targeting (location, language, device, audience). When the payload shows a non-keyword criterion (`type` present and not `KEYWORD`, or no `keyword` object next to another criterion object such as `location`), the event keeps its row, change type, raw JSON and actor but carries no entity reference (level and external id null); a payload with no type information keeps the keyword mapping above.
- Google Ads Editor changes are not reported by `change_event`.
- Actor mapping (`client_type` → `AdChangeActorKind`):

| client_type | ActorKind |
|---|---|
| `GOOGLE_ADS_API` and `user_email` = `GoogleAds:HebloUserEmail` (case-insensitive) | `Heblo` |
| `GOOGLE_ADS_RECOMMENDATIONS`, `GOOGLE_ADS_RECOMMENDATIONS_SUBSCRIPTION`, `GOOGLE_ADS_AUTOMATED_RULE`, `INTERNAL_TOOL` | `PlatformAutomation` |
| `GOOGLE_ADS_WEB_CLIENT`, `GOOGLE_ADS_EDITOR`, `GOOGLE_ADS_MOBILE_APP`, `GOOGLE_ADS_BULK_UPLOAD`, `GOOGLE_ADS_SCRIPTS`, other `GOOGLE_ADS_API` | `User` |
| `OTHER`, `UNKNOWN`, `UNSPECIFIED`, missing | `Unknown` |

Because `Heblo` requires `GOOGLE_ADS_API`, sharing the identity `ondra@anela.cz` between Heblo and Ondrej's manual work does not blur the two: his UI edits are `GOOGLE_ADS_WEB_CLIENT` → `User`. However, because `HebloUserEmail` is `ondra@anela.cz`, any other Google Ads API tool authorised as that user (e.g. re-running the access-spike script, another OAuth app) is also classified `Heblo`; a dedicated user avoids this.

## 8. Errors

Error body: `{"error": {"code", "message", "status", "details": [{"@type": "…GoogleAdsFailure", "errors": [{"errorCode": {"<category>": "<CODE>"}, "message"}], "requestId"}]}}`. Heblo reports the first error as `<category>.<CODE>`, falling back to a `google.rpc.ErrorInfo` `reason` (`errorInfo.SERVICE_DISABLED`), then `status.<STATUS>`.

| Code | HTTP | Meaning | Heblo |
|---|---|---|---|
| `authorizationError.CLOUD_PROJECT_NOT_APPROVED_FOR_PRODUCTION` | 403 | Cloud project has Test access only (legacy: `DEVELOPER_TOKEN_NOT_APPROVED`) | throw, not retried |
| `authorizationError.USER_PERMISSION_DENIED` | 403 | user lacks access / login-customer-id needed | throw |
| `authenticationError.OAUTH_TOKEN_*`, OAuth `invalid_grant` | 401 / 400 | token revoked or expired | throw |
| `errorInfo.SERVICE_DISABLED` | 403 | Google Ads API not enabled in the Cloud project | throw |
| `quotaError.RESOURCE_EXHAUSTED` | 429 | rate limit | retried (reads and mutates) |
| `INTERNAL` / `UNAVAILABLE` | 5xx | transient | reads retried; **mutates not retried** (may have applied) |
| anything else on a mutate | 400 / 404 / 409 | Google rejected the action (policy, duplicate, not found) | `AdExecutionResult` `Failed`, not thrown |

## 9. Versioning

v25 released July 2026, sunset August 2027 (v23 Feb 2027, v24 May 2027). Bump `GoogleAds:ApiVersion` (default in `GoogleAdsSettings`) and re-run the Part A spike script before the sunset; check the release notes for renamed fields.

## 10. Spike results (Part A, 2026-10-07)

- API version used: v25 (REST, `googleAds:search`). No developer token sent.
- OAuth user (Heblo identity for the spike): `ondra@anela.cz`; its role in the account: ADMIN. Production preference: a dedicated STANDARD user so Heblo's changes are distinguishable in `change_event.user_email` (see §2 for the current decision).
- Cloud project: `heblo-493908`. Access level was Test; Explorer applied for 2026-10-07 (one click, no form). Calls to the production account succeeded the same afternoon, so Explorer is effective.
- OAuth client: "Heblo Google Ads" (Desktop app). Consent screen user type: Internal (no 7-day refresh-token expiry; only @anela.cz users can consent). The old "Heblo" Web client (OAuth Playground redirect) has been unused since 2026-04-20 and is auto-deleted after 2026-10-17 unless used.
- Customer: id 3433023676 (343-302-3676, "Anela.cz"), currency CZK, time zone Europe/Prague, manager false, test account false.
- `login-customer-id` needed: no (no manager account linked).
- Yesterday (2026-10-06): 8 campaigns with spend, cost 3342.43 CZK, conversions 22.39.
- `change_event` (29 days, LIMIT 1000): 11 rows; client types {GOOGLE_ADS_WEB_CLIENT: 11}; all 11 by one agency user (gmail, STANDARD); resource types {CAMPAIGN: 6, AD_GROUP: 3, CAMPAIGN_BUDGET: 2}; first 2026-09-21 14:11, last 2026-10-06 08:35.
- `'yyyy-MM-dd HH:mm:ss'` bounds accepted by `change_event`: yes.
- `customer_user_access`: ADMIN `ondra@anela.cz` (Anela-owned), ADMIN a second user on a personal gmail address, STANDARD agency user (gmail), STANDARD a second gmail user, READ_ONLY a third gmail user. Anela-owned ADMIN present: yes.
- `search_term_view` + `segments.search_term_match_type` works: yes; match types seen {EXACT: 7, PHRASE: 13, NEAR_PHRASE: 30} (first 50 rows). NEAR_PHRASE dominates, so merging `NEAR_*` into the base match type is essential.
- Entity counts (all statuses): campaigns 19 (9 enabled), ad groups 60, keywords 223, ad-group negatives 373, campaign negatives 567, ads 66.
- Key Vault `GoogleAds*` secrets at spike time: none in `kv-heblo-stg`, none in `kv-heblo-prod`; no App Settings on `heblo` / `heblo-test`. Set on 2026-10-07 after the spike (see §2).
- Quirks seen:
  - anela.cz Workspace passkey loop on the `adwords` consent (see §2).
  - A live read-only query on 2026-10-07 selecting `*_criterion.status` returned ENABLED for all 567 campaign and 373 ad-group negatives; the read source selects status.
  - `change_event` timestamps come back with microseconds (`2026-09-21 14:11:00.459519`).

## 11. Why `ImportedMarketingTransactions` is empty

1. **No credentials until 2026-10-07.** No `GoogleAds*` secret existed in `kv-heblo-stg`, `kv-heblo-prod` or App Settings, so the billing job could never authenticate. Four of its settings plus `HebloUserEmail` (`GoogleAds--CustomerId`, `--OAuth2ClientId`, `--OAuth2ClientSecret`, `--OAuth2RefreshToken`, `--HebloUserEmail`) were set in both vaults on 2026-10-07; **`GoogleAds--DeveloperToken` is still absent**, so `GoogleAdsSettings.DeveloperToken` is empty. What the code shows: `GoogleAdsInvoiceImportJob` returns immediately while disabled (it is disabled in prod); once enabled it has no "is configured" gate, builds the SDK client with the (empty) `DeveloperToken` and calls `Services.V18.GoogleAdsService.SearchStream`, and any exception is logged and rethrown (the run fails). How the V18 call behaves with these secrets and an empty developer token is **unverified**.
2. The job `google-ads-invoice-import` is `IsEnabled = false` in prod.
3. `SdkAccountBudgetFetcher` calls API V18, which Google has sunset, so it would fail even with credentials.
4. `account_budget` returns rows only for monthly-invoicing accounts and only for budgets approved in the last 7 days.

Points 2–4 come from the plan and were not re-verified by the spike. The billing importer is left untouched; spend comes from the MarketingAds backbone.

## 12. Manual smoke procedure (executor)

Added by the executor PR.
