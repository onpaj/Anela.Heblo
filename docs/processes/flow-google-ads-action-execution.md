---
process: flow-google-ads-action-execution
kind: workflow
module: marketing-ads
summary: Executes approved Google Ads proposal actions - add a negative keyword, pause an ad, and revert either - through the Google Ads REST API v25, validate-only outside production.
owns:
  - backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Execution/**
verified_at: "1cbe67a05"
related: [sync-google-ads-campaign-data, module-marketing-ads]
---

# Google Ads action execution

## Purpose
The "hands" for Google Ads: once a marketing proposal is approved, Heblo - not the agent - makes the change in the account, records the before-state and can revert it.

## Trigger
Called by the core proposal execution pipeline per action after final approval, and by the admin-only revert. Nothing calls it until that pipeline is merged and an admin turns execution on (kill switch, spec 6.6). No Hangfire job of its own.

## Data flow
1. `ReadCurrentAsync` runs a GAQL query on the target (`executor_*` queries, integration doc section 4) and returns an `AdTargetState`: `Absent|Present` for negatives, `Enabled|Paused|Removed|Unknown` for ads. The core compares it to the action's old value; the executor does not.
2. `ExecuteAsync` sends `campaignCriteria:mutate` / `adGroupCriteria:mutate` (create a negative with `negative: true`) or `adGroupAds:mutate` (`status: PAUSED`, `updateMask: status`).
3. `RevertAsync` removes the criterion named by the stored `PlatformResourceId`, or sets the ad back to `ENABLED`.
4. Every `ExecuteAsync` and `RevertAsync` call is logged (operation, action type, target, outcome, validateOnly, error); `ReadCurrentAsync` is not.

## Logic & formulas
- Guard first: the action must be for `GoogleAds` and its account must equal the configured customer id. A guard failure gives `Failed` from `ExecuteAsync`/`RevertAsync` and `Missing` (`Exists=false`) from `ReadCurrentAsync`.
- Negative keyword presence: same match type and same text, case-insensitive, whitespace collapsed; compared in C#, never in GAQL. Keyword text is never interpolated into a query; ids are checked numeric.
- Text 1-80 characters, at most 10 words; match type must be a `KeywordMatchType` name; target level must be Campaign or AdGroup. Ad target is `{adGroupId}~{adId}` at level Ad.
- Revert of a negative keyword needs a `Succeeded` original whose `PlatformResourceId` is `customers/{cid}/{campaignCriteria|adGroupCriteria}/{parentId}~{criterionId}` for this action's own account, level and parent; otherwise `Failed` and nothing is sent. Revert of a pause needs a `Succeeded` original.
- Platform rejections (HTTP 400/404/409) return `Failed` with Google's error code; auth and transport failures throw.
- Retries: only HTTP 429. HTTP 401 drops the cached access token and resends once (nothing was processed); a second 401 throws. 5xx and timeouts are not retried - the change may already be applied (the change sync will show it).
- `GoogleAds:ValidateOnly = true` sends the request with `validateOnly` and returns `Failed` with an error starting `ValidateOnly:` (the contract has no "validated" outcome).

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `GoogleAds:ValidateOnly` | `true` (`false` only in `appsettings.Production.json`) | dry-run every mutate; ignored (always on) outside the Production environment |
| other `GoogleAds:*` | see `sync-google-ads-campaign-data` | shared with the read source |

## Runtime facts
- No live write has been made yet. Record the first production execution here.
- Validate-only smoke test (integration doc §12) passed against the live account on 2026-10-14: Google accepted PauseAd and a campaign-level AddNegativeKeyword; `change_event` recorded nothing that day.

## Known quirks
- Staging and production point at the same live Google Ads account; that is why only production may write.
- Outside the `Production` host environment the executor is validate-only regardless of `GoogleAds:ValidateOnly`, so an App Setting `GoogleAds__ValidateOnly=false` or Key Vault secret `GoogleAds--ValidateOnly` on staging cannot make it write. `ConfigurationFileTests` additionally guards the `appsettings*.json` files.
- A 200 whose body names no criterion (empty or unreadable) is reported as Failed for a negative keyword, although Google may have added it; check the account by hand. A 404 counts as a platform rejection only when it carries a GoogleAdsFailure, otherwise it is thrown as a transport/config error.
- The executor trusts the core for the old-value check and the limits; it validates only shape, account and ids.
- On staging every executed action ends `Failed` with an error starting `ValidateOnly:` (`AdExecutionErrors.ValidateOnlyPrefix`, shared with the core): Google accepted it, nothing changed. The core keeps it `Failed`, never retries it, flags `validateOnly` in the `ActionFailed` audit payload and `ValidateOnly` on the execution DTO; the proposal therefore ends `Failed` on staging.
- A thrown `ExecuteAsync` (timeout, 5xx) may still have changed the account. The core never retries it: it re-reads the target once. If the re-read shows the new value the action is recorded `Succeeded` ("Applied — confirmed by re-read after transport error", re-read JSON as the response, no `PlatformResourceId`); otherwise it stays `Failed` with the observed value as `AfterValue` and the note "may have been applied; verify in the account". Either way the run stops there.
- `AddGoogleAdsMarketingAds` also runs in the AdsBackfill tool, which therefore registers the executor too; it is scoped and never resolved there.
- A manual validate-only smoke test exists (integration doc section 12); it is skipped unless `HEBLO_GOOGLE_ADS_SMOKE_ENV` is set.

## Code entry points
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Execution/GoogleAdsActionExecutor.cs` - dispatch, validate-only, logging
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Execution/GoogleAdsNegativeKeywordHandler.cs` - negative keywords
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Execution/GoogleAdsPauseAdHandler.cs` - ad pause
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Api/GoogleAdsRestClient.cs` - mutate transport and retry rules
- `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Execution/GoogleAdsValidateOnlySmokeTests.cs` - manual live check
