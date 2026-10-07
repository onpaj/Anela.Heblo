# Marketing Agents Platform — WS2 Meta Ads Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove Heblo can read Anela's Meta ad account, then ship `MetaAdsReadSource` (entities, daily facts, change history) and `MetaAdsActionExecutor` (`PauseAd` only) against the shared `MarketingAds` contracts from core PR C1.

**Architecture:** One thin transport, `MetaGraphClient` (bearer auth, versioned URLs, safe paging, error classification, throttling backoff, path-only logging), is shared by a read source and an executor in the existing `Anela.Heblo.Adapters.MetaAds` project. Pure mappers convert Graph JSON to the C1 contract records. Both services are registered only when `AdSettingsGuard.IsConfigured(...)` accepts the Meta settings. The billing importer (`MetaAdsTransactionSource`, `MetaAdsInvoiceImportJob`) stays as it is.

**Tech Stack:** .NET 8, `System.Text.Json`, `IHttpClientFactory` typed client, xUnit + FluentAssertions, Meta Marketing API (Graph) over plain HTTPS, bash + curl + jq for the spike.

**Spec:** `docs/superpowers/specs/2026-10-07-marketing-agents-platform-design.md` (sections 4.2, 4.3, 9, 12 are binding; section 12 names are canonical). Context: `docs/handoff/marketing-agents-platform.md`.

## Global Constraints

- Module and contracts namespace: `Anela.Heblo.Application.Features.MarketingAds.Contracts` (spec 12.1). Implement the contract types exactly as they are on `origin/main`.
- Platform classes: `MetaAdsReadSource`, `MetaAdsActionExecutor` in `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds`. Test project: `backend/test/Anela.Heblo.Adapters.MetaAds.Tests`. Integration doc: `docs/integrations/meta-ads-api.md`.
- `AdPlatform.MetaAds`. A Meta ad set is an `AdEntityLevel.AdGroup`.
- Capabilities: `SearchTerms = false`, `ChangeLog = true`. Unsupported capabilities return an empty list and never throw.
- `PauseAd`: `TargetLevel = Ad`, `OldValue = "Enabled"`, `NewValue = "Paused"`, empty payload. Revert sets the ad back to `Enabled`. Executors **do not** compare old values themselves; the core does.
- `ExecuteAsync` returns `Failed` on a platform-side rejection and throws only on transport or auth failures.
- Register sources and executors only when the settings are **really** configured: `AdSettingsGuard.IsConfigured(accessToken, accountId)`. Registration and startup never throw on bad config.
- Secrets live only in Key Vault (`--` separator): `MetaAds--AccessToken`, `MetaAds--AccountId`. The agent never writes a prod or staging secret. Ondrej does that.
- Never log, print or persist the access token. It is never sent in a query string, and log lines carry the URL path only, because Meta puts `access_token` into `paging.next`.
- Retry only transient failures (429, HTTP 5xx, `is_transient: true`, throttling codes 4/17/613/80000/80004, transport errors, timeouts). Never retry another 4xx.
- `TaskCanceledException` is an `OperationCanceledException`. A timeout is told apart from caller cancellation with `ct.IsCancellationRequested`.
- Tests that check HTTP bodies and queries parse them (`HttpUtility.ParseQueryString`, `JsonDocument`). They never substring-match.
- Build first, then `dotnet test --no-build -p:UseSharedCompilation=false`. Run `dotnet format` before every PR.
- Leave the billing importer alone: no edits to `MetaAdsTransactionSource.cs` or `MetaAdsInvoiceImportJob.cs`.
- Conventional commits. Every commit message ends with a blank line, then `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. PR bodies end with `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.
- No live write to Meta is ever run by the executing agent, and that includes the smoke procedure.

## Review Focus

1. **A token leaking through `paging.next`.** Meta's next-page URL embeds `access_token=…`. The client must remove it, send the bearer header instead, and refuse a next URL whose host is not `graph.facebook.com`. Tests in Task 3.
2. **A throttle with a long regain time.** `x-business-use-case-usage` can say "regain access in 30 min". The job must fail fast rather than sleep inside Hangfire, and must stay retryable later. Test in Task 3.
3. **Server culture is `cs-CZ`.** Graph returns `"spend":"1834.56"`, and parsing it with a Czech culture reads it as 183456. All number parsing must be invariant. Test in Task 5.
4. **Insight rows dated outside the requested day.** Rows whose `date_start` differs from the requested date must be dropped, not stored under the wrong date. Test in Task 5.
5. **A target ad in someone else's account.** The ad id comes from an agent's proposal, which is a prompt-injection surface. The token may also see agency-owned accounts. The executor must refuse any ad whose `account_id` is not the configured account, and any action naming another account. Tests in Task 9.

## Spec deviations

| # | Spec / brief says | Plan does | Why |
|---|---|---|---|
| 1 | Repo default `MetaAds:ApiVersion = v21.0` | Default bumped to the newest Marketing API version the spike proves works (the plan's code assumes `v25.0`) | Marketing API v21.0 expired on 2025-09-09 (Meta versions page, checked 2026-10-07); v24.0 expires 2026-10-06. This changes only the billing importer's *config default*. Its code is untouched, and it is disabled and unconfigured anyway. |
| 2 | Back off on 429/#17/#613 only | Also retries #4 (app limit), #80000/#80004 (business-use-case throttling for insights and ads management), HTTP 5xx and `is_transient: true` | These are Meta's documented transient and throttling signals. Every other 4xx is still never retried. |
| 3 | "ads with effective_status mapping" | `AdEntitySnapshot.Status` comes from the object's **own** `status`. `effective_status` goes into `Attributes["effective_status"]`. | `PauseAd` writes `status`. Reading the same field keeps ReadCurrent → Execute → Revert symmetric. An ACTIVE ad under a paused campaign is still "Enabled" as far as pausing it goes. |
| 4 | "clicks" | `Clicks = inline_link_clicks` (Meta "Link clicks") | This is comparable with Google and Sklik ad clicks. Meta's `clicks` counts every click, including likes and profile clicks. |
| 5 | Executor PR depends on C1 only | Executor PR also depends on the read-source PR being merged | Both share `MetaGraphClient`. Branching the executor from `main` without it would duplicate the transport. |
| 6 | `AdChangeEventRow.ExternalEventId` | Built from a SHA-256 of `event_time|event_type|object_id|actor_id|extra_data` (first 32 hex chars) | `/activities` rows have no id. |
| 7 | — | No async insights jobs | The contract asks for one day at a time. Three synchronous calls per day (campaign, adset, ad levels, `limit=500`, paged) are small for Anela's account. If Meta answers error code 1 "reduce the amount of data", the run fails loudly and the integration doc records it. |
| 8 | Process doc "for the platform stream" | New `docs/processes/sync-ads-meta.md`. The `owns` glob of `sync-ad-platform-transactions.md` narrows from `Adapters.MetaAds/**` to the billing files plus the shared settings and registration files. | Otherwise every Meta read-side change would flag the billing doc. |

---

## Before you start (every part)

You are a fresh agent in a fresh worktree created from `main`. You have no other context.

1. Read this plan to the end. Then read the spec sections 4.2, 4.3, 9 and 12, `CLAUDE.md`, `docs/architecture/development_guidelines.md` (ADR-007 and ADR-008 if present), `docs/architecture/testing-strategy.md`, and `docs/processes/_TEMPLATE.md`.
2. Read the existing adapter: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/*.cs` and `backend/test/Anela.Heblo.Tests/Adapters/MetaAds/*.cs`.
3. **Part A needs no code from core.** Parts B and C need core PR C1. Verify it:

   ```bash
   git fetch origin
   git cat-file -e origin/main:backend/src/Anela.Heblo.Application/Features/MarketingAds/Contracts/IAdPlatformReadSource.cs && echo "C1 present" || echo "C1 MISSING"
   git ls-tree -r --name-only origin/main backend/src/Anela.Heblo.Application/Features/MarketingAds/Contracts backend/test/Anela.Heblo.MarketingAds.TestKit
   ```

   Expected: `C1 present`, plus a file list with `AdSettingsGuard.cs`, `IAdActionExecutor.cs`, `AdPlatformReadSourceContractTests.cs` and `AdActionExecutorContractTests.cs`. If it prints `C1 MISSING`, **stop**. Report to Ondrej: "WS2 Part B/C blocked: core PR C1 (`IAdPlatformReadSource.cs`) is not on origin/main." Do not write any code.
4. Open every file in those two folders. If a signature differs from spec section 12.2/12.3 (record parameter names, abstract members of the contract-test bases, `AdSettingsGuard.IsConfigured` signature), **the code on main wins**. Adapt this plan's code to it and note each adaptation in the PR body.
5. Read `backend/test/Anela.Heblo.MarketingAds.TestKit/*.csproj` and copy its `xunit`, `FluentAssertions` and `Microsoft.NET.Test.Sdk` versions into the new test project in Task 3, so the versions do not clash.
6. Part B also needs `~/Work/heblo-marketing-agents/meta-spike-findings.md` from Part A. If that file is missing, run Part A first.

## File map

| File | Responsibility | Part |
|---|---|---|
| `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsSettings.cs` (modify) | default `ApiVersion` bump | B |
| `.../MetaAds/Graph/MetaGraphClient.cs` | HTTP transport: auth, URL, paging, retry, redaction | B |
| `.../MetaAds/Graph/MetaGraphException.cs` | classified Graph failure | B |
| `.../MetaAds/Graph/MetaGraphError.cs` | parse the `{"error":{…}}` body | B |
| `.../MetaAds/Graph/MetaUsageHeader.cs` | parse `x-business-use-case-usage` | B |
| `.../MetaAds/Graph/MetaGraphDtos.cs` | Graph JSON DTOs (page, account, campaign, ad set, ad, insight row, activity, me) | B |
| `.../MetaAds/Graph/MetaGraphTime.cs` | parse `2026-10-06T08:15:00+0000` | B |
| `.../MetaAds/Mapping/MetaStatusMapper.cs` | Meta status ↔ `AdEntityStatus` / `AdActionValues` | B |
| `.../MetaAds/Mapping/MetaEntityMapper.cs` | DTO → `AdEntitySnapshot` | B |
| `.../MetaAds/Mapping/MetaConversionSelector.cs` | picks one purchase action type | B |
| `.../MetaAds/Mapping/MetaInsightMapper.cs` | insight row → `AdDailyFactRow` | B |
| `.../MetaAds/Mapping/MetaActivityMapper.cs` | activity → `AdChangeEventRow` | B |
| `.../MetaAds/MetaAdsReadSource.cs` | `IAdPlatformReadSource` | B |
| `.../MetaAds/MetaAdsActionExecutor.cs` | `IAdActionExecutor` (`PauseAd`) | C |
| `.../MetaAds/MetaAdsAdapterServiceCollectionExtensions.cs` (modify) | gated registration | B, C |
| `backend/src/Anela.Heblo.API/appsettings.json` (modify) | `MetaAds:ApiVersion` | B |
| `backend/test/Anela.Heblo.Adapters.MetaAds.Tests/**` | tests, fixtures, fake handlers | B, C |
| `Anela.Heblo.sln` (modify) | add the test project | B |
| `docs/integrations/meta-ads-api.md` | spike findings and API facts | B, C |
| `docs/processes/sync-ads-meta.md`, `docs/processes/sync-ad-platform-transactions.md` (modify), `docs/processes/INDEX.md` (generated) | process docs | B, C |

---

# Part A — Access spike (throwaway, read-only, no PR)

### Task 1: Meta access spike

**Files:**
- Create (scratchpad only, never in the repo): `$SCRATCH/meta-spike/meta-spike.sh`. `$SCRATCH` is your session scratchpad directory.
- Create (outside the repo, Ondrej's folder): `~/Work/heblo-marketing-agents/meta-spike-findings.md`

**Interfaces:**
- Consumes: `~/Work/heblo-marketing-agents/meta-ads.env`, written by Ondrej, with exactly these variables:
  - `META_ADS_ACCOUNT_ID` — ad account id **with** prefix, e.g. `act_1234567890`
  - `META_ADS_ACCESS_TOKEN` — system-user access token from **Anela's** Business Manager
  - `META_ADS_API_VERSION` — optional; the spike falls back to `v25.0`
- Produces: `meta-spike-findings.md`. Task 2 copies it into `docs/integrations/meta-ads-api.md`, and Tasks 3–10 rely on its decisions (API version, purchase action types, activity `object_type` naming, scopes).

- [ ] **Step 1: Check the env file without printing values**

```bash
test -f ~/Work/heblo-marketing-agents/meta-ads.env && grep -oE '^[A-Z_]+=' ~/Work/heblo-marketing-agents/meta-ads.env
```

Expected: `META_ADS_ACCOUNT_ID=` and `META_ADS_ACCESS_TOKEN=`, plus optionally `META_ADS_API_VERSION=`. If the file or either required name is missing, **stop**. Ask Ondrej to create the file with those exact names: the system-user token comes from Business Settings → Users → System users → Generate token, for the ad account, with `ads_read`, plus `ads_management` for Part C. Never ask him to paste the token into chat.

- [ ] **Step 2: Write the spike script**

```bash
mkdir -p "$SCRATCH/meta-spike"
cat > "$SCRATCH/meta-spike/meta-spike.sh" <<'SCRIPT'
#!/usr/bin/env bash
# Read-only Meta Marketing API spike. NEVER prints the token; redacts it from saved bodies.
set -euo pipefail
ENV_FILE="$HOME/Work/heblo-marketing-agents/meta-ads.env"
OUT="${SCRATCH:?set SCRATCH}/meta-spike/out"
mkdir -p "$OUT"
set -a; source "$ENV_FILE"; set +a
: "${META_ADS_ACCOUNT_ID:?missing}" "${META_ADS_ACCESS_TOKEN:?missing}"
V="${META_ADS_API_VERSION:-v25.0}"
ACT="$META_ADS_ACCOUNT_ID"
G="https://graph.facebook.com"

call() { # call <name> <version> <path> [curl -G data args...]
  local name=$1 ver=$2 path=$3; shift 3
  local code
  code=$(curl -sS -G "$G/$ver/$path" -H "Authorization: Bearer $META_ADS_ACCESS_TOKEN" "$@" \
           -o "$OUT/$name.json" -D "$OUT/$name.headers" -w '%{http_code}')
  sed -i '' -E 's/access_token=[^&"\\]+/access_token=REDACTED/g' "$OUT/$name.json"
  echo "== $name HTTP $code"
}

call me "$V" me --data-urlencode "fields=id,name"
call debug_token "$V" debug_token --data-urlencode "input_token=$META_ADS_ACCESS_TOKEN"
call permissions "$V" me/permissions
call account "$V" "$ACT" --data-urlencode "fields=id,name,currency,timezone_name,account_status,business{id,name},owner"
call insights_yesterday "$V" "$ACT/insights" \
  --data-urlencode "level=campaign" --data-urlencode "date_preset=yesterday" --data-urlencode "time_increment=1" \
  --data-urlencode "use_unified_attribution_setting=true" \
  --data-urlencode "fields=campaign_id,campaign_name,spend,impressions,clicks,inline_link_clicks,actions,action_values,account_currency,date_start,date_stop"
call campaigns "$V" "$ACT/campaigns" --data-urlencode "fields=id,name,status,effective_status,objective,daily_budget,lifetime_budget,bid_strategy" --data-urlencode "limit=500" --data-urlencode "summary=true"
call adsets "$V" "$ACT/adsets" --data-urlencode "fields=id,name,status,effective_status,campaign_id,daily_budget,lifetime_budget,optimization_goal,billing_event,bid_strategy" --data-urlencode "limit=500" --data-urlencode "summary=true"
call ads "$V" "$ACT/ads" --data-urlencode "fields=id,name,status,effective_status,adset_id,campaign_id,creative{id}" --data-urlencode "limit=500" --data-urlencode "summary=true"
AF="event_time,event_type,translated_event_type,actor_id,actor_name,object_id,object_name,object_type,extra_data,application_name"
call activities_30d "$V" "$ACT/activities" --data-urlencode "fields=$AF" --data-urlencode "since=$(date -v-30d +%s)" --data-urlencode "limit=500"
for w in "60 30" "120 90" "395 365"; do
  set -- $w
  call "activities_${1}_${2}d" "$V" "$ACT/activities" --data-urlencode "fields=$AF" \
    --data-urlencode "since=$(date -v-${1}d +%s)" --data-urlencode "until=$(date -v-${2}d +%s)" --data-urlencode "limit=5"
done
for ver in v21.0 v24.0 v25.0 v26.0 v27.0; do
  call "version_$ver" "$ver" "$ACT" --data-urlencode "fields=name"
done
FIRST_AD=$(jq -r '.data[0].id // empty' "$OUT/ads.json")
if [ -n "$FIRST_AD" ]; then
  call ad_single "$V" "$FIRST_AD" --data-urlencode "fields=id,status,effective_status,account_id"
fi
echo "done; outputs in $OUT"
SCRIPT
chmod +x "$SCRATCH/meta-spike/meta-spike.sh"
```

- [ ] **Step 3: Run it (read-only GETs only)**

Run: `SCRATCH="$SCRATCH" "$SCRATCH/meta-spike/meta-spike.sh"`
Expected: one `== <name> HTTP <code>` line per call and no token anywhere in the output. Then confirm nothing leaked to disk:

```bash
grep -rlF "$(grep -E '^META_ADS_ACCESS_TOKEN=' ~/Work/heblo-marketing-agents/meta-ads.env | cut -d= -f2-)" "$SCRATCH/meta-spike/out" && echo "LEAK" || echo "no token on disk"
```

Expected: `no token on disk`. If it prints `LEAK`, delete `$SCRATCH/meta-spike/out` and fix the redaction before going on.

- [ ] **Step 4: Summarise the results (prints no secrets)**

```bash
O="$SCRATCH/meta-spike/out"
jq '{id,name}' "$O/me.json"
jq '.data | {type, app_id, application, is_valid, expires_at, data_access_expires_at, scopes, granular_scopes}' "$O/debug_token.json"
jq '[.data[] | select(.status=="granted") | .permission]' "$O/permissions.json"
jq '.' "$O/account.json"
jq '[.data[] | {campaign_id, spend, impressions, clicks, inline_link_clicks, account_currency, date_start,
     purchase_actions: [.actions[]? | select(.action_type|test("purchase"))],
     purchase_values:  [.action_values[]? | select(.action_type|test("purchase"))]}]' "$O/insights_yesterday.json"
grep -iE '^(x-business-use-case-usage|x-ad-account-usage|x-fb-ads-insights-throttle|x-app-usage)' "$O/insights_yesterday.headers" || echo "no usage headers"
for e in campaigns adsets ads; do jq --arg e $e '{entity:$e, total:.summary.total_count, returned:(.data|length), has_next:(.paging.next!=null), by_status:([.data[].effective_status]|group_by(.)|map({(.[0]):length})|add)}' "$O/$e.json"; done
jq '{count:(.data|length), event_types:([.data[].event_type]|unique), object_types:([.data[].object_type]|unique), sample:(.data[:3])}' "$O/activities_30d.json"
for f in "$O"/activities_*_*d.json; do echo "$(basename $f): $(jq '.data|length' $f) rows, error: $(jq -r '.error.message // "none"' $f)"; done
jq -n --slurpfile c "$O/campaigns.json" --slurpfile s "$O/adsets.json" --slurpfile a "$O/ads.json" --slurpfile x "$O/activities_30d.json" '
  ($c[0].data|map(.id)) as $C | ($s[0].data|map(.id)) as $S | ($a[0].data|map(.id)) as $A |
  [$x[0].data[] | {object_type, kind: (if (.object_id|IN($C[])) then "campaign" elif (.object_id|IN($S[])) then "adset" elif (.object_id|IN($A[])) then "ad" else "unknown" end)}] | unique'
for ver in v21.0 v24.0 v25.0 v26.0 v27.0; do echo "$ver: $(jq -r 'if .error then "ERROR \(.error.code): \(.error.message)" else "ok" end' "$O/version_$ver.json")"; done
test -f "$O/ad_single.json" && jq '.' "$O/ad_single.json"
```

- [ ] **Step 5: Check why `ImportedMarketingTransactions` is empty (names only, never values)**

```bash
for kv in kv-heblo-prod kv-heblo-stg; do echo "$kv:"; az keyvault secret list --vault-name "$kv" --query "[?starts_with(name,'MetaAds') || starts_with(name,'GoogleAds')].name" -o tsv; done
for app in heblo heblo-test; do echo "$app:"; az webapp config appsettings list -g rgHeblo -n "$app" --query "[?starts_with(name,'MetaAds') || starts_with(name,'GoogleAds')].name" -o tsv; done
```

Expected (current hypothesis): empty lists everywhere. If `az` says a web app is not in `rgHeblo`, find it with `az webapp list --query "[].{n:name,g:resourceGroup}" -o table`. If `az` is not logged in, write "not verified (az not logged in)" in the findings. Do not run `az login` interactively without asking Ondrej.

- [ ] **Step 6: Apply the gate**

Every check below must pass. If any fails, **stop**. Write the findings file anyway, with a `## Gate: FAILED` section saying exactly what is missing, and report that to Ondrej. Do not start Part B.

| Check | Pass condition |
|---|---|
| Token valid | `debug_token.data.is_valid == true` |
| System user of Anela's BM | `debug_token.data.type` is `SYSTEM_USER` (`USER` means a personal token, which expires: ask for a system-user token). `account.business.name` / `account.owner` is Anela's Business Manager; Ondrej confirms the name. If the owner is the agency's BM, stop. |
| `ads_read` | `ads_read` is in `scopes` / granted permissions |
| Account readable | `account.json` has no `error`, `currency` is set, `account_status == 1` (any other value is recorded, not a failure) |
| Insights | `insights_yesterday` HTTP 200 (an empty `data` on a no-spend day is a pass) |
| Activities | `activities_30d` HTTP 200 |
| Version | at least one of v25.0 / v26.0 / v27.0 answers `ok` |

`ads_management` missing is **not** a gate failure for Part B. Record it: Part C needs it before its smoke test.

- [ ] **Step 7: Write `~/Work/heblo-marketing-agents/meta-spike-findings.md`**

Use exactly these headings, filled with the observed values (no token, no full API responses):

```markdown
# Meta Ads access spike — findings (YYYY-MM-DD)
## Gate: PASSED | FAILED (<what is missing>)
## Identity
- /me: id <id>, name <name>; debug_token type <type>, app <application> (<app_id>), expires_at <0=never|date>
- Scopes: <list>; ads_read <yes/no>; ads_management <yes/no>
## Ad account
- <act_…>, name <…>, currency <…>, timezone <…>, account_status <n>, business <id/name>, owner <id>
## API version
- v21.0: <result>; v24.0: <result>; v25.0: <result>; v26.0: <result>; v27.0: <result>
- Chosen ApiVersion: <newest ok version>
## Insights (yesterday, level=campaign)
- rows <n>; spend string format e.g. "1834.56"; currency <…>
- Purchase action types present: <list>; omni_purchase present <yes/no>; values equal across types <yes/no>
- Usage headers seen: <header names + one sample with numbers only>
## Entities
- campaigns <total>, adsets <total>, ads <total>; paging needed at limit=500 <yes/no>; statuses seen <…>
## Activities (/act_…/activities)
- 30 d: <n> rows; event types <…>; object_type values <…>
- object_type → actual kind (matched by id): <e.g. CAMPAIGN_GROUP→campaign, CAMPAIGN→adset, ADGROUP→ad>
- extra_data sample shape (values anonymised): <…>
- Retention: 30–60 d <n>, 90–120 d <n>, 365–395 d <n>
- actor_id for Heblo's system user equals /me id <yes/no/not observed>
## Single ad read
- fields returned: id, status, effective_status, account_id (<digits only|with act_>)
## Why ImportedMarketingTransactions is empty
- Key Vault kv-heblo-prod: <MetaAds*/GoogleAds* secret names or "none">; kv-heblo-stg: <…>
- App Settings heblo: <…>; heblo-test: <…>
- Conclusion: <e.g. never configured: no credentials anywhere; jobs also disabled in prod; and Marketing API v21.0 expired 2025-09-09, so the call would fail even with credentials>
```

- [ ] **Step 8: Report to Ondrej**

Send a short message: gate result; chosen API version; whether `ads_management` is granted; the `object_type` naming; the purchase action types; the root cause of the empty table. If the gate passed, add: "Part B can start once core PR C1 is on main."

No commit. Nothing goes into the repo in Part A.

---

# Part B — Read source PR

Branch: `feat/marketing-meta-read-source` from `origin/main` (C1 merged).

```bash
git fetch origin && git switch -c feat/marketing-meta-read-source origin/main
```

### Task 2: Integration doc from the spike and billing root cause

**Files:**
- Create: `docs/integrations/meta-ads-api.md`
- Modify: `docs/processes/sync-ad-platform-transactions.md` (front matter `owns`, `verified_at`, *Runtime facts*, *Known quirks*)

**Interfaces:**
- Consumes: `~/Work/heblo-marketing-agents/meta-spike-findings.md` (Task 1)
- Produces: the source of truth that Tasks 3–10 cite: API version, action-type choice, `object_type` mapping, rate-limit behaviour

- [ ] **Step 1: Write `docs/integrations/meta-ads-api.md`**

Write this structure. Sections 2, 3 and the spike-dated facts in sections 6 and 7 come from the findings file, so copy the observed values in. The rest is fixed design.

```markdown
# Meta Marketing API — Integration Findings

> **Living document.** Every new finding about the Meta Marketing (Graph) API MUST be added here before code relies on it.
> Reference: https://developers.facebook.com/docs/marketing-api/ · Versions: https://developers.facebook.com/docs/graph-api/changelog/versions

## 1. Overview
Adapter `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds`:
- `MetaAdsTransactionSource` / `MetaAdsInvoiceImportJob`: legacy billing import (`/transactions`), see `docs/processes/sync-ad-platform-transactions.md`.
- `MetaAdsReadSource`: entities, daily insights, change history for the marketing-ads backbone (`docs/processes/sync-ads-meta.md`).
- `MetaAdsActionExecutor`: `PauseAd` only (Part C).
All three share `MetaGraphClient`, except the billing source, which keeps its own HttpClient code.

## 2. Access (verified <spike date>)
<identity, token type, Business Manager, scopes, account id/currency/timezone/status from the findings>
- Secrets: Key Vault `MetaAds--AccessToken`, `MetaAds--AccountId` (`act_…` form). `MetaAds:ApiVersion` lives in appsettings.json.
- `ads_read` is required by the read source; `ads_management` by the executor.

## 3. API version
<results per version from the findings>. Marketing API v21.0 expired 2025-09-09; v24.0 expired 2026-10-06. Heblo default: <chosen>. Meta retires a Marketing API version about a year after release, so bump `MetaAds:ApiVersion` every year, and check this page first.

## 4. Endpoints used
| Method | Path | Purpose | Key params |
|---|---|---|---|
| GET | `/{act}` | account snapshot | `fields=id,name,currency,timezone_name,account_status` |
| GET | `/{act}/campaigns` | campaigns | `fields=id,name,status,effective_status,objective,daily_budget,lifetime_budget,bid_strategy`, `limit=500` |
| GET | `/{act}/adsets` | ad sets → `AdGroup` | `fields=id,name,status,effective_status,campaign_id,daily_budget,lifetime_budget,optimization_goal,billing_event,bid_strategy` |
| GET | `/{act}/ads` | ads | `fields=id,name,status,effective_status,adset_id,campaign_id,creative{id}` |
| GET | `/{act}/insights` | daily facts | `level=campaign|adset|ad`, `time_range={"since":D,"until":D}`, `time_increment=1`, `use_unified_attribution_setting=true`, `fields=<id>,impressions,inline_link_clicks,spend,actions,action_values,account_currency,date_start`, `limit=500` |
| GET | `/{act}/activities` | change history | `fields=event_time,event_type,translated_event_type,actor_id,actor_name,object_id,object_name,object_type,extra_data,application_name`, `since=<unix s>`, `limit=100` |
| GET | `/me` | Heblo's own system-user id (actor matching) | `fields=id,name` |
| GET | `/{ad_id}` | executor ReadCurrent | `fields=id,status,effective_status,account_id` |
| POST | `/{ad_id}` | executor pause/resume | form `status=PAUSED|ACTIVE` → `{"success":true}` |

Auth: `Authorization: Bearer <token>` on every call. Never `access_token=` in a query string.

## 5. Mapping decisions
- Entity status = the object's own `status`: `ACTIVE`→Enabled, `PAUSED`→Paused, `DELETED`/`ARCHIVED`→Removed, anything else → Unknown. `effective_status` (e.g. `CAMPAIGN_PAUSED`, `DISAPPROVED`) is kept in `attributes.effective_status`.
- Budgets (`daily_budget`, `lifetime_budget`) are strings in the currency's **minor unit** (haléře), stored verbatim in attributes.
- Cost = `spend` (account currency, excl. VAT). Clicks = `inline_link_clicks`. Impressions = `impressions`. All arrive as strings and are parsed with the invariant culture.
- **Conversions:** exactly ONE purchase action type per row, the first present in this order: `omni_purchase` → `purchase` → `offsite_conversion.fb_pixel_purchase`. Conversion value comes from `action_values` of the **same** type. The types report the same purchases from different sources (omni = deduplicated across pixel, CAPI, app and offline, which is what Ads Manager's "Purchases" column shows), so summing them double- or triple-counts. <spike observation: which types appear for Anela>.
- Insight dates are in the account time zone (<tz>). Facts are stored per level, so queries must aggregate a single level.
- No async insights jobs: one day × three levels is small. If error code 1 / "Please reduce the amount of data" ever appears, switch that level to `POST /{act}/insights` async jobs.
- Activities have no id, so `ExternalEventId = sha256(event_time|event_type|object_id|actor_id|extra_data)[0..32]`.
- Activity `object_type` (legacy names, verified <date>): <findings mapping>. Code: `CAMPAIGN_GROUP`→Campaign, `CAMPAIGN`/`AD_SET`/`ADSET`→AdGroup, `ADGROUP`/`AD`→Ad, else none.
- Actor kind: `actor_id` == `/me` id → Heblo; any other `actor_id` → User; none → Unknown.
- `extra_data` is a JSON-encoded string. When it holds `old_value`/`new_value`, those become old/new; otherwise new = the whole `extra_data`.

## 6. Rate limiting and errors
- Headers: `x-business-use-case-usage` (`{"<business id>":[{"type":"ads_insights","call_count":n,"total_cputime":n,"total_time":n,"estimated_time_to_regain_access":<minutes>}]}`), `x-ad-account-usage`, `x-fb-ads-insights-throttle`. <spike sample, numbers only>.
- Retried (max 3 retries, 2 s × 2^n backoff, or the regain hint when it is longer): HTTP 429, HTTP 5xx, `is_transient:true`, codes 4, 17, 613, 80000, 80004, transport errors, timeouts. If the regain hint is over 60 s, Heblo fails fast and the next scheduled run retries.
- Never retried: any other 4xx, e.g. 100 (bad param; subcode 33 = object not found), 190 (invalid/expired token → auth), 10/200–299 (permission).
- `paging.next` carries `access_token`. Heblo strips it and refuses next links to any host but `graph.facebook.com`.

## 7. Change-history retention
<findings: rows per window>. `MetaAdsReadSource.Capabilities.ChangeLogMaxAge` = 30 days (conservative; raise only with evidence).

## 8. Why `ImportedMarketingTransactions` is empty
<findings conclusion, with the Key Vault / App Settings evidence and date>
```

- [ ] **Step 2: Narrow the billing doc's ownership and record the root cause**

In `docs/processes/sync-ad-platform-transactions.md`, replace the front-matter line `  - backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/**` with:

```yaml
  - backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsTransactionSource.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsInvoiceImportJob.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsAdapterServiceCollectionExtensions.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsSettings.cs
```

Set `verified_at:` to the quoted short SHA of `git rev-parse --short origin/main`. Append to *Runtime facts* one line per finding, in the doc's format "fact — source — date". For example (use the spike's real values):

```markdown
- No `MetaAds*` or `GoogleAds*` secret exists in kv-heblo-prod, kv-heblo-stg, or the App Settings of `heblo` / `heblo-test`: the importers were never configured — `az keyvault secret list` / `az webapp config appsettings list` (names only) — <spike date>.
- Meta Marketing API v21.0 (this importer's original `ApiVersion`) expired 2025-09-09; the default is now <chosen> (shared `MetaAdsSettings`) — Meta versions page + spike — <spike date>.
```

Append to *Known quirks*:

```markdown
- **Shared settings with the marketing-ads read source.** `MetaAdsSettings` (`MetaAds:*`) also feeds `MetaAdsReadSource` (`sync-ads-meta`). Configuring Meta for the ads backbone also arms this importer's credentials; its job stays disabled by the DB flag.
```

- [ ] **Step 3: Validate the docs**

Run: `python3 scripts/process-docs/check.py check`
Expected: no `ERROR` lines that mention `sync-ad-platform-transactions`. Pre-existing `WARN`/`INFO` lines are fine.

- [ ] **Step 4: Commit**

```bash
git add docs/integrations/meta-ads-api.md docs/processes/sync-ad-platform-transactions.md
git commit -m "docs: record Meta Ads access spike findings

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 3: Test project + `MetaGraphClient` transport

**Files:**
- Create: `backend/test/Anela.Heblo.Adapters.MetaAds.Tests/Anela.Heblo.Adapters.MetaAds.Tests.csproj`
- Create: `backend/test/Anela.Heblo.Adapters.MetaAds.Tests/Support/RoutingGraphHandler.cs`
- Create: `backend/test/Anela.Heblo.Adapters.MetaAds.Tests/Support/MetaTestHarness.cs`
- Create: `backend/test/Anela.Heblo.Adapters.MetaAds.Tests/Support/ListLogger.cs`
- Create: `backend/test/Anela.Heblo.Adapters.MetaAds.Tests/MetaGraphClientTests.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Graph/MetaGraphClient.cs`, `MetaGraphException.cs`, `MetaGraphError.cs`, `MetaUsageHeader.cs`, `MetaGraphDtos.cs` (page/paging DTOs only in this task)
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsSettings.cs` (default `ApiVersion`)
- Modify: `backend/src/Anela.Heblo.API/appsettings.json` (`MetaAds:ApiVersion`)
- Modify: `Anela.Heblo.sln`

**Interfaces:**
- Consumes: `MetaAdsSettings` (existing: `AccountId`, `AccessToken`, `ApiVersion`)
- Produces:
  - `public class MetaGraphClient(HttpClient, IOptions<MetaAdsSettings>, ILogger<MetaGraphClient>, Func<TimeSpan, CancellationToken, Task>? delay = null)`
    - `Task<string> GetRawAsync(string relativePath, IReadOnlyDictionary<string,string> query, CancellationToken ct)`
    - `Task<T> GetAsync<T>(string relativePath, IReadOnlyDictionary<string,string> query, CancellationToken ct)`
    - `Task<IReadOnlyList<T>> GetAllPagesAsync<T>(string relativePath, IReadOnlyDictionary<string,string> query, CancellationToken ct)`
    - `Task<string> PostFormAsync(string relativePath, IReadOnlyDictionary<string,string> form, CancellationToken ct)`
    - `static Uri? NextPageUri(string? next)`, `static TimeSpan ComputeDelay(int attempt, TimeSpan? retryAfter)`, constants `GraphHost`, `MaxRetries = 3`, `BaseDelay = 2 s`, `MaxDelay = 60 s`
  - `public sealed class MetaGraphException : Exception` with `StatusCode`, `ErrorCode`, `ErrorSubcode`, `IsTransient`, `RetryAfter`, `ResponseBody`, `IsAuthError`, `IsPermissionError`, `IsNotFound`
  - `MetaPage<T> { List<T> Data; MetaPaging? Paging }`, `MetaPaging { string? Next }`
  - Test support: `RoutingGraphHandler`, `RecordedRequest`, `MetaTestHarness`, `ListLogger<T>`

- [ ] **Step 1: Create the test project and add it to the solution**

`backend/test/Anela.Heblo.Adapters.MetaAds.Tests/Anela.Heblo.Adapters.MetaAds.Tests.csproj`. If the TestKit csproj pins different versions of xunit, FluentAssertions or Test.Sdk, use the TestKit's (Before you start, item 5).

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net8.0</TargetFramework>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <IsPackable>false</IsPackable>
        <IsTestProject>true</IsTestProject>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="coverlet.collector" Version="6.0.0" />
        <PackageReference Include="FluentAssertions" Version="6.12.0" />
        <PackageReference Include="Microsoft.Extensions.Configuration" Version="8.0.0" />
        <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
        <PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.0" />
        <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
        <PackageReference Include="xunit" Version="2.9.2" />
        <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    </ItemGroup>

    <ItemGroup>
        <Using Include="Xunit" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\..\src\Adapters\Anela.Heblo.Adapters.MetaAds\Anela.Heblo.Adapters.MetaAds.csproj" />
        <ProjectReference Include="..\Anela.Heblo.MarketingAds.TestKit\Anela.Heblo.MarketingAds.TestKit.csproj" />
    </ItemGroup>

    <ItemGroup>
        <Content Include="Fixtures\**\*.json">
            <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
        </Content>
    </ItemGroup>

</Project>
```

```bash
dotnet sln Anela.Heblo.sln add backend/test/Anela.Heblo.Adapters.MetaAds.Tests/Anela.Heblo.Adapters.MetaAds.Tests.csproj
grep -n "Anela.Heblo.Adapters.MetaAds.Tests" Anela.Heblo.sln
```

Expected: one `Project(...)` line. Its GUID must be nested under the existing `test` folder `{23FE24B3-CD9D-4576-A7C8-85D5B012F43D}` in `GlobalSection(NestedProjects)`. If `dotnet sln` created a second `backend`/`test` folder pair, edit the nested-project line to point at `{23FE24B3-CD9D-4576-A7C8-85D5B012F43D}`, and delete the duplicate folder entries it added.

- [ ] **Step 2: Write the test support classes**

`Support/RoutingGraphHandler.cs`:

```csharp
using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;

namespace Anela.Heblo.Adapters.MetaAds.Tests.Support;

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string Body)
{
    public NameValueCollection Query => HttpUtility.ParseQueryString(Uri.Query);
    public NameValueCollection Form => HttpUtility.ParseQueryString(Body);
}

/// <summary>Fake Graph API: routes by method + exact path (+ optional query predicate); first match wins.</summary>
internal sealed class RoutingGraphHandler : HttpMessageHandler
{
    private readonly List<Route> _routes = new();

    public List<RecordedRequest> Requests { get; } = new();

    public RoutingGraphHandler On(
        HttpMethod method,
        string relativePath,
        Func<RecordedRequest, HttpResponseMessage> respond,
        Func<NameValueCollection, bool>? when = null)
    {
        _routes.Add(new Route(method, $"/{MetaTestHarness.ApiVersion}/{relativePath}", when, respond));
        return this;
    }

    public RoutingGraphHandler OnSequence(HttpMethod method, string relativePath, params Func<HttpResponseMessage>[] responses)
    {
        var queue = new Queue<Func<HttpResponseMessage>>(responses);
        return On(method, relativePath, _ => queue.Count > 0
            ? queue.Dequeue()()
            : throw new InvalidOperationException($"No more queued responses for {relativePath}"));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body);
        Requests.Add(recorded);

        var route = _routes.FirstOrDefault(r =>
            r.Method == request.Method &&
            r.Path == request.RequestUri!.AbsolutePath &&
            (r.When?.Invoke(recorded.Query) ?? true));

        return route is null
            ? GraphError(HttpStatusCode.BadRequest, 100, 33, $"No fake route for {request.Method} {request.RequestUri!.AbsolutePath}")
            : route.Respond(recorded);
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK, IReadOnlyDictionary<string, string>? headers = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }

    public static HttpResponseMessage Fixture(string name) => Json(MetaTestHarness.Fixture(name));

    public static HttpResponseMessage GraphError(
        HttpStatusCode status, int code, int? subcode, string message,
        bool isTransient = false, IReadOnlyDictionary<string, string>? headers = null)
    {
        var error = new Dictionary<string, object?>
        {
            ["message"] = message,
            ["type"] = "OAuthException",
            ["code"] = code,
            ["error_subcode"] = subcode,
            ["is_transient"] = isTransient,
            ["fbtrace_id"] = "AbCdEf123",
        };
        return Json(JsonSerializer.Serialize(new { error }), status, headers);
    }

    private sealed record Route(
        HttpMethod Method, string Path, Func<NameValueCollection, bool>? When, Func<RecordedRequest, HttpResponseMessage> Respond);
}
```

`Support/MetaTestHarness.cs`:

```csharp
using Anela.Heblo.Adapters.MetaAds.Graph;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.MetaAds.Tests.Support;

internal static class MetaTestHarness
{
    public const string AccountId = "act_1234567890";
    public const string AccountDigits = "1234567890";
    public const string ApiVersion = "v25.0";
    public const string Token = "EAAB-test-token-never-real";
    public const string OwnActorId = "100000000000001";
    public static readonly DateOnly FixtureDate = new(2026, 10, 6);

    public static MetaAdsSettings Settings() => new()
    {
        AccountId = AccountId,
        AccessToken = Token,
        ApiVersion = ApiVersion,
    };

    public static MetaGraphClient CreateGraphClient(
        HttpMessageHandler handler, List<TimeSpan>? delays = null, ILogger<MetaGraphClient>? logger = null) =>
        new(new HttpClient(handler), Options.Create(Settings()), logger ?? NullLogger<MetaGraphClient>.Instance,
            (delay, _) =>
            {
                delays?.Add(delay);
                return Task.CompletedTask;
            });

    public static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
```

`Support/ListLogger.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.MetaAds.Tests.Support;

internal sealed class ListLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Messages.Add(formatter(state, exception) + (exception is null ? string.Empty : " | " + exception.Message));
}
```

- [ ] **Step 3: Write the failing transport tests**

`MetaGraphClientTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using Anela.Heblo.Adapters.MetaAds.Graph;
using Anela.Heblo.Adapters.MetaAds.Tests.Support;
using FluentAssertions;

namespace Anela.Heblo.Adapters.MetaAds.Tests;

public class MetaGraphClientTests
{
    private const string Account = MetaTestHarness.AccountId;

    private sealed class Item
    {
        public string Id { get; set; } = string.Empty;
    }

    private static Dictionary<string, string> Q(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);

    private static Func<HttpResponseMessage> Ok(string body) => () => RoutingGraphHandler.Json(body);

    private static Func<HttpResponseMessage> Err(HttpStatusCode status, int code, bool transient = false, IReadOnlyDictionary<string, string>? headers = null) =>
        () => RoutingGraphHandler.GraphError(status, code, null, $"error {code}", transient, headers);

    [Fact]
    public async Task sends_bearer_token_in_header_and_never_in_query()
    {
        var handler = new RoutingGraphHandler().OnSequence(HttpMethod.Get, Account, Ok("""{"id":"act_1234567890"}"""));
        var client = MetaTestHarness.CreateGraphClient(handler);

        await client.GetRawAsync(Account, Q(("fields", "id,name")), CancellationToken.None);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Authorization.Should().Be($"Bearer {MetaTestHarness.Token}");
        request.Query.AllKeys.Should().NotContain("access_token");
        request.Uri.Host.Should().Be(MetaGraphClient.GraphHost);
        request.Uri.AbsolutePath.Should().Be($"/{MetaTestHarness.ApiVersion}/{Account}");
    }

    [Fact]
    public async Task encodes_json_query_values_so_they_round_trip()
    {
        var handler = new RoutingGraphHandler().OnSequence(HttpMethod.Get, $"{Account}/insights", Ok("""{"data":[]}"""));
        var client = MetaTestHarness.CreateGraphClient(handler);
        var timeRange = """{"since":"2026-10-06","until":"2026-10-06"}""";

        await client.GetAllPagesAsync<Item>($"{Account}/insights", Q(("time_range", timeRange), ("fields", "creative{id}")), CancellationToken.None);

        var query = handler.Requests.Single().Query;
        using var parsed = JsonDocument.Parse(query["time_range"]!);
        parsed.RootElement.GetProperty("since").GetString().Should().Be("2026-10-06");
        query["fields"].Should().Be("creative{id}");
    }

    [Fact]
    public async Task follows_paging_next_without_the_embedded_access_token()
    {
        var next = $"https://graph.facebook.com/{MetaTestHarness.ApiVersion}/{Account}/ads?limit=2&access_token=LEAKED&after=c2";
        var handler = new RoutingGraphHandler()
            .On(HttpMethod.Get, $"{Account}/ads", _ => RoutingGraphHandler.Json("""{"data":[{"id":"2"}]}"""), q => q["after"] == "c2")
            .On(HttpMethod.Get, $"{Account}/ads", _ => RoutingGraphHandler.Json(
                JsonSerializer.Serialize(new { data = new[] { new { id = "1" } }, paging = new { next } })));
        var client = MetaTestHarness.CreateGraphClient(handler);

        var items = await client.GetAllPagesAsync<Item>($"{Account}/ads", Q(("limit", "2")), CancellationToken.None);

        items.Select(i => i.Id).Should().Equal("1", "2");
        handler.Requests.Should().HaveCount(2);
        handler.Requests[1].Query.AllKeys.Should().NotContain("access_token");
        handler.Requests[1].Query["after"].Should().Be("c2");
        handler.Requests[1].Authorization.Should().Be($"Bearer {MetaTestHarness.Token}");
    }

    [Theory]
    [InlineData("https://evil.example.com/v25.0/act_1/ads?after=x")]
    [InlineData("http://graph.facebook.com/v25.0/act_1/ads?after=x")]
    public void refuses_paging_next_outside_https_graph_host(string next)
    {
        var act = () => MetaGraphClient.NextPageUri(next);

        act.Should().Throw<MetaGraphException>().Which.IsTransient.Should().BeFalse();
    }

    [Fact]
    public void empty_paging_next_ends_paging()
    {
        MetaGraphClient.NextPageUri(null).Should().BeNull();
        MetaGraphClient.NextPageUri("").Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, 4)]
    [InlineData(HttpStatusCode.BadRequest, 17)]
    [InlineData(HttpStatusCode.BadRequest, 613)]
    [InlineData(HttpStatusCode.BadRequest, 80000)]
    [InlineData(HttpStatusCode.BadRequest, 80004)]
    [InlineData(HttpStatusCode.InternalServerError, 2)]
    public async Task retries_transient_failures_then_succeeds(HttpStatusCode status, int code)
    {
        var handler = new RoutingGraphHandler().OnSequence(HttpMethod.Get, Account, Err(status, code), Ok("""{"id":"x"}"""));
        var delays = new List<TimeSpan>();
        var client = MetaTestHarness.CreateGraphClient(handler, delays);

        var body = await client.GetRawAsync(Account, Q(), CancellationToken.None);

        body.Should().Contain("\"id\"");
        handler.Requests.Should().HaveCount(2);
        delays.Should().Equal(MetaGraphClient.BaseDelay);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(190)]
    [InlineData(200)]
    public async Task never_retries_a_permanent_4xx(int code)
    {
        var handler = new RoutingGraphHandler().OnSequence(HttpMethod.Get, Account, Err(HttpStatusCode.BadRequest, code), Ok("{}"));
        var client = MetaTestHarness.CreateGraphClient(handler);

        var act = () => client.GetRawAsync(Account, Q(), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<MetaGraphException>()).Which;
        ex.ErrorCode.Should().Be(code);
        ex.IsTransient.Should().BeFalse();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task classifies_auth_permission_and_not_found_errors()
    {
        var handler = new RoutingGraphHandler()
            .OnSequence(HttpMethod.Get, "a", Err(HttpStatusCode.Unauthorized, 190))
            .OnSequence(HttpMethod.Get, "b", Err(HttpStatusCode.Forbidden, 200))
            .OnSequence(HttpMethod.Get, "c", () => RoutingGraphHandler.GraphError(HttpStatusCode.BadRequest, 100, 33, "does not exist"));
        var client = MetaTestHarness.CreateGraphClient(handler);

        (await Assert.ThrowsAsync<MetaGraphException>(() => client.GetRawAsync("a", Q(), default))).IsAuthError.Should().BeTrue();
        (await Assert.ThrowsAsync<MetaGraphException>(() => client.GetRawAsync("b", Q(), default))).IsPermissionError.Should().BeTrue();
        (await Assert.ThrowsAsync<MetaGraphException>(() => client.GetRawAsync("c", Q(), default))).IsNotFound.Should().BeTrue();
    }

    [Fact]
    public async Task gives_up_after_max_retries()
    {
        var handler = new RoutingGraphHandler().On(HttpMethod.Get, Account, _ => Err(HttpStatusCode.TooManyRequests, 4)());
        var delays = new List<TimeSpan>();
        var client = MetaTestHarness.CreateGraphClient(handler, delays);

        var act = () => client.GetRawAsync(Account, Q(), CancellationToken.None);

        (await act.Should().ThrowAsync<MetaGraphException>()).Which.IsTransient.Should().BeTrue();
        handler.Requests.Should().HaveCount(MetaGraphClient.MaxRetries + 1);
        delays.Should().Equal(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8));
    }

    [Fact]
    public async Task waits_for_the_business_use_case_regain_hint_when_it_fits()
    {
        var usage = new Dictionary<string, string>
        {
            ["x-business-use-case-usage"] = """{"555":[{"type":"ads_insights","call_count":100,"total_cputime":10,"total_time":10,"estimated_time_to_regain_access":1}]}""",
        };
        var handler = new RoutingGraphHandler().OnSequence(HttpMethod.Get, Account, Err(HttpStatusCode.BadRequest, 80000, headers: usage), Ok("{}"));
        var delays = new List<TimeSpan>();
        var client = MetaTestHarness.CreateGraphClient(handler, delays);

        await client.GetRawAsync(Account, Q(), CancellationToken.None);

        delays.Should().Equal(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task fails_fast_when_the_regain_hint_exceeds_max_delay()
    {
        var usage = new Dictionary<string, string>
        {
            ["x-business-use-case-usage"] = """{"555":[{"type":"ads_insights","call_count":100,"total_cputime":10,"total_time":10,"estimated_time_to_regain_access":30}]}""",
        };
        var handler = new RoutingGraphHandler().OnSequence(HttpMethod.Get, Account, Err(HttpStatusCode.BadRequest, 80000, headers: usage), Ok("{}"));
        var delays = new List<TimeSpan>();
        var client = MetaTestHarness.CreateGraphClient(handler, delays);

        var act = () => client.GetRawAsync(Account, Q(), CancellationToken.None);

        (await act.Should().ThrowAsync<MetaGraphException>()).Which.RetryAfter.Should().Be(TimeSpan.FromMinutes(30));
        handler.Requests.Should().ContainSingle();
        delays.Should().BeEmpty();
    }

    [Fact]
    public async Task retries_a_timeout_when_the_caller_did_not_cancel()
    {
        var calls = 0;
        var handler = new RoutingGraphHandler().On(HttpMethod.Get, Account, _ =>
            ++calls == 1 ? throw new TaskCanceledException("timeout") : RoutingGraphHandler.Json("{}"));
        var client = MetaTestHarness.CreateGraphClient(handler);

        await client.GetRawAsync(Account, Q(), CancellationToken.None);

        calls.Should().Be(2);
    }

    [Fact]
    public async Task propagates_caller_cancellation_without_retrying()
    {
        var handler = new RoutingGraphHandler().On(HttpMethod.Get, Account, _ => RoutingGraphHandler.Json("{}"));
        var client = MetaTestHarness.CreateGraphClient(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => client.GetRawAsync(Account, Q(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task posts_form_body_with_bearer_header()
    {
        var handler = new RoutingGraphHandler().OnSequence(HttpMethod.Post, "120000000000000301", Ok("""{"success":true}"""));
        var client = MetaTestHarness.CreateGraphClient(handler);

        await client.PostFormAsync("120000000000000301", Q(("status", "PAUSED")), CancellationToken.None);

        var request = handler.Requests.Single();
        request.Form["status"].Should().Be("PAUSED");
        request.Form.AllKeys.Should().NotContain("access_token");
        request.Query.AllKeys.Should().BeEmpty();
        request.Authorization.Should().Be($"Bearer {MetaTestHarness.Token}");
    }

    [Fact]
    public async Task never_logs_the_token_or_query_strings()
    {
        var next = $"https://graph.facebook.com/{MetaTestHarness.ApiVersion}/{Account}/ads?access_token=LEAKED&after=c2";
        var logger = new ListLogger<MetaGraphClient>();
        var handler = new RoutingGraphHandler()
            .On(HttpMethod.Get, $"{Account}/ads", _ => Err(HttpStatusCode.BadRequest, 100)(), q => q["after"] == "c2")
            .On(HttpMethod.Get, $"{Account}/ads", _ => RoutingGraphHandler.Json(
                JsonSerializer.Serialize(new { data = Array.Empty<object>(), paging = new { next } })));
        var client = MetaTestHarness.CreateGraphClient(handler, logger: logger);

        var act = () => client.GetAllPagesAsync<Item>($"{Account}/ads", Q(("fields", "id")), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<MetaGraphException>()).Which;
        logger.Messages.Should().NotBeEmpty();
        logger.Messages.Should().NotContain(m => m.Contains(MetaTestHarness.Token) || m.Contains("LEAKED") || m.Contains("access_token") || m.Contains('?'));
        ex.Message.Should().NotContain(MetaTestHarness.Token).And.NotContain("LEAKED");
    }
}
```

- [ ] **Step 4: Run the tests and verify they fail**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.MetaAds.Tests -p:UseSharedCompilation=false`
Expected: the build FAILS with `CS0246: The type or namespace name 'MetaGraphClient' could not be found` (and `MetaGraphException`).

- [ ] **Step 5: Bump the default API version**

In `MetaAdsSettings.cs`, change the property to the version chosen in `meta-spike-findings.md`. The code below shows `v25.0`; if the spike chose a newer working version, use that here and in `appsettings.json`:

```csharp
    /// <summary>Marketing API version, e.g. "v25.0". v21.0 expired 2025-09-09 — see docs/integrations/meta-ads-api.md §3.</summary>
    public string ApiVersion { get; set; } = "v25.0";
```

In `backend/src/Anela.Heblo.API/appsettings.json`, change `"ApiVersion": "v21.0"` under `"MetaAds"` to `"ApiVersion": "v25.0"`. Check no other appsettings file overrides it: `grep -rn '"ApiVersion"' backend/src/Anela.Heblo.API/appsettings*.json` should show only that line.

- [ ] **Step 6: Implement the transport**

`Graph/MetaGraphDtos.cs` (this task adds only the paging types; later tasks append to the file):

```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.MetaAds.Graph;

public sealed class MetaPage<T>
{
    [JsonPropertyName("data")]
    public List<T> Data { get; set; } = [];

    [JsonPropertyName("paging")]
    public MetaPaging? Paging { get; set; }
}

public sealed class MetaPaging
{
    [JsonPropertyName("next")]
    public string? Next { get; set; }
}
```

`Graph/MetaGraphError.cs`:

```csharp
using System.Text.Json;

namespace Anela.Heblo.Adapters.MetaAds.Graph;

/// <summary>The <c>{"error":{...}}</c> body Graph returns on every failure.</summary>
public sealed record MetaGraphError(int? Code, int? Subcode, string Message, bool IsTransient, string? FbTraceId)
{
    public static MetaGraphError? TryParse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("error", out var e) || e.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new MetaGraphError(
                Int(e, "code"),
                Int(e, "error_subcode"),
                Str(e, "message") ?? "(no message)",
                e.TryGetProperty("is_transient", out var t) && t.ValueKind == JsonValueKind.True,
                Str(e, "fbtrace_id"));
        }
        catch (JsonException)
        {
            return null; // not a Graph error body; caller reports the HTTP status instead
        }
    }

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
```

`Graph/MetaGraphException.cs`:

```csharp
using System.Net;

namespace Anela.Heblo.Adapters.MetaAds.Graph;

/// <summary>A classified Graph API failure. Messages never contain URLs or tokens.</summary>
public sealed class MetaGraphException : Exception
{
    public const int NotFoundCode = 100;
    public const int NotFoundSubcode = 33;

    // 4 app limit, 17 user/account limit, 613 call-rate limit, 80000 ads_insights BUC, 80004 ads_management BUC.
    private static readonly HashSet<int> ThrottlingCodes = new() { 4, 17, 613, 80000, 80004 };

    public MetaGraphException(
        string message, HttpStatusCode? statusCode, int? errorCode, int? errorSubcode,
        bool isTransient, TimeSpan? retryAfter, string? responseBody, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ErrorSubcode = errorSubcode;
        IsTransient = isTransient;
        RetryAfter = retryAfter;
        ResponseBody = responseBody;
    }

    public HttpStatusCode? StatusCode { get; }
    public int? ErrorCode { get; }
    public int? ErrorSubcode { get; }
    public bool IsTransient { get; }
    public TimeSpan? RetryAfter { get; }
    public string? ResponseBody { get; }

    public bool IsAuthError => ErrorCode is 190 or 102;
    public bool IsPermissionError => ErrorCode is 10 or (>= 200 and <= 299);
    public bool IsNotFound => ErrorCode == NotFoundCode && ErrorSubcode == NotFoundSubcode;

    public static MetaGraphException FromResponse(HttpStatusCode status, string body, TimeSpan? retryAfter)
    {
        var error = MetaGraphError.TryParse(body);
        var isThrottled = error?.Code is { } code && ThrottlingCodes.Contains(code);
        var isTransient = status == HttpStatusCode.TooManyRequests
                          || (int)status >= 500
                          || isThrottled
                          || error?.IsTransient == true;
        var message = error is null
            ? $"Meta Graph API returned HTTP {(int)status} with a non-Graph error body."
            : $"Meta Graph API error {error.Code}/{error.Subcode?.ToString() ?? "-"} (HTTP {(int)status}): {error.Message} [fbtrace_id {error.FbTraceId}]";
        return new MetaGraphException(message, status, error?.Code, error?.Subcode, isTransient, retryAfter, body);
    }

    public static MetaGraphException Transport(string message, Exception inner) =>
        new(message, null, null, null, isTransient: true, retryAfter: null, responseBody: null, inner);

    public static MetaGraphException Permanent(string message, Exception? inner = null) =>
        new(message, null, null, null, isTransient: false, retryAfter: null, responseBody: null, inner);
}
```

`Graph/MetaUsageHeader.cs`:

```csharp
using System.Net.Http.Headers;
using System.Text.Json;

namespace Anela.Heblo.Adapters.MetaAds.Graph;

/// <summary>Reads <c>x-business-use-case-usage</c>: highest usage % and the longest "regain access" hint.</summary>
public static class MetaUsageHeader
{
    public const string BusinessUseCaseHeader = "x-business-use-case-usage";
    private static readonly string[] PercentKeys = { "call_count", "total_cputime", "total_time" };

    public static (int MaxUsagePercent, TimeSpan? RegainAccessIn) Parse(HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues(BusinessUseCaseHeader, out var values))
        {
            return (0, null);
        }

        var max = 0;
        TimeSpan? regain = null;
        foreach (var raw in values)
        {
            foreach (var entry in Entries(raw))
            {
                foreach (var key in PercentKeys)
                {
                    if (entry.TryGetProperty(key, out var v) && v.TryGetInt32(out var pct))
                    {
                        max = Math.Max(max, pct);
                    }
                }

                if (entry.TryGetProperty("estimated_time_to_regain_access", out var r) && r.TryGetInt32(out var minutes) && minutes > 0)
                {
                    var hint = TimeSpan.FromMinutes(minutes);
                    regain = regain is null || hint > regain ? hint : regain;
                }
            }
        }

        return (max, regain);
    }

    private static IReadOnlyList<JsonElement> Entries(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            return doc.RootElement.EnumerateObject()
                .Where(b => b.Value.ValueKind == JsonValueKind.Array)
                .SelectMany(b => b.Value.EnumerateArray())
                .Where(e => e.ValueKind == JsonValueKind.Object)
                .Select(e => e.Clone())
                .ToList();
        }
        catch (JsonException)
        {
            return []; // malformed usage header is informational only; the HTTP status still drives retry
        }
    }
}
```

`Graph/MetaGraphClient.cs`:

```csharp
using System.Net.Http.Headers;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.MetaAds.Graph;

/// <summary>
/// Graph API transport shared by the Meta read source and executor: bearer auth (never a query-string
/// token), versioned URLs, safe paging, error classification, throttling backoff.
/// Logs URL paths only — query strings can carry tokens (Meta embeds access_token in paging.next).
/// </summary>
public class MetaGraphClient
{
    public const string GraphHost = "graph.facebook.com";
    public const int MaxRetries = 3;
    public const int MaxPages = 1000;
    public const int UsageWarningPercent = 80;
    public static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly MetaAdsSettings _settings;
    private readonly ILogger<MetaGraphClient> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public MetaGraphClient(
        HttpClient httpClient,
        IOptions<MetaAdsSettings> options,
        ILogger<MetaGraphClient> logger,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settings = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _delay = delay ?? Task.Delay;
    }

    public Task<string> GetRawAsync(string relativePath, IReadOnlyDictionary<string, string> query, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, BuildUri(relativePath, query), form: null, ct);

    public async Task<T> GetAsync<T>(string relativePath, IReadOnlyDictionary<string, string> query, CancellationToken ct) =>
        Deserialize<T>(await GetRawAsync(relativePath, query, ct), relativePath);

    public async Task<IReadOnlyList<T>> GetAllPagesAsync<T>(
        string relativePath, IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        var items = new List<T>();
        Uri? next = BuildUri(relativePath, query);
        for (var page = 1; next is not null; page++)
        {
            if (page > MaxPages)
            {
                throw MetaGraphException.Permanent($"Meta Graph paging for {relativePath} exceeded {MaxPages} pages.");
            }

            var parsed = Deserialize<MetaPage<T>>(await SendAsync(HttpMethod.Get, next, form: null, ct), relativePath);
            items.AddRange(parsed.Data);
            next = NextPageUri(parsed.Paging?.Next);
        }

        return items;
    }

    public Task<string> PostFormAsync(string relativePath, IReadOnlyDictionary<string, string> form, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, BuildUri(relativePath, new Dictionary<string, string>()), form, ct);

    public static Uri? NextPageUri(string? next)
    {
        if (string.IsNullOrWhiteSpace(next))
        {
            return null;
        }

        if (!Uri.TryCreate(next, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, GraphHost, StringComparison.OrdinalIgnoreCase))
        {
            throw MetaGraphException.Permanent($"Refusing to follow a paging.next link outside https://{GraphHost}.");
        }

        var query = HttpUtility.ParseQueryString(uri.Query);
        query.Remove("access_token");
        return new UriBuilder(uri) { Query = query.ToString() ?? string.Empty }.Uri;
    }

    public static TimeSpan ComputeDelay(int attempt, TimeSpan? retryAfter)
    {
        var exponential = TimeSpan.FromTicks(BaseDelay.Ticks * (1L << attempt));
        return retryAfter is { } hinted && hinted > exponential ? hinted : exponential;
    }

    private async Task<string> SendAsync(HttpMethod method, Uri uri, IReadOnlyDictionary<string, string>? form, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var (body, failure) = await SendOnceAsync(method, uri, form, ct);
            if (failure is null)
            {
                return body!;
            }

            var canRetry = failure.IsTransient && attempt < MaxRetries && (failure.RetryAfter ?? TimeSpan.Zero) <= MaxDelay;
            if (!canRetry)
            {
                _logger.LogWarning("Meta Graph {Method} {Path} failed: {Error}", method, uri.AbsolutePath, failure.Message);
                throw failure;
            }

            var delay = ComputeDelay(attempt, failure.RetryAfter);
            _logger.LogInformation(
                "Meta Graph {Method} {Path} transient failure (code {Code}); retry {Attempt}/{Max} in {Delay}",
                method, uri.AbsolutePath, failure.ErrorCode, attempt + 1, MaxRetries, delay);
            await _delay(delay, ct);
        }
    }

    private async Task<(string? Body, MetaGraphException? Failure)> SendOnceAsync(
        HttpMethod method, Uri uri, IReadOnlyDictionary<string, string>? form, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AccessToken);
            if (form is not null)
            {
                request.Content = new FormUrlEncodedContent(form);
            }

            using var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var usage = MetaUsageHeader.Parse(response.Headers);
            if (usage.MaxUsagePercent >= UsageWarningPercent)
            {
                _logger.LogWarning("Meta Graph rate-limit usage at {Usage}% after {Path}", usage.MaxUsagePercent, uri.AbsolutePath);
            }

            return response.IsSuccessStatusCode
                ? (body, null)
                : (null, MetaGraphException.FromResponse(response.StatusCode, body, usage.RegainAccessIn ?? response.Headers.RetryAfter?.Delta));
        }
        catch (HttpRequestException ex)
        {
            return (null, MetaGraphException.Transport($"Meta Graph transport error on {uri.AbsolutePath}: {ex.Message}", ex));
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return (null, MetaGraphException.Transport($"Meta Graph request timed out on {uri.AbsolutePath}.", ex));
        }
    }

    private Uri BuildUri(string relativePath, IReadOnlyDictionary<string, string> query)
    {
        var path = $"https://{GraphHost}/{_settings.ApiVersion}/{relativePath.TrimStart('/')}";
        if (query.Count == 0)
        {
            return new Uri(path);
        }

        var queryString = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return new Uri($"{path}?{queryString}");
    }

    private static T Deserialize<T>(string body, string relativePath)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions)
                   ?? throw MetaGraphException.Permanent($"Meta Graph API returned an empty body for {relativePath}.");
        }
        catch (JsonException ex)
        {
            throw MetaGraphException.Permanent($"Meta Graph API returned an unexpected response shape for {relativePath}.", ex);
        }
    }
}
```

- [ ] **Step 7: Run the tests and verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Adapters.MetaAds.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.MetaAds.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~MetaGraphClientTests"
```

Expected: `Passed!`, 0 failed.

- [ ] **Step 8: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.MetaAds.Tests Anela.Heblo.sln \
        backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Graph \
        backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsSettings.cs \
        backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat: add Meta Graph transport with safe paging and throttling backoff

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 4: `MetaAdsReadSource` — account, entities, status mapping

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Graph/MetaGraphDtos.cs` (append the account, campaign, ad set, ad and creative DTOs)
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Mapping/MetaStatusMapper.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Mapping/MetaEntityMapper.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsReadSource.cs`
- Create: `backend/test/Anela.Heblo.Adapters.MetaAds.Tests/Support/MetaFixtureRoutes.cs`
- Create: `backend/test/Anela.Heblo.Adapters.MetaAds.Tests/Fixtures/account.json`, `campaigns.json`, `adsets_page1.json`, `adsets_page2.json`, `ads.json`
- Create: `backend/test/Anela.Heblo.Adapters.MetaAds.Tests/MetaAdsReadSourceEntitiesTests.cs`
- Modify: `backend/test/Anela.Heblo.Adapters.MetaAds.Tests/Support/MetaTestHarness.cs` (add `CreateReadSource`)

**Interfaces:**
- Consumes: `MetaGraphClient.GetAsync<T>`, `GetAllPagesAsync<T>` (Task 3); C1 contracts `IAdPlatformReadSource`, `AdSourceCapabilities`, `AdAccountSnapshot`, `AdEntitySnapshot`, `AdEntityLevel`, `AdEntityStatus`, `AdActionValues`
- Produces:
  - `public sealed class MetaAdsReadSource(MetaGraphClient graph, IOptions<MetaAdsSettings> options, ILogger<MetaAdsReadSource> logger) : IAdPlatformReadSource`, with `public static readonly TimeSpan ChangeLogMaxAge = 30 days`
  - `MetaStatusMapper.ToEntityStatus(string?) → AdEntityStatus`, `MetaStatusMapper.ToActionValue(string?) → string?`, constants `Active = "ACTIVE"`, `Paused = "PAUSED"` (the executor in Task 9 uses these)
  - `MetaEntityMapper.ToSnapshot(MetaCampaignDto | MetaAdSetDto | MetaAdDto) → AdEntitySnapshot`
  - DTOs `MetaAccountDto`, `MetaCampaignDto`, `MetaAdSetDto`, `MetaAdDto` (`Id`, `Name`, `Status`, `EffectiveStatus`, `AdsetId`, `CampaignId`, `AccountId`, `Creative`), `MetaCreativeRefDto`
  - Test support: `MetaTestHarness.CreateReadSource(HttpMessageHandler)`, `MetaFixtureRoutes.ReadSource()`

- [ ] **Step 1: Add the fixtures**

These are synthetic, shaped like Graph v25 responses. If `meta-spike-findings.md` shows a different field shape (e.g. `account_id` with an `act_` prefix), change the fixtures **and** the code to match the real shape.

`Fixtures/account.json`:

```json
{"id":"act_1234567890","name":"Anela","currency":"CZK","timezone_name":"Europe/Prague","account_status":1}
```

`Fixtures/campaigns.json`:

```json
{"data":[
  {"id":"120000000000000101","name":"Prospecting – kosmetika","status":"ACTIVE","effective_status":"ACTIVE","objective":"OUTCOME_SALES","daily_budget":"50000","bid_strategy":"LOWEST_COST_WITHOUT_CAP"},
  {"id":"120000000000000102","name":"Retargeting","status":"PAUSED","effective_status":"PAUSED","objective":"OUTCOME_SALES","lifetime_budget":"0"}
],"paging":{"cursors":{"before":"QVFIa","after":"QVFIb"}}}
```

`Fixtures/adsets_page1.json`:

```json
{"data":[
  {"id":"120000000000000201","name":"CZ 25-54 ženy","status":"ACTIVE","effective_status":"ACTIVE","campaign_id":"120000000000000101","daily_budget":"30000","optimization_goal":"OFFSITE_CONVERSIONS","billing_event":"IMPRESSIONS","bid_strategy":"LOWEST_COST_WITHOUT_CAP"}
],"paging":{"cursors":{"before":"a","after":"cursorB"},"next":"https://graph.facebook.com/v25.0/act_1234567890/adsets?fields=id&limit=500&access_token=LEAKED_TOKEN_IN_NEXT&after=cursorB"}}
```

`Fixtures/adsets_page2.json`:

```json
{"data":[
  {"id":"120000000000000202","name":"Lookalike 1 %","status":"PAUSED","effective_status":"PAUSED","campaign_id":"120000000000000101","daily_budget":"20000","optimization_goal":"OFFSITE_CONVERSIONS","billing_event":"IMPRESSIONS"},
  {"id":"120000000000000203","name":"Návštěvníci webu 30 dní","status":"ACTIVE","effective_status":"CAMPAIGN_PAUSED","campaign_id":"120000000000000102","optimization_goal":"OFFSITE_CONVERSIONS","billing_event":"IMPRESSIONS"}
],"paging":{"cursors":{"before":"cursorB","after":"c"}}}
```

`Fixtures/ads.json`:

```json
{"data":[
  {"id":"120000000000000301","name":"Sérum – video","status":"ACTIVE","effective_status":"ACTIVE","adset_id":"120000000000000201","campaign_id":"120000000000000101","creative":{"id":"120000000000000401"}},
  {"id":"120000000000000302","name":"Krém – carousel","status":"PAUSED","effective_status":"PAUSED","adset_id":"120000000000000202","campaign_id":"120000000000000101","creative":{"id":"120000000000000402"}},
  {"id":"120000000000000303","name":"Retarget – katalog","status":"ACTIVE","effective_status":"CAMPAIGN_PAUSED","adset_id":"120000000000000203","campaign_id":"120000000000000102","creative":{"id":"120000000000000403"}},
  {"id":"120000000000000304","name":"Stará reklama","status":"ARCHIVED","effective_status":"ARCHIVED","adset_id":"120000000000000201","campaign_id":"120000000000000101"}
],"paging":{"cursors":{"before":"x","after":"y"}}}
```

- [ ] **Step 2: Add the fixture router and the harness factory**

`Support/MetaFixtureRoutes.cs`. This is the complete route table. The insights, activities and `me` fixtures are added in Tasks 5 and 6, and their routes only fire when those methods are called.

```csharp
namespace Anela.Heblo.Adapters.MetaAds.Tests.Support;

internal static class MetaFixtureRoutes
{
    public static RoutingGraphHandler ReadSource()
    {
        const string a = MetaTestHarness.AccountId;
        static HttpResponseMessage F(string name) => RoutingGraphHandler.Fixture(name);

        return new RoutingGraphHandler()
            .On(HttpMethod.Get, a, _ => F("account.json"))
            .On(HttpMethod.Get, "me", _ => F("me.json"))
            .On(HttpMethod.Get, $"{a}/campaigns", _ => F("campaigns.json"))
            .On(HttpMethod.Get, $"{a}/adsets", _ => F("adsets_page2.json"), q => q["after"] == "cursorB")
            .On(HttpMethod.Get, $"{a}/adsets", _ => F("adsets_page1.json"))
            .On(HttpMethod.Get, $"{a}/ads", _ => F("ads.json"))
            .On(HttpMethod.Get, $"{a}/insights", _ => F("insights_campaign.json"), q => q["level"] == "campaign")
            .On(HttpMethod.Get, $"{a}/insights", _ => F("insights_adset.json"), q => q["level"] == "adset")
            .On(HttpMethod.Get, $"{a}/insights", _ => F("insights_ad.json"), q => q["level"] == "ad")
            .On(HttpMethod.Get, $"{a}/activities", _ => F("activities.json"));
    }
}
```

Append to `MetaTestHarness`. Add `using Microsoft.Extensions.Logging.Abstractions;` and `using Microsoft.Extensions.Options;` if they are not already there; they are, from Task 3.

```csharp
    public static MetaAdsReadSource CreateReadSource(HttpMessageHandler handler) =>
        new(CreateGraphClient(handler), Options.Create(Settings()), NullLogger<MetaAdsReadSource>.Instance);
```

- [ ] **Step 3: Write the failing tests**

`MetaAdsReadSourceEntitiesTests.cs`:

```csharp
using Anela.Heblo.Adapters.MetaAds.Mapping;
using Anela.Heblo.Adapters.MetaAds.Tests.Support;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Adapters.MetaAds.Tests;

public class MetaAdsReadSourceEntitiesTests
{
    private readonly RoutingGraphHandler _handler = MetaFixtureRoutes.ReadSource();

    private MetaAdsReadSource Source() => MetaTestHarness.CreateReadSource(_handler);

    [Fact]
    public void declares_meta_platform_without_search_terms_with_change_log()
    {
        var source = Source();

        source.Platform.Should().Be(AdPlatform.MetaAds);
        source.Capabilities.SearchTerms.Should().BeFalse();
        source.Capabilities.ChangeLog.Should().BeTrue();
        source.Capabilities.ChangeLogMaxAge.Should().Be(TimeSpan.FromDays(30));
    }

    [Fact]
    public async Task returns_the_configured_account_with_currency_and_time_zone()
    {
        var accounts = await Source().GetAccountsAsync(CancellationToken.None);

        accounts.Should().ContainSingle().Which.Should().Be(
            new AdAccountSnapshot(MetaTestHarness.AccountId, "Anela", "CZK", "Europe/Prague"));
    }

    [Fact]
    public async Task maps_campaigns_adsets_and_ads_with_parents()
    {
        var entities = await Source().GetEntitiesAsync(MetaTestHarness.AccountId, CancellationToken.None);

        entities.Should().HaveCount(9);
        entities.Count(e => e.Level == AdEntityLevel.Campaign).Should().Be(2);
        entities.Count(e => e.Level == AdEntityLevel.AdGroup).Should().Be(3);
        entities.Count(e => e.Level == AdEntityLevel.Ad).Should().Be(4);

        var campaign = entities.Single(e => e.ExternalId == "120000000000000101");
        campaign.ParentLevel.Should().BeNull();
        campaign.ParentExternalId.Should().BeNull();
        campaign.Attributes.Should().Contain("daily_budget", "50000").And.Contain("objective", "OUTCOME_SALES");
        campaign.Attributes.Should().NotContainKey("lifetime_budget");

        var adSet = entities.Single(e => e.ExternalId == "120000000000000202");
        adSet.Level.Should().Be(AdEntityLevel.AdGroup);
        adSet.ParentLevel.Should().Be(AdEntityLevel.Campaign);
        adSet.ParentExternalId.Should().Be("120000000000000101");

        var ad = entities.Single(e => e.ExternalId == "120000000000000301");
        ad.ParentLevel.Should().Be(AdEntityLevel.AdGroup);
        ad.ParentExternalId.Should().Be("120000000000000201");
        ad.Attributes.Should().Contain("creative_id", "120000000000000401");
    }

    [Fact]
    public async Task status_comes_from_own_status_and_effective_status_is_kept_as_attribute()
    {
        var entities = await Source().GetEntitiesAsync(MetaTestHarness.AccountId, CancellationToken.None);

        var underPausedCampaign = entities.Single(e => e.ExternalId == "120000000000000303");
        underPausedCampaign.Status.Should().Be(AdEntityStatus.Enabled);
        underPausedCampaign.Attributes.Should().Contain("effective_status", "CAMPAIGN_PAUSED");
        entities.Single(e => e.ExternalId == "120000000000000302").Status.Should().Be(AdEntityStatus.Paused);
        entities.Single(e => e.ExternalId == "120000000000000304").Status.Should().Be(AdEntityStatus.Removed);
    }

    [Fact]
    public async Task follows_adset_paging_without_forwarding_the_token_in_the_query()
    {
        await Source().GetEntitiesAsync(MetaTestHarness.AccountId, CancellationToken.None);

        var adsetCalls = _handler.Requests.Where(r => r.Uri.AbsolutePath.EndsWith("/adsets")).ToList();
        adsetCalls.Should().HaveCount(2);
        adsetCalls[1].Query["after"].Should().Be("cursorB");
        _handler.Requests.Should().OnlyContain(r => r.Query["access_token"] == null);
        _handler.Requests.Should().OnlyContain(r => r.Authorization == $"Bearer {MetaTestHarness.Token}");
    }

    [Fact]
    public async Task refuses_an_account_other_than_the_configured_one()
    {
        var act = () => Source().GetEntitiesAsync("act_999", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ACTIVE", AdEntityStatus.Enabled, AdActionValues.Enabled)]
    [InlineData("active", AdEntityStatus.Enabled, AdActionValues.Enabled)]
    [InlineData("PAUSED", AdEntityStatus.Paused, AdActionValues.Paused)]
    [InlineData("ARCHIVED", AdEntityStatus.Removed, null)]
    [InlineData("DELETED", AdEntityStatus.Removed, null)]
    [InlineData("IN_PROCESS", AdEntityStatus.Unknown, null)]
    [InlineData(null, AdEntityStatus.Unknown, null)]
    public void maps_meta_status(string? meta, AdEntityStatus expected, string? actionValue)
    {
        MetaStatusMapper.ToEntityStatus(meta).Should().Be(expected);
        MetaStatusMapper.ToActionValue(meta).Should().Be(actionValue);
    }
}
```

- [ ] **Step 4: Run the tests and verify they fail**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.MetaAds.Tests -p:UseSharedCompilation=false`
Expected: the build FAILS with `CS0246` for `MetaAdsReadSource` and `MetaStatusMapper`.

- [ ] **Step 5: Implement the DTOs, mappers and read source (entities part)**

Append to `Graph/MetaGraphDtos.cs`:

```csharp
public sealed class MetaAccountDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("currency")] public string Currency { get; set; } = string.Empty;
    [JsonPropertyName("timezone_name")] public string TimezoneName { get; set; } = string.Empty;
    [JsonPropertyName("account_status")] public int? AccountStatus { get; set; }
}

public sealed class MetaCampaignDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("effective_status")] public string? EffectiveStatus { get; set; }
    [JsonPropertyName("objective")] public string? Objective { get; set; }
    [JsonPropertyName("daily_budget")] public string? DailyBudget { get; set; }
    [JsonPropertyName("lifetime_budget")] public string? LifetimeBudget { get; set; }
    [JsonPropertyName("bid_strategy")] public string? BidStrategy { get; set; }
}

public sealed class MetaAdSetDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("effective_status")] public string? EffectiveStatus { get; set; }
    [JsonPropertyName("campaign_id")] public string CampaignId { get; set; } = string.Empty;
    [JsonPropertyName("daily_budget")] public string? DailyBudget { get; set; }
    [JsonPropertyName("lifetime_budget")] public string? LifetimeBudget { get; set; }
    [JsonPropertyName("optimization_goal")] public string? OptimizationGoal { get; set; }
    [JsonPropertyName("billing_event")] public string? BillingEvent { get; set; }
    [JsonPropertyName("bid_strategy")] public string? BidStrategy { get; set; }
}

public sealed class MetaAdDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("effective_status")] public string? EffectiveStatus { get; set; }
    [JsonPropertyName("adset_id")] public string AdsetId { get; set; } = string.Empty;
    [JsonPropertyName("campaign_id")] public string? CampaignId { get; set; }
    [JsonPropertyName("account_id")] public string? AccountId { get; set; }
    [JsonPropertyName("creative")] public MetaCreativeRefDto? Creative { get; set; }
}

public sealed class MetaCreativeRefDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
}
```

`Mapping/MetaStatusMapper.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.MetaAds.Mapping;

/// <summary>Maps an object's own Meta <c>status</c> (the field PauseAd writes). effective_status is an attribute.</summary>
public static class MetaStatusMapper
{
    public const string Active = "ACTIVE";
    public const string Paused = "PAUSED";

    public static AdEntityStatus ToEntityStatus(string? status) => status?.ToUpperInvariant() switch
    {
        Active => AdEntityStatus.Enabled,
        Paused => AdEntityStatus.Paused,
        "DELETED" or "ARCHIVED" => AdEntityStatus.Removed,
        _ => AdEntityStatus.Unknown,
    };

    public static string? ToActionValue(string? status) => ToEntityStatus(status) switch
    {
        AdEntityStatus.Enabled => AdActionValues.Enabled,
        AdEntityStatus.Paused => AdActionValues.Paused,
        _ => null,
    };
}
```

`Mapping/MetaEntityMapper.cs`:

```csharp
using Anela.Heblo.Adapters.MetaAds.Graph;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.MetaAds.Mapping;

public static class MetaEntityMapper
{
    public static AdEntitySnapshot ToSnapshot(MetaCampaignDto c) => new(
        AdEntityLevel.Campaign, c.Id, null, null, c.Name, MetaStatusMapper.ToEntityStatus(c.Status),
        Attributes(
            ("effective_status", c.EffectiveStatus), ("objective", c.Objective), ("daily_budget", c.DailyBudget),
            ("lifetime_budget", c.LifetimeBudget), ("bid_strategy", c.BidStrategy)));

    public static AdEntitySnapshot ToSnapshot(MetaAdSetDto s) => new(
        AdEntityLevel.AdGroup, s.Id, AdEntityLevel.Campaign, s.CampaignId, s.Name, MetaStatusMapper.ToEntityStatus(s.Status),
        Attributes(
            ("effective_status", s.EffectiveStatus), ("daily_budget", s.DailyBudget), ("lifetime_budget", s.LifetimeBudget),
            ("optimization_goal", s.OptimizationGoal), ("billing_event", s.BillingEvent), ("bid_strategy", s.BidStrategy)));

    public static AdEntitySnapshot ToSnapshot(MetaAdDto a) => new(
        AdEntityLevel.Ad, a.Id, AdEntityLevel.AdGroup, a.AdsetId, a.Name, MetaStatusMapper.ToEntityStatus(a.Status),
        Attributes(("effective_status", a.EffectiveStatus), ("creative_id", a.Creative?.Id)));

    private static IReadOnlyDictionary<string, string?> Attributes(params (string Key, string? Value)[] pairs) =>
        pairs.Where(p => !string.IsNullOrEmpty(p.Value)).ToDictionary(p => p.Key, p => p.Value);
}
```

`MetaAdsReadSource.cs`. `GetDailyFactsAsync` and `GetChangeEventsAsync` throw `NotSupportedException` **only until** Tasks 5 and 6 replace their bodies; the class has to compile against the interface now.

```csharp
using Anela.Heblo.Adapters.MetaAds.Graph;
using Anela.Heblo.Adapters.MetaAds.Mapping;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.MetaAds;

/// <summary>Reads Anela's single configured Meta ad account for the marketing-ads backbone (see docs/integrations/meta-ads-api.md).</summary>
public sealed class MetaAdsReadSource : IAdPlatformReadSource
{
    public static readonly TimeSpan ChangeLogMaxAge = TimeSpan.FromDays(30);

    private const string PageLimit = "500";
    private const string AccountFields = "id,name,currency,timezone_name,account_status";
    private const string CampaignFields = "id,name,status,effective_status,objective,daily_budget,lifetime_budget,bid_strategy";
    private const string AdSetFields = "id,name,status,effective_status,campaign_id,daily_budget,lifetime_budget,optimization_goal,billing_event,bid_strategy";
    private const string AdFields = "id,name,status,effective_status,adset_id,campaign_id,creative{id}";
    private const int ActiveAccountStatus = 1;

    private readonly MetaGraphClient _graph;
    private readonly MetaAdsSettings _settings;
    private readonly ILogger<MetaAdsReadSource> _logger;

    public MetaAdsReadSource(MetaGraphClient graph, IOptions<MetaAdsSettings> options, ILogger<MetaAdsReadSource> logger)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _settings = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public AdPlatform Platform => AdPlatform.MetaAds;

    public AdSourceCapabilities Capabilities { get; } = new(SearchTerms: false, ChangeLog: true, ChangeLogMaxAge: ChangeLogMaxAge);

    public async Task<IReadOnlyList<AdAccountSnapshot>> GetAccountsAsync(CancellationToken ct)
    {
        var account = await _graph.GetAsync<MetaAccountDto>(_settings.AccountId, Fields(AccountFields), ct);
        if (account.AccountStatus is { } status && status != ActiveAccountStatus)
        {
            _logger.LogWarning("Meta ad account {AccountId} has account_status {Status} (1 = active)", _settings.AccountId, status);
        }

        return [new AdAccountSnapshot(_settings.AccountId, account.Name, account.Currency, account.TimezoneName)];
    }

    public async Task<IReadOnlyList<AdEntitySnapshot>> GetEntitiesAsync(string accountExternalId, CancellationToken ct)
    {
        EnsureConfiguredAccount(accountExternalId);
        var campaigns = await _graph.GetAllPagesAsync<MetaCampaignDto>($"{accountExternalId}/campaigns", Paged(CampaignFields), ct);
        var adSets = await _graph.GetAllPagesAsync<MetaAdSetDto>($"{accountExternalId}/adsets", Paged(AdSetFields), ct);
        var ads = await _graph.GetAllPagesAsync<MetaAdDto>($"{accountExternalId}/ads", Paged(AdFields), ct);

        return campaigns.Select(MetaEntityMapper.ToSnapshot)
            .Concat(adSets.Select(MetaEntityMapper.ToSnapshot))
            .Concat(ads.Select(MetaEntityMapper.ToSnapshot))
            .ToList();
    }

    public Task<IReadOnlyList<AdDailyFactRow>> GetDailyFactsAsync(string accountExternalId, DateOnly date, CancellationToken ct) =>
        throw new NotSupportedException("Implemented in Task 5.");

    public Task<IReadOnlyList<AdSearchTermRow>> GetSearchTermsAsync(string accountExternalId, DateOnly date, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AdSearchTermRow>>([]); // Meta has no search terms (capability SearchTerms = false)

    public Task<IReadOnlyList<AdChangeEventRow>> GetChangeEventsAsync(string accountExternalId, DateTimeOffset since, CancellationToken ct) =>
        throw new NotSupportedException("Implemented in Task 6.");

    private void EnsureConfiguredAccount(string accountExternalId)
    {
        if (!string.Equals(accountExternalId, _settings.AccountId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"MetaAdsReadSource serves only the configured account {_settings.AccountId}; got {accountExternalId}.");
        }
    }

    private static Dictionary<string, string> Fields(string fields) => new() { ["fields"] = fields };

    private static Dictionary<string, string> Paged(string fields) => new() { ["fields"] = fields, ["limit"] = PageLimit };
}
```

- [ ] **Step 6: Run the tests and verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Adapters.MetaAds.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.MetaAds.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~MetaAdsReadSourceEntitiesTests"
```

Expected: `Passed!`, 0 failed.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.MetaAds backend/test/Anela.Heblo.Adapters.MetaAds.Tests
git commit -m "feat: read Meta campaigns, ad sets and ads as ad entities

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 5: Daily facts from insights (one purchase type, invariant parsing)

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Graph/MetaGraphDtos.cs` (append `MetaInsightRowDto`, `MetaActionValueDto`)
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Mapping/MetaConversionSelector.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Mapping/MetaInsightMapper.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsReadSource.cs` (replace the `GetDailyFactsAsync` body)
- Create: `backend/test/Anela.Heblo.Adapters.MetaAds.Tests/Fixtures/insights_campaign.json`, `insights_adset.json`, `insights_ad.json`
- Create: `backend/test/Anela.Heblo.Adapters.MetaAds.Tests/MetaAdsReadSourceDailyFactsTests.cs`

**Interfaces:**
- Consumes: `MetaGraphClient.GetAllPagesAsync<T>` (Task 3); `MetaAdsReadSource` (Task 4); C1 `AdDailyFactRow`
- Produces:
  - `MetaConversionSelector.PurchaseActionTypes` (ordered `omni_purchase`, `purchase`, `offsite_conversion.fb_pixel_purchase`)
  - `MetaConversionSelector.Select(IReadOnlyList<MetaActionValueDto>? actions, IReadOnlyList<MetaActionValueDto>? actionValues) → (decimal Conversions, decimal Value, string? ActionType)`
  - `MetaInsightMapper.ToFactRow(AdEntityLevel level, MetaInsightRowDto row, DateOnly date) → AdDailyFactRow?` (null when the row has no entity id)
  - `MetaInsightMapper.ParseDecimal(string?)`, `MetaInsightMapper.ParseLong(string?)` (invariant culture; null/blank → 0)

- [ ] **Step 1: Add the fixtures**

The numbers are consistent across levels: campaign 101 = ad set 201 + 202 = ad 301 + 302. Each row repeats the purchase under every type, as Graph does, so double counting would show.

`Fixtures/insights_campaign.json`:

```json
{"data":[
  {"campaign_id":"120000000000000101","impressions":"15234","inline_link_clicks":"312","spend":"1834.56","account_currency":"CZK","date_start":"2026-10-06","date_stop":"2026-10-06",
   "actions":[{"action_type":"link_click","value":"312"},{"action_type":"offsite_conversion.fb_pixel_purchase","value":"7"},{"action_type":"purchase","value":"7"},{"action_type":"omni_purchase","value":"7"}],
   "action_values":[{"action_type":"offsite_conversion.fb_pixel_purchase","value":"5480.5"},{"action_type":"purchase","value":"5480.5"},{"action_type":"omni_purchase","value":"5480.5"}]}
],"paging":{"cursors":{"before":"a","after":"b"}}}
```

`Fixtures/insights_adset.json`:

```json
{"data":[
  {"adset_id":"120000000000000201","impressions":"10000","inline_link_clicks":"200","spend":"1200.00","account_currency":"CZK","date_start":"2026-10-06","date_stop":"2026-10-06",
   "actions":[{"action_type":"link_click","value":"200"},{"action_type":"omni_purchase","value":"5"},{"action_type":"purchase","value":"5"},{"action_type":"offsite_conversion.fb_pixel_purchase","value":"5"}],
   "action_values":[{"action_type":"omni_purchase","value":"4000"},{"action_type":"purchase","value":"4000"},{"action_type":"offsite_conversion.fb_pixel_purchase","value":"4000"}]},
  {"adset_id":"120000000000000202","impressions":"5234","inline_link_clicks":"112","spend":"634.56","account_currency":"CZK","date_start":"2026-10-06","date_stop":"2026-10-06",
   "actions":[{"action_type":"link_click","value":"112"},{"action_type":"offsite_conversion.fb_pixel_purchase","value":"2"}],
   "action_values":[{"action_type":"offsite_conversion.fb_pixel_purchase","value":"1480.5"}]}
],"paging":{"cursors":{"before":"a","after":"b"}}}
```

`Fixtures/insights_ad.json`:

```json
{"data":[
  {"ad_id":"120000000000000301","impressions":"10000","inline_link_clicks":"200","spend":"1200.00","account_currency":"CZK","date_start":"2026-10-06","date_stop":"2026-10-06",
   "actions":[{"action_type":"omni_purchase","value":"5"}],"action_values":[{"action_type":"omni_purchase","value":"4000"}]},
  {"ad_id":"120000000000000302","impressions":"5234","inline_link_clicks":"112","spend":"634.56","account_currency":"CZK","date_start":"2026-10-06","date_stop":"2026-10-06",
   "actions":[{"action_type":"purchase","value":"2"}],"action_values":[{"action_type":"purchase","value":"1480.5"}]}
],"paging":{"cursors":{"before":"a","after":"b"}}}
```

- [ ] **Step 2: Write the failing tests**

`MetaAdsReadSourceDailyFactsTests.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using Anela.Heblo.Adapters.MetaAds.Graph;
using Anela.Heblo.Adapters.MetaAds.Mapping;
using Anela.Heblo.Adapters.MetaAds.Tests.Support;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Adapters.MetaAds.Tests;

public class MetaAdsReadSourceDailyFactsTests
{
    private readonly RoutingGraphHandler _handler = MetaFixtureRoutes.ReadSource();

    private Task<IReadOnlyList<AdDailyFactRow>> Facts() =>
        MetaTestHarness.CreateReadSource(_handler).GetDailyFactsAsync(MetaTestHarness.AccountId, MetaTestHarness.FixtureDate, CancellationToken.None);

    [Fact]
    public async Task returns_rows_for_campaign_adset_and_ad_levels()
    {
        var rows = await Facts();

        rows.Should().HaveCount(5);
        rows.Count(r => r.Level == AdEntityLevel.Campaign).Should().Be(1);
        rows.Count(r => r.Level == AdEntityLevel.AdGroup).Should().Be(2);
        rows.Count(r => r.Level == AdEntityLevel.Ad).Should().Be(2);
        rows.Should().OnlyContain(r => r.Date == MetaTestHarness.FixtureDate && r.Currency == "CZK" && r.Cost >= 0);
    }

    [Fact]
    public async Task counts_exactly_one_purchase_action_type()
    {
        var campaign = (await Facts()).Single(r => r.Level == AdEntityLevel.Campaign);

        campaign.Should().Be(new AdDailyFactRow(
            AdEntityLevel.Campaign, "120000000000000101", MetaTestHarness.FixtureDate,
            Impressions: 15234, Clicks: 312, Cost: 1834.56m, Conversions: 7m, ConversionValue: 5480.5m, Currency: "CZK"));
    }

    [Fact]
    public async Task falls_back_to_pixel_purchase_when_omni_and_purchase_are_absent()
    {
        var adSet = (await Facts()).Single(r => r.EntityExternalId == "120000000000000202");

        adSet.Conversions.Should().Be(2m);
        adSet.ConversionValue.Should().Be(1480.5m);
    }

    [Fact]
    public async Task levels_add_up_so_no_level_double_counts()
    {
        var rows = await Facts();

        foreach (var level in new[] { AdEntityLevel.AdGroup, AdEntityLevel.Ad })
        {
            rows.Where(r => r.Level == level).Sum(r => r.Cost).Should().Be(1834.56m);
            rows.Where(r => r.Level == level).Sum(r => r.Conversions).Should().Be(7m);
        }
    }

    [Fact]
    public async Task asks_for_a_single_day_with_unified_attribution()
    {
        await Facts();

        var insightCalls = _handler.Requests.Where(r => r.Uri.AbsolutePath.EndsWith("/insights")).ToList();
        insightCalls.Select(r => r.Query["level"]).Should().BeEquivalentTo("campaign", "adset", "ad");
        foreach (var call in insightCalls)
        {
            using var range = JsonDocument.Parse(call.Query["time_range"]!);
            range.RootElement.GetProperty("since").GetString().Should().Be("2026-10-06");
            range.RootElement.GetProperty("until").GetString().Should().Be("2026-10-06");
            call.Query["time_increment"].Should().Be("1");
            call.Query["use_unified_attribution_setting"].Should().Be("true");
            call.Query["fields"]!.Split(',').Should().Contain(new[] { "spend", "impressions", "inline_link_clicks", "actions", "action_values", "account_currency", "date_start" });
        }
    }

    [Fact]
    public async Task drops_rows_dated_outside_the_requested_day()
    {
        var handler = new RoutingGraphHandler()
            .On(HttpMethod.Get, $"{MetaTestHarness.AccountId}/insights", _ => RoutingGraphHandler.Json("""
                {"data":[{"campaign_id":"1","impressions":"1","inline_link_clicks":"1","spend":"1.00","account_currency":"CZK","date_start":"2026-10-05"}]}
                """));

        var rows = await MetaTestHarness.CreateReadSource(handler)
            .GetDailyFactsAsync(MetaTestHarness.AccountId, MetaTestHarness.FixtureDate, CancellationToken.None);

        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task parses_numbers_invariantly_under_czech_culture()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("cs-CZ");
        try
        {
            var campaign = (await Facts()).Single(r => r.Level == AdEntityLevel.Campaign);

            campaign.Cost.Should().Be(1834.56m);
            campaign.ConversionValue.Should().Be(5480.5m);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task search_terms_are_empty_and_cost_no_http_call()
    {
        var rows = await MetaTestHarness.CreateReadSource(_handler)
            .GetSearchTermsAsync(MetaTestHarness.AccountId, MetaTestHarness.FixtureDate, CancellationToken.None);

        rows.Should().BeEmpty();
        _handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public void selector_returns_zero_when_no_purchase_type_is_present()
    {
        var actions = new List<MetaActionValueDto> { new() { ActionType = "link_click", Value = "10" } };

        MetaConversionSelector.Select(actions, null).Should().Be((0m, 0m, (string?)null));
    }

    [Fact]
    public void selector_takes_the_value_of_the_same_type_it_counted()
    {
        var actions = new List<MetaActionValueDto>
        {
            new() { ActionType = "offsite_conversion.fb_pixel_purchase", Value = "9" },
            new() { ActionType = "omni_purchase", Value = "7" },
        };
        var values = new List<MetaActionValueDto>
        {
            new() { ActionType = "offsite_conversion.fb_pixel_purchase", Value = "900" },
            new() { ActionType = "omni_purchase", Value = "700.5" },
        };

        MetaConversionSelector.Select(actions, values).Should().Be((7m, 700.5m, "omni_purchase"));
    }
}
```

- [ ] **Step 3: Run the tests and verify they fail**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.MetaAds.Tests -p:UseSharedCompilation=false`
Expected: the build FAILS with `CS0246` for `MetaActionValueDto` and `MetaConversionSelector`.

- [ ] **Step 4: Implement**

Append to `Graph/MetaGraphDtos.cs`:

```csharp
public sealed class MetaInsightRowDto
{
    [JsonPropertyName("campaign_id")] public string? CampaignId { get; set; }
    [JsonPropertyName("adset_id")] public string? AdsetId { get; set; }
    [JsonPropertyName("ad_id")] public string? AdId { get; set; }
    [JsonPropertyName("impressions")] public string? Impressions { get; set; }
    [JsonPropertyName("inline_link_clicks")] public string? InlineLinkClicks { get; set; }
    [JsonPropertyName("spend")] public string? Spend { get; set; }
    [JsonPropertyName("account_currency")] public string? AccountCurrency { get; set; }
    [JsonPropertyName("date_start")] public string? DateStart { get; set; }
    [JsonPropertyName("actions")] public List<MetaActionValueDto>? Actions { get; set; }
    [JsonPropertyName("action_values")] public List<MetaActionValueDto>? ActionValues { get; set; }
}

public sealed class MetaActionValueDto
{
    [JsonPropertyName("action_type")] public string ActionType { get; set; } = string.Empty;
    [JsonPropertyName("value")] public string? Value { get; set; }
}
```

`Mapping/MetaConversionSelector.cs`:

```csharp
using Anela.Heblo.Adapters.MetaAds.Graph;

namespace Anela.Heblo.Adapters.MetaAds.Mapping;

/// <summary>
/// Meta reports the same purchase under several action types (omni = deduplicated across pixel/CAPI/app/offline;
/// purchase; pixel-only). Exactly one type is counted — summing them double/triple-counts. See meta-ads-api.md §5.
/// </summary>
public static class MetaConversionSelector
{
    public static readonly IReadOnlyList<string> PurchaseActionTypes =
        ["omni_purchase", "purchase", "offsite_conversion.fb_pixel_purchase"];

    public static (decimal Conversions, decimal Value, string? ActionType) Select(
        IReadOnlyList<MetaActionValueDto>? actions, IReadOnlyList<MetaActionValueDto>? actionValues)
    {
        var chosen = PurchaseActionTypes.FirstOrDefault(type => Find(actions, type) is not null || Find(actionValues, type) is not null);
        if (chosen is null)
        {
            return (0m, 0m, null);
        }

        return (
            MetaInsightMapper.ParseDecimal(Find(actions, chosen)?.Value),
            MetaInsightMapper.ParseDecimal(Find(actionValues, chosen)?.Value),
            chosen);
    }

    private static MetaActionValueDto? Find(IReadOnlyList<MetaActionValueDto>? list, string type) =>
        list?.FirstOrDefault(a => string.Equals(a.ActionType, type, StringComparison.Ordinal));
}
```

`Mapping/MetaInsightMapper.cs`:

```csharp
using System.Globalization;
using Anela.Heblo.Adapters.MetaAds.Graph;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.MetaAds.Mapping;

public static class MetaInsightMapper
{
    /// <summary>Null when the row carries no id for the requested level (Graph should never do this).</summary>
    public static AdDailyFactRow? ToFactRow(AdEntityLevel level, MetaInsightRowDto row, DateOnly date)
    {
        var entityId = level switch
        {
            AdEntityLevel.Campaign => row.CampaignId,
            AdEntityLevel.AdGroup => row.AdsetId,
            AdEntityLevel.Ad => row.AdId,
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Meta insights exist for Campaign, AdGroup and Ad only."),
        };
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(row.AccountCurrency))
        {
            throw new InvalidOperationException($"Meta insights row for {level} {entityId} has no account_currency.");
        }

        var (conversions, value, _) = MetaConversionSelector.Select(row.Actions, row.ActionValues);
        return new AdDailyFactRow(
            level, entityId, date,
            ParseLong(row.Impressions), ParseLong(row.InlineLinkClicks), ParseDecimal(row.Spend),
            conversions, value, row.AccountCurrency);
    }

    public static decimal ParseDecimal(string? value) =>
        string.IsNullOrWhiteSpace(value) ? 0m : decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);

    public static long ParseLong(string? value) =>
        string.IsNullOrWhiteSpace(value) ? 0L : long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
}
```

In `MetaAdsReadSource.cs`, add `using System.Globalization;` and `using System.Text.Json;`. Add these members:

```csharp
    private const string InsightMetricFields = "impressions,inline_link_clicks,spend,actions,action_values,account_currency,date_start";

    private static readonly (AdEntityLevel Level, string MetaLevel, string IdField)[] InsightLevels =
    [
        (AdEntityLevel.Campaign, "campaign", "campaign_id"),
        (AdEntityLevel.AdGroup, "adset", "adset_id"),
        (AdEntityLevel.Ad, "ad", "ad_id"),
    ];
```

Replace the `GetDailyFactsAsync` placeholder with:

```csharp
    public async Task<IReadOnlyList<AdDailyFactRow>> GetDailyFactsAsync(string accountExternalId, DateOnly date, CancellationToken ct)
    {
        EnsureConfiguredAccount(accountExternalId);
        var day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var timeRange = JsonSerializer.Serialize(new { since = day, until = day });
        var rows = new List<AdDailyFactRow>();

        foreach (var (level, metaLevel, idField) in InsightLevels)
        {
            var query = new Dictionary<string, string>
            {
                ["level"] = metaLevel,
                ["fields"] = $"{idField},{InsightMetricFields}",
                ["time_range"] = timeRange,
                ["time_increment"] = "1",
                ["use_unified_attribution_setting"] = "true",
                ["limit"] = PageLimit,
            };
            var insights = await _graph.GetAllPagesAsync<MetaInsightRowDto>($"{accountExternalId}/insights", query, ct);
            var mapped = insights
                .Where(r => r.DateStart == day)
                .Select(r => MetaInsightMapper.ToFactRow(level, r, date))
                .OfType<AdDailyFactRow>()
                .ToList();
            if (mapped.Count != insights.Count)
            {
                _logger.LogWarning("Meta insights {Level} {Day}: kept {Kept} of {Total} rows (other dates or missing ids)", metaLevel, day, mapped.Count, insights.Count);
            }

            rows.AddRange(mapped);
        }

        return rows;
    }
```

- [ ] **Step 5: Run the tests and verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Adapters.MetaAds.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.MetaAds.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~MetaAdsReadSourceDailyFactsTests"
```

Expected: `Passed!`, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.MetaAds backend/test/Anela.Heblo.Adapters.MetaAds.Tests
git commit -m "feat: read Meta daily insights per campaign, ad set and ad

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 6: Change history from `/activities`

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Graph/MetaGraphDtos.cs` (append `MetaActivityDto`, `MetaMeDto`)
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Graph/MetaGraphTime.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Mapping/MetaActivityMapper.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsReadSource.cs` (replace the `GetChangeEventsAsync` body, add the own-actor lookup)
- Create: `backend/test/Anela.Heblo.Adapters.MetaAds.Tests/Fixtures/activities.json`, `me.json`
- Create: `backend/test/Anela.Heblo.Adapters.MetaAds.Tests/MetaAdsReadSourceChangeEventsTests.cs`

**Interfaces:**
- Consumes: `MetaGraphClient` (Task 3); `MetaAdsReadSource` (Task 4); C1 `AdChangeEventRow`, `AdChangeActorKind`
- Produces:
  - `MetaActivityMapper.ToRow(MetaActivityDto activity, string? ownActorId) → AdChangeEventRow`
  - `MetaActivityMapper.LevelOf(string? objectType) → AdEntityLevel?`
  - `MetaActivityMapper.ComputeEventId(MetaActivityDto) → string` (32 lowercase hex chars)
  - `MetaGraphTime.Parse(string) → DateTimeOffset` (accepts `+0000` and `+00:00`)

The `object_type` mapping below assumes Meta's **legacy** naming (`CAMPAIGN_GROUP` = campaign, `CAMPAIGN` = ad set, `ADGROUP` = ad). The spike's "object_type → actual kind" line in `meta-spike-findings.md` is binding. If it shows `CAMPAIGN` is a campaign, map `CAMPAIGN` → `Campaign` and change the matching `InlineData` row, then record the change in `meta-ads-api.md` §5.

- [ ] **Step 1: Add the fixtures**

`Fixtures/me.json`:

```json
{"id":"100000000000001","name":"Heblo Marketing"}
```

`Fixtures/activities.json`. `extra_data` is a JSON-encoded **string**, as Graph returns it:

```json
{"data":[
  {"event_time":"2026-10-06T08:15:00+0000","event_type":"update_ad_run_status","translated_event_type":"Ad status updated","actor_id":"100000000000001","actor_name":"Heblo Marketing","object_id":"120000000000000302","object_name":"Krém – carousel","object_type":"ADGROUP","extra_data":"{\"old_value\":\"ACTIVE\",\"new_value\":\"PAUSED\",\"type\":\"run_status\"}","application_name":"Heblo"},
  {"event_time":"2026-10-05T17:42:10+0000","event_type":"update_campaign_budget","translated_event_type":"Ad set budget updated","actor_id":"100000000000002","actor_name":"Jana Nováková","object_id":"120000000000000201","object_name":"CZ 25-54 ženy","object_type":"CAMPAIGN","extra_data":"{\"old_value\":{\"new_value\":25000,\"type\":\"daily_budget\"},\"new_value\":{\"new_value\":30000,\"type\":\"daily_budget\"}}"},
  {"event_time":"2026-10-04T03:00:01+0000","event_type":"ad_review_approved","translated_event_type":"Ad approved","object_id":"120000000000000301","object_name":"Sérum – video","object_type":"ADGROUP","extra_data":""}
]}
```

- [ ] **Step 2: Write the failing tests**

`MetaAdsReadSourceChangeEventsTests.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Adapters.MetaAds.Graph;
using Anela.Heblo.Adapters.MetaAds.Mapping;
using Anela.Heblo.Adapters.MetaAds.Tests.Support;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Adapters.MetaAds.Tests;

public class MetaAdsReadSourceChangeEventsTests
{
    private static readonly DateTimeOffset Since = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly RoutingGraphHandler _handler = MetaFixtureRoutes.ReadSource();

    private Task<IReadOnlyList<AdChangeEventRow>> Events() =>
        MetaTestHarness.CreateReadSource(_handler).GetChangeEventsAsync(MetaTestHarness.AccountId, Since, CancellationToken.None);

    [Fact]
    public async Task maps_heblos_own_system_user_as_Heblo_with_old_and_new_values()
    {
        var row = (await Events()).Single(e => e.ChangeType == "update_ad_run_status");

        row.ActorKind.Should().Be(AdChangeActorKind.Heblo);
        row.Actor.Should().Be("Heblo Marketing");
        row.EntityLevel.Should().Be(AdEntityLevel.Ad);
        row.EntityExternalId.Should().Be("120000000000000302");
        row.OccurredAt.Should().Be(new DateTimeOffset(2026, 10, 6, 8, 15, 0, TimeSpan.Zero));
        JsonDocument.Parse(row.OldValueJson!).RootElement.GetString().Should().Be("ACTIVE");
        JsonDocument.Parse(row.NewValueJson!).RootElement.GetString().Should().Be("PAUSED");
    }

    [Fact]
    public async Task maps_another_person_as_User_and_keeps_structured_values()
    {
        var row = (await Events()).Single(e => e.ChangeType == "update_campaign_budget");

        row.ActorKind.Should().Be(AdChangeActorKind.User);
        row.Actor.Should().Be("Jana Nováková");
        row.EntityLevel.Should().Be(AdEntityLevel.AdGroup);
        JsonDocument.Parse(row.NewValueJson!).RootElement.GetProperty("new_value").GetInt32().Should().Be(30000);
    }

    [Fact]
    public async Task event_without_actor_is_Unknown_with_no_values()
    {
        var row = (await Events()).Single(e => e.ChangeType == "ad_review_approved");

        row.ActorKind.Should().Be(AdChangeActorKind.Unknown);
        row.Actor.Should().BeNull();
        row.OldValueJson.Should().BeNull();
        row.NewValueJson.Should().BeNull();
    }

    [Fact]
    public async Task sends_since_as_unix_seconds_and_looks_up_own_identity_once()
    {
        var source = MetaTestHarness.CreateReadSource(_handler);

        await source.GetChangeEventsAsync(MetaTestHarness.AccountId, Since, CancellationToken.None);
        await source.GetChangeEventsAsync(MetaTestHarness.AccountId, Since, CancellationToken.None);

        var activityCall = _handler.Requests.First(r => r.Uri.AbsolutePath.EndsWith("/activities"));
        activityCall.Query["since"].Should().Be(Since.ToUnixTimeSeconds().ToString());
        activityCall.Query["fields"]!.Split(',').Should().Contain(new[] { "event_time", "event_type", "actor_id", "object_id", "object_type", "extra_data" });
        _handler.Requests.Count(r => r.Uri.AbsolutePath.EndsWith("/me")).Should().Be(1);
    }

    [Fact]
    public async Task external_event_ids_are_non_empty_distinct_and_stable()
    {
        var first = await Events();
        var second = await Events();

        first.Select(e => e.ExternalEventId).Should().OnlyHaveUniqueItems().And.OnlyContain(id => id.Length == 32);
        second.Select(e => e.ExternalEventId).Should().Equal(first.Select(e => e.ExternalEventId));
    }

    [Theory]
    [InlineData("CAMPAIGN_GROUP", AdEntityLevel.Campaign)]
    [InlineData("CAMPAIGN", AdEntityLevel.AdGroup)]
    [InlineData("AD_SET", AdEntityLevel.AdGroup)]
    [InlineData("ADSET", AdEntityLevel.AdGroup)]
    [InlineData("ADGROUP", AdEntityLevel.Ad)]
    [InlineData("AD", AdEntityLevel.Ad)]
    [InlineData("ACCOUNT", null)]
    [InlineData(null, null)]
    public void maps_object_type_to_level(string? objectType, AdEntityLevel? expected) =>
        MetaActivityMapper.LevelOf(objectType).Should().Be(expected);

    [Theory]
    [InlineData("2026-10-06T08:15:00+0000")]
    [InlineData("2026-10-06T10:15:00+0200")]
    [InlineData("2026-10-06T08:15:00+00:00")]
    public void parses_graph_timestamps(string value) =>
        MetaGraphTime.Parse(value).UtcDateTime.Should().Be(new DateTime(2026, 10, 6, 8, 15, 0, DateTimeKind.Utc));

    [Fact]
    public void non_json_extra_data_is_kept_as_a_json_string()
    {
        var row = MetaActivityMapper.ToRow(
            new MetaActivityDto { EventTime = "2026-10-06T08:15:00+0000", EventType = "x", ExtraData = "plain text" }, null);

        JsonDocument.Parse(row.NewValueJson!).RootElement.GetString().Should().Be("plain text");
        row.OldValueJson.Should().BeNull();
    }
}
```

- [ ] **Step 3: Run the tests and verify they fail**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.MetaAds.Tests -p:UseSharedCompilation=false`
Expected: the build FAILS with `CS0246` for `MetaActivityMapper`, `MetaGraphTime` and `MetaActivityDto`.

- [ ] **Step 4: Implement**

Append to `Graph/MetaGraphDtos.cs`:

```csharp
public sealed class MetaActivityDto
{
    [JsonPropertyName("event_time")] public string EventTime { get; set; } = string.Empty;
    [JsonPropertyName("event_type")] public string EventType { get; set; } = string.Empty;
    [JsonPropertyName("translated_event_type")] public string? TranslatedEventType { get; set; }
    [JsonPropertyName("actor_id")] public string? ActorId { get; set; }
    [JsonPropertyName("actor_name")] public string? ActorName { get; set; }
    [JsonPropertyName("object_id")] public string? ObjectId { get; set; }
    [JsonPropertyName("object_name")] public string? ObjectName { get; set; }
    [JsonPropertyName("object_type")] public string? ObjectType { get; set; }
    [JsonPropertyName("extra_data")] public string? ExtraData { get; set; }
    [JsonPropertyName("application_name")] public string? ApplicationName { get; set; }
}

public sealed class MetaMeDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string? Name { get; set; }
}
```

`Graph/MetaGraphTime.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace Anela.Heblo.Adapters.MetaAds.Graph;

/// <summary>Graph timestamps look like <c>2026-10-06T08:15:00+0000</c> (offset without a colon).</summary>
public static partial class MetaGraphTime
{
    public static DateTimeOffset Parse(string value)
    {
        var normalized = OffsetWithoutColon().Replace(value.Trim(), "$1:$2");
        return DateTimeOffset.Parse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }

    [GeneratedRegex(@"([+-]\d{2})(\d{2})$")]
    private static partial Regex OffsetWithoutColon();
}
```

`Mapping/MetaActivityMapper.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.MetaAds.Graph;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.MetaAds.Mapping;

public static class MetaActivityMapper
{
    private const int EventIdLength = 32;

    public static AdChangeEventRow ToRow(MetaActivityDto activity, string? ownActorId)
    {
        var (oldJson, newJson) = SplitExtraData(activity.ExtraData);
        return new AdChangeEventRow(
            ExternalEventId: ComputeEventId(activity),
            OccurredAt: MetaGraphTime.Parse(activity.EventTime),
            Actor: string.IsNullOrWhiteSpace(activity.ActorName) ? null : activity.ActorName,
            ActorKind: ActorKindOf(activity.ActorId, ownActorId),
            EntityLevel: LevelOf(activity.ObjectType),
            EntityExternalId: string.IsNullOrWhiteSpace(activity.ObjectId) ? null : activity.ObjectId,
            ChangeType: activity.EventType,
            OldValueJson: oldJson,
            NewValueJson: newJson);
    }

    /// <summary>Legacy Graph naming: CAMPAIGN_GROUP = campaign, CAMPAIGN = ad set, ADGROUP = ad (verified by the spike).</summary>
    public static AdEntityLevel? LevelOf(string? objectType) => objectType?.ToUpperInvariant() switch
    {
        "CAMPAIGN_GROUP" => AdEntityLevel.Campaign,
        "CAMPAIGN" or "AD_SET" or "ADSET" => AdEntityLevel.AdGroup,
        "ADGROUP" or "AD" => AdEntityLevel.Ad,
        _ => null,
    };

    /// <summary>/activities rows have no id; a content hash is stable across re-reads of the same event.</summary>
    public static string ComputeEventId(MetaActivityDto a)
    {
        var key = $"{a.EventTime}|{a.EventType}|{a.ObjectId}|{a.ActorId}|{a.ExtraData}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..EventIdLength].ToLowerInvariant();
    }

    private static AdChangeActorKind ActorKindOf(string? actorId, string? ownActorId)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return AdChangeActorKind.Unknown;
        }

        return string.Equals(actorId, ownActorId, StringComparison.Ordinal) ? AdChangeActorKind.Heblo : AdChangeActorKind.User;
    }

    private static (string? OldJson, string? NewJson) SplitExtraData(string? extraData)
    {
        if (string.IsNullOrWhiteSpace(extraData))
        {
            return (null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(extraData);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && (root.TryGetProperty("old_value", out _) || root.TryGetProperty("new_value", out _)))
            {
                return (Raw(root, "old_value"), Raw(root, "new_value"));
            }

            return (null, root.GetRawText());
        }
        catch (JsonException)
        {
            return (null, JsonSerializer.Serialize(extraData)); // keep non-JSON extra_data as a JSON string
        }
    }

    private static string? Raw(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) ? v.GetRawText() : null;
}
```

In `MetaAdsReadSource.cs`, add:

```csharp
    private const string ActivityPageLimit = "100";
    private const string ActivityFields =
        "event_time,event_type,translated_event_type,actor_id,actor_name,object_id,object_name,object_type,extra_data,application_name";

    private string? _ownActorId;
```

Replace the `GetChangeEventsAsync` placeholder with these two methods:

```csharp
    public async Task<IReadOnlyList<AdChangeEventRow>> GetChangeEventsAsync(string accountExternalId, DateTimeOffset since, CancellationToken ct)
    {
        EnsureConfiguredAccount(accountExternalId);
        var ownActorId = await GetOwnActorIdAsync(ct);
        var query = new Dictionary<string, string>
        {
            ["fields"] = ActivityFields,
            ["since"] = since.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            ["limit"] = ActivityPageLimit,
        };
        var activities = await _graph.GetAllPagesAsync<MetaActivityDto>($"{accountExternalId}/activities", query, ct);
        return activities.Select(a => MetaActivityMapper.ToRow(a, ownActorId)).ToList();
    }

    /// <summary>The token's own system-user id; changes it made are Heblo's. Cached for the scope's lifetime.</summary>
    private async Task<string> GetOwnActorIdAsync(CancellationToken ct)
    {
        _ownActorId ??= (await _graph.GetAsync<MetaMeDto>("me", Fields("id,name"), ct)).Id;
        return _ownActorId;
    }
```

- [ ] **Step 5: Run the tests and verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Adapters.MetaAds.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.MetaAds.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~MetaAdsReadSourceChangeEventsTests"
```

Expected: `Passed!`, 0 failed. Also run `grep -n "NotSupportedException" backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsReadSource.cs`. Expected: no output; both placeholders are gone.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.MetaAds backend/test/Anela.Heblo.Adapters.MetaAds.Tests
git commit -m "feat: read Meta ad account activity history as change events

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 7: Contract suite and gated registration

**Files:**
- Create: `backend/test/Anela.Heblo.Adapters.MetaAds.Tests/MetaAdsReadSourceContractTests.cs`
- Create: `backend/test/Anela.Heblo.Adapters.MetaAds.Tests/MetaAdsRegistrationTests.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsAdapterServiceCollectionExtensions.cs`

**Interfaces:**
- Consumes: C1 `AdPlatformReadSourceContractTests` (abstract `CreateSource()`, `AccountExternalId`, `FixtureDate`), C1 `AdSettingsGuard.IsConfigured(params string?[])`; `MetaFixtureRoutes.ReadSource()` (Task 4)
- Produces: `AddMetaAdsAdapter(IServiceCollection, IConfiguration)` also registers `MetaGraphClient` (typed `HttpClient`) and `IAdPlatformReadSource → MetaAdsReadSource` (scoped) **only** when `AdSettingsGuard.IsConfigured(AccessToken, AccountId)`. Part C adds `IAdActionExecutor` inside the same gate, via the private helper `AddAdPlatformServices`.

- [ ] **Step 1: Write the contract test and the registration tests**

`MetaAdsReadSourceContractTests.cs`. Use the abstract member names exactly as they appear on `origin/main`:

```csharp
using Anela.Heblo.Adapters.MetaAds.Tests.Support;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.MarketingAds.TestKit;

namespace Anela.Heblo.Adapters.MetaAds.Tests;

public sealed class MetaAdsReadSourceContractTests : AdPlatformReadSourceContractTests
{
    protected override IAdPlatformReadSource CreateSource() => MetaTestHarness.CreateReadSource(MetaFixtureRoutes.ReadSource());

    protected override string AccountExternalId => MetaTestHarness.AccountId;

    protected override DateOnly FixtureDate => MetaTestHarness.FixtureDate;
}
```

`MetaAdsRegistrationTests.cs`:

```csharp
using Anela.Heblo.Adapters.MetaAds.Graph;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.MetaAds.Tests;

public class MetaAdsRegistrationTests
{
    private static IServiceCollection Register(string? token, string? accountId)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MetaAds:AccessToken"] = token,
                ["MetaAds:AccountId"] = accountId,
                ["MetaAds:ApiVersion"] = "v25.0",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetaAdsAdapter(configuration);
        return services;
    }

    [Fact]
    public void registers_the_read_source_when_really_configured()
    {
        var services = Register("EAAB-real-looking-token", "act_1234567890");

        services.Should().Contain(d => d.ServiceType == typeof(IAdPlatformReadSource) && d.ImplementationType == typeof(MetaAdsReadSource));
    }

    [Theory]
    [InlineData(null, "act_1234567890")]
    [InlineData("", "act_1234567890")]
    [InlineData("-- stored in Key Vault --", "act_1234567890")]
    [InlineData("EAAB-real-looking-token", "act_XXXXXXXXX")]
    [InlineData("EAAB-real-looking-token", null)]
    public void registers_no_ad_platform_service_when_settings_are_missing_or_placeholders(string? token, string? accountId)
    {
        var services = Register(token, accountId);

        services.Should().NotContain(d => d.ServiceType == typeof(IAdPlatformReadSource));
        services.Should().NotContain(d => d.ServiceType == typeof(IAdActionExecutor));
    }

    [Fact]
    public void keeps_the_billing_importer_registered_unconditionally()
    {
        var services = Register(null, null);

        services.Should().Contain(d => d.ServiceType == typeof(IMarketingTransactionSource));
    }

    [Fact]
    public void resolves_the_read_source_from_the_container()
    {
        using var provider = Register("EAAB-real-looking-token", "act_1234567890").BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var source = scope.ServiceProvider.GetServices<IAdPlatformReadSource>().Should().ContainSingle().Subject;

        source.Should().BeOfType<MetaAdsReadSource>();
        scope.ServiceProvider.GetRequiredService<MetaGraphClient>().Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Run the tests and verify they fail**

```bash
dotnet build backend/test/Anela.Heblo.Adapters.MetaAds.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.MetaAds.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~MetaAdsRegistrationTests|FullyQualifiedName~MetaAdsReadSourceContractTests"
```

Expected: the registration tests `registers_the_read_source_when_really_configured` and `resolves_the_read_source_from_the_container` FAIL, because nothing registers `IAdPlatformReadSource`. The contract tests should already PASS. If any contract test fails, fix the fixture or the read source (not the contract base), re-run, and record what it caught in the PR body.

- [ ] **Step 3: Implement the gated registration**

Replace the body of `MetaAdsAdapterServiceCollectionExtensions.cs` with the code below. The billing lines are unchanged:

```csharp
using Anela.Heblo.Adapters.MetaAds.Graph;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.MetaAds;

public static class MetaAdsAdapterServiceCollectionExtensions
{
    private static readonly TimeSpan GraphTimeout = TimeSpan.FromSeconds(100);

    public static IServiceCollection AddMetaAdsAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(MetaAdsSettings.ConfigurationKey);
        services.Configure<MetaAdsSettings>(section);

        // Legacy billing import (sync-ad-platform-transactions) — unchanged, registered unconditionally.
        services.AddHttpClient<MetaAdsTransactionSource>();
        services.AddScoped<IMarketingTransactionSource>(sp =>
            sp.GetRequiredService<MetaAdsTransactionSource>());
        services.AddScoped<IRecurringJob, MetaAdsInvoiceImportJob>();

        AddAdPlatformServices(services, section);
        return services;
    }

    /// <summary>
    /// Marketing-ads read source (and, in Part C, executor). Registered only when the token and account are
    /// really configured — placeholders like "act_XXXXXXXXX" or "-- stored in Key Vault --" leave the platform out.
    /// Never throws: a misconfigured environment simply has no Meta source.
    /// </summary>
    private static void AddAdPlatformServices(IServiceCollection services, IConfigurationSection section)
    {
        var settings = section.Get<MetaAdsSettings>() ?? new MetaAdsSettings();
        if (!AdSettingsGuard.IsConfigured(settings.AccessToken, settings.AccountId))
        {
            return;
        }

        services.AddHttpClient<MetaGraphClient>(client => client.Timeout = GraphTimeout);
        services.AddScoped<IAdPlatformReadSource, MetaAdsReadSource>();
    }
}
```

- [ ] **Step 4: Run all adapter tests and verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Adapters.MetaAds.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.MetaAds.Tests --no-build -p:UseSharedCompilation=false
```

Expected: `Passed!`, 0 failed. This includes every inherited contract test.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.MetaAds backend/test/Anela.Heblo.Adapters.MetaAds.Tests
git commit -m "feat: register Meta ads read source only when really configured

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 8: Process doc, full validation, PR

**Files:**
- Create: `docs/processes/sync-ads-meta.md`
- Modify (generated): `docs/processes/INDEX.md`

**Interfaces:**
- Consumes: everything from Tasks 2–7
- Produces: PR `feat/marketing-meta-read-source` → `main`

- [ ] **Step 1: Find the core sync docs to link**

Run: `ls docs/processes | grep -iE 'ads'`
The C2 plan creates `sync-ads-daily.md`, `sync-ads-change-history.md` and `module-marketing-ads.md`. Put every one of the two `sync-ads-*` stems that exists on main into `related:` below, e.g. `related: [sync-ads-daily, sync-ads-change-history]`. If neither exists yet, leave `related: []`, because `check.py` rejects unknown names.

- [ ] **Step 2: Write `docs/processes/sync-ads-meta.md`**

```markdown
---
process: sync-ads-meta
kind: sync
module: marketing-ads
summary: Meta (Facebook/Instagram) ad entities, daily insights per campaign/ad set/ad and the account activity log, read for the marketing-ads backbone that AI agents and Metabase use.
owns:
  - backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsReadSource.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Graph/**
  - backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Mapping/**
  - backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsAdapterServiceCollectionExtensions.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsSettings.cs
verified_at: "<git rev-parse --short origin/main, quoted>"
related: []
---

# Meta Ads read stream (marketing-ads backbone)

## Purpose
Gives the marketing-ads backbone (`ads` schema) Meta's side of "what did we spend, what did it bring, what changed": entities at campaign → ad set (`AdGroup`) → ad level, platform-reported daily cost/impressions/link clicks/purchases/purchase value, and the account change history flagged Heblo vs out-of-band. Agents read it over MCP; people read it in Metabase (`v_ads_*`).

## Trigger
No job of its own. `MetaAdsReadSource` is one of the `IAdPlatformReadSource` implementations the core jobs iterate (`AdsDailySyncJob`, 05:30 Europe/Prague: entities + yesterday and a 14-day lookback of facts; `AdsChangeSyncJob`, hourly: change events since the watermark). It only exists in DI when `MetaAds:AccessToken` and `MetaAds:AccountId` are really configured (`AdSettingsGuard`).

## Data flow
1. `GET /{act}` → one `AdAccountSnapshot` (the configured account only).
2. `GET /{act}/campaigns|adsets|ads` (paged, `limit=500`) → `AdEntitySnapshot`s.
3. Per date: `GET /{act}/insights` × `level=campaign|adset|ad`, single-day `time_range`, `time_increment=1`, unified attribution → `AdDailyFactRow`s.
4. `GET /me` (once per scope) + `GET /{act}/activities?since=<unix>` → `AdChangeEventRow`s.
5. The core upserts these into `ads.ad_accounts`, `ads.ad_entities`, `ads.ad_daily_facts` and `ads.ad_change_events`.
Transport: `MetaGraphClient` (bearer header, `graph.facebook.com/{MetaAds:ApiVersion}`).

## Logic & formulas
- Cost = `spend` (account currency, excl. VAT); Clicks = `inline_link_clicks`; Impressions = `impressions`; invariant-culture parsing.
- Conversions and conversion value: ONE action type per row: `omni_purchase`, else `purchase`, else `offsite_conversion.fb_pixel_purchase`; the value comes from the same type. Never summed across types.
- Rows whose `date_start` ≠ the requested date are dropped (with a warning log).
- Facts exist at all three levels; sum one level only.
- Status = the object's own `status` (ACTIVE→Enabled, PAUSED→Paused, DELETED/ARCHIVED→Removed); `effective_status` goes into attributes.
- Change events: actor = `/me` id → Heblo; other actor → User; none → Unknown. `ExternalEventId` = sha256(event_time|event_type|object_id|actor_id|extra_data)[0..32].
- Retries: max 3, 2/4/8 s, or Meta's regain hint when longer; fails fast if the hint is over 60 s. Transient = 429, 5xx, `is_transient`, codes 4/17/613/80000/80004, timeouts. Other 4xx fail immediately.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `MetaAds:AccountId` | `act_XXXXXXXXX` (placeholder → source not registered) | ad account, `act_…`; Key Vault `MetaAds--AccountId` |
| `MetaAds:AccessToken` | *(none)* | system-user token (`ads_read`; `ads_management` for the executor); Key Vault `MetaAds--AccessToken` |
| `MetaAds:ApiVersion` | `<chosen>` | Marketing API version; bump yearly (meta-ads-api.md §3) |

## Runtime facts
<one line per spike fact that is not derivable from code — e.g. account currency/time zone, purchase types present, activities retention — each "fact — Meta access spike — <date>">

## Known quirks
- Graph embeds `access_token` in `paging.next`; the client strips it and refuses foreign hosts.
- `/activities` uses legacy object names (`CAMPAIGN` = ad set); see meta-ads-api.md §5.
- Deleted ads may be missing from ad-level insights while still counted at campaign level, so level totals can differ slightly.
- `MetaAdsSettings` is shared with the legacy billing importer (`sync-ad-platform-transactions`).

## Code entry points
- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsReadSource.cs` — the four read calls
- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Graph/MetaGraphClient.cs` — auth, paging, retry, redaction
- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Mapping/MetaConversionSelector.cs` — purchase type choice
- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Mapping/MetaActivityMapper.cs` — change-event mapping
- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsAdapterServiceCollectionExtensions.cs` — gated registration
```

Fill `verified_at`, the `<chosen>` version, and *Runtime facts* from the findings file before committing. No `<…>` may remain:

```bash
grep -nE '<(git rev-parse|chosen|one line)' docs/processes/sync-ads-meta.md || echo "no unfilled markers"
```

- [ ] **Step 3: Regenerate the index and check the docs**

```bash
python3 scripts/process-docs/check.py index
python3 scripts/process-docs/check.py check
```

Expected: `wrote docs/processes/INDEX.md (N processes)`, then no `ERROR` lines (pre-existing `WARN`/`INFO` are fine).

- [ ] **Step 4: Full backend validation**

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.MetaAds.Tests --no-build -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~MetaAds|FullyQualifiedName~MarketingInvoices"
dotnet format Anela.Heblo.sln
git status --short
```

Expected: build `0 Error(s)`; both test runs `Passed!`; after `dotnet format`, `git status` lists only files this PR touched. If `dotnet format` changed a file you did not touch, revert it with `git checkout -- <file>`. If the tests hang at 0 % CPU, another worktree is running `dotnet test`. Wait, then rerun.

- [ ] **Step 5: Commit and open the PR**

```bash
git add docs/processes/sync-ads-meta.md docs/processes/INDEX.md
git add -u
git commit -m "docs: add Meta ads read stream process doc

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git push -u origin feat/marketing-meta-read-source
gh pr create --base main --title "feat: Meta Ads read source for the marketing-ads backbone" --body "$(cat <<'BODY'
## Summary
- `MetaAdsReadSource : IAdPlatformReadSource`: account, campaigns/ad sets/ads, daily insights at three levels (one purchase action type, no double counting), `/activities` change history.
- `MetaGraphClient`: bearer-only auth, `paging.next` token stripping + host check, transient-only retry with Meta throttling hints, path-only logging.
- Registered only when `MetaAds:AccessToken` + `MetaAds:AccountId` pass `AdSettingsGuard`; billing importer untouched.
- `MetaAds:ApiVersion` default v21.0 → <chosen> (v21.0 expired 2025-09-09).
- Spike findings: `docs/integrations/meta-ads-api.md`; process doc `sync-ads-meta`.

## Spec deviations
See the plan `docs/superpowers/plans/2026-10-07-marketing-meta.md` § Spec deviations (status from own `status`, link clicks, extra retry codes, synthesized activity ids).

## Rollout (Ondrej)
- [ ] `az keyvault secret set --vault-name kv-heblo-stg --name "MetaAds--AccessToken" --value "<token>"` and `--name "MetaAds--AccountId" --value "act_…"`; restart heblo-test.
- [ ] After staging looks right, the same for `kv-heblo-prod` and restart heblo.

## Test plan
- [ ] `dotnet test backend/test/Anela.Heblo.Adapters.MetaAds.Tests` (unit + inherited C1 contract suite)
- [ ] Existing MetaAds/MarketingInvoices tests in Anela.Heblo.Tests
- [ ] `python3 scripts/process-docs/check.py check`
- [ ] After C2 + secrets on staging: `ads.ad_daily_facts` holds yesterday's Meta rows; campaign-level cost matches Ads Manager

🤖 Generated with [Claude Code](https://claude.com/claude-code)
BODY
)"
```

Replace `<chosen>` in the PR body with the real version before running. Leave `<token>` as literal text: it is Ondrej's instruction, not a value. Then wait for checks: `gh pr checks --watch`. "no checks reported" right after creation means the checks have not registered yet. Wait and re-run.

---

# Part C — Executor PR (`PauseAd`)

**Prerequisites:** core PR C1 **and** the Part B PR are merged on `origin/main`. Check:

```bash
git fetch origin
git cat-file -e origin/main:backend/src/Anela.Heblo.Application/Features/MarketingAds/Contracts/IAdActionExecutor.cs && echo "C1 ok"
git cat-file -e origin/main:backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Graph/MetaGraphClient.cs && echo "Part B ok"
git switch -c feat/marketing-meta-executor origin/main
```

If either check prints nothing, **stop** and report which prerequisite is missing. Read `docs/integrations/meta-ads-api.md` §2. If `ads_management` is not granted, still build the executor (it is tested on fakes), but say in the PR body that the manual smoke test is blocked until Ondrej adds the scope.

### Task 9: `MetaAdsActionExecutor`

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsActionExecutor.cs`
- Create: `backend/test/Anela.Heblo.Adapters.MetaAds.Tests/Support/MetaAdStateFake.cs`
- Modify: `backend/test/Anela.Heblo.Adapters.MetaAds.Tests/Support/MetaTestHarness.cs` (add `CreateExecutor`, `PauseAd`)
- Create: `backend/test/Anela.Heblo.Adapters.MetaAds.Tests/MetaAdsActionExecutorTests.cs`

**Interfaces:**
- Consumes: `MetaGraphClient.GetRawAsync`, `PostFormAsync`, `MetaGraphException.IsNotFound/IsAuthError/IsTransient/ResponseBody` (Task 3); `MetaStatusMapper.Active/Paused/ToActionValue` and `MetaAdDto` (Task 4); C1 `IAdActionExecutor`, `AdAction`, `AdTargetState`, `AdExecutionResult`, `AdExecutionOutcome`, `AdActionValues`
- Produces: `public sealed class MetaAdsActionExecutor(MetaGraphClient graph, IOptions<MetaAdsSettings> options, ILogger<MetaAdsActionExecutor> logger) : IAdActionExecutor`, with `SupportedActions = { PauseAd }`

Behaviour:
- `ReadCurrentAsync` → `GET /{ad_id}?fields=id,status,effective_status,account_id`. It returns `Exists = false` when the ad is missing (Graph 100/33), when it belongs to another account, or when the action names an account other than the configured one. `CurrentValue` = `Enabled`/`Paused` from the ad's own `status`, otherwise the raw Meta status (e.g. `ARCHIVED`), so the core's comparison with `OldValue` fails as `StaleState`.
- `ExecuteAsync` → reads the before-state, `POST /{ad_id}` with form `status=PAUSED`, reads back. It returns `Succeeded` only when the read-back is `Paused`. A platform-side rejection or a permission error returns `Failed` with the Graph error JSON. An auth error (190/102) or an exhausted transient error throws.
- `RevertAsync` → the same path with `status=ACTIVE`. It refuses (`Failed`, no HTTP call) when the original outcome was not `Succeeded`.
- Any action other than `PauseAd` on `MetaAds`/`Ad` → `NotSupportedException` (a caller bug; the core consults `SupportedActions`).

- [ ] **Step 1: Write the stateful fake and harness helpers**

`Support/MetaAdStateFake.cs`:

```csharp
using System.Net;
using System.Text.Json;

namespace Anela.Heblo.Adapters.MetaAds.Tests.Support;

/// <summary>Stateful fake of Graph's ad node: GET reads, POST status=… writes. Unknown ids answer 100/33.</summary>
internal sealed class MetaAdStateFake
{
    private readonly Dictionary<string, (string Status, string AccountDigits)> _ads = new();

    public RoutingGraphHandler Handler { get; } = new();

    public bool IgnoreWrites { get; set; }

    public (int Code, string Message)? RejectWritesWith { get; set; }

    public MetaAdStateFake WithAd(string adId, string status, string accountDigits = MetaTestHarness.AccountDigits)
    {
        _ads[adId] = (status, accountDigits);
        Handler.On(HttpMethod.Get, adId, _ => Read(adId));
        Handler.On(HttpMethod.Post, adId, request => Write(adId, request));
        return this;
    }

    public string StatusOf(string adId) => _ads[adId].Status;

    public int PostCount => Handler.Requests.Count(r => r.Method == HttpMethod.Post);

    private HttpResponseMessage Read(string adId)
    {
        var (status, account) = _ads[adId];
        return RoutingGraphHandler.Json(JsonSerializer.Serialize(new
        {
            id = adId,
            status,
            effective_status = status,
            account_id = account,
        }));
    }

    private HttpResponseMessage Write(string adId, RecordedRequest request)
    {
        if (RejectWritesWith is { } reject)
        {
            return RoutingGraphHandler.GraphError(HttpStatusCode.BadRequest, reject.Code, null, reject.Message);
        }

        var status = request.Form["status"];
        if (status is not ("ACTIVE" or "PAUSED"))
        {
            return RoutingGraphHandler.GraphError(HttpStatusCode.BadRequest, 100, null, $"Invalid status '{status}'");
        }

        if (!IgnoreWrites)
        {
            _ads[adId] = (status, _ads[adId].AccountDigits);
        }

        return RoutingGraphHandler.Json("""{"success":true}""");
    }
}
```

Append to `MetaTestHarness` (add `using Anela.Heblo.Application.Features.MarketingAds.Contracts;`):

```csharp
    public static MetaAdsActionExecutor CreateExecutor(HttpMessageHandler handler) =>
        new(CreateGraphClient(handler), Options.Create(Settings()), NullLogger<MetaAdsActionExecutor>.Instance);

    public static AdAction PauseAd(string adId, string accountId = AccountId) => new(
        AdActionType.PauseAd, AdPlatform.MetaAds, accountId, AdEntityLevel.Ad, adId,
        AdActionValues.Enabled, AdActionValues.Paused, new Dictionary<string, string>());
```

- [ ] **Step 2: Write the failing tests**

`MetaAdsActionExecutorTests.cs`:

```csharp
using Anela.Heblo.Adapters.MetaAds.Graph;
using Anela.Heblo.Adapters.MetaAds.Tests.Support;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Adapters.MetaAds.Tests;

public class MetaAdsActionExecutorTests
{
    private const string AdId = "120000000000000301";
    private readonly MetaAdStateFake _fake = new MetaAdStateFake().WithAd(AdId, "ACTIVE");

    private MetaAdsActionExecutor Executor() => MetaTestHarness.CreateExecutor(_fake.Handler);

    [Fact]
    public void supports_only_pause_ad_on_meta()
    {
        var executor = Executor();

        executor.Platform.Should().Be(AdPlatform.MetaAds);
        executor.SupportedActions.Should().BeEquivalentTo(new[] { AdActionType.PauseAd });
    }

    [Fact]
    public async Task reads_an_active_ad_as_enabled()
    {
        var state = await Executor().ReadCurrentAsync(MetaTestHarness.PauseAd(AdId), CancellationToken.None);

        state.Exists.Should().BeTrue();
        state.CurrentValue.Should().Be(AdActionValues.Enabled);
        state.RawJson.Should().Contain(AdId);
    }

    [Fact]
    public async Task reports_an_unknown_ad_as_missing()
    {
        var state = await Executor().ReadCurrentAsync(MetaTestHarness.PauseAd("999"), CancellationToken.None);

        state.Exists.Should().BeFalse();
    }

    [Fact]
    public async Task treats_an_ad_from_another_account_as_missing()
    {
        _fake.WithAd("120000000000000999", "ACTIVE", accountDigits: "5555555555");

        var state = await Executor().ReadCurrentAsync(MetaTestHarness.PauseAd("120000000000000999"), CancellationToken.None);

        state.Exists.Should().BeFalse();
    }

    [Fact]
    public async Task refuses_an_action_naming_an_unconfigured_account_without_calling_meta()
    {
        var action = MetaTestHarness.PauseAd(AdId, accountId: "act_5555555555");

        var state = await Executor().ReadCurrentAsync(action, CancellationToken.None);
        var result = await Executor().ExecuteAsync(action, CancellationToken.None);

        state.Exists.Should().BeFalse();
        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        _fake.Handler.Requests.Should().BeEmpty();
        _fake.StatusOf(AdId).Should().Be("ACTIVE");
    }

    [Fact]
    public async Task pauses_with_a_form_post_and_reports_before_and_after()
    {
        var result = await Executor().ExecuteAsync(MetaTestHarness.PauseAd(AdId), CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Succeeded);
        result.BeforeValue.Should().Be(AdActionValues.Enabled);
        result.AfterValue.Should().Be(AdActionValues.Paused);
        result.PlatformResourceId.Should().Be(AdId);
        _fake.StatusOf(AdId).Should().Be("PAUSED");

        var post = _fake.Handler.Requests.Single(r => r.Method == HttpMethod.Post);
        post.Form["status"].Should().Be("PAUSED");
        post.Form.AllKeys.Should().BeEquivalentTo("status");
        post.Query.AllKeys.Should().BeEmpty();
        post.Authorization.Should().Be($"Bearer {MetaTestHarness.Token}");
    }

    [Fact]
    public async Task returns_failed_without_throwing_when_meta_rejects_the_write()
    {
        _fake.RejectWritesWith = (200, "Requires ads_management permission");

        var result = await Executor().ExecuteAsync(MetaTestHarness.PauseAd(AdId), CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        result.Error.Should().Contain("ads_management");
        result.PlatformResponseJson.Should().Contain("\"code\":200");
        _fake.StatusOf(AdId).Should().Be("ACTIVE");
    }

    [Fact]
    public async Task throws_when_the_token_is_invalid()
    {
        _fake.RejectWritesWith = (190, "Invalid OAuth access token");

        var act = () => Executor().ExecuteAsync(MetaTestHarness.PauseAd(AdId), CancellationToken.None);

        (await act.Should().ThrowAsync<MetaGraphException>()).Which.IsAuthError.Should().BeTrue();
    }

    [Fact]
    public async Task returns_failed_when_the_read_back_disagrees()
    {
        _fake.IgnoreWrites = true;

        var result = await Executor().ExecuteAsync(MetaTestHarness.PauseAd(AdId), CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        result.AfterValue.Should().Be(AdActionValues.Enabled);
    }

    [Fact]
    public async Task revert_sets_the_ad_active_again()
    {
        var executor = Executor();
        var action = MetaTestHarness.PauseAd(AdId);
        var original = await executor.ExecuteAsync(action, CancellationToken.None);

        var reverted = await executor.RevertAsync(action, original, CancellationToken.None);

        reverted.Outcome.Should().Be(AdExecutionOutcome.Succeeded);
        reverted.AfterValue.Should().Be(AdActionValues.Enabled);
        _fake.StatusOf(AdId).Should().Be("ACTIVE");
        _fake.Handler.Requests.Last(r => r.Method == HttpMethod.Post).Form["status"].Should().Be("ACTIVE");
    }

    [Fact]
    public async Task revert_refuses_when_the_original_execution_did_not_succeed()
    {
        var failed = new AdExecutionResult(AdExecutionOutcome.Failed, null, null, AdId, null, "boom");

        var result = await Executor().RevertAsync(MetaTestHarness.PauseAd(AdId), failed, CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        _fake.PostCount.Should().Be(0);
    }

    [Fact]
    public async Task reports_an_archived_ad_by_its_raw_status_so_the_core_sees_stale_state()
    {
        _fake.WithAd("120000000000000304", "ARCHIVED");

        var state = await Executor().ReadCurrentAsync(MetaTestHarness.PauseAd("120000000000000304"), CancellationToken.None);

        state.Exists.Should().BeTrue();
        state.CurrentValue.Should().Be("ARCHIVED");
    }

    [Fact]
    public async Task rejects_add_negative_keyword()
    {
        var action = new AdAction(
            AdActionType.AddNegativeKeyword, AdPlatform.MetaAds, MetaTestHarness.AccountId, AdEntityLevel.AdGroup, "1",
            AdActionValues.Absent, AdActionValues.Present,
            new Dictionary<string, string> { [AdActionPayloadKeys.Text] = "zdarma", [AdActionPayloadKeys.MatchType] = "Exact" });

        var act = () => Executor().ExecuteAsync(action, CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
```

- [ ] **Step 3: Run the tests and verify they fail**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.MetaAds.Tests -p:UseSharedCompilation=false`
Expected: the build FAILS with `CS0246: The type or namespace name 'MetaAdsActionExecutor' could not be found`.

- [ ] **Step 4: Implement the executor**

`MetaAdsActionExecutor.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Adapters.MetaAds.Graph;
using Anela.Heblo.Adapters.MetaAds.Mapping;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.MetaAds;

/// <summary>
/// Executes approved Meta actions. v1: PauseAd only (POST /{ad_id} status=PAUSED; revert → ACTIVE).
/// Old-value comparison is the core's job; this class only refuses ads outside the configured account.
/// </summary>
public sealed class MetaAdsActionExecutor : IAdActionExecutor
{
    private const string AccountPrefix = "act_";
    private const string AdFields = "id,status,effective_status,account_id";
    private static readonly IReadOnlySet<AdActionType> Supported = new HashSet<AdActionType> { AdActionType.PauseAd };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly MetaGraphClient _graph;
    private readonly MetaAdsSettings _settings;
    private readonly ILogger<MetaAdsActionExecutor> _logger;

    public MetaAdsActionExecutor(MetaGraphClient graph, IOptions<MetaAdsSettings> options, ILogger<MetaAdsActionExecutor> logger)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _settings = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public AdPlatform Platform => AdPlatform.MetaAds;

    public IReadOnlySet<AdActionType> SupportedActions => Supported;

    public async Task<AdTargetState> ReadCurrentAsync(AdAction action, CancellationToken ct)
    {
        EnsureSupported(action);
        var ad = await ReadOwnAdAsync(action, ct);
        return ad is null
            ? new AdTargetState(false, null, null)
            : new AdTargetState(true, CurrentValue(ad.Value.Dto), ad.Value.Raw);
    }

    public Task<AdExecutionResult> ExecuteAsync(AdAction action, CancellationToken ct)
    {
        EnsureSupported(action);
        return SetStatusAsync(action, MetaStatusMapper.Paused, AdActionValues.Paused, ct);
    }

    public Task<AdExecutionResult> RevertAsync(AdAction action, AdExecutionResult original, CancellationToken ct)
    {
        EnsureSupported(action);
        if (original.Outcome != AdExecutionOutcome.Succeeded)
        {
            return Task.FromResult(Failed(action, null, null, null, "Nothing to revert: the original execution did not succeed."));
        }

        return SetStatusAsync(action, MetaStatusMapper.Active, AdActionValues.Enabled, ct);
    }

    private async Task<AdExecutionResult> SetStatusAsync(AdAction action, string metaStatus, string expectedValue, CancellationToken ct)
    {
        var before = await ReadOwnAdAsync(action, ct);
        if (before is null)
        {
            return Failed(action, null, null, null, $"Ad {action.TargetExternalId} does not exist in account {_settings.AccountId}.");
        }

        var beforeValue = CurrentValue(before.Value.Dto);
        string response;
        try
        {
            response = await _graph.PostFormAsync(action.TargetExternalId, new Dictionary<string, string> { ["status"] = metaStatus }, ct);
        }
        catch (MetaGraphException ex) when (!ex.IsAuthError && !ex.IsTransient)
        {
            _logger.LogWarning("Meta rejected status={Status} for ad {AdId}: {Error}", metaStatus, action.TargetExternalId, ex.Message);
            return Failed(action, beforeValue, beforeValue, ex.ResponseBody, ex.Message);
        }

        var after = await ReadOwnAdAsync(action, ct);
        var afterValue = after is null ? null : CurrentValue(after.Value.Dto);
        if (afterValue != expectedValue)
        {
            return Failed(action, beforeValue, afterValue, response,
                $"Meta accepted status={metaStatus} but ad {action.TargetExternalId} now reads {afterValue ?? "missing"}.");
        }

        _logger.LogInformation("Meta ad {AdId} status {Before} -> {After}", action.TargetExternalId, beforeValue, afterValue);
        return new AdExecutionResult(AdExecutionOutcome.Succeeded, beforeValue, afterValue, action.TargetExternalId, response, null);
    }

    /// <summary>Null when the ad is missing, outside the configured account, or the action names another account.</summary>
    private async Task<(MetaAdDto Dto, string Raw)?> ReadOwnAdAsync(AdAction action, CancellationToken ct)
    {
        if (!string.Equals(action.AccountExternalId, _settings.AccountId, StringComparison.Ordinal))
        {
            return null;
        }

        string raw;
        try
        {
            raw = await _graph.GetRawAsync(action.TargetExternalId, new Dictionary<string, string> { ["fields"] = AdFields }, ct);
        }
        catch (MetaGraphException ex) when (ex.IsNotFound)
        {
            return null;
        }

        var dto = JsonSerializer.Deserialize<MetaAdDto>(raw, JsonOptions)
                  ?? throw MetaGraphException.Permanent($"Meta returned an empty body for ad {action.TargetExternalId}.");
        var ownAccount = string.Equals(AccountPrefix + dto.AccountId, _settings.AccountId, StringComparison.Ordinal);
        return ownAccount ? (dto, raw) : null;
    }

    private static string CurrentValue(MetaAdDto ad) => MetaStatusMapper.ToActionValue(ad.Status) ?? ad.Status ?? "Unknown";

    private static AdExecutionResult Failed(AdAction action, string? before, string? after, string? response, string error) =>
        new(AdExecutionOutcome.Failed, before, after, action.TargetExternalId, response, error);

    private static void EnsureSupported(AdAction action)
    {
        if (action.Type != AdActionType.PauseAd || action.Platform != AdPlatform.MetaAds || action.TargetLevel != AdEntityLevel.Ad)
        {
            throw new NotSupportedException(
                $"MetaAdsActionExecutor supports PauseAd on Meta ads only; got {action.Type} on {action.Platform}/{action.TargetLevel}.");
        }
    }
}
```

If the spike found that `account_id` comes back **with** the `act_` prefix, compare `dto.AccountId` to `_settings.AccountId` directly instead of prepending `AccountPrefix`, and change `MetaAdStateFake.Read` to match.

- [ ] **Step 5: Run the tests and verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Adapters.MetaAds.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.MetaAds.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~MetaAdsActionExecutorTests"
```

Expected: `Passed!`, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsActionExecutor.cs backend/test/Anela.Heblo.Adapters.MetaAds.Tests
git commit -m "feat: pause and resume Meta ads through MetaAdsActionExecutor

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 10: Executor contract suite, registration, smoke procedure, PR

**Files:**
- Create: `backend/test/Anela.Heblo.Adapters.MetaAds.Tests/MetaAdsActionExecutorContractTests.cs`
- Modify: `backend/test/Anela.Heblo.Adapters.MetaAds.Tests/MetaAdsRegistrationTests.cs` (add executor cases)
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsAdapterServiceCollectionExtensions.cs` (one line inside `AddAdPlatformServices`)
- Modify: `docs/integrations/meta-ads-api.md` (new §9 executor + manual smoke procedure)
- Modify: `docs/processes/sync-ads-meta.md` (`owns` + a write-path paragraph)
- Modify (generated): `docs/processes/INDEX.md`

**Interfaces:**
- Consumes: C1 `AdActionExecutorContractTests` (abstract `CreateExecutor()`, `SamplePauseAd()`, `SampleAddNegativeKeyword()`); `MetaAdStateFake`, `MetaTestHarness.CreateExecutor/PauseAd` (Task 9)
- Produces: `IAdActionExecutor → MetaAdsActionExecutor` (scoped), registered under the same `AdSettingsGuard` gate as the read source

- [ ] **Step 1: Write the contract test and the registration tests**

`MetaAdsActionExecutorContractTests.cs`:

```csharp
using Anela.Heblo.Adapters.MetaAds.Tests.Support;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.MarketingAds.TestKit;

namespace Anela.Heblo.Adapters.MetaAds.Tests;

public sealed class MetaAdsActionExecutorContractTests : AdActionExecutorContractTests
{
    private const string AdId = "120000000000000301";

    protected override IAdActionExecutor CreateExecutor() =>
        MetaTestHarness.CreateExecutor(new MetaAdStateFake().WithAd(AdId, "ACTIVE").Handler);

    protected override AdAction SamplePauseAd() => MetaTestHarness.PauseAd(AdId);

    protected override AdAction? SampleAddNegativeKeyword() => null; // Meta has no keywords
}
```

Add to `MetaAdsRegistrationTests`:

```csharp
    [Fact]
    public void registers_the_executor_under_the_same_gate()
    {
        var services = Register("EAAB-real-looking-token", "act_1234567890");

        services.Should().Contain(d => d.ServiceType == typeof(IAdActionExecutor) && d.ImplementationType == typeof(MetaAdsActionExecutor));
    }

    [Fact]
    public void resolves_the_executor_from_the_container()
    {
        using var provider = Register("EAAB-real-looking-token", "act_1234567890").BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetServices<IAdActionExecutor>().Should().ContainSingle().Which.Should().BeOfType<MetaAdsActionExecutor>();
    }
```

(The existing theory `registers_no_ad_platform_service_when_settings_are_missing_or_placeholders` already asserts that no executor is registered for bad settings.)

The inherited suite also reads `AdActionExecutorContractTests.MissingTargetExternalId` (`"heblo-contract-missing-target"`). `MetaAdStateFake` has no route for it, so the handler answers Graph 100/33 and the executor reports the ad as missing. No extra setup is needed.

- [ ] **Step 2: Run the tests and verify the new registration tests fail**

```bash
dotnet build backend/test/Anela.Heblo.Adapters.MetaAds.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.MetaAds.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~MetaAdsRegistrationTests|FullyQualifiedName~MetaAdsActionExecutorContractTests"
```

Expected: `registers_the_executor_under_the_same_gate` and `resolves_the_executor_from_the_container` FAIL. The contract tests PASS. If a contract test fails, fix the executor or the fake, never the base.

- [ ] **Step 3: Register the executor**

In `AddAdPlatformServices`, after the `IAdPlatformReadSource` line, add:

```csharp
        services.AddScoped<IAdActionExecutor, MetaAdsActionExecutor>();
```

Update the method's summary comment so the first line reads `Marketing-ads read source and PauseAd executor.`

- [ ] **Step 4: Run all adapter tests**

```bash
dotnet build backend/test/Anela.Heblo.Adapters.MetaAds.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.MetaAds.Tests --no-build -p:UseSharedCompilation=false
```

Expected: `Passed!`, 0 failed.

- [ ] **Step 5: Document the executor and the manual smoke procedure**

Append to `docs/integrations/meta-ads-api.md`:

````markdown
## 9. Executor (`MetaAdsActionExecutor`) and manual smoke procedure

- Supported: `PauseAd` only. `AddNegativeKeyword` is not supported (Meta has no keywords).
- ReadCurrent: `GET /{ad_id}?fields=id,status,effective_status,account_id`. The value is the ad's **own** `status`. An ad outside `MetaAds:AccountId` is reported as non-existent.
- Execute: `POST /{ad_id}` form `status=PAUSED` → `{"success":true}`, then read back. Revert: `status=ACTIVE`.
- Needs `ads_management` on the system-user token. Without it Meta answers code 200, which Heblo records as `Failed`.
- The executor never runs unless core C3 + C4 are merged **and** an admin turns on execution (kill switch, spec 6.6).

### Smoke procedure (run by Ondrej, never by an agent)

Preparation (Ads Manager, once):
1. Create campaign `HEBLO-SMOKE`. Set the campaign to **Paused** and keep it paused for the whole procedure, so the ad can never deliver or spend.
2. In it, one ad set (lowest possible budget) and one ad `HEBLO-SMOKE-AD` whose **own** status is **Active**. Its effective status stays `CAMPAIGN_PAUSED`.
3. Note the ad id.

Step A — token and API semantics (after Part C merges; needs the token from `meta-ads.env`):
```bash
set -a; source ~/Work/heblo-marketing-agents/meta-ads.env; set +a
V=<MetaAds:ApiVersion>; AD=<ad id>
curl -sS -G "https://graph.facebook.com/$V/$AD" -H "Authorization: Bearer $META_ADS_ACCESS_TOKEN" --data-urlencode "fields=id,status,effective_status,account_id" | jq
curl -sS -X POST "https://graph.facebook.com/$V/$AD" -H "Authorization: Bearer $META_ADS_ACCESS_TOKEN" --data-urlencode "status=PAUSED" | jq   # expect {"success":true}
curl -sS -X POST "https://graph.facebook.com/$V/$AD" -H "Authorization: Bearer $META_ADS_ACCESS_TOKEN" --data-urlencode "status=ACTIVE" | jq   # restore
```
Expected: status ACTIVE → PAUSED → ACTIVE, `effective_status` CAMPAIGN_PAUSED throughout, and within an hour `/act_…/activities` shows two `update_ad_run_status` events whose `actor_id` is the system user's id.

Step B — end to end on staging (after C3 + C4 are merged and the Meta secrets are in kv-heblo-stg):
1. Turn execution on for Meta only (`/marketing/ads/settings`, kill switch).
2. Through MCP, submit a `PauseAd` proposal for `HEBLO-SMOKE-AD`. Approve it in the web UI.
3. Expect the proposal to be `Executed` and the audit log to show before `Enabled` / after `Paused`. On the next `AdsChangeSyncJob` run, the change event appears with origin `Heblo`.
4. Revert from the web UI. Expect the ad `ACTIVE` again and a second `Heblo` change event.
5. Turn execution off again. Keep `HEBLO-SMOKE` paused for future smoke runs.
````

In `docs/processes/sync-ads-meta.md`, add `  - backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsActionExecutor.cs` to `owns`. Set `verified_at` to the quoted short SHA of `origin/main`. At the end of *Data flow*, add:

```markdown
**Write path (executor).** `MetaAdsActionExecutor` (`PauseAd` only) is called by the core proposal execution job after approval: `GET /{ad_id}` → `POST /{ad_id} status=PAUSED|ACTIVE` → `GET /{ad_id}` read-back. Ads outside `MetaAds:AccountId` are treated as missing. Smoke procedure: `docs/integrations/meta-ads-api.md` §9.
```

Add to *Code entry points*: `- backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsActionExecutor.cs — PauseAd / revert`.

- [ ] **Step 6: Validate everything**

```bash
python3 scripts/process-docs/check.py index
python3 scripts/process-docs/check.py check
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.MetaAds.Tests --no-build -p:UseSharedCompilation=false
dotnet format Anela.Heblo.sln
git status --short
```

Expected: the index is written; no doc `ERROR`; build `0 Error(s)`; tests `Passed!`; `git status` shows only this PR's files.

- [ ] **Step 7: Commit and open the PR**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.MetaAds backend/test/Anela.Heblo.Adapters.MetaAds.Tests \
        docs/integrations/meta-ads-api.md docs/processes/sync-ads-meta.md docs/processes/INDEX.md
git commit -m "feat: register Meta PauseAd executor and document smoke procedure

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git push -u origin feat/marketing-meta-executor
gh pr create --base main --title "feat: Meta Ads PauseAd executor" --body "$(cat <<'BODY'
## Summary
- `MetaAdsActionExecutor : IAdActionExecutor`: `PauseAd` only (`POST /{ad_id} status=PAUSED`, read-back verified; revert → `ACTIVE`). `AddNegativeKeyword` is not supported.
- Refuses ads outside `MetaAds:AccountId` (agent-supplied ids are untrusted).
- Platform rejections → `Failed` with Graph error JSON; invalid token / exhausted transient errors throw.
- Same `AdSettingsGuard` gate as the read source. Nothing calls it until core C3 + C4 are merged and an admin enables execution.
- Manual smoke procedure for Ondrej: `docs/integrations/meta-ads-api.md` §9.

## Test plan
- [ ] `dotnet test backend/test/Anela.Heblo.Adapters.MetaAds.Tests` (unit + inherited `AdActionExecutorContractTests` on a stateful fake)
- [ ] `python3 scripts/process-docs/check.py check`
- [ ] Ondrej: smoke Step A (token has `ads_management`) on the paused `HEBLO-SMOKE` campaign
- [ ] Ondrej: smoke Step B on staging once C3 + C4 are live

🤖 Generated with [Claude Code](https://claude.com/claude-code)
BODY
)"
gh pr checks --watch
```

If `ads_management` was missing per `meta-ads-api.md` §2, add a line under *Summary*: "⚠ Token lacks `ads_management`; smoke Step A is blocked until Ondrej adds it in Business Settings."

---

## Self-review (done while writing this plan)

- **Spec coverage:** spec 9 spike → Task 1. Integration doc → Task 2 (Part C extends it in Task 10). Read source: entities, facts, change log, capabilities, empty search terms → Tasks 4–6. Gated registration and KV secret names → Task 7 and the PR rollout. Contract tests on recorded fixtures → Tasks 7 and 10. Process doc → Tasks 2, 8 and 10. Executor (`PauseAd`, revert, unsupported `AddNegativeKeyword`, `SupportedActions`) → Tasks 9–10. Manual smoke procedure → Task 10. The empty-`ImportedMarketingTransactions` root cause → Tasks 1 and 2. Billing importer code untouched throughout.
- **Placeholders:** the only `<…>` values are spike-dependent facts (version, account details). They are copied from `meta-spike-findings.md`, and Task 8 Step 2 greps for leftovers.
- **Type consistency:** `MetaGraphClient` (`GetRawAsync`, `GetAsync<T>`, `GetAllPagesAsync<T>`, `PostFormAsync`), `MetaGraphException` (`IsNotFound`, `IsAuthError`, `IsTransient`, `ResponseBody`, `Permanent`, `Transport`, `FromResponse`), `MetaStatusMapper` (`Active`, `Paused`, `ToEntityStatus`, `ToActionValue`), `MetaAdDto.AccountId`, `MetaTestHarness` (`AccountId`, `AccountDigits`, `CreateGraphClient`, `CreateReadSource`, `CreateExecutor`, `PauseAd`), and `AddAdPlatformServices` are used with the same names in every task.
- **Review Focus:** 1 → Task 3 (`follows_paging_next…`, `refuses_paging_next…`, `never_logs…`) and Task 4 (`follows_adset_paging…`). 2 → Task 3 (`fails_fast_when_the_regain_hint_exceeds_max_delay`). 3 → Task 5 (`parses_numbers_invariantly_under_czech_culture`). 4 → Task 5 (`drops_rows_dated_outside_the_requested_day`). 5 → Task 9 (`treats_an_ad_from_another_account_as_missing`, `refuses_an_action_naming_an_unconfigured_account…`).
