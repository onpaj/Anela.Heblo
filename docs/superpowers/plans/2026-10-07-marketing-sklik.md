# Marketing WS3 — Sklik (Seznam.cz) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove Anela's own read access to Sklik, then ship a new `Anela.Heblo.Adapters.Sklik` with `SklikReadSource : IAdPlatformReadSource` (PR 1) and `SklikActionExecutor : IAdActionExecutor` for `AddNegativeKeyword` + `PauseAd` (PR 2).

**Architecture:** Sklik is reached through API Drak v5 in its JSON flavour: `POST https://api.sklik.cz/drak/json/v5/<method>` with a JSON **array of positional parameters**, the first being `{ "session": "...", "userId"?: n }`. A small internal JSON-RPC client logs in with `client.loginByToken` once per scope, takes the refreshed `session` from every response, re-logs once on status 401, strips `session` from everything it hands back, and maps Drak body statuses to `SklikApiException`. The read source lists entities with `*.list`, reads one-day statistics with `*.createReport` + paged `*.readReport`, and reads search terms with `queries.*Report`; Sklik has no change-history API, so `Capabilities.ChangeLog = false` and the core's snapshot-diff fallback covers it. The executor uses a separate, never-retrying HTTP client.

**Tech Stack:** .NET 8, `System.Text.Json` (+ `System.Text.Json.Nodes`), `IHttpClientFactory`, `Microsoft.Extensions.Http.Resilience` 8.0.0, xUnit 2.9.2, FluentAssertions 6.12.0, Python 3 stdlib (spike only).

**Spec:** `docs/superpowers/specs/2026-10-07-marketing-agents-platform-design.md` (binding; sections 4.2, 4.3, 9 and 12 especially). Context: `docs/handoff/marketing-agents-platform.md`.

## Before you start

You are a fresh agent with no conversation context. Read, in this order:

1. `CLAUDE.md` (repo root) — project rules: secrets only in Key Vault (`--` separator), process docs updated in the same PR, validation gates.
2. The spec above — sections 4.2, 4.3, 9, 10, 12. Section 12 names are binding.
3. `docs/architecture/development_guidelines.md` (module boundaries) and `docs/integrations/shoptet-api.md` (integration-doc style).
4. `docs/processes/_TEMPLATE.md` (process-doc format).
5. Sklik API Drak docs: https://api.sklik.cz/drak/ — especially `usage.html`, `client.loginByToken.html`, `client.get.html`, `campaigns.createReport.html`, `campaigns.readReport.html`, `queries.readReport.html`, `keywords.negative.create.html`, `keywords.negative.remove.html`, `campaigns.update.html`, `ads.update.html`, `api.limits.html`, `changelog.html`.

The plan has three parts with separate prerequisites:

| Part | What | Branch / PR | Prerequisite |
|---|---|---|---|
| A | Access spike (Task 1) | none — nothing is committed | `~/Work/heblo-marketing-agents/sklik.env` exists |
| B | Adapter + read source (Tasks 2–8) | `feat/marketing-sklik-read-source` → PR | Part A gate passed **and** core PR C1 merged on `origin/main` |
| C | Executor (Tasks 9–12) | `feat/marketing-sklik-executor` → PR | Part B merged on `origin/main` (Part C extends Part B's project) |

**Part B / Part C worktree setup** (run from the main checkout; replace `<dir>` / `<branch>`):

```bash
git fetch origin
git cat-file -e origin/main:backend/src/Anela.Heblo.Application/Features/MarketingAds/Contracts/IAdPlatformReadSource.cs && echo "C1 present" || echo "C1 MISSING"
git cat-file -e origin/main:backend/test/Anela.Heblo.MarketingAds.TestKit/Anela.Heblo.MarketingAds.TestKit.csproj && echo "TestKit present" || echo "TestKit MISSING"
git worktree add ../<dir> -b <branch> origin/main
```

If either check prints `MISSING`, **stop and report** "WS3 blocked: core PR C1 not on origin/main". Do not create the contracts yourself.

For Part C additionally: `git cat-file -e origin/main:backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Anela.Heblo.Adapters.Sklik.csproj` must succeed, else stop and report "Part C blocked: Part B not merged".

**Verify C1's actual names before coding.** Open, on `origin/main`:
- every file in `backend/src/Anela.Heblo.Application/Features/MarketingAds/Contracts/`
- `backend/test/Anela.Heblo.MarketingAds.TestKit/` (the `AdPlatformReadSourceContractTests` and `AdActionExecutorContractTests` classes, and the csproj's xunit/FluentAssertions versions)

This plan uses spec 12.2/12.3 names exactly (`AdPlatform`, `AdEntityLevel`, `AdEntityStatus`, `KeywordMatchType`, `AdSourceCapabilities`, `AdAccountSnapshot`, `AdEntitySnapshot`, `AdDailyFactRow`, `AdSearchTermRow`, `AdChangeEventRow`, `AdAction`, `AdTargetState`, `AdExecutionResult`, `AdExecutionOutcome`, `AdActionType`, `AdActionValues`, `AdActionPayloadKeys`, `AdSettingsGuard`). If C1 differs (e.g. an extra abstract member on a contract-test base), follow C1's code and note the difference in the PR description.

**Build/test gotchas in this repo:**
- Build first, then test with `--no-build`, always with `-p:UseSharedCompilation=false` (concurrent worktrees otherwise hang `dotnet test`).
- The solution file is `Anela.Heblo.sln` at the repo root (not under `backend/`).
- If a test failure's stack trace contradicts the source you can read, the binaries are stale: touch the test file and rebuild.
- If restore fails with `NU1605` (package downgrade), raise the named package in the new csproj to the version the error asks for.
- If building `Anela.Heblo.API` fails because the frontend client generation can't find `node_modules`, run `npm install --legacy-peer-deps` in `frontend/` once.

**Commits:** conventional commits; every message ends with a blank line and `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. PR bodies end with `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.

## Global Constraints

- Names, placement and contract types exactly as spec section 12 (namespace `Anela.Heblo.Application.Features.MarketingAds.Contracts`; adapter `backend/src/Adapters/Anela.Heblo.Adapters.Sklik`; tests `backend/test/Anela.Heblo.Adapters.Sklik.Tests`; classes `SklikReadSource` / `SklikActionExecutor`; doc `docs/integrations/sklik-api.md`).
- `ad_daily_facts` cost is **net of VAT, in account currency** (spec 4.1). Sklik money fields are integers in **haléře** → divide by 100.
- "Unsupported capabilities return an empty list, never throw" (spec 4.2). Sources "throw on transport/auth errors; the core catches per source".
- "Each platform registers its source/executor only when its settings are **really** configured: a non-empty value that is not a `-- stored in … --` placeholder" → `AdSettingsGuard.IsConfigured(...)`. Registration **never throws** on bad config; it just stays inert.
- Executors "do not compare old values themselves — the core does"; "`ExecuteAsync` returns `Failed` on a platform error and never throws for platform-side rejections (it throws only for transport/auth failures)" (spec 12.2).
- Action conventions (spec 12.2): `AddNegativeKeyword` → `TargetLevel` `Campaign`|`AdGroup`, `OldValue = Absent`, `NewValue = Present`, payload `text` + `matchType` (`KeywordMatchType` name), `ReadCurrentAsync` returns `Absent|Present`, `PlatformResourceId` = created negative's id. `PauseAd` → `TargetLevel = Ad`, `Enabled` → `Paused`, empty payload, revert → `Enabled`.
- Secrets only in Key Vault: `Sklik--ApiToken`, `Sklik--UserId`. Never in appsettings, App Settings, logs, exception messages, or persisted JSON. The **session** string is a bearer credential too: never logged, never persisted.
- Retry only transient failures (HTTP 408/429/5xx, transport) and only on the **read** client. Never retry writes. Never retry Drak body-level 4xx.
- Never catch `OperationCanceledException`/`TaskCanceledException`; the only `catch` in executor code is `catch (SklikApiException ex) when (ex.IsPlatformRejection)`.
- HTTP body assertions in tests parse JSON (`JsonElement`); never `Contains` on raw strings.
- The executing agent **never runs a live write** against Sklik. Part A calls only read methods; the spike script refuses anything else.

## Review Focus

1. **Session/token leakage** — the Drak session rides in every response body; if it reaches `AdTargetState.RawJson`, `AdExecutionResult.PlatformResponseJson`, a log line or an exception message, it is persisted in Postgres/App Insights. Expect: stripped everywhere. Pinned in Task 4 (`strips_the_session_from_raw_json_and_body`, `never_puts_the_token_or_session_into_exceptions_or_logs`) and Task 11 (`reports_a_platform_rejection_as_failed_without_throwing` checks `PlatformResponseJson`).
2. **Agency/shared-login access (`Sklik:UserId` set)** — every call except `client.get` must carry `userId`, or Heblo silently reads the login's own (empty) account. Pinned in Task 4 (`adds_the_managed_user_id_to_every_call_but_client_get`) and Task 5 (`reports_the_managed_account_when_a_user_id_is_configured`).
3. **Money units** — haléře stored as CZK inflates spend ×100. Pinned in Task 6 (`converts_halers_to_crowns_for_every_level_and_skips_entities_without_stats`).
4. **Campaign-level negative keywords are a whole-list replace** (`campaigns.update.negativeKeywords`); adding or reverting one keyword must not wipe the others. Pinned in Task 10 (`adds_a_campaign_negative_without_dropping_existing_ones_and_reverts_only_its_own`).
5. **One query reported under several keywords** — `queries.readReport` returns one row per (query, keyword); without aggregation the core's PK (`ad_group`, `date`, `search_term`, `match_type`) collides and the upsert fails. Pinned in Task 6 (`aggregates_search_terms_that_share_group_query_and_match_type`).

## Spec deviations

1. **Search-term match type** (spec 4.1 `ad_search_term_daily.match_type`): Sklik reports the *matching keyword's* match type per query row, not a query match type. `AdSearchTermRow.MatchType` = mapped `keyword.matchType`; rows sharing (group, query, match type) are summed.
2. **Negative-keyword entity ids**: group negatives come from `keywords.negative.list`, campaign negatives from `keywords.campaign.negative.*Report`; their numeric ids are not documented as one id space, so `AdEntitySnapshot.ExternalId` is `g:<id>` / `c:<id>`.
3. **Campaign-level `AddNegativeKeyword` is read-modify-write** of the campaign's whole negative list via `campaigns.update` (Drak has no add/remove for campaign negatives). Not atomic: a concurrent UI edit between read and write can be lost. Replace semantics are inferred from the docs and must be confirmed by Ondrej's manual smoke (Task 12) before the Sklik executor is enabled.
4. **Group-level negatives have no id in the create response** (`keywords.negative.create` returns none); the executor re-lists to fill `PlatformResourceId`. Revert removes by name + match type (`keywords.negative.remove`, which matches by name since Drak 11.27.0), using the action's payload, not the id.
5. **Part C depends on Part B** (spec says "executor PR after C1"): the executor reuses Part B's client, settings and account resolver, so Part B must be merged first.
6. **Account model**: one Sklik account = one Sklik user id. Currency is fixed `CZK`, time zone `Europe/Prague` (Drak exposes neither per account; docs state CET).
7. **Drak body-level 429/5xx are not retried** inside a run (only HTTP-level ones are, by the resilience handler); the source throws and the core's next run / 14-day lookback recovers.

## File structure

```
backend/src/Adapters/Anela.Heblo.Adapters.Sklik/
  Anela.Heblo.Adapters.Sklik.csproj
  SklikSettings.cs                         settings + IsUsable/ManagedUserId (Part B)
  SklikConstants.cs                        CZK, Europe/Prague, haléře factor (B)
  SklikMappings.cs                         status / match type / money mapping (B)
  SklikAccountResolver.cs                  client.get → AdAccountSnapshot, account check (B)
  SklikAdapterServiceCollectionExtensions.cs  gated registration (B, extended in C)
  Drak/SklikApiException.cs                (B)
  Drak/SklikResponse.cs                    (B)
  Drak/SklikResponseParser.cs              status parse + session strip (B)
  Drak/ISklikDrakClient.cs                 (B)
  Drak/SklikDrakClient.cs                  login, session refresh, 401 re-login (B)
  Drak/SklikHttpClientNames.cs             (B)
  Drak/ISklikDrakClientFactory.cs          (B)
  Drak/SklikDrakClientFactory.cs           (B)
  Drak/SklikJson.cs                        JsonElement helpers (B)
  Drak/SklikPaging.cs                      list paging + report create/read paging (B)
  Read/SklikEntityMapper.cs                (B)
  Read/SklikEntityReader.cs                (B)
  Read/SklikMetrics.cs                     (B)
  Read/SklikStatsReader.cs                 facts + search terms (B)
  Read/SklikReadSource.cs                  (B)
  Execution/SklikPauseAdOperation.cs       (C)
  Execution/SklikNegativeKeyword.cs        (C)
  Execution/SklikNegativeKeywordOperation.cs (C)
  Execution/SklikActionExecutor.cs         (C)
backend/test/Anela.Heblo.Adapters.Sklik.Tests/
  Anela.Heblo.Adapters.Sklik.Tests.csproj
  Fakes/RecordedSklikHandler.cs, Fakes/SklikRecordedRequest.cs, Fakes/CapturingLogger.cs, Fakes/SklikFixtures.cs (B)
  Fakes/FakeSklikDrakServer.cs, Fakes/SklikExecutorFixtures.cs (C)
  Fixtures/*.json (B)
  SklikSettingsTests.cs, SklikDrakClientTests.cs, SklikReadSourceEntityTests.cs,
  SklikReadSourceStatsTests.cs, SklikReadSourceContractTests.cs, SklikAdapterRegistrationTests.cs (B)
  SklikPauseAdOperationTests.cs, SklikNegativeKeywordOperationTests.cs,
  SklikActionExecutorTests.cs, SklikActionExecutorContractTests.cs (C)
docs/integrations/sklik-api.md (B, extended in C)
docs/processes/sync-ads-sklik.md (B, extended in C)
Modified: Anela.Heblo.sln, backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj,
          backend/src/Anela.Heblo.API/Program.cs, backend/src/Anela.Heblo.API/appsettings.json,
          docs/processes/INDEX.md (generated)
```

---

# Part A — Access spike (throwaway, read-only, no PR)

### Task 1: Sklik access spike

**Files:**
- Create (scratchpad only, never in the repo): `$SCRATCH/sklik_spike.py`, output dir `$SCRATCH/sklik-spike-out/`
- Create (outside the repo, Ondrej's folder): `~/Work/heblo-marketing-agents/sklik-spike-findings.md`
- Read: `~/Work/heblo-marketing-agents/sklik.env`

`$SCRATCH` is your session scratchpad directory (from your system prompt). If you have none, use `mktemp -d`.

**Interfaces:**
- Consumes: `sklik.env` with exactly these variables:
  - `SKLIK_API_TOKEN` — required. The API token from Sklik UI → *Nastavení účtu* → *API*.
  - `SKLIK_USER_ID` — optional. Set **only** when the token belongs to a login that reaches Anela's account as a *foreign* (managed) account; the Sklik numeric user id of Anela's account.
- Produces: `sklik-spike-findings.md` with the keys listed in Step 5 — Task 2 copies them into `docs/integrations/sklik-api.md`.

- [ ] **Step 1: Check the env file exists without printing it**

Run: `test -f ~/Work/heblo-marketing-agents/sklik.env && grep -c '^SKLIK_API_TOKEN=.\+' ~/Work/heblo-marketing-agents/sklik.env`
Expected: `1`. If the file is missing or prints `0`: **STOP** and report "Part A blocked: `~/Work/heblo-marketing-agents/sklik.env` with `SKLIK_API_TOKEN=` is missing". Never `cat` the file.

- [ ] **Step 2: Write the spike script to the scratchpad**

`$SCRATCH/sklik_spike.py`:

```python
#!/usr/bin/env python3
"""Sklik Drak access spike (WS3 Part A). READ-ONLY.

Calls only login, client.get, api.limits, *.list, *.createReport and *.readReport.
Never prints or stores the token or the session (only 8-char fingerprints of the session).
Usage: python3 -I sklik_spike.py <output-dir>
"""
import datetime
import hashlib
import json
import pathlib
import sys
import urllib.error
import urllib.request

ENV_FILE = pathlib.Path.home() / "Work" / "heblo-marketing-agents" / "sklik.env"
BASE_URL = "https://api.sklik.cz/drak/json/v5"
READ_ONLY_SUFFIXES = (".loginByToken", ".get", ".list", ".createReport", ".readReport", ".limits")
METRICS = ["impressions", "clicks", "totalMoney", "conversions", "conversionValue"]


def load_env(path):
    values = {}
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        values[key.strip()] = value.strip().strip('"').strip("'")
    return values


def fingerprint(value):
    return hashlib.sha256(value.encode("utf-8")).hexdigest()[:8]


def post(method, params):
    if not method.endswith(READ_ONLY_SUFFIXES):
        raise SystemExit(f"refusing non-read method {method}")
    request = urllib.request.Request(
        f"{BASE_URL}/{method}",
        data=json.dumps(params).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            return response.status, json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as error:
        text = error.read().decode("utf-8", errors="replace")
        try:
            return error.code, json.loads(text)
        except ValueError:
            return error.code, {"status": error.code, "statusMessage": "non-JSON body"}


class Spike:
    def __init__(self, token, user_id, out_dir):
        self.token = token
        self.user_id = user_id
        self.out_dir = out_dir
        self.session = None
        self.fingerprints = []

    def login(self):
        for label, params in (("array", [self.token]), ("string", self.token)):
            http, body = post("client.loginByToken", params)
            print(f"login body-form={label}: http={http} status={body.get('status')} {body.get('statusMessage')}")
            if body.get("status") == 200 and body.get("session"):
                self.session = body["session"]
                self.fingerprints.append(fingerprint(self.session))
                return label
        return None

    def call(self, method, *params, managed=True, save=None, session=None):
        user = {"session": session or self.session}
        if managed and self.user_id:
            user["userId"] = int(self.user_id)
        http, body = post(method, [user, *params])
        if body.get("session") and session is None:
            self.session = body["session"]
            self.fingerprints.append(fingerprint(self.session))
        clean = {k: v for k, v in body.items() if k != "session"}
        diagnostics = [d.get("id") for d in (clean.get("diagnostics") or []) if isinstance(d, dict)]
        print(f"{method}: http={http} status={clean.get('status')} {clean.get('statusMessage')} diagnostics={diagnostics}")
        if save:
            (self.out_dir / f"{save}.json").write_text(json.dumps(clean, indent=2, ensure_ascii=False), encoding="utf-8")
        return clean


def distinct(rows, key):
    return sorted({str(row.get(key)) for row in rows})


def listing(spike, method, prop, columns, save):
    rows = spike.call(method, {}, {"offset": 0, "limit": 100, "displayColumns": columns}, save=save).get(prop) or []
    print(f"  {method}: {len(rows)} rows; status={distinct(rows, 'status')} matchType={distinct(rows, 'matchType')} "
          f"adType={distinct(rows, 'adType')} type={distinct(rows, 'type')} deleted={distinct(rows, 'deleted')}")
    return rows


def report(spike, prefix, restriction, columns, save, allow_empty=False):
    created = spike.call(f"{prefix}.createReport", restriction, {"statGranularity": "total"}, save=f"{save}.create")
    report_id = created.get("reportId")
    print(f"  {prefix}.createReport totalCount={created.get('totalCount')}")
    if not report_id:
        return []
    options = {"offset": 0, "limit": 100, "allowEmptyStatistics": allow_empty, "displayColumns": columns}
    rows = spike.call(f"{prefix}.readReport", report_id, options, save=f"{save}.read").get("report") or []
    print(f"  {prefix}.readReport rows={len(rows)}")
    return rows


def main():
    out_dir = pathlib.Path(sys.argv[1])
    out_dir.mkdir(parents=True, exist_ok=True)
    env = load_env(ENV_FILE)
    token = env.get("SKLIK_API_TOKEN", "")
    user_id = env.get("SKLIK_USER_ID", "")
    if not token:
        raise SystemExit("GATE FAIL: SKLIK_API_TOKEN missing in sklik.env")
    print(f"SKLIK_USER_ID configured: {bool(user_id)}")

    spike = Spike(token, user_id, out_dir)
    form = spike.login()
    if not form:
        raise SystemExit("GATE FAIL: client.loginByToken rejected the token")
    print(f"login body form that works: {form}")

    me = spike.call("client.get", managed=False, save="client.get")
    user = me.get("user") or {}
    print(f"own login: userId={user.get('userId')} username={user.get('username')} agencyStatus={user.get('agencyStatus')} "
          f"walletCredit={user.get('walletCredit')} walletCreditWithVat={user.get('walletCreditWithVat')}")
    for account in me.get("foreignAccounts") or []:
        print(f"foreign account: userId={account.get('userId')} username={account.get('username')} access={account.get('access')} "
              f"relationType={account.get('relationType')} relationStatus={account.get('relationStatus')}")

    limits = spike.call("api.limits", managed=False, save="api.limits")
    values = limits.get("limits") or {}
    print("limits:", {k: values.get(k) for k in ("minuteRequestLimit", "dayRequestLimit", "statsDataLimit", "valueAddedTax")})
    print("batchCallLimits:", limits.get("batchCallLimits"))

    campaigns = listing(spike, "campaigns.list", "campaigns", ["id", "name", "status", "deleted", "type"], "campaigns.list")
    if not campaigns:
        raise SystemExit("GATE FAIL: no campaigns readable with this token/userId")
    listing(spike, "groups.list", "groups", ["id", "name", "status", "deleted", "maxCpc", "campaign.id"], "groups.list")
    listing(spike, "keywords.list", "keywords", ["id", "name", "matchType", "status", "deleted", "group.id"], "keywords.list")
    listing(spike, "keywords.negative.list", "keywords", ["id", "name", "matchType", "deleted", "group.id"], "keywords.negative.list")
    listing(spike, "ads.list", "ads", ["id", "name", "headline1", "adType", "status", "adStatus", "deleted", "group.id"], "ads.list")
    negatives = report(spike, "keywords.campaign.negative", {}, ["id", "name", "matchType", "deleted", "campaign.id"],
                       "campaign-negatives", allow_empty=True)
    print(f"  campaign negatives matchType={distinct(negatives, 'matchType')}")

    yesterday = (datetime.date.today() - datetime.timedelta(days=1)).isoformat()
    day = {"dateFrom": yesterday, "dateTo": yesterday}
    rows = report(spike, "campaigns", day, ["id", "name"] + METRICS, "campaigns.stats")
    total = sum((s.get("totalMoney") or 0) for r in rows for s in (r.get("stats") or []))
    value = sum((s.get("conversionValue") or 0) for r in rows for s in (r.get("stats") or []))
    print(f"YESTERDAY {yesterday}: campaigns with stats={len(rows)} totalMoney(raw)={total} -> {total / 100:.2f} CZK if haléře; "
          f"conversionValue(raw)={value}")
    for prefix in ("groups", "keywords", "ads"):
        report(spike, prefix, day, ["id"] + METRICS, f"{prefix}.stats")
    queries = report(spike, "queries", day, ["query", "group.id", "keyword.id", "keyword.matchType"] + METRICS, "queries.stats")
    keyword_types = sorted({str((q.get("keyword") or {}).get("matchType")) for q in queries})
    print(f"  queries keyword.matchType values={keyword_types}")

    bogus = spike.call("campaigns.list", {}, {"offset": 0, "limit": 1}, session="invalid-session-from-spike")
    print(f"invalid session -> status {bogus.get('status')}")
    print(f"session fingerprints: {spike.fingerprints[:5]} ... distinct={len(set(spike.fingerprints))} of {len(spike.fingerprints)}")


if __name__ == "__main__":
    main()
```

- [ ] **Step 3: Run the spike**

Run: `python3 -I "$SCRATCH/sklik_spike.py" "$SCRATCH/sklik-spike-out" 2>&1 | tee "$SCRATCH/sklik-spike.log"`
Expected: a `login body form that works:` line, `client.get: http=200 status=200`, non-zero campaigns and a `YESTERDAY …` line. Then confirm nothing secret leaked:
Run: `grep -c "$(grep '^SKLIK_API_TOKEN=' ~/Work/heblo-marketing-agents/sklik.env | cut -d= -f2-)" "$SCRATCH/sklik-spike.log" "$SCRATCH"/sklik-spike-out/*.json | grep -v ':0$' || echo "no token in output"`
Expected: `no token in output`.

- [ ] **Step 4: Apply the gate**

**STOP and report exactly what is missing** (do not continue to Part B) if any of these holds:
- login failed (`GATE FAIL: client.loginByToken rejected the token`) → "token invalid or revoked; Ondrej must generate a new one in Sklik UI → Nastavení → API".
- no campaigns readable → "token's login sees no campaigns; if Anela's account is a foreign account, set `SKLIK_USER_ID`".
- the login that owns the token is the **agency's** login (the `own login: username=` is not an Anela address and Anela's campaigns are reachable only as a `foreign account` with `relationType=agency`) → "Anela has no access of its own; Ondrej must create/obtain an Anela-owned Sklik login with rw access and a token from it". Ask Ondrej to confirm the printed `own login username` is Anela's if in doubt.
- any read method returned status `403` → "access denied on `<method>`".

Also fetch https://api.sklik.cz/fenix (API Fénix, the REST sibling) and check whether it offers any change-history/activity endpoint; record yes/no with the URL.

- [ ] **Step 5: Write the findings file (no secrets)**

Create `~/Work/heblo-marketing-agents/sklik-spike-findings.md` containing these keys, each with the observed value (copied from `sklik-spike.log`) and the date:

```markdown
# Sklik spike findings — <YYYY-MM-DD>
- login_body_form: array | string
- own_login: userId=<n>, username=<…>, agencyStatus=<…>
- anela_account_access: own login | foreign account (relationType=…, access=r|rw) — SKLIK_USER_ID needed: yes|no
- limits: minuteRequestLimit=<n>, dayRequestLimit=<n>, statsDataLimit=<n>, valueAddedTax=<…>
- batch_call_limits: <copy>
- entity_counts: campaigns=<n>, groups=<n>, keywords=<n>, group_negatives=<n>, campaign_negatives=<n>, ads=<n>
- status_values: campaigns=<…>, groups=<…>, keywords=<…>, ads.status=<…>, ads.adStatus=<…>
- match_type_values: keywords=<…>, negatives=<…>, campaign_negatives=<…>, queries.keyword=<…>
- ad_types: <…>
- yesterday: <date>, totalMoney_raw=<n>, czk_if_halers=<x.xx>, conversionValue_raw=<n>
- ui_cost_check: <Ondrej: Sklik UI cost for that day = … CZK; UI column is without VAT: yes/no> (ask Ondrej; leave "pending" if unanswered)
- report_rows_yesterday: campaigns=<n>, groups=<n>, keywords=<n>, ads=<n>, queries=<n>
- search_terms_available: yes|no
- session: changes_per_response=<yes|no>, invalid_session_status=<n>
- change_history_api: Drak=none (no history/event method in the method list); Fénix=<yes (url)|no>
```

Then report to Ondrej in ≤ 10 lines: gate result, access model (and whether `Sklik:UserId` is needed), the yesterday CZK figure to compare with the UI, and the question "is that number with or without VAT in the Sklik UI?". Nothing is committed in Part A. Delete `$SCRATCH/sklik-spike-out/` after Task 2 has used it.

---

# Part B — Adapter + read source PR (`feat/marketing-sklik-read-source`)

Set up the worktree as described in "Before you start" (`<dir>` = `marketing-sklik-read`, `<branch>` = `feat/marketing-sklik-read-source`). All paths below are relative to that worktree root.

### Task 2: Integration doc from the spike findings

**Files:**
- Create: `docs/integrations/sklik-api.md`

**Interfaces:**
- Consumes: `~/Work/heblo-marketing-agents/sklik-spike-findings.md` (Task 1). If it does not exist, **stop**: Part A has not passed.
- Produces: the documented facts later tasks rely on (status strings, match-type strings, money units, `userId` need).

- [ ] **Step 1: Write the doc**

Create `docs/integrations/sklik-api.md` with the content below. Every `⟨A: key⟩` marker is replaced by the value of that key from the findings file (verbatim). Do not leave any marker.

````markdown
# Sklik (Seznam.cz) API — Integration Findings

> **Living document.** Every new finding about the Sklik API MUST be added here before code relies on it.
> API Drak docs: https://api.sklik.cz/drak/ · changelog: https://api.sklik.cz/drak/changelog.html
> No sandbox exists: every call hits the live account. Heblo's spike (⟨A: date of findings file⟩) was read-only.

## 1. Overview

Heblo reads Sklik through **API Drak v5, JSON flavour** (`Anela.Heblo.Adapters.Sklik`):
`POST https://api.sklik.cz/drak/json/v5/<method>` with a JSON **array of positional parameters**.
(The XML-RPC endpoint `…/drak/RPC2/v5` is the same API; Heblo does not use it. API Fénix is a separate REST API for
features Drak lacks, e.g. Shopping ads — unused.)

## 2. Authentication and session

- Token: Sklik UI → *Nastavení účtu* → *API* → token. Stored only in Key Vault as `Sklik--ApiToken`.
- `client.loginByToken` body: ⟨A: login_body_form⟩ form, i.e. `["<token>"]`. Response `{status, statusMessage, session}`.
- Every later call's first parameter is `{"session": "<session>", "userId": <managed account id, optional>}`.
- Every response carries a **refreshed `session`**; Heblo always continues with the newest one
  (changes per response: ⟨A: session.changes_per_response⟩). An invalid/expired session answers body status ⟨A: session.invalid_session_status⟩;
  Heblo logs in again once and repeats the call.
- Session lifetime is not documented. Heblo logs in once per job scope.
- The session is a bearer credential: Heblo strips it from every response before logging or persisting anything.

## 3. Accounts and ownership

- Token's own login: ⟨A: own_login⟩.
- Anela's account access: ⟨A: anela_account_access⟩.
- `client.get` returns only the **login's own** attributes plus `foreignAccounts[]` (`userId`, `username`, `access` r/rw,
  `relationType` normal/agency, `relationStatus`). It must be called **without** `userId`.
- When Anela's account is reached as a foreign account, set Key Vault `Sklik--UserId` to its numeric user id; Drak then
  needs `userId` in the user struct of **every other call** ("You MUST pass managed user ID in order to manipulate entities
  you do not own directly").
- Heblo's `AdAccountSnapshot`: `ExternalId` = Sklik user id, `Name` = username, `Currency` = `CZK`, `TimeZone` = `Europe/Prague`
  (Drak works in CET and exposes no per-account currency).

## 4. Response envelope and status codes

Every response: `{ "status": int, "statusMessage": string, "session": string, "diagnostics"?: [{id, field, type, …}], …data }`.
HTTP status is 200 for most body statuses; since Drak 11.24.3 (Jul 2026) the JSON API also propagates **HTTP 429**.

| Body status | Meaning | Heblo |
|---|---|---|
| 200 | OK | return |
| 206 | OK with warnings (`diagnostics[].type = warning`) | return, log diagnostic ids |
| 400 / 404 / 406 / 409 / 413 | bad arguments / not found / bad values / conflict / too many items | `SklikApiException`, `IsPlatformRejection` — executor returns `Failed` |
| 401 | invalid session | re-login once, then `IsAuthFailure` |
| 403 | access denied | `IsAuthFailure` — always thrown |
| 429, 5xx | rate limit / server error | `IsTransient` — thrown; HTTP-level ones retried on the read client only |

Batch methods are all-or-nothing: one `error` diagnostic rolls the whole call back.

## 5. Limits

- ⟨A: limits⟩
- Batch call limits: ⟨A: batch_call_limits⟩
- `statsDataLimit` caps one `readReport` page as *entities × periods*. Heblo reads one day with `statGranularity = total`
  (1 period per entity) and pages with `Sklik:ReportPageSize` (default 1000; keep ≤ `statsDataLimit`). List methods page
  with `Sklik:ListPageSize` (default 100).

## 6. Money, VAT, units

- All money fields (`totalMoney`, `clickMoney`, `maxCpc`, `dayBudget`, wallet credit) are integers in **haléře** (1/100 CZK).
  Heblo divides by 100.
- `conversionValue`: raw yesterday ⟨A: yesterday.conversionValue_raw⟩; treated as haléře (÷100) — consistent with the other money fields.
- VAT: statistics money is **without VAT** (Drak exposes VAT separately: `walletCreditWithVat`, `api.limits.valueAddedTax`).
  UI cross-check: ⟨A: ui_cost_check⟩.
- Spike: ⟨A: yesterday⟩.
- Not related: Heblo's *Marketing → Analýzy* shows negative Sklik cost in some months. That figure comes from Flexi
  received invoices filtered by supplier DIČ (credit notes caught, deposits missed), not from this API — see
  `docs/processes/calc-marketing-performance.md`.

## 7. Read methods Heblo uses

| Data | Method | Parameters after `user` | Notes |
|---|---|---|---|
| account | `client.get` | — | without `userId` |
| campaigns | `campaigns.list` | `{}`, `{offset, limit, displayColumns:[id,name,status,deleted,type]}` | |
| ad groups | `groups.list` | `{}`, `{…, displayColumns:[id,name,status,deleted,maxCpc,campaign.id]}` | |
| keywords | `keywords.list` | `{}`, `{…, displayColumns:[id,name,matchType,status,deleted,group.id]}` | |
| group negatives | `keywords.negative.list` | `{}` or `{group:{ids:[…]}, isDeleted:false}`, `{…, displayColumns:[id,name,matchType,deleted,group.id]}` | |
| campaign negatives | `keywords.campaign.negative.createReport` → `.readReport` | `{}` → `reportId`, `{offset, limit, allowEmptyStatistics:true, displayColumns:[id,name,matchType,deleted,campaign.id]}` | no stats; `allowEmptyStatistics` must be true |
| ads | `ads.list` | `{}`, `{…, displayColumns:[id,name,headline1,adType,status,adStatus,deleted,group.id]}` | `status` = user-set, `adStatus` = system |
| daily stats | `{campaigns,groups,keywords,ads}.createReport` → `.readReport` | `{dateFrom, dateTo}` (inside the restriction filter), `{statGranularity:"total"}` → `reportId`, `{offset, limit, allowEmptyStatistics:false, displayColumns:[id,impressions,clicks,totalMoney,conversions,conversionValue]}` | rows: `{id, stats:[{…}]}` |
| search terms | `queries.createReport` → `.readReport` | same day filter, columns `[query, group.id, keyword.matchType, …metrics]` | one row per (query, matched keyword) |

Spike entity counts: ⟨A: entity_counts⟩. Report rows yesterday: ⟨A: report_rows_yesterday⟩. Search terms available: ⟨A: search_terms_available⟩.

## 8. Value mapping

| Sklik | Heblo |
|---|---|
| `deleted = true` | `AdEntityStatus.Removed` |
| `status = "active"` | `Enabled` |
| `status = "suspend"` | `Paused` |
| anything else | `Unknown` |
| keyword `matchType` `exact`/`phrase`/`broad` | `KeywordMatchType.Exact`/`Phrase`/`Broad` |
| negative `negativeExact`/`negativePhrase`/`negativeBroad` | `Exact`/`Phrase`/`Broad` |

Observed values: ⟨A: status_values⟩; match types ⟨A: match_type_values⟩; ad types ⟨A: ad_types⟩.
If an observed value is not in the table, add it here **and** to `SklikMappings` in the same PR.

## 9. Change history

⟨A: change_history_api⟩. Heblo's `SklikReadSource` reports `Capabilities.ChangeLog = false`; the core's snapshot-diff
fallback (`AdsChangeSyncJob`) records changes with `actor_kind = Unknown`.

## 10. Known quirks

- Negative keyword entity ids are stored as `g:<id>` (group) and `c:<id>` (campaign).
- `queries.readReport` reports the matching **keyword's** match type; Heblo sums rows sharing (group, query, match type).
- `keywords.negative.remove` matches by **name + match type** (changed in Drak 11.27.0, Sep 2026; `keywords.negative.restore` was removed).
````

- [ ] **Step 2: Check no marker is left**

Run: `grep -c '⟨A:' docs/integrations/sklik-api.md`
Expected: `0`

- [ ] **Step 3: Reconcile the plan's assumptions with the findings**

Compare the findings with these assumptions used by Tasks 4–6. For each mismatch, change the named constant/fixture in the later task as you implement it, and list it in the PR description:
- login body form `["<token>"]` (Task 4 `LoginAsync`) — if the findings say `string`, send the bare JSON string instead and change the login test accordingly;
- status strings `active` / `suspend` (Task 5 `SklikMappings`);
- keyword match types `exact` / `phrase` / `broad` and negatives `negative*` (Task 5 `SklikMappings`);
- `queries.readReport` returns `group.id` and `keyword.matchType` (Task 6).

- [ ] **Step 4: Commit**

```bash
git add docs/integrations/sklik-api.md
git commit -m "docs: sklik api integration findings

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 3: Project scaffold and settings

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Anela.Heblo.Adapters.Sklik.csproj`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/SklikSettings.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/SklikConstants.cs`
- Create: `backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj`
- Test: `backend/test/Anela.Heblo.Adapters.Sklik.Tests/SklikSettingsTests.cs`
- Modify: `Anela.Heblo.sln`

**Interfaces:**
- Consumes: `AdSettingsGuard.IsConfigured(params string?[])` (C1).
- Produces: `public sealed class SklikSettings { ConfigurationKey = "Sklik"; DefaultBaseUrl; string ApiToken; string UserId; string BaseUrl; int ListPageSize; int ReportPageSize; int RequestTimeoutSeconds; int RetryCount; long? ManagedUserId; bool IsUsable }`; `internal static class SklikConstants { Currency = "CZK"; TimeZone = "Europe/Prague"; HalersPerCrown = 100m }`.

- [ ] **Step 1: Create the two csproj files**

`backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Anela.Heblo.Adapters.Sklik.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Anela.Heblo.Adapters.Sklik</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Http.Resilience" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Anela.Heblo.Application\Anela.Heblo.Application.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Anela.Heblo.Adapters.Sklik.Tests" />
  </ItemGroup>
</Project>
```

`backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj` — **xunit must be 2.9.2** (the C1 test kit depends on `xunit.extensibility.core` 2.9.2 and `xunit` 2.5.3 pins its core exactly); if the TestKit csproj on main names other versions, use those:

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
    <PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Adapters\Anela.Heblo.Adapters.Sklik\Anela.Heblo.Adapters.Sklik.csproj" />
    <ProjectReference Include="..\Anela.Heblo.MarketingAds.TestKit\Anela.Heblo.MarketingAds.TestKit.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Include="Fixtures\**\*.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add both projects to the solution under the existing `Adapters` / `test` folders**

`dotnet sln add --solution-folder` creates duplicate folders in this solution, so add in root and nest by hand (existing folder GUIDs: `Adapters` = `{4B6F17C3-0A57-487A-BE8C-1808B40EC604}`, `test` = `{23FE24B3-CD9D-4576-A7C8-85D5B012F43D}`):

```bash
dotnet sln Anela.Heblo.sln add --in-root backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Anela.Heblo.Adapters.Sklik.csproj
dotnet sln Anela.Heblo.sln add --in-root backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj
SRC=$(grep -o '"Anela.Heblo.Adapters.Sklik", "[^"]*", "{[^}]*}"' Anela.Heblo.sln | grep -o '{[^}]*}')
TST=$(grep -o '"Anela.Heblo.Adapters.Sklik.Tests", "[^"]*", "{[^}]*}"' Anela.Heblo.sln | grep -o '{[^}]*}')
perl -0pi -e "s/(GlobalSection\(NestedProjects\) = preSolution\n)/\$1\t\t$SRC = {4B6F17C3-0A57-487A-BE8C-1808B40EC604}\n\t\t$TST = {23FE24B3-CD9D-4576-A7C8-85D5B012F43D}\n/" Anela.Heblo.sln
grep -c "$SRC = {4B6F17C3\|$TST = {23FE24B3" Anela.Heblo.sln
file Anela.Heblo.sln
```

Expected: the `grep -c` prints `2`; `file` prints `Unicode text, UTF-8 (with BOM) text` (no `CRLF` — if it says CRLF, `dotnet sln` rewrote endings: run `perl -pi -e 's/\r\n/\n/' Anela.Heblo.sln`).

- [ ] **Step 3: Write the failing settings tests**

`backend/test/Anela.Heblo.Adapters.Sklik.Tests/SklikSettingsTests.cs`:

```csharp
using FluentAssertions;

namespace Anela.Heblo.Adapters.Sklik.Tests;

public class SklikSettingsTests
{
    private static SklikSettings Valid() => new() { ApiToken = "real-token" };

    [Fact]
    public void is_usable_with_only_a_token()
    {
        Valid().IsUsable.Should().BeTrue();
        Valid().ManagedUserId.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-- stored in Key Vault --")]
    public void is_not_usable_without_a_real_token(string token)
    {
        new SklikSettings { ApiToken = token }.IsUsable.Should().BeFalse();
    }

    [Fact]
    public void parses_a_numeric_managed_user_id()
    {
        var settings = Valid();
        settings.UserId = " 7654321 ";

        settings.ManagedUserId.Should().Be(7654321);
        settings.IsUsable.Should().BeTrue();
    }

    [Fact]
    public void treats_a_placeholder_user_id_as_not_set()
    {
        var settings = Valid();
        settings.UserId = "-- stored in Key Vault --";

        settings.ManagedUserId.Should().BeNull();
        settings.IsUsable.Should().BeTrue();
    }

    [Theory]
    [InlineData("anela")]
    [InlineData("-5")]
    [InlineData("0")]
    public void is_not_usable_with_a_non_numeric_or_non_positive_user_id(string userId)
    {
        var settings = Valid();
        settings.UserId = userId;

        settings.IsUsable.Should().BeFalse();
    }

    [Theory]
    [InlineData("http://api.sklik.cz/drak/json/v5")]
    [InlineData("not a url")]
    [InlineData("")]
    public void is_not_usable_with_a_non_https_base_url(string baseUrl)
    {
        var settings = Valid();
        settings.BaseUrl = baseUrl;

        settings.IsUsable.Should().BeFalse();
    }

    [Fact]
    public void is_not_usable_with_non_positive_page_sizes_or_timeout()
    {
        new SklikSettings { ApiToken = "t", ListPageSize = 0 }.IsUsable.Should().BeFalse();
        new SklikSettings { ApiToken = "t", ReportPageSize = 0 }.IsUsable.Should().BeFalse();
        new SklikSettings { ApiToken = "t", RequestTimeoutSeconds = 0 }.IsUsable.Should().BeFalse();
        new SklikSettings { ApiToken = "t", RetryCount = -1 }.IsUsable.Should().BeFalse();
    }

    [Fact]
    public void never_prints_the_token()
    {
        new SklikSettings { ApiToken = "super-secret" }.ToString().Should().NotContain("super-secret");
    }
}
```

- [ ] **Step 4: Run it to verify it fails**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj -p:UseSharedCompilation=false`
Expected: build FAILS with `CS0246: The type or namespace name 'SklikSettings' could not be found`.

- [ ] **Step 5: Implement settings and constants**

`backend/src/Adapters/Anela.Heblo.Adapters.Sklik/SklikSettings.cs`:

```csharp
using System.Globalization;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.Sklik;

public sealed class SklikSettings
{
    public const string ConfigurationKey = "Sklik";
    public const string DefaultBaseUrl = "https://api.sklik.cz/drak/json/v5";

    /// <summary>Sklik API token (Sklik UI → Nastavení účtu → API). Key Vault secret <c>Sklik--ApiToken</c> only.</summary>
    public string ApiToken { get; set; } = string.Empty;

    /// <summary>
    /// Optional numeric Sklik user id of Anela's account, set only when the token's login reaches that account as a
    /// foreign (managed) account. Key Vault secret <c>Sklik--UserId</c>.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = DefaultBaseUrl;
    public int ListPageSize { get; set; } = 100;
    public int ReportPageSize { get; set; } = 1000;
    public int RequestTimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 3;

    public long? ManagedUserId =>
        AdSettingsGuard.IsConfigured(UserId)
        && long.TryParse(UserId.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
        && id > 0
            ? id
            : null;

    public bool IsUsable =>
        AdSettingsGuard.IsConfigured(ApiToken)
        && (!AdSettingsGuard.IsConfigured(UserId) || ManagedUserId is not null)
        && Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && ListPageSize > 0
        && ReportPageSize > 0
        && RequestTimeoutSeconds > 0
        && RetryCount >= 0;

    public override string ToString() =>
        $"SklikSettings {{ ApiToken = ***, UserId = {ManagedUserId?.ToString(CultureInfo.InvariantCulture) ?? "-"}, BaseUrl = {BaseUrl} }}";
}
```

`backend/src/Adapters/Anela.Heblo.Adapters.Sklik/SklikConstants.cs`:

```csharp
namespace Anela.Heblo.Adapters.Sklik;

internal static class SklikConstants
{
    /// <summary>Sklik bills in CZK only; Drak exposes no per-account currency.</summary>
    public const string Currency = "CZK";

    /// <summary>Drak assumes CET for all datetimes.</summary>
    public const string TimeZone = "Europe/Prague";

    /// <summary>Every Drak money field is an integer in haléře.</summary>
    public const decimal HalersPerCrown = 100m;
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~SklikSettingsTests"`
Expected: `Passed!` with 14 tests (the TestKit base classes are abstract; nothing else runs yet). If `is_not_usable_without_a_real_token("   ")` fails, C1's `AdSettingsGuard` does not treat whitespace as unconfigured — report it as a C1 bug and keep the test.

- [ ] **Step 7: Commit**

```bash
git add Anela.Heblo.sln backend/src/Adapters/Anela.Heblo.Adapters.Sklik backend/test/Anela.Heblo.Adapters.Sklik.Tests
git commit -m "feat: scaffold sklik adapter with gated settings

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 4: Drak JSON-RPC client (login, session refresh, status mapping)

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Drak/SklikApiException.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Drak/SklikResponse.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Drak/SklikResponseParser.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Drak/ISklikDrakClient.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Drak/SklikDrakClient.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Drak/SklikHttpClientNames.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Drak/ISklikDrakClientFactory.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Drak/SklikDrakClientFactory.cs`
- Create (test support): `backend/test/Anela.Heblo.Adapters.Sklik.Tests/Fakes/SklikRecordedRequest.cs`, `Fakes/RecordedSklikHandler.cs`, `Fakes/CapturingLogger.cs`, `Fakes/SklikFixtures.cs`, `Fixtures/login.json`
- Test: `backend/test/Anela.Heblo.Adapters.Sklik.Tests/SklikDrakClientTests.cs`

**Interfaces:**
- Consumes: `SklikSettings` (Task 3).
- Produces:
  - `public sealed class SklikApiException : Exception { string Method; int Status; IReadOnlyList<string> DiagnosticIds; string? RawJson; bool IsAuthFailure; bool IsTransient; bool IsPlatformRejection }`
  - `internal sealed record SklikResponse(int Status, string? StatusMessage, IReadOnlyList<string> DiagnosticIds, JsonElement Body, string RawJson)` — session already removed.
  - `internal interface ISklikDrakClient { Task<SklikResponse> CallAsync(string method, IReadOnlyList<object?> parameters, CancellationToken ct); Task<SklikResponse> CallAsOwnUserAsync(string method, IReadOnlyList<object?> parameters, CancellationToken ct); }` — `parameters` are the positional parameters **after** the user struct.
  - `internal sealed class SklikDrakClient(HttpClient, SklikSettings, ILogger<SklikDrakClient>) : ISklikDrakClient`
  - `internal interface ISklikDrakClientFactory { ISklikDrakClient CreateReadClient(); ISklikDrakClient CreateWriteClient(); }`, `SklikHttpClientNames.Read = "Sklik.Read"`, `.Write = "Sklik.Write"`
  - Test support: `RecordedSklikHandler` (`Respond`, `RespondHttp`, `RespondWithFixture`, `CallsTo`, `Requests`), `SklikRecordedRequest(string Method, Uri Uri, JsonElement Body)` with `User` and `Parameter(int)`, `CapturingLogger<T>`, `SklikFixtures` (`Token`, `AccountId`, `Date`, `Read`, `Settings`, `Client`).

- [ ] **Step 1: Write the test support**

`backend/test/Anela.Heblo.Adapters.Sklik.Tests/Fakes/SklikRecordedRequest.cs`:

```csharp
using System.Text.Json;

namespace Anela.Heblo.Adapters.Sklik.Tests.Fakes;

/// <summary>One Drak call as sent: Body is the JSON array of positional parameters.</summary>
internal sealed record SklikRecordedRequest(string Method, Uri Uri, JsonElement Body)
{
    /// <summary>The first positional parameter: the user struct (or the token, for client.loginByToken).</summary>
    public JsonElement User => Body[0];

    /// <summary>Positional parameter after the user struct, zero-based.</summary>
    public JsonElement Parameter(int index) => Body[index + 1];
}
```

`backend/test/Anela.Heblo.Adapters.Sklik.Tests/Fakes/RecordedSklikHandler.cs`:

```csharp
using System.Net;
using System.Text;
using System.Text.Json;

namespace Anela.Heblo.Adapters.Sklik.Tests.Fakes;

/// <summary>
/// Replays canned Drak responses per method (the last URL segment). Each method's queue is consumed in order and its
/// last response repeats. Unknown methods answer a Drak 404 so a missing fixture fails loudly.
/// </summary>
internal sealed class RecordedSklikHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<(HttpStatusCode Status, string Body)>> _responses = new(StringComparer.Ordinal);

    public List<SklikRecordedRequest> Requests { get; } = new();

    public RecordedSklikHandler Respond(string method, params string[] jsonBodies)
    {
        _responses[method] = new Queue<(HttpStatusCode, string)>(jsonBodies.Select(b => (HttpStatusCode.OK, b)));
        return this;
    }

    public RecordedSklikHandler RespondHttp(string method, HttpStatusCode status, string body)
    {
        _responses[method] = new Queue<(HttpStatusCode, string)>(new[] { (status, body) });
        return this;
    }

    public RecordedSklikHandler RespondWithFixture(string method, params string[] fixtureFiles) =>
        Respond(method, fixtureFiles.Select(SklikFixtures.Read).ToArray());

    public IReadOnlyList<SklikRecordedRequest> CallsTo(string method) =>
        Requests.Where(r => r.Method == method).ToList();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var method = request.RequestUri!.Segments[^1];
        var text = request.Content is null ? "null" : await request.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(text);
        Requests.Add(new SklikRecordedRequest(method, request.RequestUri, document.RootElement.Clone()));

        var (status, body) = Next(method);
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private (HttpStatusCode Status, string Body) Next(string method)
    {
        if (!_responses.TryGetValue(method, out var queue) || queue.Count == 0)
        {
            return (HttpStatusCode.OK, $$"""{"status":404,"statusMessage":"no fixture for {{method}}"}""");
        }

        return queue.Count > 1 ? queue.Dequeue() : queue.Peek();
    }
}
```

`backend/test/Anela.Heblo.Adapters.Sklik.Tests/Fakes/CapturingLogger.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.Sklik.Tests.Fakes;

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Messages.Add(formatter(state, exception) + (exception is null ? string.Empty : " " + exception));
}
```

`backend/test/Anela.Heblo.Adapters.Sklik.Tests/Fakes/SklikFixtures.cs` (Task 5 and 6 add members to this class):

```csharp
using Anela.Heblo.Adapters.Sklik.Drak;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Adapters.Sklik.Tests.Fakes;

internal static partial class SklikFixtures
{
    public const string Token = "test-token-must-never-leak";
    public const string AccountId = "1234567";
    public static readonly DateOnly Date = new(2026, 10, 6);

    public static string Read(string file) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", file));

    public static SklikSettings Settings(string userId = "", int listPageSize = 100, int reportPageSize = 1000) => new()
    {
        ApiToken = Token,
        UserId = userId,
        ListPageSize = listPageSize,
        ReportPageSize = reportPageSize,
    };

    public static SklikDrakClient Client(
        HttpMessageHandler handler, SklikSettings? settings = null, ILogger<SklikDrakClient>? logger = null) =>
        new(new HttpClient(handler), settings ?? Settings(), logger ?? NullLogger<SklikDrakClient>.Instance);
}
```

`backend/test/Anela.Heblo.Adapters.Sklik.Tests/Fixtures/login.json`:

```json
{ "status": 200, "statusMessage": "OK", "session": "fixture-session-0" }
```

- [ ] **Step 2: Write the failing client tests**

`backend/test/Anela.Heblo.Adapters.Sklik.Tests/SklikDrakClientTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using Anela.Heblo.Adapters.Sklik.Drak;
using Anela.Heblo.Adapters.Sklik.Tests.Fakes;
using FluentAssertions;

namespace Anela.Heblo.Adapters.Sklik.Tests;

public class SklikDrakClientTests
{
    private const string Ok = """{"status":200,"statusMessage":"OK","session":"session-2","campaigns":[]}""";
    private const string OkAgain = """{"status":200,"statusMessage":"OK","session":"session-3","campaigns":[]}""";
    private const string InvalidSession = """{"status":401,"statusMessage":"Invalid session","session":"x"}""";
    private static readonly object?[] AnyFilter = { new { } };
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static RecordedSklikHandler Handler(params string[] campaignResponses) =>
        new RecordedSklikHandler()
            .RespondWithFixture("client.loginByToken", "login.json")
            .Respond("campaigns.list", campaignResponses);

    [Fact]
    public async Task logs_in_with_the_token_as_the_only_positional_parameter()
    {
        var handler = Handler(Ok);

        await SklikFixtures.Client(handler).CallAsync("campaigns.list", AnyFilter, Ct);

        var login = handler.CallsTo("client.loginByToken").Should().ContainSingle().Subject;
        login.Uri.AbsoluteUri.Should().Be("https://api.sklik.cz/drak/json/v5/client.loginByToken");
        login.Body.ValueKind.Should().Be(JsonValueKind.Array);
        login.Body.GetArrayLength().Should().Be(1);
        login.Body[0].GetString().Should().Be(SklikFixtures.Token);
    }

    [Fact]
    public async Task sends_the_user_struct_first_and_continues_with_the_refreshed_session()
    {
        var handler = Handler(Ok, OkAgain);
        var client = SklikFixtures.Client(handler);

        await client.CallAsync("campaigns.list", AnyFilter, Ct);
        await client.CallAsync("campaigns.list", AnyFilter, Ct);

        var calls = handler.CallsTo("campaigns.list");
        calls[0].Uri.AbsoluteUri.Should().Be("https://api.sklik.cz/drak/json/v5/campaigns.list");
        calls[0].Body.GetArrayLength().Should().Be(2);
        calls[0].User.GetProperty("session").GetString().Should().Be("fixture-session-0");
        calls[0].User.TryGetProperty("userId", out _).Should().BeFalse();
        calls[1].User.GetProperty("session").GetString().Should().Be("session-2");
        handler.CallsTo("client.loginByToken").Should().HaveCount(1);
    }

    [Fact]
    public async Task adds_the_managed_user_id_to_every_call_but_client_get()
    {
        var handler = Handler(Ok).Respond("client.get", """{"status":200,"statusMessage":"OK","user":{"userId":1}}""");
        var client = SklikFixtures.Client(handler, SklikFixtures.Settings(userId: "7654321"));

        await client.CallAsync("campaigns.list", AnyFilter, Ct);
        await client.CallAsOwnUserAsync("client.get", Array.Empty<object?>(), Ct);

        handler.CallsTo("campaigns.list").Single().User.GetProperty("userId").GetInt64().Should().Be(7654321);
        handler.CallsTo("client.get").Single().User.TryGetProperty("userId", out _).Should().BeFalse();
    }

    [Fact]
    public async Task logs_in_again_once_when_the_session_is_rejected()
    {
        var handler = Handler(InvalidSession, Ok);

        var response = await SklikFixtures.Client(handler).CallAsync("campaigns.list", AnyFilter, Ct);

        response.Status.Should().Be(200);
        handler.CallsTo("client.loginByToken").Should().HaveCount(2);
        handler.CallsTo("campaigns.list").Should().HaveCount(2);
    }

    [Fact]
    public async Task throws_an_auth_failure_when_the_session_is_rejected_twice()
    {
        var handler = Handler(InvalidSession);

        var act = () => SklikFixtures.Client(handler).CallAsync("campaigns.list", AnyFilter, Ct);

        (await act.Should().ThrowAsync<SklikApiException>()).Which.IsAuthFailure.Should().BeTrue();
        handler.CallsTo("campaigns.list").Should().HaveCount(2);
    }

    [Fact]
    public async Task throws_a_platform_rejection_carrying_the_diagnostic_ids()
    {
        var handler = Handler("""
            {"status":406,"statusMessage":"Bad values of attributes","session":"s",
             "diagnostics":[{"id":"keyword_duplicate","field":"name","type":"error"}]}
            """);

        var act = () => SklikFixtures.Client(handler).CallAsync("campaigns.list", AnyFilter, Ct);

        var error = (await act.Should().ThrowAsync<SklikApiException>()).Which;
        error.IsPlatformRejection.Should().BeTrue();
        error.Status.Should().Be(406);
        error.Method.Should().Be("campaigns.list");
        error.DiagnosticIds.Should().Equal("keyword_duplicate");
        JsonDocument.Parse(error.RawJson!).RootElement.TryGetProperty("session", out _).Should().BeFalse();
    }

    [Fact]
    public async Task returns_partial_success_with_its_diagnostics()
    {
        var handler = Handler("""
            {"status":206,"statusMessage":"Partially OK","session":"s","diagnostics":[{"id":"missing_space_after_dot","type":"warning"}]}
            """);

        var response = await SklikFixtures.Client(handler).CallAsync("campaigns.list", AnyFilter, Ct);

        response.Status.Should().Be(206);
        response.DiagnosticIds.Should().Equal("missing_space_after_dot");
    }

    [Fact]
    public async Task strips_the_session_from_raw_json_and_body()
    {
        var handler = Handler(Ok);

        var response = await SklikFixtures.Client(handler).CallAsync("campaigns.list", AnyFilter, Ct);

        response.Body.TryGetProperty("session", out _).Should().BeFalse();
        JsonDocument.Parse(response.RawJson).RootElement.TryGetProperty("session", out _).Should().BeFalse();
        response.Body.GetProperty("campaigns").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task treats_a_non_json_http_error_as_transient()
    {
        var handler = new RecordedSklikHandler()
            .RespondWithFixture("client.loginByToken", "login.json")
            .RespondHttp("campaigns.list", HttpStatusCode.ServiceUnavailable, "<html>maintenance</html>");

        var act = () => SklikFixtures.Client(handler).CallAsync("campaigns.list", AnyFilter, Ct);

        (await act.Should().ThrowAsync<SklikApiException>()).Which.IsTransient.Should().BeTrue();
    }

    [Fact]
    public async Task maps_http_429_with_a_drak_body_to_transient()
    {
        var handler = new RecordedSklikHandler()
            .RespondWithFixture("client.loginByToken", "login.json")
            .RespondHttp("campaigns.list", HttpStatusCode.TooManyRequests, """{"status":429,"statusMessage":"Too many requests"}""");

        var act = () => SklikFixtures.Client(handler).CallAsync("campaigns.list", AnyFilter, Ct);

        (await act.Should().ThrowAsync<SklikApiException>()).Which.IsTransient.Should().BeTrue();
    }

    [Fact]
    public async Task never_puts_the_token_or_session_into_exceptions_or_logs()
    {
        var logger = new CapturingLogger<SklikDrakClient>();
        var handler = Handler(InvalidSession, """
            {"status":206,"statusMessage":"Partially OK","session":"session-secret","diagnostics":[{"id":"w","type":"warning"}]}
            """);
        var client = SklikFixtures.Client(handler, logger: logger);
        await client.CallAsync("campaigns.list", AnyFilter, Ct);

        var failing = new RecordedSklikHandler().Respond("client.loginByToken",
            """{"status":401,"statusMessage":"Invalid token"}""");
        var act = () => SklikFixtures.Client(failing, logger: logger).CallAsync("campaigns.list", AnyFilter, Ct);
        var error = (await act.Should().ThrowAsync<SklikApiException>()).Which;

        error.IsAuthFailure.Should().BeTrue();
        error.ToString().Should().NotContain(SklikFixtures.Token);
        logger.Messages.Should().NotBeEmpty();
        logger.Messages.Should().OnlyContain(m =>
            !m.Contains(SklikFixtures.Token) && !m.Contains("fixture-session-0") && !m.Contains("session-secret"));
    }

    [Fact]
    public async Task honours_cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => SklikFixtures.Client(Handler(Ok)).CallAsync("campaigns.list", AnyFilter, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
```

- [ ] **Step 3: Run them to verify they fail**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj -p:UseSharedCompilation=false`
Expected: build FAILS with `CS0246` for `SklikDrakClient` / `SklikApiException`.

- [ ] **Step 4: Implement the exception, response and parser**

`backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Drak/SklikApiException.cs`:

```csharp
namespace Anela.Heblo.Adapters.Sklik.Drak;

/// <summary>
/// A Drak call that did not succeed. The message holds only the method, the status, Sklik's status message and the
/// diagnostic ids — never the request body (token) or the session.
/// </summary>
public sealed class SklikApiException : Exception
{
    private const int StatusRequestTimeout = 408;
    private const int StatusTooManyRequests = 429;
    private const int FirstServerError = 500;

    public SklikApiException(
        string method, int status, string? statusMessage, IReadOnlyList<string> diagnosticIds, string? rawJson = null)
        : base(BuildMessage(method, status, statusMessage, diagnosticIds))
    {
        Method = method;
        Status = status;
        DiagnosticIds = diagnosticIds;
        RawJson = rawJson;
    }

    public string Method { get; }
    public int Status { get; }
    public IReadOnlyList<string> DiagnosticIds { get; }

    /// <summary>The response body with the session removed; null when there was no Drak body.</summary>
    public string? RawJson { get; }

    public bool IsAuthFailure => Status is 401 or 403;
    public bool IsTransient => Status is StatusRequestTimeout or StatusTooManyRequests || Status >= FirstServerError;
    public bool IsPlatformRejection => !IsAuthFailure && !IsTransient;

    private static string BuildMessage(string method, int status, string? statusMessage, IReadOnlyList<string> diagnosticIds) =>
        diagnosticIds.Count == 0
            ? $"Sklik {method} returned {status} {statusMessage}"
            : $"Sklik {method} returned {status} {statusMessage} [{string.Join(", ", diagnosticIds)}]";
}
```

`backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Drak/SklikResponse.cs`:

```csharp
using System.Text.Json;

namespace Anela.Heblo.Adapters.Sklik.Drak;

/// <summary>A Drak response with the session already removed from both <see cref="Body"/> and <see cref="RawJson"/>.</summary>
internal sealed record SklikResponse(
    int Status, string? StatusMessage, IReadOnlyList<string> DiagnosticIds, JsonElement Body, string RawJson);
```

`backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Drak/SklikResponseParser.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Anela.Heblo.Adapters.Sklik.Drak;

internal static class SklikResponseParser
{
    /// <summary>Parses a Drak body, returning the response without its session plus the session on its own.</summary>
    public static (SklikResponse Response, string? Session) Parse(string method, int httpStatus, string text)
    {
        var root = TryParseObject(text);
        var status = root is null ? null : ReadStatus(root["status"]);
        if (root is null || status is null)
        {
            throw new SklikApiException(method, httpStatus, $"HTTP {httpStatus} without a Drak status body", Array.Empty<string>());
        }

        var session = root["session"] is JsonValue sessionValue && sessionValue.TryGetValue<string>(out var value) ? value : null;
        root.Remove("session");

        var rawJson = root.ToJsonString();
        using var document = JsonDocument.Parse(rawJson);
        var response = new SklikResponse(
            status.Value,
            root["statusMessage"]?.ToString(),
            ReadDiagnosticIds(root["diagnostics"]),
            document.RootElement.Clone(),
            rawJson);
        return (response, session);
    }

    private static JsonObject? TryParseObject(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? ReadStatus(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<int>(out var number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text)
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
    }

    private static IReadOnlyList<string> ReadDiagnosticIds(JsonNode? node) =>
        node is JsonArray items
            ? items.OfType<JsonObject>()
                .Select(item => item["id"]?.ToString())
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!)
                .ToList()
            : Array.Empty<string>();
}
```

- [ ] **Step 5: Implement the client and its factory**

`backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Drak/ISklikDrakClient.cs`:

```csharp
namespace Anela.Heblo.Adapters.Sklik.Drak;

internal interface ISklikDrakClient
{
    /// <summary>
    /// Calls a Drak method for the configured account: the user struct (session, plus userId when Sklik:UserId is set)
    /// is prepended to <paramref name="parameters"/>. Returns 200/206 responses; throws <see cref="SklikApiException"/> otherwise.
    /// </summary>
    Task<SklikResponse> CallAsync(string method, IReadOnlyList<object?> parameters, CancellationToken ct);

    /// <summary>Same, but never adds userId — for client.get, which only describes the token's own login.</summary>
    Task<SklikResponse> CallAsOwnUserAsync(string method, IReadOnlyList<object?> parameters, CancellationToken ct);
}
```

`backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Drak/SklikDrakClient.cs`:

```csharp
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.Sklik.Drak;

/// <summary>
/// Drak JSON-API client. One instance = one login session; register it per scope (one job run).
/// Never logs request bodies (they hold the token) nor the session.
/// </summary>
internal sealed class SklikDrakClient : ISklikDrakClient
{
    internal const string LoginMethod = "client.loginByToken";
    private const int StatusOk = 200;
    private const int StatusPartiallyOk = 206;
    private const int StatusInvalidSession = 401;

    private static readonly JsonSerializerOptions RequestJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly SklikSettings _settings;
    private readonly ILogger<SklikDrakClient> _logger;
    private string? _session;

    public SklikDrakClient(HttpClient httpClient, SklikSettings settings, ILogger<SklikDrakClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public Task<SklikResponse> CallAsync(string method, IReadOnlyList<object?> parameters, CancellationToken ct) =>
        CallCoreAsync(method, parameters, asManagedUser: true, ct);

    public Task<SklikResponse> CallAsOwnUserAsync(string method, IReadOnlyList<object?> parameters, CancellationToken ct) =>
        CallCoreAsync(method, parameters, asManagedUser: false, ct);

    private async Task<SklikResponse> CallCoreAsync(
        string method, IReadOnlyList<object?> parameters, bool asManagedUser, CancellationToken ct)
    {
        _session ??= await LoginAsync(ct);
        var response = await SendWithSessionAsync(method, parameters, asManagedUser, ct);
        if (response.Status == StatusInvalidSession)
        {
            _logger.LogInformation("Sklik rejected the session on {Method}; logging in again", method);
            _session = await LoginAsync(ct);
            response = await SendWithSessionAsync(method, parameters, asManagedUser, ct);
        }

        return EnsureSucceeded(method, response);
    }

    private async Task<SklikResponse> SendWithSessionAsync(
        string method, IReadOnlyList<object?> parameters, bool asManagedUser, CancellationToken ct)
    {
        var user = new Dictionary<string, object> { ["session"] = _session! };
        if (asManagedUser && _settings.ManagedUserId is { } userId)
        {
            user["userId"] = userId;
        }

        var body = new List<object?>(parameters.Count + 1) { user };
        body.AddRange(parameters);

        var (response, refreshedSession) = await PostAsync(method, body, ct);
        if (!string.IsNullOrEmpty(refreshedSession))
        {
            _session = refreshedSession;
        }

        return response;
    }

    private async Task<string> LoginAsync(CancellationToken ct)
    {
        var (response, session) = await PostAsync(LoginMethod, new List<object?> { _settings.ApiToken }, ct);
        if (response.Status != StatusOk || string.IsNullOrEmpty(session))
        {
            var status = response.Status == StatusOk ? StatusInvalidSession : response.Status;
            throw new SklikApiException(LoginMethod, status, response.StatusMessage, response.DiagnosticIds);
        }

        return session;
    }

    private async Task<(SklikResponse Response, string? Session)> PostAsync(
        string method, List<object?> body, CancellationToken ct)
    {
        var url = $"{_settings.BaseUrl.TrimEnd('/')}/{method}";
        using var content = new StringContent(JsonSerializer.Serialize(body, RequestJson), Encoding.UTF8, "application/json");
        using var httpResponse = await _httpClient.PostAsync(url, content, ct);
        var text = await httpResponse.Content.ReadAsStringAsync(ct);
        return SklikResponseParser.Parse(method, (int)httpResponse.StatusCode, text);
    }

    private SklikResponse EnsureSucceeded(string method, SklikResponse response)
    {
        if (response.Status == StatusOk)
        {
            return response;
        }

        if (response.Status == StatusPartiallyOk)
        {
            _logger.LogWarning("Sklik {Method} succeeded with warnings: {Diagnostics}",
                method, string.Join(", ", response.DiagnosticIds));
            return response;
        }

        throw new SklikApiException(method, response.Status, response.StatusMessage, response.DiagnosticIds, response.RawJson);
    }
}
```

`backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Drak/SklikHttpClientNames.cs`:

```csharp
namespace Anela.Heblo.Adapters.Sklik.Drak;

internal static class SklikHttpClientNames
{
    /// <summary>Reads: transient HTTP failures are retried.</summary>
    public const string Read = "Sklik.Read";

    /// <summary>Writes: never retried — a replayed create would surface as keyword_duplicate for a change that happened.</summary>
    public const string Write = "Sklik.Write";
}
```

`backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Drak/ISklikDrakClientFactory.cs`:

```csharp
namespace Anela.Heblo.Adapters.Sklik.Drak;

internal interface ISklikDrakClientFactory
{
    ISklikDrakClient CreateReadClient();
    ISklikDrakClient CreateWriteClient();
}
```

`backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Drak/SklikDrakClientFactory.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Sklik.Drak;

internal sealed class SklikDrakClientFactory : ISklikDrakClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<SklikSettings> _options;
    private readonly ILoggerFactory _loggerFactory;

    public SklikDrakClientFactory(
        IHttpClientFactory httpClientFactory, IOptions<SklikSettings> options, ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _loggerFactory = loggerFactory;
    }

    public ISklikDrakClient CreateReadClient() => Create(SklikHttpClientNames.Read);

    public ISklikDrakClient CreateWriteClient() => Create(SklikHttpClientNames.Write);

    private ISklikDrakClient Create(string httpClientName) =>
        new SklikDrakClient(
            _httpClientFactory.CreateClient(httpClientName),
            _options.Value,
            _loggerFactory.CreateLogger<SklikDrakClient>());
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~SklikDrakClientTests"`
Expected: `Passed!` — 12 tests.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Drak backend/test/Anela.Heblo.Adapters.Sklik.Tests
git commit -m "feat: sklik drak json client with session handling

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 5: Mappings, paging, account resolver and entity reader

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/SklikMappings.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/SklikAccountResolver.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Drak/SklikJson.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Drak/SklikPaging.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Read/SklikEntityMapper.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Read/SklikEntityReader.cs`
- Create (test support): `backend/test/Anela.Heblo.Adapters.Sklik.Tests/Fakes/SklikFixtures.Entities.cs` and fixtures `client.get.json`, `client.get.managed.json`, `campaigns.list.json`, `groups.list.json`, `keywords.list.json`, `keywords.negative.list.json`, `keywords.campaign.negative.readReport.json`, `ads.list.json` under `Fixtures/`
- Test: `backend/test/Anela.Heblo.Adapters.Sklik.Tests/SklikReadSourceEntityTests.cs`

**Interfaces:**
- Consumes: `ISklikDrakClient`, `SklikResponse`, `SklikApiException` (Task 4); `SklikSettings`, `SklikConstants` (Task 3); C1 `AdEntitySnapshot`, `AdAccountSnapshot`, `AdEntityLevel`, `AdEntityStatus`, `KeywordMatchType`.
- Produces:
  - `internal static class SklikMappings { StatusActive = "active"; StatusSuspended = "suspend"; AdEntityStatus ToEntityStatus(string? status, bool isDeleted); KeywordMatchType? ToKeywordMatchType(string?); string ToNegativeMatchType(KeywordMatchType); decimal HalersToCrowns(long) }`
  - `internal static class SklikJson { IReadOnlyList<JsonElement> Array(JsonElement, string); string? String(JsonElement, string); string? Id(JsonElement, string property = "id"); string? Scalar(JsonElement); string? NestedId(JsonElement, string parent); string? NestedString(JsonElement, string parent, string property); long Long(JsonElement, string); decimal Decimal(JsonElement, string); bool Bool(JsonElement, string); bool TryParseId(string externalId, out long id); long ParseId(string externalId) }`
  - `internal static class SklikPaging { Task<IReadOnlyList<JsonElement>> ListAllAsync(ISklikDrakClient client, string method, string itemsProperty, object restrictionFilter, IReadOnlyList<string> columns, int pageSize, CancellationToken ct); Task<IReadOnlyList<JsonElement>> ReadReportAsync(ISklikDrakClient client, string entityPrefix, object restrictionFilter, object? createDisplayOptions, IReadOnlyList<string> columns, int pageSize, bool allowEmptyStatistics, CancellationToken ct) }`
  - `internal sealed class SklikAccountResolver(ISklikDrakClient, SklikSettings) { Task<AdAccountSnapshot> GetAsync(CancellationToken); Task EnsureAsync(string accountExternalId, CancellationToken) }`
  - `internal sealed class SklikEntityReader(ISklikDrakClient client, int listPageSize, int reportPageSize) { Task<IReadOnlyList<AdEntitySnapshot>> ReadAllAsync(CancellationToken) }`
  - Test support: `SklikFixtures.CreatedReport(string reportId, long totalCount)`, `SklikFixtures.EmptyList(string property)`, `SklikFixtures.WithEntities(RecordedSklikHandler)`, `SklikFixtures.EntityAccount()`.

- [ ] **Step 1: Write the fixtures**

All under `backend/test/Anela.Heblo.Adapters.Sklik.Tests/Fixtures/`. Shapes follow the Drak docs; if Task 2 Step 3 found different field names or values, use the observed ones.

`client.get.json`:
```json
{ "status": 200, "statusMessage": "OK", "session": "fixture-session-1", "sessionStarted": "2026-10-07T05:30:00+0200",
  "user": { "userId": 1234567, "username": "eshop@anela.cz", "agencyStatus": "client-agency",
            "walletCredit": 1000000, "walletCreditWithVat": 1210000, "walletVerified": true, "dayBudgetSum": 50000 },
  "foreignAccounts": [] }
```

`client.get.managed.json`:
```json
{ "status": 200, "statusMessage": "OK", "session": "fixture-session-1",
  "user": { "userId": 5555555, "username": "shared-login@anela.cz", "agencyStatus": "normal" },
  "foreignAccounts": [
    { "userId": 7654321, "username": "anela-eshop", "access": "rw", "relationName": "Anela",
      "relationStatus": "live", "relationType": "normal" } ] }
```

`campaigns.list.json`:
```json
{ "status": 200, "statusMessage": "OK", "session": "fixture-session-2",
  "campaigns": [
    { "id": 111, "name": "Vyhledávání – značka", "status": "active", "deleted": false, "type": "fulltext" },
    { "id": 112, "name": "Obsah – remarketing", "status": "suspend", "deleted": false, "type": "context" } ] }
```

`groups.list.json`:
```json
{ "status": 200, "statusMessage": "OK", "session": "fixture-session-3",
  "groups": [ { "id": 211, "name": "Anela krémy", "status": "active", "deleted": false, "maxCpc": 500, "campaign": { "id": 111 } } ] }
```

`keywords.list.json`:
```json
{ "status": 200, "statusMessage": "OK", "session": "fixture-session-4",
  "keywords": [ { "id": 311, "name": "anela krém", "matchType": "phrase", "status": "active", "deleted": false, "group": { "id": 211 } } ] }
```

`keywords.negative.list.json`:
```json
{ "status": 200, "statusMessage": "OK", "session": "fixture-session-5",
  "keywords": [ { "id": 411, "name": "zdarma", "matchType": "negativePhrase", "deleted": false, "group": { "id": 211 } } ] }
```

`keywords.campaign.negative.readReport.json`:
```json
{ "status": 200, "statusMessage": "OK", "session": "fixture-session-6", "reportId": "rep-campaign-negatives",
  "report": [ { "id": 511, "name": "návod", "matchType": "negativeBroad", "deleted": false, "campaign": { "id": 111 } } ] }
```

`ads.list.json`:
```json
{ "status": 200, "statusMessage": "OK", "session": "fixture-session-7",
  "ads": [
    { "id": 611, "adType": "eta", "headline1": "Anela přírodní kosmetika", "status": "active", "adStatus": "active",
      "deleted": false, "group": { "id": 211 } },
    { "id": 612, "adType": "eta", "headline1": "Stará reklama", "status": "suspend", "adStatus": "active",
      "deleted": true, "group": { "id": 211 } } ] }
```

`backend/test/Anela.Heblo.Adapters.Sklik.Tests/Fakes/SklikFixtures.Entities.cs`:

```csharp
namespace Anela.Heblo.Adapters.Sklik.Tests.Fakes;

internal static partial class SklikFixtures
{
    public static string CreatedReport(string reportId, long totalCount) =>
        $$"""{"status":200,"statusMessage":"OK","session":"fixture-session-r","reportId":"{{reportId}}","totalCount":{{totalCount}}}""";

    public static string EmptyList(string property) =>
        $$"""{"status":200,"statusMessage":"OK","session":"fixture-session-e","{{property}}":[]}""";

    public static RecordedSklikHandler WithEntities(RecordedSklikHandler handler) => handler
        .RespondWithFixture("campaigns.list", "campaigns.list.json")
        .RespondWithFixture("groups.list", "groups.list.json")
        .RespondWithFixture("keywords.list", "keywords.list.json")
        .RespondWithFixture("keywords.negative.list", "keywords.negative.list.json")
        .Respond("keywords.campaign.negative.createReport", CreatedReport("rep-campaign-negatives", 1))
        .RespondWithFixture("keywords.campaign.negative.readReport", "keywords.campaign.negative.readReport.json")
        .RespondWithFixture("ads.list", "ads.list.json");

    public static RecordedSklikHandler EntityAccount() => WithEntities(new RecordedSklikHandler()
        .RespondWithFixture("client.loginByToken", "login.json")
        .RespondWithFixture("client.get", "client.get.json"));
}
```

- [ ] **Step 2: Write the failing tests**

`backend/test/Anela.Heblo.Adapters.Sklik.Tests/SklikReadSourceEntityTests.cs`:

```csharp
using System.Globalization;
using Anela.Heblo.Adapters.Sklik.Drak;
using Anela.Heblo.Adapters.Sklik.Read;
using Anela.Heblo.Adapters.Sklik.Tests.Fakes;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Adapters.Sklik.Tests;

public class SklikReadSourceEntityTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static async Task<IReadOnlyList<AdEntitySnapshot>> ReadEntities(RecordedSklikHandler handler, SklikSettings? settings = null)
    {
        var effective = settings ?? SklikFixtures.Settings();
        var reader = new SklikEntityReader(SklikFixtures.Client(handler, effective), effective.ListPageSize, effective.ReportPageSize);
        return await reader.ReadAllAsync(Ct);
    }

    [Fact]
    public async Task resolves_the_token_owners_account_in_czk_and_prague_time()
    {
        var resolver = new SklikAccountResolver(SklikFixtures.Client(SklikFixtures.EntityAccount()), SklikFixtures.Settings());

        var account = await resolver.GetAsync(Ct);

        account.Should().Be(new AdAccountSnapshot("1234567", "eshop@anela.cz", "CZK", "Europe/Prague"));
    }

    [Fact]
    public async Task resolves_the_managed_account_when_a_user_id_is_configured()
    {
        var handler = SklikFixtures.EntityAccount().RespondWithFixture("client.get", "client.get.managed.json");
        var settings = SklikFixtures.Settings(userId: "7654321");
        var resolver = new SklikAccountResolver(SklikFixtures.Client(handler, settings), settings);

        var account = await resolver.GetAsync(Ct);

        account.ExternalId.Should().Be("7654321");
        account.Name.Should().Be("anela-eshop");
        handler.CallsTo("client.get").Single().User.TryGetProperty("userId", out _).Should().BeFalse();
    }

    [Fact]
    public async Task fails_when_the_managed_user_is_not_reachable_with_the_token()
    {
        var handler = SklikFixtures.EntityAccount().RespondWithFixture("client.get", "client.get.managed.json");
        var settings = SklikFixtures.Settings(userId: "999");
        var resolver = new SklikAccountResolver(SklikFixtures.Client(handler, settings), settings);

        var act = () => resolver.GetAsync(Ct);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*999*");
    }

    [Fact]
    public async Task rejects_an_account_it_does_not_serve()
    {
        var resolver = new SklikAccountResolver(SklikFixtures.Client(SklikFixtures.EntityAccount()), SklikFixtures.Settings());

        var act = () => resolver.EnsureAsync("999", Ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task maps_campaigns_and_groups_with_statuses_parents_and_attributes()
    {
        var entities = await ReadEntities(SklikFixtures.EntityAccount());

        var brand = entities.Single(e => e.Level == AdEntityLevel.Campaign && e.ExternalId == "111");
        brand.Status.Should().Be(AdEntityStatus.Enabled);
        brand.Name.Should().Be("Vyhledávání – značka");
        brand.ParentLevel.Should().BeNull();
        brand.Attributes["type"].Should().Be("fulltext");
        entities.Single(e => e.Level == AdEntityLevel.Campaign && e.ExternalId == "112").Status.Should().Be(AdEntityStatus.Paused);

        var group = entities.Single(e => e.Level == AdEntityLevel.AdGroup);
        group.ExternalId.Should().Be("211");
        group.ParentLevel.Should().Be(AdEntityLevel.Campaign);
        group.ParentExternalId.Should().Be("111");
        decimal.Parse(group.Attributes["maxCpc"]!, CultureInfo.InvariantCulture).Should().Be(5m);
    }

    [Fact]
    public async Task maps_keywords_negatives_and_ads()
    {
        var entities = await ReadEntities(SklikFixtures.EntityAccount());

        var keyword = entities.Single(e => e.Level == AdEntityLevel.Keyword);
        keyword.ExternalId.Should().Be("311");
        keyword.ParentExternalId.Should().Be("211");
        keyword.Attributes["text"].Should().Be("anela krém");
        keyword.Attributes["matchType"].Should().Be(nameof(KeywordMatchType.Phrase));

        var negatives = entities.Where(e => e.Level == AdEntityLevel.NegativeKeyword).ToList();
        negatives.Select(n => n.ExternalId).Should().BeEquivalentTo("g:411", "c:511");
        var groupNegative = negatives.Single(n => n.ExternalId == "g:411");
        groupNegative.ParentLevel.Should().Be(AdEntityLevel.AdGroup);
        groupNegative.ParentExternalId.Should().Be("211");
        groupNegative.Attributes["matchType"].Should().Be(nameof(KeywordMatchType.Phrase));
        var campaignNegative = negatives.Single(n => n.ExternalId == "c:511");
        campaignNegative.ParentLevel.Should().Be(AdEntityLevel.Campaign);
        campaignNegative.ParentExternalId.Should().Be("111");
        campaignNegative.Attributes["text"].Should().Be("návod");
        campaignNegative.Attributes["matchType"].Should().Be(nameof(KeywordMatchType.Broad));

        var ads = entities.Where(e => e.Level == AdEntityLevel.Ad).ToList();
        ads.Single(a => a.ExternalId == "611").Should().Match<AdEntitySnapshot>(a =>
            a.Name == "Anela přírodní kosmetika" && a.Status == AdEntityStatus.Enabled && a.ParentExternalId == "211");
        ads.Single(a => a.ExternalId == "612").Status.Should().Be(AdEntityStatus.Removed);
    }

    [Fact]
    public async Task reads_campaign_negatives_with_empty_statistics_allowed()
    {
        var handler = SklikFixtures.EntityAccount();

        await ReadEntities(handler);

        var read = handler.CallsTo("keywords.campaign.negative.readReport").Single();
        read.Parameter(0).GetString().Should().Be("rep-campaign-negatives");
        read.Parameter(1).GetProperty("allowEmptyStatistics").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task pages_through_lists_until_a_short_page()
    {
        var handler = SklikFixtures.EntityAccount().Respond("campaigns.list",
            """{"status":200,"statusMessage":"OK","campaigns":[{"id":111,"name":"A","status":"active","deleted":false}]}""",
            """{"status":200,"statusMessage":"OK","campaigns":[{"id":112,"name":"B","status":"active","deleted":false}]}""",
            SklikFixtures.EmptyList("campaigns"));

        var entities = await ReadEntities(handler, SklikFixtures.Settings(listPageSize: 1));

        entities.Where(e => e.Level == AdEntityLevel.Campaign).Select(e => e.ExternalId).Should().Equal("111", "112");
        handler.CallsTo("campaigns.list").Select(c => c.Parameter(1).GetProperty("offset").GetInt32()).Should().Equal(0, 1, 2);
        handler.CallsTo("campaigns.list").Should().OnlyContain(c => c.Parameter(1).GetProperty("limit").GetInt32() == 1);
    }

    [Fact]
    public async Task propagates_an_access_error_instead_of_returning_partial_entities()
    {
        var handler = SklikFixtures.EntityAccount().Respond("groups.list",
            """{"status":403,"statusMessage":"Access Denied","diagnostics":[{"id":"group_access_denied"}]}""");

        var act = () => ReadEntities(handler);

        (await act.Should().ThrowAsync<SklikApiException>()).Which.IsAuthFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData("active", false, AdEntityStatus.Enabled)]
    [InlineData("suspend", false, AdEntityStatus.Paused)]
    [InlineData("active", true, AdEntityStatus.Removed)]
    [InlineData("noactive", false, AdEntityStatus.Unknown)]
    [InlineData(null, false, AdEntityStatus.Unknown)]
    public void maps_sklik_statuses(string? status, bool deleted, AdEntityStatus expected)
    {
        SklikMappings.ToEntityStatus(status, deleted).Should().Be(expected);
    }

    [Theory]
    [InlineData("exact", KeywordMatchType.Exact)]
    [InlineData("phrase", KeywordMatchType.Phrase)]
    [InlineData("broad", KeywordMatchType.Broad)]
    [InlineData("negativeExact", KeywordMatchType.Exact)]
    [InlineData("negativePhrase", KeywordMatchType.Phrase)]
    [InlineData("negativeBroad", KeywordMatchType.Broad)]
    public void maps_sklik_match_types(string sklik, KeywordMatchType expected)
    {
        SklikMappings.ToKeywordMatchType(sklik).Should().Be(expected);
    }

    [Fact]
    public void leaves_unknown_match_types_unmapped()
    {
        SklikMappings.ToKeywordMatchType("modifiedBroad").Should().BeNull();
    }
}
```

- [ ] **Step 3: Run them to verify they fail**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj -p:UseSharedCompilation=false`
Expected: build FAILS with `CS0246` for `SklikEntityReader`, `SklikAccountResolver`, `SklikMappings`.

- [ ] **Step 4: Implement mappings and JSON helpers**

`backend/src/Adapters/Anela.Heblo.Adapters.Sklik/SklikMappings.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.Sklik;

/// <summary>Sklik ↔ Heblo value mapping; documented in docs/integrations/sklik-api.md §8.</summary>
internal static class SklikMappings
{
    public const string StatusActive = "active";
    public const string StatusSuspended = "suspend";

    public static AdEntityStatus ToEntityStatus(string? status, bool isDeleted)
    {
        if (isDeleted)
        {
            return AdEntityStatus.Removed;
        }

        return status switch
        {
            StatusActive => AdEntityStatus.Enabled,
            StatusSuspended => AdEntityStatus.Paused,
            _ => AdEntityStatus.Unknown,
        };
    }

    public static KeywordMatchType? ToKeywordMatchType(string? sklikMatchType) => sklikMatchType switch
    {
        "exact" or "negativeExact" => KeywordMatchType.Exact,
        "phrase" or "negativePhrase" => KeywordMatchType.Phrase,
        "broad" or "negativeBroad" => KeywordMatchType.Broad,
        _ => null,
    };

    public static string ToNegativeMatchType(KeywordMatchType matchType) => matchType switch
    {
        KeywordMatchType.Exact => "negativeExact",
        KeywordMatchType.Phrase => "negativePhrase",
        KeywordMatchType.Broad => "negativeBroad",
        _ => throw new ArgumentOutOfRangeException(nameof(matchType), matchType, "No Sklik negative match type."),
    };

    public static decimal HalersToCrowns(long halers) => halers / SklikConstants.HalersPerCrown;
}
```

`backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Drak/SklikJson.cs`:

```csharp
using System.Globalization;
using System.Text.Json;

namespace Anela.Heblo.Adapters.Sklik.Drak;

/// <summary>Tolerant readers for Drak JSON: a missing or mistyped field reads as null/0/false, never throws.</summary>
internal static class SklikJson
{
    public static IReadOnlyList<JsonElement> Array(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(item => item.Clone()).ToList()
            : new List<JsonElement>();

    public static string? String(JsonElement element, string property) =>
        TryGet(element, property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static string? Id(JsonElement element, string property = "id") =>
        TryGet(element, property, out var value) ? Scalar(value) : null;

    public static string? Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.String => value.GetString(),
        _ => null,
    };

    public static string? NestedId(JsonElement element, string parent) =>
        TryGet(element, parent, out var nested) ? Id(nested) : null;

    public static string? NestedString(JsonElement element, string parent, string property) =>
        TryGet(element, parent, out var nested) ? String(nested, property) : null;

    public static long Long(JsonElement element, string property) =>
        TryGet(element, property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : 0;

    public static decimal Decimal(JsonElement element, string property) =>
        TryGet(element, property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
            ? number
            : 0m;

    public static bool Bool(JsonElement element, string property) =>
        TryGet(element, property, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>Parses a Heblo external id of a Sklik entity (decimal digits) into the numeric Drak id.</summary>
    public static bool TryParseId(string externalId, out long id) =>
        long.TryParse(externalId, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;

    public static long ParseId(string externalId) =>
        TryParseId(externalId, out var id)
            ? id
            : throw new ArgumentException($"'{externalId}' is not a Sklik entity id.", nameof(externalId));

    private static bool TryGet(JsonElement element, string property, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out value);
    }
}
```

- [ ] **Step 5: Implement paging**

`backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Drak/SklikPaging.cs`:

```csharp
using System.Text.Json;

namespace Anela.Heblo.Adapters.Sklik.Drak;

internal static class SklikPaging
{
    /// <summary>Calls a *.list method page by page until a page shorter than <paramref name="pageSize"/>.</summary>
    public static async Task<IReadOnlyList<JsonElement>> ListAllAsync(
        ISklikDrakClient client, string method, string itemsProperty, object restrictionFilter,
        IReadOnlyList<string> columns, int pageSize, CancellationToken ct)
    {
        var items = new List<JsonElement>();
        for (var offset = 0; ; offset += pageSize)
        {
            var displayOptions = new { offset, limit = pageSize, displayColumns = columns };
            var response = await client.CallAsync(method, new[] { restrictionFilter, displayOptions }, ct);
            var page = SklikJson.Array(response.Body, itemsProperty);
            items.AddRange(page);
            if (page.Count < pageSize)
            {
                return items;
            }
        }
    }

    /// <summary>
    /// <c>{prefix}.createReport</c> then <c>{prefix}.readReport</c> pages until totalCount entities were covered.
    /// With allowEmptyStatistics=false a page may be shorter than the limit while more entities follow, so paging
    /// is driven by totalCount, not by page length.
    /// </summary>
    public static async Task<IReadOnlyList<JsonElement>> ReadReportAsync(
        ISklikDrakClient client, string entityPrefix, object restrictionFilter, object? createDisplayOptions,
        IReadOnlyList<string> columns, int pageSize, bool allowEmptyStatistics, CancellationToken ct)
    {
        var createMethod = $"{entityPrefix}.createReport";
        var createParameters = createDisplayOptions is null
            ? new[] { restrictionFilter }
            : new[] { restrictionFilter, createDisplayOptions };
        var created = await client.CallAsync(createMethod, createParameters, ct);
        var reportId = SklikJson.String(created.Body, "reportId")
            ?? throw new SklikApiException(createMethod, created.Status, "response carries no reportId", created.DiagnosticIds, created.RawJson);
        var totalCount = SklikJson.Long(created.Body, "totalCount");

        var rows = new List<JsonElement>();
        for (var offset = 0; offset < totalCount; offset += pageSize)
        {
            var displayOptions = new { offset, limit = pageSize, allowEmptyStatistics, displayColumns = columns };
            var response = await client.CallAsync($"{entityPrefix}.readReport", new object[] { reportId, displayOptions }, ct);
            rows.AddRange(SklikJson.Array(response.Body, "report"));
        }

        return rows;
    }
}
```

- [ ] **Step 6: Implement the account resolver**

`backend/src/Adapters/Anela.Heblo.Adapters.Sklik/SklikAccountResolver.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using Anela.Heblo.Adapters.Sklik.Drak;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.Sklik;

/// <summary>
/// The one Sklik account Heblo manages: the token's own login, or the foreign account named by Sklik:UserId.
/// Resolved once per instance via client.get (which must be called without userId).
/// </summary>
internal sealed class SklikAccountResolver
{
    private readonly ISklikDrakClient _client;
    private readonly SklikSettings _settings;
    private AdAccountSnapshot? _account;

    public SklikAccountResolver(ISklikDrakClient client, SklikSettings settings)
    {
        _client = client;
        _settings = settings;
    }

    public async Task<AdAccountSnapshot> GetAsync(CancellationToken ct)
    {
        if (_account is not null)
        {
            return _account;
        }

        var response = await _client.CallAsOwnUserAsync("client.get", Array.Empty<object?>(), ct);
        _account = _settings.ManagedUserId is { } managedUserId
            ? FindManaged(response.Body, managedUserId)
            : Own(response.Body);
        return _account;
    }

    public async Task EnsureAsync(string accountExternalId, CancellationToken ct)
    {
        var account = await GetAsync(ct);
        if (!string.Equals(account.ExternalId, accountExternalId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The Sklik adapter serves account {account.ExternalId}, not {accountExternalId}.", nameof(accountExternalId));
        }
    }

    private static AdAccountSnapshot Own(JsonElement body)
    {
        var user = body.TryGetProperty("user", out var value) ? value : default;
        var userId = SklikJson.Id(user, "userId")
            ?? throw new InvalidOperationException("Sklik client.get returned no user.userId.");
        return Snapshot(userId, SklikJson.String(user, "username"));
    }

    private static AdAccountSnapshot FindManaged(JsonElement body, long managedUserId)
    {
        var expected = managedUserId.ToString(CultureInfo.InvariantCulture);
        var match = SklikJson.Array(body, "foreignAccounts").FirstOrDefault(a => SklikJson.Id(a, "userId") == expected);
        if (match.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Sklik user {expected} (Sklik:UserId) is not among the accounts this token can reach.");
        }

        return Snapshot(expected, SklikJson.String(match, "username"));
    }

    private static AdAccountSnapshot Snapshot(string userId, string? username) =>
        new(userId, string.IsNullOrWhiteSpace(username) ? $"Sklik {userId}" : username,
            SklikConstants.Currency, SklikConstants.TimeZone);
}
```

- [ ] **Step 7: Implement the entity mapper and reader**

`backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Read/SklikEntityMapper.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using Anela.Heblo.Adapters.Sklik.Drak;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.Sklik.Read;

/// <summary>Maps *.list / report rows to AdEntitySnapshot; returns null for a row without an id.</summary>
internal static class SklikEntityMapper
{
    public const string GroupNegativePrefix = "g:";
    public const string CampaignNegativePrefix = "c:";

    public static AdEntitySnapshot? Campaign(JsonElement row) =>
        Build(row, AdEntityLevel.Campaign, null, null, SklikJson.String(row, "name"),
            ("type", SklikJson.String(row, "type")));

    public static AdEntitySnapshot? AdGroup(JsonElement row) =>
        Build(row, AdEntityLevel.AdGroup, AdEntityLevel.Campaign, SklikJson.NestedId(row, "campaign"),
            SklikJson.String(row, "name"), ("maxCpc", Crowns(row, "maxCpc")));

    public static AdEntitySnapshot? Keyword(JsonElement row)
    {
        var text = SklikJson.String(row, "name");
        return Build(row, AdEntityLevel.Keyword, AdEntityLevel.AdGroup, SklikJson.NestedId(row, "group"), text,
            ("text", text), ("matchType", MatchTypeName(row)));
    }

    public static AdEntitySnapshot? GroupNegativeKeyword(JsonElement row) =>
        Negative(row, GroupNegativePrefix, AdEntityLevel.AdGroup, SklikJson.NestedId(row, "group"));

    public static AdEntitySnapshot? CampaignNegativeKeyword(JsonElement row) =>
        Negative(row, CampaignNegativePrefix, AdEntityLevel.Campaign, SklikJson.NestedId(row, "campaign"));

    public static AdEntitySnapshot? Ad(JsonElement row)
    {
        var id = SklikJson.Id(row);
        var name = SklikJson.String(row, "headline1") ?? SklikJson.String(row, "name") ?? $"Ad {id}";
        return Build(row, AdEntityLevel.Ad, AdEntityLevel.AdGroup, SklikJson.NestedId(row, "group"), name,
            ("adType", SklikJson.String(row, "adType")), ("adStatus", SklikJson.String(row, "adStatus")));
    }

    private static AdEntitySnapshot? Negative(JsonElement row, string prefix, AdEntityLevel parentLevel, string? parentId)
    {
        var id = SklikJson.Id(row);
        if (id is null)
        {
            return null;
        }

        var text = SklikJson.String(row, "name");
        var status = SklikJson.Bool(row, "deleted") ? AdEntityStatus.Removed : AdEntityStatus.Enabled;
        return new AdEntitySnapshot(AdEntityLevel.NegativeKeyword, prefix + id, parentLevel, parentId, text ?? string.Empty,
            status, Attributes(("text", text), ("matchType", MatchTypeName(row)), ("scope", parentLevel.ToString())));
    }

    private static AdEntitySnapshot? Build(
        JsonElement row, AdEntityLevel level, AdEntityLevel? parentLevel, string? parentId, string? name,
        params (string Key, string? Value)[] attributes)
    {
        var id = SklikJson.Id(row);
        if (id is null)
        {
            return null;
        }

        var status = SklikMappings.ToEntityStatus(SklikJson.String(row, "status"), SklikJson.Bool(row, "deleted"));
        return new AdEntitySnapshot(level, id, parentLevel, parentId, name ?? string.Empty, status, Attributes(attributes));
    }

    private static IReadOnlyDictionary<string, string?> Attributes(params (string Key, string? Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static string? MatchTypeName(JsonElement row)
    {
        var raw = SklikJson.String(row, "matchType");
        return SklikMappings.ToKeywordMatchType(raw)?.ToString() ?? raw;
    }

    private static string? Crowns(JsonElement row, string property) =>
        row.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var halers)
            ? SklikMappings.HalersToCrowns(halers).ToString(CultureInfo.InvariantCulture)
            : null;
}
```

`backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Read/SklikEntityReader.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Adapters.Sklik.Drak;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.Sklik.Read;

internal sealed class SklikEntityReader
{
    private static readonly string[] CampaignColumns = { "id", "name", "status", "deleted", "type" };
    private static readonly string[] GroupColumns = { "id", "name", "status", "deleted", "maxCpc", "campaign.id" };
    private static readonly string[] KeywordColumns = { "id", "name", "matchType", "status", "deleted", "group.id" };
    private static readonly string[] GroupNegativeColumns = { "id", "name", "matchType", "deleted", "group.id" };
    private static readonly string[] CampaignNegativeColumns = { "id", "name", "matchType", "deleted", "campaign.id" };
    private static readonly string[] AdColumns = { "id", "name", "headline1", "adType", "status", "adStatus", "deleted", "group.id" };
    private static readonly object AllEntities = new { };

    private readonly ISklikDrakClient _client;
    private readonly int _listPageSize;
    private readonly int _reportPageSize;

    public SklikEntityReader(ISklikDrakClient client, int listPageSize, int reportPageSize)
    {
        _client = client;
        _listPageSize = listPageSize;
        _reportPageSize = reportPageSize;
    }

    public async Task<IReadOnlyList<AdEntitySnapshot>> ReadAllAsync(CancellationToken ct)
    {
        var campaigns = await ListAsync("campaigns.list", "campaigns", CampaignColumns, ct);
        var groups = await ListAsync("groups.list", "groups", GroupColumns, ct);
        var keywords = await ListAsync("keywords.list", "keywords", KeywordColumns, ct);
        var groupNegatives = await ListAsync("keywords.negative.list", "keywords", GroupNegativeColumns, ct);
        var campaignNegatives = await SklikPaging.ReadReportAsync(_client, "keywords.campaign.negative", AllEntities,
            createDisplayOptions: null, CampaignNegativeColumns, _reportPageSize, allowEmptyStatistics: true, ct);
        var ads = await ListAsync("ads.list", "ads", AdColumns, ct);

        return campaigns.Select(SklikEntityMapper.Campaign)
            .Concat(groups.Select(SklikEntityMapper.AdGroup))
            .Concat(keywords.Select(SklikEntityMapper.Keyword))
            .Concat(groupNegatives.Select(SklikEntityMapper.GroupNegativeKeyword))
            .Concat(campaignNegatives.Select(SklikEntityMapper.CampaignNegativeKeyword))
            .Concat(ads.Select(SklikEntityMapper.Ad))
            .OfType<AdEntitySnapshot>()
            .ToList();
    }

    private Task<IReadOnlyList<JsonElement>> ListAsync(string method, string itemsProperty, IReadOnlyList<string> columns, CancellationToken ct) =>
        SklikPaging.ListAllAsync(_client, method, itemsProperty, AllEntities, columns, _listPageSize, ct);
}
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~SklikReadSourceEntityTests"`
Expected: `Passed!` — 21 tests.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Sklik backend/test/Anela.Heblo.Adapters.Sklik.Tests
git commit -m "feat: read sklik accounts and entities

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 6: Daily facts, search terms and `SklikReadSource` (+ contract tests)

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Read/SklikMetrics.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Read/SklikStatsReader.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Read/SklikReadSource.cs`
- Create (test support): `backend/test/Anela.Heblo.Adapters.Sklik.Tests/Fakes/SklikFixtures.Stats.cs`; fixtures `campaigns.readReport.json`, `groups.readReport.json`, `keywords.readReport.json`, `ads.readReport.json`, `queries.readReport.json`
- Test: `backend/test/Anela.Heblo.Adapters.Sklik.Tests/SklikReadSourceStatsTests.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Sklik.Tests/SklikReadSourceContractTests.cs`

**Interfaces:**
- Consumes: Tasks 3–5; C1 `IAdPlatformReadSource`, `AdDailyFactRow`, `AdSearchTermRow`, `AdChangeEventRow`, `AdSourceCapabilities`; TestKit `AdPlatformReadSourceContractTests` (abstract `CreateSource()`, `AccountExternalId`, `FixtureDate`).
- Produces:
  - `internal readonly record struct SklikMetrics(long Impressions, long Clicks, long MoneyHalers, decimal Conversions, long ConversionValueHalers)` with `Add`, `FromStats(JsonElement row)`
  - `internal sealed class SklikStatsReader(ISklikDrakClient client, int reportPageSize) { Task<IReadOnlyList<AdDailyFactRow>> ReadFactsAsync(DateOnly, CancellationToken); Task<IReadOnlyList<AdSearchTermRow>> ReadSearchTermsAsync(DateOnly, CancellationToken) }`
  - `internal sealed class SklikReadSource : IAdPlatformReadSource` — public ctor `(ISklikDrakClientFactory, IOptions<SklikSettings>)` (DI, uses the **read** client), internal ctor `(ISklikDrakClient, SklikSettings)` (tests). `Capabilities = new(SearchTerms: true, ChangeLog: false, ChangeLogMaxAge: null)`.
  - Test support: `SklikFixtures.WithStats(handler)`, `SklikFixtures.StandardAccount()`, `SklikFixtures.ReadSource(handler, settings?)`.

- [ ] **Step 1: Write the fixtures**

Under `backend/test/Anela.Heblo.Adapters.Sklik.Tests/Fixtures/` (money in haléře):

`campaigns.readReport.json`:
```json
{ "status": 200, "statusMessage": "OK", "session": "fixture-session-8", "reportId": "rep-campaigns",
  "report": [
    { "id": 111, "stats": [ { "date": "2026-10-06", "impressions": 1200, "clicks": 85, "totalMoney": 42350,
                              "conversions": 3, "conversionValue": 189000 } ] },
    { "id": 112, "stats": [] } ] }
```

`groups.readReport.json`:
```json
{ "status": 200, "statusMessage": "OK", "session": "fixture-session-9", "reportId": "rep-groups",
  "report": [ { "id": 211, "stats": [ { "impressions": 1200, "clicks": 85, "totalMoney": 42350, "conversions": 3, "conversionValue": 189000 } ] } ] }
```

`keywords.readReport.json`:
```json
{ "status": 200, "statusMessage": "OK", "session": "fixture-session-10", "reportId": "rep-keywords",
  "report": [ { "id": 311, "stats": [ { "impressions": 900, "clicks": 70, "totalMoney": 35000, "conversions": 2, "conversionValue": 126000 } ] } ] }
```

`ads.readReport.json`:
```json
{ "status": 200, "statusMessage": "OK", "session": "fixture-session-11", "reportId": "rep-ads",
  "report": [ { "id": 611, "stats": [ { "impressions": 1200, "clicks": 85, "totalMoney": 42350, "conversions": 3, "conversionValue": 189000 } ] } ] }
```

`queries.readReport.json`:
```json
{ "status": 200, "statusMessage": "OK", "session": "fixture-session-12", "reportId": "rep-queries",
  "report": [
    { "query": "anela krem", "group": { "id": 211 }, "keyword": { "id": 311, "matchType": "phrase" },
      "stats": [ { "impressions": 500, "clicks": 40, "totalMoney": 20000, "conversions": 1, "conversionValue": 63000 } ] },
    { "query": "anela krem", "group": { "id": 211 }, "keyword": { "id": 312, "matchType": "phrase" },
      "stats": [ { "impressions": 100, "clicks": 10, "totalMoney": 5000, "conversions": 0, "conversionValue": 0 } ] },
    { "query": "krém na ruce", "group": { "id": 211 }, "keyword": { "id": 313, "matchType": "broad" },
      "stats": [ { "impressions": 300, "clicks": 20, "totalMoney": 10000, "conversions": 1, "conversionValue": 63000 } ] } ] }
```

`backend/test/Anela.Heblo.Adapters.Sklik.Tests/Fakes/SklikFixtures.Stats.cs`:

```csharp
using Anela.Heblo.Adapters.Sklik.Read;

namespace Anela.Heblo.Adapters.Sklik.Tests.Fakes;

internal static partial class SklikFixtures
{
    public static RecordedSklikHandler WithStats(RecordedSklikHandler handler) => handler
        .Respond("campaigns.createReport", CreatedReport("rep-campaigns", 2))
        .RespondWithFixture("campaigns.readReport", "campaigns.readReport.json")
        .Respond("groups.createReport", CreatedReport("rep-groups", 1))
        .RespondWithFixture("groups.readReport", "groups.readReport.json")
        .Respond("keywords.createReport", CreatedReport("rep-keywords", 1))
        .RespondWithFixture("keywords.readReport", "keywords.readReport.json")
        .Respond("ads.createReport", CreatedReport("rep-ads", 1))
        .RespondWithFixture("ads.readReport", "ads.readReport.json")
        .Respond("queries.createReport", CreatedReport("rep-queries", 3))
        .RespondWithFixture("queries.readReport", "queries.readReport.json");

    public static RecordedSklikHandler StandardAccount() => WithStats(EntityAccount());

    public static SklikReadSource ReadSource(RecordedSklikHandler handler, SklikSettings? settings = null)
    {
        var effective = settings ?? Settings();
        return new SklikReadSource(Client(handler, effective), effective);
    }
}
```

- [ ] **Step 2: Write the failing tests**

`backend/test/Anela.Heblo.Adapters.Sklik.Tests/SklikReadSourceStatsTests.cs`:

```csharp
using Anela.Heblo.Adapters.Sklik.Tests.Fakes;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Adapters.Sklik.Tests;

public class SklikReadSourceStatsTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateOnly Day = SklikFixtures.Date;

    [Fact]
    public async Task converts_halers_to_crowns_for_every_level_and_skips_entities_without_stats()
    {
        var source = SklikFixtures.ReadSource(SklikFixtures.StandardAccount());

        var facts = await source.GetDailyFactsAsync(SklikFixtures.AccountId, Day, Ct);

        facts.Should().HaveCount(4);
        facts.Single(f => f.Level == AdEntityLevel.Campaign).Should().Be(
            new AdDailyFactRow(AdEntityLevel.Campaign, "111", Day, 1200, 85, 423.50m, 3m, 1890.00m, "CZK"));
        facts.Single(f => f.Level == AdEntityLevel.AdGroup).EntityExternalId.Should().Be("211");
        facts.Single(f => f.Level == AdEntityLevel.Keyword).Cost.Should().Be(350.00m);
        facts.Single(f => f.Level == AdEntityLevel.Ad).EntityExternalId.Should().Be("611");
        facts.Should().NotContain(f => f.EntityExternalId == "112");
    }

    [Fact]
    public async Task requests_one_day_of_total_statistics_per_level()
    {
        var handler = SklikFixtures.StandardAccount();

        await SklikFixtures.ReadSource(handler).GetDailyFactsAsync(SklikFixtures.AccountId, Day, Ct);

        foreach (var prefix in new[] { "campaigns", "groups", "keywords", "ads" })
        {
            var create = handler.CallsTo($"{prefix}.createReport").Should().ContainSingle().Subject;
            create.Parameter(0).GetProperty("dateFrom").GetString().Should().Be("2026-10-06");
            create.Parameter(0).GetProperty("dateTo").GetString().Should().Be("2026-10-06");
            create.Parameter(1).GetProperty("statGranularity").GetString().Should().Be("total");

            var read = handler.CallsTo($"{prefix}.readReport").Should().ContainSingle().Subject;
            read.Parameter(0).GetString().Should().Be($"rep-{prefix}");
            read.Parameter(1).GetProperty("allowEmptyStatistics").GetBoolean().Should().BeFalse();
            read.Parameter(1).GetProperty("displayColumns").EnumerateArray().Select(c => c.GetString())
                .Should().Contain(new[] { "id", "impressions", "clicks", "totalMoney", "conversions", "conversionValue" });
        }
    }

    [Fact]
    public async Task pages_through_a_report_until_total_count()
    {
        var handler = SklikFixtures.StandardAccount().Respond("campaigns.readReport",
            """{"status":200,"statusMessage":"OK","report":[{"id":111,"stats":[{"impressions":1,"clicks":1,"totalMoney":100,"conversions":0,"conversionValue":0}]}]}""",
            """{"status":200,"statusMessage":"OK","report":[{"id":113,"stats":[{"impressions":2,"clicks":1,"totalMoney":250,"conversions":0,"conversionValue":0}]}]}""");
        var source = SklikFixtures.ReadSource(handler, SklikFixtures.Settings(reportPageSize: 1));

        var facts = await source.GetDailyFactsAsync(SklikFixtures.AccountId, Day, Ct);

        facts.Where(f => f.Level == AdEntityLevel.Campaign).Select(f => f.EntityExternalId).Should().Equal("111", "113");
        handler.CallsTo("campaigns.readReport").Select(c => c.Parameter(1).GetProperty("offset").GetInt32()).Should().Equal(0, 1);
    }

    [Fact]
    public async Task aggregates_search_terms_that_share_group_query_and_match_type()
    {
        var source = SklikFixtures.ReadSource(SklikFixtures.StandardAccount());

        var terms = await source.GetSearchTermsAsync(SklikFixtures.AccountId, Day, Ct);

        terms.Should().BeEquivalentTo(new[]
        {
            new AdSearchTermRow("211", Day, "anela krem", KeywordMatchType.Phrase, 600, 50, 250.00m, 1m, 630.00m, "CZK"),
            new AdSearchTermRow("211", Day, "krém na ruce", KeywordMatchType.Broad, 300, 20, 100.00m, 1m, 630.00m, "CZK"),
        });
    }

    [Fact]
    public async Task requests_the_search_term_columns_for_one_day()
    {
        var handler = SklikFixtures.StandardAccount();

        await SklikFixtures.ReadSource(handler).GetSearchTermsAsync(SklikFixtures.AccountId, Day, Ct);

        handler.CallsTo("queries.createReport").Single().Parameter(0).GetProperty("dateFrom").GetString().Should().Be("2026-10-06");
        handler.CallsTo("queries.readReport").Single().Parameter(1).GetProperty("displayColumns").EnumerateArray()
            .Select(c => c.GetString()).Should().Contain(new[] { "query", "group.id", "keyword.matchType", "totalMoney" });
    }

    [Fact]
    public async Task has_no_change_log_and_returns_no_change_events_without_calling_sklik()
    {
        var handler = SklikFixtures.StandardAccount();
        var source = SklikFixtures.ReadSource(handler);

        var events = await source.GetChangeEventsAsync(SklikFixtures.AccountId, DateTimeOffset.UtcNow.AddDays(-1), Ct);

        source.Platform.Should().Be(AdPlatform.Sklik);
        source.Capabilities.Should().Be(new AdSourceCapabilities(SearchTerms: true, ChangeLog: false, ChangeLogMaxAge: null));
        events.Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task reports_one_account_and_refuses_others()
    {
        var source = SklikFixtures.ReadSource(SklikFixtures.StandardAccount());

        var accounts = await source.GetAccountsAsync(Ct);
        var act = () => source.GetDailyFactsAsync("999", Day, Ct);

        accounts.Should().ContainSingle().Which.ExternalId.Should().Be(SklikFixtures.AccountId);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task sends_the_managed_user_id_on_every_read_when_configured()
    {
        var handler = SklikFixtures.StandardAccount().RespondWithFixture("client.get", "client.get.managed.json");
        var source = SklikFixtures.ReadSource(handler, SklikFixtures.Settings(userId: "7654321"));

        await source.GetEntitiesAsync("7654321", Ct);
        await source.GetDailyFactsAsync("7654321", Day, Ct);
        await source.GetSearchTermsAsync("7654321", Day, Ct);

        handler.Requests.Where(r => r.Method is not ("client.loginByToken" or "client.get"))
            .Should().NotBeEmpty()
            .And.OnlyContain(r => r.User.GetProperty("userId").GetInt64() == 7654321);
    }
}
```

`backend/test/Anela.Heblo.Adapters.Sklik.Tests/SklikReadSourceContractTests.cs`:

```csharp
using Anela.Heblo.Adapters.Sklik.Tests.Fakes;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.MarketingAds.TestKit;

namespace Anela.Heblo.Adapters.Sklik.Tests;

public sealed class SklikReadSourceContractTests : AdPlatformReadSourceContractTests
{
    protected override IAdPlatformReadSource CreateSource() => SklikFixtures.ReadSource(SklikFixtures.StandardAccount());

    protected override string AccountExternalId => SklikFixtures.AccountId;

    protected override DateOnly FixtureDate => SklikFixtures.Date;
}
```

- [ ] **Step 3: Run them to verify they fail**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj -p:UseSharedCompilation=false`
Expected: build FAILS with `CS0246: The type or namespace name 'SklikReadSource' could not be found`.

- [ ] **Step 4: Implement metrics and the stats reader**

`backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Read/SklikMetrics.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Adapters.Sklik.Drak;

namespace Anela.Heblo.Adapters.Sklik.Read;

/// <summary>Raw Drak statistics; money stays in haléře until mapped.</summary>
internal readonly record struct SklikMetrics(
    long Impressions, long Clicks, long MoneyHalers, decimal Conversions, long ConversionValueHalers)
{
    public static SklikMetrics Add(SklikMetrics left, SklikMetrics right) => new(
        left.Impressions + right.Impressions,
        left.Clicks + right.Clicks,
        left.MoneyHalers + right.MoneyHalers,
        left.Conversions + right.Conversions,
        left.ConversionValueHalers + right.ConversionValueHalers);

    /// <summary>Sums a report row's <c>stats</c> array (one entry with statGranularity=total).</summary>
    public static SklikMetrics FromStats(JsonElement row) =>
        SklikJson.Array(row, "stats").Select(FromStat).Aggregate(default(SklikMetrics), Add);

    private static SklikMetrics FromStat(JsonElement stat) => new(
        SklikJson.Long(stat, "impressions"),
        SklikJson.Long(stat, "clicks"),
        SklikJson.Long(stat, "totalMoney"),
        SklikJson.Decimal(stat, "conversions"),
        SklikJson.Long(stat, "conversionValue"));
}
```

`backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Read/SklikStatsReader.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using Anela.Heblo.Adapters.Sklik.Drak;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.Sklik.Read;

internal sealed class SklikStatsReader
{
    private static readonly string[] MetricColumns = { "impressions", "clicks", "totalMoney", "conversions", "conversionValue" };
    private static readonly string[] FactColumns = new[] { "id" }.Concat(MetricColumns).ToArray();
    private static readonly string[] SearchTermColumns = new[] { "query", "group.id", "keyword.matchType" }.Concat(MetricColumns).ToArray();
    private static readonly (string Prefix, AdEntityLevel Level)[] FactLevels =
    {
        ("campaigns", AdEntityLevel.Campaign),
        ("groups", AdEntityLevel.AdGroup),
        ("keywords", AdEntityLevel.Keyword),
        ("ads", AdEntityLevel.Ad),
    };
    private static readonly object TotalGranularity = new { statGranularity = "total" };

    private readonly ISklikDrakClient _client;
    private readonly int _reportPageSize;

    public SklikStatsReader(ISklikDrakClient client, int reportPageSize)
    {
        _client = client;
        _reportPageSize = reportPageSize;
    }

    public async Task<IReadOnlyList<AdDailyFactRow>> ReadFactsAsync(DateOnly date, CancellationToken ct)
    {
        var facts = new List<AdDailyFactRow>();
        foreach (var (prefix, level) in FactLevels)
        {
            var rows = await ReadDayAsync(prefix, date, FactColumns, ct);
            facts.AddRange(rows.Select(row => ToFact(level, date, row)).OfType<AdDailyFactRow>());
        }

        return facts;
    }

    public async Task<IReadOnlyList<AdSearchTermRow>> ReadSearchTermsAsync(DateOnly date, CancellationToken ct)
    {
        var rows = await ReadDayAsync("queries", date, SearchTermColumns, ct);
        return rows
            .Select(row => new
            {
                GroupId = SklikJson.NestedId(row, "group"),
                Query = SklikJson.String(row, "query"),
                MatchType = SklikMappings.ToKeywordMatchType(SklikJson.NestedString(row, "keyword", "matchType")),
                HasStats = SklikJson.Array(row, "stats").Count > 0,
                Metrics = SklikMetrics.FromStats(row),
            })
            .Where(row => row.GroupId is not null && !string.IsNullOrWhiteSpace(row.Query) && row.HasStats)
            .GroupBy(row => (GroupId: row.GroupId!, Query: row.Query!, row.MatchType))
            .Select(group =>
            {
                var metrics = group.Select(row => row.Metrics).Aggregate(default(SklikMetrics), SklikMetrics.Add);
                return new AdSearchTermRow(group.Key.GroupId, date, group.Key.Query, group.Key.MatchType,
                    metrics.Impressions, metrics.Clicks, SklikMappings.HalersToCrowns(metrics.MoneyHalers),
                    metrics.Conversions, SklikMappings.HalersToCrowns(metrics.ConversionValueHalers), SklikConstants.Currency);
            })
            .ToList();
    }

    private Task<IReadOnlyList<JsonElement>> ReadDayAsync(string prefix, DateOnly date, IReadOnlyList<string> columns, CancellationToken ct)
    {
        var day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return SklikPaging.ReadReportAsync(_client, prefix, new { dateFrom = day, dateTo = day }, TotalGranularity,
            columns, _reportPageSize, allowEmptyStatistics: false, ct);
    }

    private static AdDailyFactRow? ToFact(AdEntityLevel level, DateOnly date, JsonElement row)
    {
        var id = SklikJson.Id(row);
        if (id is null || SklikJson.Array(row, "stats").Count == 0)
        {
            return null;
        }

        var metrics = SklikMetrics.FromStats(row);
        return new AdDailyFactRow(level, id, date, metrics.Impressions, metrics.Clicks,
            SklikMappings.HalersToCrowns(metrics.MoneyHalers), metrics.Conversions,
            SklikMappings.HalersToCrowns(metrics.ConversionValueHalers), SklikConstants.Currency);
    }
}
```

- [ ] **Step 5: Implement the read source**

`backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Read/SklikReadSource.cs`:

```csharp
using Anela.Heblo.Adapters.Sklik.Drak;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Sklik.Read;

/// <summary>
/// Sklik (API Drak v5) read side. One account per Heblo installation; Sklik has no change-history API, so the core's
/// snapshot-diff fallback covers change detection. Throws on transport/auth errors (the core isolates per source).
/// </summary>
internal sealed class SklikReadSource : IAdPlatformReadSource
{
    private readonly SklikAccountResolver _accounts;
    private readonly SklikEntityReader _entities;
    private readonly SklikStatsReader _stats;

    public SklikReadSource(ISklikDrakClientFactory clientFactory, IOptions<SklikSettings> options)
        : this(clientFactory.CreateReadClient(), options.Value)
    {
    }

    internal SklikReadSource(ISklikDrakClient client, SklikSettings settings)
    {
        _accounts = new SklikAccountResolver(client, settings);
        _entities = new SklikEntityReader(client, settings.ListPageSize, settings.ReportPageSize);
        _stats = new SklikStatsReader(client, settings.ReportPageSize);
    }

    public AdPlatform Platform => AdPlatform.Sklik;

    public AdSourceCapabilities Capabilities { get; } = new(SearchTerms: true, ChangeLog: false, ChangeLogMaxAge: null);

    public async Task<IReadOnlyList<AdAccountSnapshot>> GetAccountsAsync(CancellationToken ct) =>
        new[] { await _accounts.GetAsync(ct) };

    public async Task<IReadOnlyList<AdEntitySnapshot>> GetEntitiesAsync(string accountExternalId, CancellationToken ct)
    {
        await _accounts.EnsureAsync(accountExternalId, ct);
        return await _entities.ReadAllAsync(ct);
    }

    public async Task<IReadOnlyList<AdDailyFactRow>> GetDailyFactsAsync(string accountExternalId, DateOnly date, CancellationToken ct)
    {
        await _accounts.EnsureAsync(accountExternalId, ct);
        return await _stats.ReadFactsAsync(date, ct);
    }

    public async Task<IReadOnlyList<AdSearchTermRow>> GetSearchTermsAsync(string accountExternalId, DateOnly date, CancellationToken ct)
    {
        await _accounts.EnsureAsync(accountExternalId, ct);
        return await _stats.ReadSearchTermsAsync(date, ct);
    }

    public Task<IReadOnlyList<AdChangeEventRow>> GetChangeEventsAsync(
        string accountExternalId, DateTimeOffset since, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AdChangeEventRow>>(Array.Empty<AdChangeEventRow>());
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~SklikReadSourceStatsTests|FullyQualifiedName~SklikReadSourceContractTests"`
Expected: `Passed!` — 8 stats tests plus every test inherited from `AdPlatformReadSourceContractTests`. If an inherited contract test fails, read its assertion: fix the fixture only if it violates the documented Drak shape; otherwise fix the adapter. Never edit the TestKit from this branch.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Sklik backend/test/Anela.Heblo.Adapters.Sklik.Tests
git commit -m "feat: sklik read source with daily facts and search terms

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 7: Gated registration, API wiring, configuration

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/SklikAdapterServiceCollectionExtensions.cs`
- Modify: `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` (ProjectReference block, next to `Anela.Heblo.Adapters.Ecomail`)
- Modify: `backend/src/Anela.Heblo.API/Program.cs` (usings at the top; adapter block around line 126, after `AddGoogleAnalyticsAdapter`)
- Modify: `backend/src/Anela.Heblo.API/appsettings.json` (new `Sklik` section after `GoogleAds`)
- Test: `backend/test/Anela.Heblo.Adapters.Sklik.Tests/SklikAdapterRegistrationTests.cs`

**Interfaces:**
- Consumes: `SklikSettings.IsUsable`, `SklikDrakClientFactory`, `SklikReadSource`.
- Produces: `public static IServiceCollection AddSklikAdapter(this IServiceCollection services, IConfiguration configuration)` — Part C adds the executor registration inside it.

- [ ] **Step 1: Write the failing registration tests**

`backend/test/Anela.Heblo.Adapters.Sklik.Tests/SklikAdapterRegistrationTests.cs`:

```csharp
using Anela.Heblo.Adapters.Sklik.Read;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.Sklik.Tests;

public class SklikAdapterRegistrationTests
{
    private static IServiceCollection Register(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddSklikAdapter(configuration);
    }

    private static Dictionary<string, string?> Configured() => new()
    {
        ["Sklik:ApiToken"] = "real-token",
        ["Sklik:BaseUrl"] = "https://api.sklik.cz/drak/json/v5",
    };

    [Theory]
    [InlineData("")]
    [InlineData("-- stored in Key Vault --")]
    [InlineData("XXX")]
    public void registers_nothing_without_a_real_token(string token)
    {
        var settings = Configured();
        settings["Sklik:ApiToken"] = token;

        Register(settings).Should().BeEmpty("an unconfigured environment must stay completely inert");
    }

    [Fact]
    public void registers_nothing_when_the_section_is_missing()
    {
        Register(new Dictionary<string, string?>()).Should().BeEmpty();
    }

    [Fact]
    public void registers_nothing_with_a_malformed_user_id()
    {
        var settings = Configured();
        settings["Sklik:UserId"] = "anela";

        Register(settings).Should().BeEmpty();
    }

    [Fact]
    public void registers_nothing_and_does_not_throw_on_an_unparseable_number()
    {
        var settings = Configured();
        settings["Sklik:ListPageSize"] = "lots";

        var act = () => Register(settings);

        act.Should().NotThrow().Which.Should().BeEmpty();
    }

    [Fact]
    public void registers_the_read_source_when_configured()
    {
        var services = Register(Configured());

        services.Should().Contain(d =>
            d.ServiceType == typeof(IAdPlatformReadSource) && d.ImplementationType == typeof(SklikReadSource)
            && d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void resolves_the_read_source_from_a_scope()
    {
        var services = Register(Configured());
        services.AddLogging();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var sources = scope.ServiceProvider.GetServices<IAdPlatformReadSource>().ToList();

        sources.Should().ContainSingle().Which.Platform.Should().Be(AdPlatform.Sklik);
    }
}
```

The `"XXX"` case relies on spec 12.2: `AdSettingsGuard` treats values containing `XXX` as placeholders.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj -p:UseSharedCompilation=false`
Expected: build FAILS with `CS1061: 'ServiceCollection' does not contain a definition for 'AddSklikAdapter'`.

- [ ] **Step 3: Implement the registration**

`backend/src/Adapters/Anela.Heblo.Adapters.Sklik/SklikAdapterServiceCollectionExtensions.cs`:

```csharp
using Anela.Heblo.Adapters.Sklik.Drak;
using Anela.Heblo.Adapters.Sklik.Read;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Anela.Heblo.Adapters.Sklik;

public static class SklikAdapterServiceCollectionExtensions
{
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Registers the Sklik read source only when <c>Sklik:ApiToken</c> is really configured (not empty, not a Key Vault
    /// placeholder) and the rest of the section is valid. Never throws: a missing or broken configuration leaves the
    /// environment completely inert, so one bad secret cannot stop the app from booting.
    /// </summary>
    public static IServiceCollection AddSklikAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(SklikSettings.ConfigurationKey);
        var settings = TryBind(section);
        if (settings is null || !settings.IsUsable)
        {
            return services;
        }

        services.Configure<SklikSettings>(section);

        services.AddHttpClient(SklikHttpClientNames.Read, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .AddResilienceHandler("sklik-read", builder =>
            {
                if (settings.RetryCount > 0)
                {
                    // Default ShouldHandle: HTTP 408/429/5xx and transport errors only — never Drak 4xx.
                    builder.AddRetry(new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = settings.RetryCount,
                        Delay = RetryBaseDelay,
                        BackoffType = DelayBackoffType.Exponential,
                        UseJitter = true,
                    });
                }

                builder.AddTimeout(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
            });

        // Writes are never retried: see SklikHttpClientNames.Write.
        services.AddHttpClient(SklikHttpClientNames.Write, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .AddResilienceHandler("sklik-write", builder => builder.AddTimeout(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds)));

        services.AddScoped<ISklikDrakClientFactory, SklikDrakClientFactory>();
        services.AddScoped<IAdPlatformReadSource, SklikReadSource>();
        return services;
    }

    private static SklikSettings? TryBind(IConfigurationSection section)
    {
        var settings = new SklikSettings();
        try
        {
            section.Bind(settings);
            return settings;
        }
        catch (InvalidOperationException)
        {
            // A non-numeric page size/timeout. Registration must not throw; the source simply stays unregistered.
            return null;
        }
    }
}
```

- [ ] **Step 4: Run the registration tests**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~SklikAdapterRegistrationTests"`
Expected: `Passed!` — 8 tests.

- [ ] **Step 5: Wire the adapter into the API**

In `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`, inside the ItemGroup holding the adapter references, after the `Anela.Heblo.Adapters.Ecomail` line add:

```xml
        <ProjectReference Include="..\Adapters\Anela.Heblo.Adapters.Sklik\Anela.Heblo.Adapters.Sklik.csproj" />
```

In `backend/src/Anela.Heblo.API/Program.cs` add `using Anela.Heblo.Adapters.Sklik;` after `using Anela.Heblo.Adapters.MetaAds;`, and after `builder.Services.AddGoogleAnalyticsAdapter(builder.Configuration, builder.Environment);` add:

```csharp
        builder.Services.AddSklikAdapter(builder.Configuration);
```

In `backend/src/Anela.Heblo.API/appsettings.json`, after the `"GoogleAds": { … }` section add (no `ApiToken` / `UserId` keys — those live only in Key Vault):

```json
  "Sklik": {
    "BaseUrl": "https://api.sklik.cz/drak/json/v5",
    "ListPageSize": 100,
    "ReportPageSize": 1000,
    "RequestTimeoutSeconds": 30,
    "RetryCount": 3
  },
```

- [ ] **Step 6: Build the API**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj -p:UseSharedCompilation=false`
Expected: `Build succeeded.` with 0 errors. Also: `python3 -c "import json;json.load(open('backend/src/Anela.Heblo.API/appsettings.json'))" && echo valid` → `valid`.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Sklik/SklikAdapterServiceCollectionExtensions.cs backend/test/Anela.Heblo.Adapters.Sklik.Tests/SklikAdapterRegistrationTests.cs backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj backend/src/Anela.Heblo.API/Program.cs backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat: register sklik read source when configured

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 8: Process doc, secrets checklist, full validation, PR

**Files:**
- Create: `docs/processes/sync-ads-sklik.md`
- Modify (generated): `docs/processes/INDEX.md`

**Interfaces:**
- Consumes: everything above; the core C2 process docs if they exist.
- Produces: PR `feat/marketing-sklik-read-source` → `main`.

- [ ] **Step 1: Find the core's ads process docs**

Run: `ls docs/processes | grep -i -E 'ads|marketing-ads'`
Core PR C2 plans `sync-ads-daily.md`, `sync-ads-change-history.md` and `module-marketing-ads.md`. Set `related: [sync-ads-daily, sync-ads-change-history]` for the names that exist on main; if neither exists yet, use `related: []` and mention it in the PR description.

- [ ] **Step 2: Write the process doc**

`docs/processes/sync-ads-sklik.md` (`verified_at` = output of `git rev-parse --short=9 HEAD`, quoted):

```markdown
---
process: sync-ads-sklik
kind: sync
module: marketing-ads
summary: Sklik (Seznam.cz) data source for the ads backbone — account, campaigns, ad groups, keywords, negative keywords, ads, one-day statistics and search terms read through API Drak v5; no change log, so changes come from snapshot diffs.
owns:
  - backend/src/Adapters/Anela.Heblo.Adapters.Sklik/**
verified_at: "<short sha>"
related: []
---

# Sklik read source for the ads backbone

## Purpose
Feeds Sklik data into the `ads` schema so agents (MCP) and people (Metabase `v_ads_*`) see Sklik next to Google Ads and
Meta at management granularity: campaign → ad group → keyword / search term → ad. Sklik spend is ~0.15 M CZK/year.

## Trigger
No job of its own. `SklikReadSource` is called by the core's daily ads sync job (05:30 Europe/Prague, yesterday plus
`Ads:FactLookbackDays` lookback) and change sync job (hourly; Sklik has no change log, so only the snapshot-diff
fallback runs for it). Registered only when Key Vault `Sklik--ApiToken` holds a real value.

## Data flow
1. `client.loginByToken` with the API token → session (one login per job scope; refreshed session from every response;
   one re-login on status 401).
2. `client.get` → the managed account (token's own login, or the foreign account named by `Sklik:UserId`).
3. Entities: `campaigns.list`, `groups.list`, `keywords.list`, `keywords.negative.list`,
   `keywords.campaign.negative.createReport/readReport`, `ads.list` → `AdEntitySnapshot` → `ads.ad_entities`.
4. Facts: `{campaigns,groups,keywords,ads}.createReport` (one day, `statGranularity=total`) → paged `readReport`
   → `AdDailyFactRow` → `ads.ad_daily_facts`.
5. Search terms: `queries.createReport/readReport` → aggregated `AdSearchTermRow` → `ads.ad_search_term_daily`.
6. Change events: none from Sklik (`Capabilities.ChangeLog = false`); the core diffs entity snapshots.

## Logic & formulas
- Money (`totalMoney`, `conversionValue`, `maxCpc`) is integer haléře ÷ 100 → CZK, **without VAT**.
- Facts are emitted only for entities with statistics that day; every level is stored — queries must aggregate one level.
- Search terms: one Drak row per (query, matched keyword); rows sharing (ad group, query, keyword match type) are summed.
- Status: `deleted` → Removed, `active` → Enabled, `suspend` → Paused, else Unknown.
- Negative keyword external ids: `g:<id>` (ad group), `c:<id>` (campaign).
- Account: Sklik user id; currency `CZK`, time zone `Europe/Prague`.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `Sklik:ApiToken` | — (Key Vault `Sklik--ApiToken`) | API token; registration gate |
| `Sklik:UserId` | — (Key Vault `Sklik--UserId`, optional) | Managed account id when reached via a shared/agency login |
| `Sklik:BaseUrl` | `https://api.sklik.cz/drak/json/v5` | Drak JSON endpoint |
| `Sklik:ListPageSize` | `100` | `*.list` page size |
| `Sklik:ReportPageSize` | `1000` | `*.readReport` page size (≤ `statsDataLimit`) |
| `Sklik:RequestTimeoutSeconds` | `30` | Per-attempt HTTP timeout |
| `Sklik:RetryCount` | `3` | Read-side retries of HTTP 408/429/5xx; writes never retry |

## Runtime facts
- Access model, limits and spike totals — `docs/integrations/sklik-api.md` §3, §5, §6 — <date of the spike>.

## Known quirks
- Drak answers most errors with HTTP 200 and a body `status`; only body status decides success.
- Body-level 429/5xx are not retried within a run; the 14-day lookback recovers missed days.
- Heblo's *Marketing → Analýzy* negative Sklik cost comes from Flexi invoices, not from this source.

## Code entry points
- `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Read/SklikReadSource.cs` — the `IAdPlatformReadSource`
- `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Drak/SklikDrakClient.cs` — login, session, status mapping
- `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/SklikAdapterServiceCollectionExtensions.cs` — registration gate
```

Replace `<short sha>` and `<date of the spike>` with the real values before saving.

- [ ] **Step 3: Regenerate and check the process index**

Run: `python3 scripts/process-docs/check.py index && python3 scripts/process-docs/check.py check`
Expected: the second command exits 0 with no `ERROR` lines mentioning `sync-ads-sklik`. If it reports an unknown `related` name or module, fix the front matter, not the checker.

- [ ] **Step 4: Full backend validation**

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet format Anela.Heblo.sln --include backend/src/Adapters/Anela.Heblo.Adapters.Sklik/ backend/test/Anela.Heblo.Adapters.Sklik.Tests/ backend/src/Anela.Heblo.API/Program.cs
dotnet format Anela.Heblo.sln --verify-no-changes --include backend/src/Adapters/Anela.Heblo.Adapters.Sklik/ backend/test/Anela.Heblo.Adapters.Sklik.Tests/ backend/src/Anela.Heblo.API/Program.cs
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj --no-build -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "Category!=Playwright&Category!=Integration"
```

Expected: both builds `0 Error(s)`; `--verify-no-changes` exits 0; all Sklik tests pass; `Anela.Heblo.Tests` passes (it boots the API composition root, which now calls `AddSklikAdapter` with no token → inert).

Leak check: `grep -rn -i "loginByToken\|session" backend/src/Adapters/Anela.Heblo.Adapters.Sklik --include=*.cs | grep -i "Log\(Information\|Warning\|Error\|Debug\)"` → must print nothing that interpolates a token or session value.

- [ ] **Step 5: Commit the docs**

```bash
git add docs/processes/sync-ads-sklik.md docs/processes/INDEX.md
git commit -m "docs: process doc for the sklik ads source

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 6: Push and open the PR**

```bash
git push -u origin feat/marketing-sklik-read-source
gh pr create --base main --title "feat: Sklik read source for the ads backbone (WS3)" --body "$(cat <<'EOF'
## Summary
- New adapter `Anela.Heblo.Adapters.Sklik` (API Drak v5 JSON): login/session handling, status → exception mapping, session stripped from everything returned.
- `SklikReadSource : IAdPlatformReadSource` — account, campaigns/groups/keywords/negatives/ads, one-day facts per level (haléře → CZK, without VAT), aggregated search terms; `ChangeLog = false` (snapshot-diff fallback).
- Registration gated on `AdSettingsGuard` + valid section; never throws at startup.
- `docs/integrations/sklik-api.md` (spike findings) and `docs/processes/sync-ads-sklik.md`.

## Secrets Ondrej must set (the agent did not)
Staging:
- `az keyvault secret set --vault-name kv-heblo-stg --name "Sklik--ApiToken" --value "<token>"`
- only if access is via a shared/agency login: `az keyvault secret set --vault-name kv-heblo-stg --name "Sklik--UserId" --value "<sklik user id>"`
Production: the same names in `kv-heblo-prod`. Restart the web app after setting them.

## Deviations from the spec
<copy the "Spec deviations" items 1, 2, 6, 7 of the plan, plus any Task 2 Step 3 mismatches>

## Test plan
- [x] `Anela.Heblo.Adapters.Sklik.Tests` incl. inherited `AdPlatformReadSourceContractTests`
- [x] `Anela.Heblo.Tests` (non-integration)
- [ ] After secrets + C2 deploy on staging: daily sync job writes Sklik rows to `ads.ad_daily_facts`; yesterday's campaign cost matches the Sklik UI

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Replace the `<copy …>` line with the actual text before running. Then check CI: `gh pr checks --watch` (an empty list right after creation means "not registered yet", wait and re-run).

---

# Part C — Executor PR (`feat/marketing-sklik-executor`)

Set up the worktree as in "Before you start" (`<dir>` = `marketing-sklik-executor`, `<branch>` = `feat/marketing-sklik-executor`), including the Part B presence check. Nothing in this part performs a live call; Ondrej runs the manual smoke (Task 12) himself.

### Task 9: Stateful fake Drak server and `SklikPauseAdOperation`

**Files:**
- Create: `backend/test/Anela.Heblo.Adapters.Sklik.Tests/Fakes/FakeSklikDrakServer.cs`
- Create: `backend/test/Anela.Heblo.Adapters.Sklik.Tests/Fakes/SklikExecutorFixtures.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Execution/SklikPauseAdOperation.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Sklik.Tests/SklikPauseAdOperationTests.cs`

**Interfaces:**
- Consumes: `ISklikDrakClient`, `SklikJson`, `SklikMappings`, `SklikRecordedRequest`, `SklikFixtures.Settings/Client` (Part B); C1 `AdTargetState`, `AdExecutionResult`, `AdExecutionOutcome`, `AdActionValues`.
- Produces:
  - `internal sealed class SklikPauseAdOperation(ISklikDrakClient client) { Task<AdTargetState> ReadAsync(string adId, CancellationToken); Task<AdExecutionResult> PauseAsync(string adId, CancellationToken); Task<AdExecutionResult> ResumeAsync(string adId, CancellationToken) }`
  - `FakeSklikDrakServer` (`WithAd`, `WithGroup`, `WithCampaign`, `FailNext`, `AdStatus`, `GroupNegatives`, `CampaignNegatives`, `CallsTo`, `Requests`)
  - `SklikExecutorFixtures` (`AccountId`, `StandardServer()`, `Client(server)`)

- [ ] **Step 1: Write the fake server**

`backend/test/Anela.Heblo.Adapters.Sklik.Tests/Fakes/FakeSklikDrakServer.cs`:

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Anela.Heblo.Adapters.Sklik.Tests.Fakes;

/// <summary>
/// Stateful in-memory stand-in for the Drak methods SklikActionExecutor uses, following the documented semantics.
/// campaigns.update negativeKeywords is modelled as a full REPLACE — inferred from the docs, confirmed only by the
/// manual smoke (docs/integrations/sklik-api.md §12).
/// </summary>
internal sealed class FakeSklikDrakServer : HttpMessageHandler
{
    private sealed record Negative(long Id, string Name, string MatchType);
    private sealed record Failure(int Status, string Message, string DiagnosticId);

    private readonly Dictionary<long, (string Status, string AdType)> _ads = new();
    private readonly HashSet<long> _groups = new();
    private readonly HashSet<long> _campaigns = new();
    private readonly Dictionary<long, List<Negative>> _groupNegatives = new();
    private readonly Dictionary<long, List<Negative>> _campaignNegatives = new();
    private readonly Dictionary<string, Queue<Failure>> _failures = new(StringComparer.Ordinal);
    private long _nextId = 9000;
    private long _reportCampaignId;

    public List<SklikRecordedRequest> Requests { get; } = new();

    public FakeSklikDrakServer WithAd(long id, string status = "active", string adType = "eta")
    {
        _ads[id] = (status, adType);
        return this;
    }

    public FakeSklikDrakServer WithGroup(long id, params (string Name, string MatchType)[] negatives)
    {
        _groups.Add(id);
        _groupNegatives[id] = negatives.Select(n => new Negative(++_nextId, n.Name, n.MatchType)).ToList();
        return this;
    }

    public FakeSklikDrakServer WithCampaign(long id, params (string Name, string MatchType)[] negatives)
    {
        _campaigns.Add(id);
        _campaignNegatives[id] = negatives.Select(n => new Negative(++_nextId, n.Name, n.MatchType)).ToList();
        return this;
    }

    public FakeSklikDrakServer FailNext(string method, int status, string message, string diagnosticId)
    {
        if (!_failures.TryGetValue(method, out var queue))
        {
            queue = new Queue<Failure>();
            _failures[method] = queue;
        }

        queue.Enqueue(new Failure(status, message, diagnosticId));
        return this;
    }

    public string? AdStatus(long id) => _ads.TryGetValue(id, out var ad) ? ad.Status : null;

    public IReadOnlyList<(string Name, string MatchType)> GroupNegatives(long groupId) => Snapshot(_groupNegatives, groupId);

    public IReadOnlyList<(string Name, string MatchType)> CampaignNegatives(long campaignId) => Snapshot(_campaignNegatives, campaignId);

    public IReadOnlyList<SklikRecordedRequest> CallsTo(string method) => Requests.Where(r => r.Method == method).ToList();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var method = request.RequestUri!.Segments[^1];
        using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        var body = document.RootElement.Clone();
        Requests.Add(new SklikRecordedRequest(method, request.RequestUri, body));

        var response = _failures.TryGetValue(method, out var queue) && queue.Count > 0
            ? Error(queue.Peek().Status, queue.Peek().Message, queue.Dequeue().DiagnosticId)
            : Handle(method, body);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }

    private JsonObject Handle(string method, JsonElement body) => method switch
    {
        "client.loginByToken" => Ok(new JsonObject()),
        "client.get" => Ok(new JsonObject
        {
            ["user"] = new JsonObject { ["userId"] = 1234567, ["username"] = "eshop@anela.cz" },
            ["foreignAccounts"] = new JsonArray(),
        }),
        "ads.list" => ListAds(body),
        "ads.update" => UpdateAds(body),
        "groups.list" => ListExisting(body, _groups, "groups"),
        "campaigns.list" => ListExisting(body, _campaigns, "campaigns"),
        "keywords.negative.list" => ListGroupNegatives(body),
        "keywords.negative.create" => CreateGroupNegatives(body),
        "keywords.negative.remove" => RemoveGroupNegatives(body),
        "keywords.campaign.negative.createReport" => CreateCampaignNegativeReport(body),
        "keywords.campaign.negative.readReport" => ReadCampaignNegativeReport(body),
        "campaigns.update" => UpdateCampaigns(body),
        _ => Error(404, $"fake has no {method}", "entity_not_found"),
    };

    private static JsonObject Ok(JsonObject payload)
    {
        payload["status"] = 200;
        payload["statusMessage"] = "OK";
        payload["session"] = "fake-session";
        return payload;
    }

    private static JsonObject Error(int status, string message, string diagnosticId) => new()
    {
        ["status"] = status,
        ["statusMessage"] = message,
        ["session"] = "fake-session",
        ["diagnostics"] = new JsonArray(new JsonObject { ["id"] = diagnosticId, ["type"] = "error" }),
    };

    private static IReadOnlyList<(string Name, string MatchType)> Snapshot(Dictionary<long, List<Negative>> store, long id) =>
        store.TryGetValue(id, out var list) ? list.Select(n => (n.Name, n.MatchType)).ToList() : new List<(string, string)>();

    private static IEnumerable<long> Ids(JsonElement filter) =>
        filter.ValueKind == JsonValueKind.Object && filter.TryGetProperty("ids", out var ids)
            ? ids.EnumerateArray().Select(i => i.GetInt64()).ToList()
            : Enumerable.Empty<long>();

    private static IEnumerable<long> NestedIds(JsonElement filter, string parent) =>
        filter.TryGetProperty(parent, out var nested) ? Ids(nested) : Enumerable.Empty<long>();

    private static (int Offset, int Limit) Page(JsonElement options) => (
        options.TryGetProperty("offset", out var offset) ? offset.GetInt32() : 0,
        options.TryGetProperty("limit", out var limit) ? limit.GetInt32() : int.MaxValue);

    private static bool Same(Negative negative, string name, string matchType) =>
        string.Equals(negative.Name, name, StringComparison.OrdinalIgnoreCase) && negative.MatchType == matchType;

    private JsonObject ListAds(JsonElement body)
    {
        var rows = Ids(body[1]).Where(_ads.ContainsKey).Select(id => (JsonNode)new JsonObject
        {
            ["id"] = id, ["status"] = _ads[id].Status, ["adType"] = _ads[id].AdType, ["deleted"] = false,
        });
        return Ok(new JsonObject { ["ads"] = new JsonArray(rows.ToArray()) });
    }

    private JsonObject UpdateAds(JsonElement body)
    {
        foreach (var ad in body[1].EnumerateArray())
        {
            var id = ad.GetProperty("id").GetInt64();
            if (!_ads.TryGetValue(id, out var current))
            {
                return Error(404, "Not found", "entity_not_found");
            }

            var status = ad.TryGetProperty("status", out var value) ? value.GetString()! : current.Status;
            _ads[id] = (status, current.AdType);
        }

        return Ok(new JsonObject());
    }

    private static JsonObject ListExisting(JsonElement body, HashSet<long> existing, string property)
    {
        var rows = Ids(body[1]).Where(existing.Contains).Select(id => (JsonNode)new JsonObject { ["id"] = id, ["deleted"] = false });
        return Ok(new JsonObject { [property] = new JsonArray(rows.ToArray()) });
    }

    private JsonObject ListGroupNegatives(JsonElement body)
    {
        var (offset, limit) = Page(body[2]);
        var rows = NestedIds(body[1], "group")
            .SelectMany(groupId => _groupNegatives.TryGetValue(groupId, out var list)
                ? list.Select(n => (GroupId: groupId, Negative: n))
                : Enumerable.Empty<(long GroupId, Negative Negative)>())
            .Skip(offset).Take(limit)
            .Select(x => (JsonNode)new JsonObject
            {
                ["id"] = x.Negative.Id, ["name"] = x.Negative.Name, ["matchType"] = x.Negative.MatchType,
                ["group"] = new JsonObject { ["id"] = x.GroupId },
            });
        return Ok(new JsonObject { ["keywords"] = new JsonArray(rows.ToArray()) });
    }

    private JsonObject CreateGroupNegatives(JsonElement body)
    {
        foreach (var keyword in body[1].EnumerateArray())
        {
            var groupId = keyword.GetProperty("groupId").GetInt64();
            var name = keyword.GetProperty("name").GetString()!;
            var matchType = keyword.TryGetProperty("matchType", out var value) ? value.GetString()! : "negativeBroad";
            if (!_groupNegatives.TryGetValue(groupId, out var list))
            {
                return Error(404, "Not found", "entity_not_found");
            }

            if (list.Any(n => Same(n, name, matchType)))
            {
                return Error(406, "Bad values of attributes", "keyword_duplicate");
            }

            list.Add(new Negative(++_nextId, name, matchType));
        }

        return Ok(new JsonObject());
    }

    private JsonObject RemoveGroupNegatives(JsonElement body)
    {
        if (!_groupNegatives.TryGetValue(body[1].GetInt64(), out var list))
        {
            return Error(404, "Not found", "entity_not_found");
        }

        foreach (var keyword in body[2].EnumerateArray())
        {
            var removed = list.RemoveAll(n =>
                Same(n, keyword.GetProperty("name").GetString()!, keyword.GetProperty("matchType").GetString()!));
            if (removed == 0)
            {
                return Error(404, "Not found", "entity_not_found");
            }
        }

        return Ok(new JsonObject());
    }

    private JsonObject CreateCampaignNegativeReport(JsonElement body)
    {
        _reportCampaignId = NestedIds(body[1], "campaign").Single();
        var count = _campaignNegatives.TryGetValue(_reportCampaignId, out var list) ? list.Count : 0;
        return Ok(new JsonObject { ["reportId"] = "fake-campaign-negatives", ["totalCount"] = count });
    }

    private JsonObject ReadCampaignNegativeReport(JsonElement body)
    {
        var (offset, limit) = Page(body[2]);
        var list = _campaignNegatives.TryGetValue(_reportCampaignId, out var negatives) ? negatives : new List<Negative>();
        var rows = list.Skip(offset).Take(limit).Select(n => (JsonNode)new JsonObject
        {
            ["id"] = n.Id, ["name"] = n.Name, ["matchType"] = n.MatchType,
            ["campaign"] = new JsonObject { ["id"] = _reportCampaignId },
        });
        return Ok(new JsonObject { ["reportId"] = "fake-campaign-negatives", ["report"] = new JsonArray(rows.ToArray()) });
    }

    private JsonObject UpdateCampaigns(JsonElement body)
    {
        foreach (var campaign in body[1].EnumerateArray())
        {
            var id = campaign.GetProperty("id").GetInt64();
            if (!_campaigns.Contains(id))
            {
                return Error(404, "Not found", "entity_not_found");
            }

            if (!campaign.TryGetProperty("negativeKeywords", out var negatives))
            {
                continue;
            }

            var previous = _campaignNegatives[id];
            _campaignNegatives[id] = negatives.EnumerateArray().Select(n =>
            {
                var name = n.GetProperty("name").GetString()!;
                var matchType = n.GetProperty("matchType").GetString()!;
                return previous.FirstOrDefault(p => Same(p, name, matchType)) ?? new Negative(++_nextId, name, matchType);
            }).ToList();
        }

        return Ok(new JsonObject());
    }
}
```

`backend/test/Anela.Heblo.Adapters.Sklik.Tests/Fakes/SklikExecutorFixtures.cs` (Task 11 adds the executor helpers):

```csharp
using Anela.Heblo.Adapters.Sklik.Drak;

namespace Anela.Heblo.Adapters.Sklik.Tests.Fakes;

internal static partial class SklikExecutorFixtures
{
    public const string AccountId = "1234567";

    /// <summary>Ad 611 (active), ad group 211 with negative "zdarma"/phrase, campaign 111 with negative "návod"/broad.</summary>
    public static FakeSklikDrakServer StandardServer() => new FakeSklikDrakServer()
        .WithAd(611)
        .WithGroup(211, ("zdarma", "negativePhrase"))
        .WithCampaign(111, ("návod", "negativeBroad"));

    public static SklikDrakClient Client(FakeSklikDrakServer server) => SklikFixtures.Client(server);
}
```

- [ ] **Step 2: Write the failing operation tests**

`backend/test/Anela.Heblo.Adapters.Sklik.Tests/SklikPauseAdOperationTests.cs`:

```csharp
using Anela.Heblo.Adapters.Sklik.Drak;
using Anela.Heblo.Adapters.Sklik.Execution;
using Anela.Heblo.Adapters.Sklik.Tests.Fakes;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Adapters.Sklik.Tests;

public class SklikPauseAdOperationTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task reads_an_active_ad_as_enabled()
    {
        var operation = new SklikPauseAdOperation(SklikExecutorFixtures.Client(SklikExecutorFixtures.StandardServer()));

        var state = await operation.ReadAsync("611", Ct);

        state.Exists.Should().BeTrue();
        state.CurrentValue.Should().Be(AdActionValues.Enabled);
    }

    [Fact]
    public async Task reads_a_missing_ad_as_not_existing()
    {
        var operation = new SklikPauseAdOperation(SklikExecutorFixtures.Client(SklikExecutorFixtures.StandardServer()));

        var state = await operation.ReadAsync("699", Ct);

        state.Exists.Should().BeFalse();
        state.CurrentValue.Should().BeNull();
    }

    [Fact]
    public async Task pauses_with_suspend_status_keeping_the_ad_type_and_resumes_with_active()
    {
        var server = SklikExecutorFixtures.StandardServer();
        var operation = new SklikPauseAdOperation(SklikExecutorFixtures.Client(server));

        var paused = await operation.PauseAsync("611", Ct);

        paused.Should().BeEquivalentTo(new
        {
            Outcome = AdExecutionOutcome.Succeeded,
            BeforeValue = AdActionValues.Enabled,
            AfterValue = AdActionValues.Paused,
            PlatformResourceId = "611",
            Error = (string?)null,
        });
        server.AdStatus(611).Should().Be("suspend");
        (await operation.ReadAsync("611", Ct)).CurrentValue.Should().Be(AdActionValues.Paused);
        var update = server.CallsTo("ads.update").Single().Parameter(0)[0];
        update.GetProperty("id").GetInt64().Should().Be(611);
        update.GetProperty("status").GetString().Should().Be("suspend");
        update.GetProperty("adType").GetString().Should().Be("eta");

        var resumed = await operation.ResumeAsync("611", Ct);

        resumed.Outcome.Should().Be(AdExecutionOutcome.Succeeded);
        resumed.AfterValue.Should().Be(AdActionValues.Enabled);
        server.AdStatus(611).Should().Be("active");
    }

    [Fact]
    public async Task treats_a_non_sklik_id_as_a_missing_ad_without_calling_sklik()
    {
        var server = SklikExecutorFixtures.StandardServer();
        var operation = new SklikPauseAdOperation(SklikExecutorFixtures.Client(server));

        var state = await operation.ReadAsync("heblo-contract-missing-target", Ct);
        var result = await operation.PauseAsync("heblo-contract-missing-target", Ct);

        state.Exists.Should().BeFalse();
        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        result.Error.Should().NotBeNullOrWhiteSpace();
        server.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task fails_without_calling_update_when_the_ad_is_gone()
    {
        var server = SklikExecutorFixtures.StandardServer();
        var operation = new SklikPauseAdOperation(SklikExecutorFixtures.Client(server));

        var result = await operation.PauseAsync("699", Ct);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        result.Error.Should().Contain("699");
        server.CallsTo("ads.update").Should().BeEmpty();
    }

    [Fact]
    public async Task surfaces_a_platform_rejection_as_an_exception_for_the_executor_to_map()
    {
        var server = SklikExecutorFixtures.StandardServer().FailNext("ads.update", 406, "Bad values", "not_allowed_for_campaign_type");
        var operation = new SklikPauseAdOperation(SklikExecutorFixtures.Client(server));

        var act = () => operation.PauseAsync("611", Ct);

        (await act.Should().ThrowAsync<SklikApiException>()).Which.IsPlatformRejection.Should().BeTrue();
        server.AdStatus(611).Should().Be("active");
    }
}
```

- [ ] **Step 3: Run them to verify they fail**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj -p:UseSharedCompilation=false`
Expected: build FAILS with `CS0246: The type or namespace name 'SklikPauseAdOperation' could not be found`.

- [ ] **Step 4: Implement the operation**

`backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Execution/SklikPauseAdOperation.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Adapters.Sklik.Drak;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.Sklik.Execution;

/// <summary>
/// PauseAd via ads.update status suspend/active. The ad's adType is sent back unchanged because ads.update defaults
/// adType to "eta" (docs/integrations/sklik-api.md §11). Platform errors propagate as SklikApiException.
/// </summary>
internal sealed class SklikPauseAdOperation
{
    private static readonly string[] AdColumns = { "id", "adType", "status", "deleted" };

    private readonly ISklikDrakClient _client;

    public SklikPauseAdOperation(ISklikDrakClient client)
    {
        _client = client;
    }

    public async Task<AdTargetState> ReadAsync(string adId, CancellationToken ct)
    {
        var (ad, rawJson) = await FindAdAsync(adId, ct);
        if (ad is not { } row || SklikJson.Bool(row, "deleted"))
        {
            return new AdTargetState(false, null, rawJson);
        }

        return new AdTargetState(true, ToActionValue(SklikJson.String(row, "status")), rawJson);
    }

    public Task<AdExecutionResult> PauseAsync(string adId, CancellationToken ct) =>
        SetStatusAsync(adId, SklikMappings.StatusSuspended, AdActionValues.Enabled, AdActionValues.Paused, ct);

    public Task<AdExecutionResult> ResumeAsync(string adId, CancellationToken ct) =>
        SetStatusAsync(adId, SklikMappings.StatusActive, AdActionValues.Paused, AdActionValues.Enabled, ct);

    private async Task<AdExecutionResult> SetStatusAsync(
        string adId, string sklikStatus, string before, string after, CancellationToken ct)
    {
        var (ad, rawJson) = await FindAdAsync(adId, ct);
        if (ad is not { } row)
        {
            return new AdExecutionResult(AdExecutionOutcome.Failed, before, null, null, rawJson, $"Sklik ad {adId} not found.");
        }

        var update = new Dictionary<string, object> { ["id"] = SklikJson.ParseId(adId), ["status"] = sklikStatus };
        if (SklikJson.String(row, "adType") is { } adType)
        {
            update["adType"] = adType;
        }

        var response = await _client.CallAsync("ads.update", new object?[] { new[] { update } }, ct);
        var resourceId = SklikJson.Array(response.Body, "newAdIds").Select(SklikJson.Scalar).FirstOrDefault(id => id is not null)
            ?? adId;
        return new AdExecutionResult(AdExecutionOutcome.Succeeded, before, after, resourceId, response.RawJson, null);
    }

    /// <summary>
    /// A target id that is not a Sklik id (e.g. the contract suite's MissingTargetExternalId) cannot exist in Sklik:
    /// it reads as "not found" without calling the API, so ReadCurrent reports Exists=false and Execute returns Failed.
    /// </summary>
    private async Task<(JsonElement? Ad, string? RawJson)> FindAdAsync(string adId, CancellationToken ct)
    {
        if (!SklikJson.TryParseId(adId, out var numericId))
        {
            return (null, null);
        }

        var restriction = new { ids = new[] { numericId } };
        var display = new { offset = 0, limit = 1, displayColumns = AdColumns };
        var response = await _client.CallAsync("ads.list", new object?[] { restriction, display }, ct);
        var ads = SklikJson.Array(response.Body, "ads");
        return (ads.Count == 0 ? null : ads[0], response.RawJson);
    }

    private static string? ToActionValue(string? sklikStatus) => sklikStatus switch
    {
        SklikMappings.StatusActive => AdActionValues.Enabled,
        SklikMappings.StatusSuspended => AdActionValues.Paused,
        _ => sklikStatus,
    };
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~SklikPauseAdOperationTests"`
Expected: `Passed!` — 6 tests.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Execution backend/test/Anela.Heblo.Adapters.Sklik.Tests
git commit -m "feat: sklik pause-ad operation with stateful drak fake

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 10: Negative keyword operation (ad group + campaign level)

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Execution/SklikNegativeKeyword.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Execution/SklikNegativeKeywordOperation.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Sklik.Tests/SklikNegativeKeywordOperationTests.cs`

**Interfaces:**
- Consumes: `ISklikDrakClient`, `SklikPaging`, `SklikJson`, `SklikMappings.ToNegativeMatchType` (Part B); C1 `AdAction`, `AdActionPayloadKeys`, `KeywordMatchType`, `AdEntityLevel`.
- Produces:
  - `internal sealed record SklikNegativeRow(string? Id, string Name, string MatchType)`
  - `internal sealed record SklikNegativeKeyword(AdEntityLevel Scope, long TargetId, string Text, KeywordMatchType MatchType) { string SklikMatchType; bool Matches(SklikNegativeRow); static SklikNegativeKeyword From(AdAction) }` — `From` throws `ArgumentException` on a bad target level, missing text or invalid matchType.
  - `internal sealed class SklikNegativeKeywordOperation(ISklikDrakClient client, int listPageSize, int reportPageSize) { Task<AdTargetState> ReadAsync(SklikNegativeKeyword, CancellationToken); Task<AdExecutionResult> AddAsync(SklikNegativeKeyword, CancellationToken); Task<AdExecutionResult> RemoveAsync(SklikNegativeKeyword, CancellationToken) }`

**Match-type mapping (documented in sklik-api.md §11):** `Exact` → `negativeExact` (query must equal the keyword), `Phrase` → `negativePhrase` (words in this order), `Broad` → `negativeBroad` (all words in any order). Sklik matching is case-insensitive for presence checks; Heblo compares trimmed text with `OrdinalIgnoreCase`.

- [ ] **Step 1: Write the failing tests**

`backend/test/Anela.Heblo.Adapters.Sklik.Tests/SklikNegativeKeywordOperationTests.cs`:

```csharp
using Anela.Heblo.Adapters.Sklik.Execution;
using Anela.Heblo.Adapters.Sklik.Tests.Fakes;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Adapters.Sklik.Tests;

public class SklikNegativeKeywordOperationTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static SklikNegativeKeywordOperation Operation(FakeSklikDrakServer server) =>
        new(SklikExecutorFixtures.Client(server), listPageSize: 100, reportPageSize: 100);

    private static AdAction Action(AdEntityLevel level, string targetId, string? text, string? matchType)
    {
        var payload = new Dictionary<string, string>();
        if (text is not null) payload[AdActionPayloadKeys.Text] = text;
        if (matchType is not null) payload[AdActionPayloadKeys.MatchType] = matchType;
        return new AdAction(AdActionType.AddNegativeKeyword, AdPlatform.Sklik, SklikExecutorFixtures.AccountId,
            level, targetId, AdActionValues.Absent, AdActionValues.Present, payload);
    }

    private static SklikNegativeKeyword Keyword(AdEntityLevel level, string targetId, string text, KeywordMatchType matchType) =>
        SklikNegativeKeyword.From(Action(level, targetId, text, matchType.ToString()));

    [Fact]
    public async Task adds_an_ad_group_negative_with_the_sklik_match_type_and_removes_it_on_revert()
    {
        var server = SklikExecutorFixtures.StandardServer();
        var operation = Operation(server);
        var keyword = Keyword(AdEntityLevel.AdGroup, "211", "levně", KeywordMatchType.Phrase);

        (await operation.ReadAsync(keyword, Ct)).CurrentValue.Should().Be(AdActionValues.Absent);
        var added = await operation.AddAsync(keyword, Ct);

        added.Outcome.Should().Be(AdExecutionOutcome.Succeeded);
        added.BeforeValue.Should().Be(AdActionValues.Absent);
        added.AfterValue.Should().Be(AdActionValues.Present);
        added.PlatformResourceId.Should().NotBeNullOrEmpty();
        var created = server.CallsTo("keywords.negative.create").Single().Parameter(0)[0];
        created.GetProperty("name").GetString().Should().Be("levně");
        created.GetProperty("groupId").GetInt64().Should().Be(211);
        created.GetProperty("matchType").GetString().Should().Be("negativePhrase");
        server.GroupNegatives(211).Should().BeEquivalentTo(new[] { ("zdarma", "negativePhrase"), ("levně", "negativePhrase") });
        (await operation.ReadAsync(keyword, Ct)).CurrentValue.Should().Be(AdActionValues.Present);

        var removed = await operation.RemoveAsync(keyword, Ct);

        removed.Outcome.Should().Be(AdExecutionOutcome.Succeeded);
        removed.AfterValue.Should().Be(AdActionValues.Absent);
        var remove = server.CallsTo("keywords.negative.remove").Single();
        remove.Parameter(0).GetInt64().Should().Be(211);
        remove.Parameter(1)[0].GetProperty("name").GetString().Should().Be("levně");
        remove.Parameter(1)[0].GetProperty("matchType").GetString().Should().Be("negativePhrase");
        server.GroupNegatives(211).Should().BeEquivalentTo(new[] { ("zdarma", "negativePhrase") });
    }

    [Fact]
    public async Task adds_a_campaign_negative_without_dropping_existing_ones_and_reverts_only_its_own()
    {
        var server = SklikExecutorFixtures.StandardServer();
        var operation = Operation(server);
        var keyword = Keyword(AdEntityLevel.Campaign, "111", "recept", KeywordMatchType.Exact);

        var added = await operation.AddAsync(keyword, Ct);

        added.Outcome.Should().Be(AdExecutionOutcome.Succeeded);
        added.PlatformResourceId.Should().NotBeNullOrEmpty();
        server.CampaignNegatives(111).Should().Equal(("návod", "negativeBroad"), ("recept", "negativeExact"));
        var update = server.CallsTo("campaigns.update").Single().Parameter(0)[0];
        update.GetProperty("id").GetInt64().Should().Be(111);
        update.GetProperty("negativeKeywords").GetArrayLength().Should().Be(2);

        await operation.RemoveAsync(keyword, Ct);

        server.CampaignNegatives(111).Should().Equal(("návod", "negativeBroad"));
    }

    [Fact]
    public async Task sees_an_existing_negative_regardless_of_letter_case_and_surrounding_spaces()
    {
        var operation = Operation(SklikExecutorFixtures.StandardServer());

        var state = await operation.ReadAsync(Keyword(AdEntityLevel.AdGroup, "211", "  Zdarma ", KeywordMatchType.Phrase), Ct);

        state.Exists.Should().BeTrue();
        state.CurrentValue.Should().Be(AdActionValues.Present);
    }

    [Fact]
    public async Task treats_a_different_match_type_as_absent()
    {
        var operation = Operation(SklikExecutorFixtures.StandardServer());

        var state = await operation.ReadAsync(Keyword(AdEntityLevel.AdGroup, "211", "zdarma", KeywordMatchType.Exact), Ct);

        state.CurrentValue.Should().Be(AdActionValues.Absent);
    }

    [Theory]
    [InlineData(AdEntityLevel.AdGroup, "299")]
    [InlineData(AdEntityLevel.Campaign, "199")]
    public async Task reports_a_missing_target_as_not_existing(AdEntityLevel level, string targetId)
    {
        var operation = Operation(SklikExecutorFixtures.StandardServer());

        var state = await operation.ReadAsync(Keyword(level, targetId, "levně", KeywordMatchType.Phrase), Ct);

        state.Exists.Should().BeFalse();
        state.CurrentValue.Should().BeNull();
    }

    [Theory]
    [InlineData(KeywordMatchType.Exact, "negativeExact")]
    [InlineData(KeywordMatchType.Phrase, "negativePhrase")]
    [InlineData(KeywordMatchType.Broad, "negativeBroad")]
    public void maps_heblo_match_types_to_sklik_negative_match_types(KeywordMatchType heblo, string sklik)
    {
        Keyword(AdEntityLevel.AdGroup, "211", "x", heblo).SklikMatchType.Should().Be(sklik);
    }

    [Theory]
    [InlineData(AdEntityLevel.Ad, "levně", "Phrase")]
    [InlineData(AdEntityLevel.AdGroup, null, "Phrase")]
    [InlineData(AdEntityLevel.AdGroup, "   ", "Phrase")]
    [InlineData(AdEntityLevel.AdGroup, "levně", null)]
    [InlineData(AdEntityLevel.AdGroup, "levně", "Fuzzy")]
    [InlineData(AdEntityLevel.AdGroup, "levně", "99")]
    public void rejects_malformed_actions(AdEntityLevel level, string? text, string? matchType)
    {
        var act = () => SklikNegativeKeyword.From(Action(level, "211", text, matchType));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void rejects_a_non_numeric_target_id()
    {
        var act = () => SklikNegativeKeyword.From(Action(AdEntityLevel.AdGroup, "g:411", "levně", "Phrase"));

        act.Should().Throw<ArgumentException>();
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj -p:UseSharedCompilation=false`
Expected: build FAILS with `CS0246` for `SklikNegativeKeyword` / `SklikNegativeKeywordOperation`.

- [ ] **Step 3: Implement the value types**

`backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Execution/SklikNegativeKeyword.cs`:

```csharp
using Anela.Heblo.Adapters.Sklik.Drak;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.Sklik.Execution;

/// <summary>A negative keyword as Sklik lists it.</summary>
internal sealed record SklikNegativeRow(string? Id, string Name, string MatchType);

/// <summary>The AddNegativeKeyword action, validated and translated to Sklik terms.</summary>
internal sealed record SklikNegativeKeyword(AdEntityLevel Scope, long TargetId, string Text, KeywordMatchType MatchType)
{
    public string SklikMatchType => SklikMappings.ToNegativeMatchType(MatchType);

    public bool Matches(SklikNegativeRow row) =>
        string.Equals(row.Name.Trim(), Text, StringComparison.OrdinalIgnoreCase) && row.MatchType == SklikMatchType;

    public SklikNegativeRow ToRow() => new(null, Text, SklikMatchType);

    public static SklikNegativeKeyword From(AdAction action)
    {
        if (action.TargetLevel is not (AdEntityLevel.Campaign or AdEntityLevel.AdGroup))
        {
            throw new ArgumentException(
                $"AddNegativeKeyword targets a campaign or an ad group, not {action.TargetLevel}.", nameof(action));
        }

        if (!action.Payload.TryGetValue(AdActionPayloadKeys.Text, out var text) || string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("AddNegativeKeyword payload has no text.", nameof(action));
        }

        if (!action.Payload.TryGetValue(AdActionPayloadKeys.MatchType, out var matchTypeName)
            || !Enum.TryParse<KeywordMatchType>(matchTypeName, ignoreCase: false, out var matchType)
            || !Enum.IsDefined(matchType))
        {
            throw new ArgumentException($"AddNegativeKeyword payload has an invalid matchType '{matchTypeName}'.", nameof(action));
        }

        return new SklikNegativeKeyword(action.TargetLevel, SklikJson.ParseId(action.TargetExternalId), text.Trim(), matchType);
    }
}
```

- [ ] **Step 4: Implement the operation**

`backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Execution/SklikNegativeKeywordOperation.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Adapters.Sklik.Drak;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.Sklik.Execution;

/// <summary>
/// Ad-group negatives: keywords.negative.create / .remove (remove matches by name + match type).
/// Campaign negatives: Drak has no add/remove, so the campaign's whole list is read and written back via
/// campaigns.update negativeKeywords (replace semantics — see sklik-api.md §11; not atomic against concurrent UI edits).
/// Platform errors propagate as SklikApiException.
/// </summary>
internal sealed class SklikNegativeKeywordOperation
{
    private static readonly string[] ExistenceColumns = { "id", "deleted" };
    private static readonly string[] GroupNegativeColumns = { "id", "name", "matchType", "group.id" };
    private static readonly string[] CampaignNegativeColumns = { "id", "name", "matchType", "campaign.id" };

    private readonly ISklikDrakClient _client;
    private readonly int _listPageSize;
    private readonly int _reportPageSize;

    public SklikNegativeKeywordOperation(ISklikDrakClient client, int listPageSize, int reportPageSize)
    {
        _client = client;
        _listPageSize = listPageSize;
        _reportPageSize = reportPageSize;
    }

    public async Task<AdTargetState> ReadAsync(SklikNegativeKeyword keyword, CancellationToken ct)
    {
        var (exists, rawJson) = await TargetExistsAsync(keyword, ct);
        if (!exists)
        {
            return new AdTargetState(false, null, rawJson);
        }

        var isPresent = (await ListAsync(keyword, ct)).Any(keyword.Matches);
        return new AdTargetState(true, isPresent ? AdActionValues.Present : AdActionValues.Absent, rawJson);
    }

    public async Task<AdExecutionResult> AddAsync(SklikNegativeKeyword keyword, CancellationToken ct)
    {
        var response = keyword.Scope == AdEntityLevel.AdGroup
            ? await _client.CallAsync("keywords.negative.create", new object?[]
            {
                new[] { new { name = keyword.Text, groupId = keyword.TargetId, matchType = keyword.SklikMatchType } },
            }, ct)
            : await ReplaceCampaignNegativesAsync(keyword, current => current.Append(keyword.ToRow()), ct);

        var created = (await ListAsync(keyword, ct)).FirstOrDefault(keyword.Matches);
        return new AdExecutionResult(AdExecutionOutcome.Succeeded, AdActionValues.Absent, AdActionValues.Present,
            created?.Id, response.RawJson, null);
    }

    public async Task<AdExecutionResult> RemoveAsync(SklikNegativeKeyword keyword, CancellationToken ct)
    {
        var response = keyword.Scope == AdEntityLevel.AdGroup
            ? await _client.CallAsync("keywords.negative.remove", new object?[]
            {
                keyword.TargetId,
                new[] { new { name = keyword.Text, matchType = keyword.SklikMatchType } },
            }, ct)
            : await ReplaceCampaignNegativesAsync(keyword, current => current.Where(row => !keyword.Matches(row)), ct);

        return new AdExecutionResult(AdExecutionOutcome.Succeeded, AdActionValues.Present, AdActionValues.Absent,
            null, response.RawJson, null);
    }

    private async Task<SklikResponse> ReplaceCampaignNegativesAsync(
        SklikNegativeKeyword keyword, Func<IEnumerable<SklikNegativeRow>, IEnumerable<SklikNegativeRow>> change,
        CancellationToken ct)
    {
        var next = change(await ListAsync(keyword, ct))
            .Select(row => new { name = row.Name, matchType = row.MatchType })
            .ToArray();
        return await _client.CallAsync("campaigns.update", new object?[]
        {
            new[] { new { id = keyword.TargetId, negativeKeywords = next } },
        }, ct);
    }

    private async Task<(bool Exists, string RawJson)> TargetExistsAsync(SklikNegativeKeyword keyword, CancellationToken ct)
    {
        var (method, property) = keyword.Scope == AdEntityLevel.AdGroup ? ("groups.list", "groups") : ("campaigns.list", "campaigns");
        var restriction = new { ids = new[] { keyword.TargetId } };
        var display = new { offset = 0, limit = 1, displayColumns = ExistenceColumns };
        var response = await _client.CallAsync(method, new object?[] { restriction, display }, ct);
        var rows = SklikJson.Array(response.Body, property);
        return (rows.Count > 0 && !SklikJson.Bool(rows[0], "deleted"), response.RawJson);
    }

    private async Task<IReadOnlyList<SklikNegativeRow>> ListAsync(SklikNegativeKeyword keyword, CancellationToken ct)
    {
        var ids = new[] { keyword.TargetId };
        IReadOnlyList<JsonElement> rows = keyword.Scope == AdEntityLevel.AdGroup
            ? await SklikPaging.ListAllAsync(_client, "keywords.negative.list", "keywords",
                new { group = new { ids }, isDeleted = false }, GroupNegativeColumns, _listPageSize, ct)
            : await SklikPaging.ReadReportAsync(_client, "keywords.campaign.negative",
                new { campaign = new { ids }, isDeleted = false }, createDisplayOptions: null,
                CampaignNegativeColumns, _reportPageSize, allowEmptyStatistics: true, ct);

        return rows
            .Select(row => new SklikNegativeRow(SklikJson.Id(row), SklikJson.String(row, "name") ?? string.Empty,
                SklikJson.String(row, "matchType") ?? string.Empty))
            .ToList();
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~SklikNegativeKeywordOperationTests"`
Expected: `Passed!` — 16 tests.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Execution backend/test/Anela.Heblo.Adapters.Sklik.Tests
git commit -m "feat: sklik negative keyword operation for groups and campaigns

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 11: `SklikActionExecutor`, contract tests, registration

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Execution/SklikActionExecutor.cs`
- Create (test support): `backend/test/Anela.Heblo.Adapters.Sklik.Tests/Fakes/SklikExecutorFixtures.Actions.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Sklik/SklikAdapterServiceCollectionExtensions.cs` (one line after the read-source registration)
- Modify: `backend/test/Anela.Heblo.Adapters.Sklik.Tests/SklikAdapterRegistrationTests.cs` (one new test)
- Test: `backend/test/Anela.Heblo.Adapters.Sklik.Tests/SklikActionExecutorTests.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Sklik.Tests/SklikActionExecutorContractTests.cs`

**Interfaces:**
- Consumes: Tasks 9–10; `SklikAccountResolver`, `ISklikDrakClientFactory.CreateWriteClient()` (Part B); C1 `IAdActionExecutor`; TestKit `AdActionExecutorContractTests` (abstract `CreateExecutor()`, `SamplePauseAd()`, `SampleAddNegativeKeyword()`).
- Produces: `internal sealed class SklikActionExecutor : IAdActionExecutor` — public ctor `(ISklikDrakClientFactory, IOptions<SklikSettings>, ILogger<SklikActionExecutor>)` using the **write** client; internal ctor `(ISklikDrakClient, SklikSettings, ILogger<SklikActionExecutor>)`. `SupportedActions = { AddNegativeKeyword, PauseAd }`. Malformed actions (wrong platform, wrong level, bad payload, foreign account) throw `ArgumentException`; unsupported types throw `NotSupportedException`; platform rejections → `Failed`; auth/transport → thrown.

- [ ] **Step 1: Write the fixtures and failing tests**

`backend/test/Anela.Heblo.Adapters.Sklik.Tests/Fakes/SklikExecutorFixtures.Actions.cs`:

```csharp
using Anela.Heblo.Adapters.Sklik.Execution;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Adapters.Sklik.Tests.Fakes;

internal static partial class SklikExecutorFixtures
{
    public static SklikActionExecutor Executor(FakeSklikDrakServer server, ILogger<SklikActionExecutor>? logger = null) =>
        new(Client(server), SklikFixtures.Settings(), logger ?? NullLogger<SklikActionExecutor>.Instance);

    public static AdAction PauseAd(string adId, string accountId = AccountId) =>
        new(AdActionType.PauseAd, AdPlatform.Sklik, accountId, AdEntityLevel.Ad, adId,
            AdActionValues.Enabled, AdActionValues.Paused, new Dictionary<string, string>());

    public static AdAction AddNegative(AdEntityLevel level, string targetId, string text, KeywordMatchType matchType) =>
        new(AdActionType.AddNegativeKeyword, AdPlatform.Sklik, AccountId, level, targetId,
            AdActionValues.Absent, AdActionValues.Present,
            new Dictionary<string, string>
            {
                [AdActionPayloadKeys.Text] = text,
                [AdActionPayloadKeys.MatchType] = matchType.ToString(),
            });
}
```

`backend/test/Anela.Heblo.Adapters.Sklik.Tests/SklikActionExecutorContractTests.cs`:

```csharp
using Anela.Heblo.Adapters.Sklik.Tests.Fakes;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.MarketingAds.TestKit;

namespace Anela.Heblo.Adapters.Sklik.Tests;

public sealed class SklikActionExecutorContractTests : AdActionExecutorContractTests
{
    protected override IAdActionExecutor CreateExecutor() =>
        SklikExecutorFixtures.Executor(SklikExecutorFixtures.StandardServer());

    protected override AdAction SamplePauseAd() => SklikExecutorFixtures.PauseAd("611");

    protected override AdAction? SampleAddNegativeKeyword() =>
        SklikExecutorFixtures.AddNegative(AdEntityLevel.AdGroup, "211", "levně", KeywordMatchType.Phrase);
}
```

`backend/test/Anela.Heblo.Adapters.Sklik.Tests/SklikActionExecutorTests.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Adapters.Sklik.Drak;
using Anela.Heblo.Adapters.Sklik.Execution;
using Anela.Heblo.Adapters.Sklik.Tests.Fakes;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Adapters.Sklik.Tests;

public class SklikActionExecutorTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public void declares_sklik_and_both_v1_actions()
    {
        var executor = SklikExecutorFixtures.Executor(SklikExecutorFixtures.StandardServer());

        executor.Platform.Should().Be(AdPlatform.Sklik);
        executor.SupportedActions.Should().BeEquivalentTo(new[] { AdActionType.AddNegativeKeyword, AdActionType.PauseAd });
    }

    [Fact]
    public async Task pauses_and_reverts_an_ad_end_to_end()
    {
        var server = SklikExecutorFixtures.StandardServer();
        var executor = SklikExecutorFixtures.Executor(server);
        var action = SklikExecutorFixtures.PauseAd("611");

        (await executor.ReadCurrentAsync(action, Ct)).CurrentValue.Should().Be(AdActionValues.Enabled);
        var result = await executor.ExecuteAsync(action, Ct);
        (await executor.ReadCurrentAsync(action, Ct)).CurrentValue.Should().Be(AdActionValues.Paused);
        var reverted = await executor.RevertAsync(action, result, Ct);

        result.Outcome.Should().Be(AdExecutionOutcome.Succeeded);
        reverted.Outcome.Should().Be(AdExecutionOutcome.Succeeded);
        (await executor.ReadCurrentAsync(action, Ct)).CurrentValue.Should().Be(AdActionValues.Enabled);
        server.AdStatus(611).Should().Be("active");
    }

    [Fact]
    public async Task adds_and_reverts_a_negative_keyword_end_to_end()
    {
        var server = SklikExecutorFixtures.StandardServer();
        var executor = SklikExecutorFixtures.Executor(server);
        var action = SklikExecutorFixtures.AddNegative(AdEntityLevel.Campaign, "111", "recept", KeywordMatchType.Exact);

        var result = await executor.ExecuteAsync(action, Ct);
        (await executor.ReadCurrentAsync(action, Ct)).CurrentValue.Should().Be(AdActionValues.Present);
        await executor.RevertAsync(action, result, Ct);

        (await executor.ReadCurrentAsync(action, Ct)).CurrentValue.Should().Be(AdActionValues.Absent);
        server.CampaignNegatives(111).Should().Equal(("návod", "negativeBroad"));
    }

    [Fact]
    public async Task reports_a_platform_rejection_as_failed_without_throwing()
    {
        var server = SklikExecutorFixtures.StandardServer()
            .FailNext("ads.update", 406, "Bad values of attributes", "not_allowed_for_campaign_type");
        var executor = SklikExecutorFixtures.Executor(server);

        var result = await executor.ExecuteAsync(SklikExecutorFixtures.PauseAd("611"), Ct);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        result.BeforeValue.Should().Be(AdActionValues.Enabled);
        result.AfterValue.Should().BeNull();
        result.Error.Should().Contain("not_allowed_for_campaign_type");
        JsonDocument.Parse(result.PlatformResponseJson!).RootElement.TryGetProperty("session", out _).Should().BeFalse();
        server.AdStatus(611).Should().Be("active");
    }

    [Fact]
    public async Task reports_a_duplicate_negative_as_failed()
    {
        var server = SklikExecutorFixtures.StandardServer();
        var executor = SklikExecutorFixtures.Executor(server);

        var result = await executor.ExecuteAsync(
            SklikExecutorFixtures.AddNegative(AdEntityLevel.AdGroup, "211", "zdarma", KeywordMatchType.Phrase), Ct);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        result.Error.Should().Contain("keyword_duplicate");
    }

    [Fact]
    public async Task reports_a_failed_revert_with_the_reverted_before_value()
    {
        var server = SklikExecutorFixtures.StandardServer();
        var executor = SklikExecutorFixtures.Executor(server);
        var action = SklikExecutorFixtures.PauseAd("611");
        var result = await executor.ExecuteAsync(action, Ct);
        server.FailNext("ads.update", 406, "Bad values of attributes", "ad_not_editable");

        var reverted = await executor.RevertAsync(action, result, Ct);

        reverted.Outcome.Should().Be(AdExecutionOutcome.Failed);
        reverted.BeforeValue.Should().Be(AdActionValues.Paused);
        server.AdStatus(611).Should().Be("suspend");
    }

    [Theory]
    [InlineData(403, "Access Denied", "user_access_denied")]
    [InlineData(500, "Server Error", "internal_error")]
    public async Task throws_on_auth_and_transient_failures(int status, string message, string diagnosticId)
    {
        var server = SklikExecutorFixtures.StandardServer().FailNext("ads.update", status, message, diagnosticId);
        var executor = SklikExecutorFixtures.Executor(server);

        var act = () => executor.ExecuteAsync(SklikExecutorFixtures.PauseAd("611"), Ct);

        (await act.Should().ThrowAsync<SklikApiException>()).Which.IsPlatformRejection.Should().BeFalse();
    }

    [Fact]
    public async Task rejects_actions_it_cannot_run()
    {
        var executor = SklikExecutorFixtures.Executor(SklikExecutorFixtures.StandardServer());
        var pause = SklikExecutorFixtures.PauseAd("611");

        await FluentActions.Awaiting(() => executor.ExecuteAsync(pause with { Platform = AdPlatform.GoogleAds }, Ct))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => executor.ExecuteAsync(pause with { TargetLevel = AdEntityLevel.AdGroup }, Ct))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => executor.ReadCurrentAsync(SklikExecutorFixtures.PauseAd("611", accountId: "999"), Ct))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => executor.ExecuteAsync(pause with { Type = (AdActionType)99 }, Ct))
            .Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task logs_rejections_without_the_session()
    {
        var logger = new CapturingLogger<SklikActionExecutor>();
        var server = SklikExecutorFixtures.StandardServer().FailNext("ads.update", 406, "Bad values", "not_allowed_for_campaign_type");

        await SklikExecutorFixtures.Executor(server, logger).ExecuteAsync(SklikExecutorFixtures.PauseAd("611"), Ct);

        logger.Messages.Should().ContainSingle().Which.Should().Contain("not_allowed_for_campaign_type")
            .And.NotContain("fake-session").And.NotContain(SklikFixtures.Token);
    }
}
```

Add to `SklikAdapterRegistrationTests` (inside the class):

```csharp
    [Fact]
    public void resolves_the_executor_from_a_scope()
    {
        var services = Register(Configured());
        services.AddLogging();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var executors = scope.ServiceProvider.GetServices<IAdActionExecutor>().ToList();

        executors.Should().ContainSingle().Which.Platform.Should().Be(AdPlatform.Sklik);
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj -p:UseSharedCompilation=false`
Expected: build FAILS with `CS0246: The type or namespace name 'SklikActionExecutor' could not be found`.

- [ ] **Step 3: Implement the executor**

`backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Execution/SklikActionExecutor.cs`:

```csharp
using Anela.Heblo.Adapters.Sklik.Drak;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Sklik.Execution;

/// <summary>
/// Executes approved Sklik actions (spec 4.3). Does not compare old values — the core does (spec 6.2).
/// Platform-side rejections become <see cref="AdExecutionOutcome.Failed"/>; auth and transport failures are thrown.
/// Uses the never-retrying write HTTP client.
/// </summary>
internal sealed class SklikActionExecutor : IAdActionExecutor
{
    private static readonly IReadOnlySet<AdActionType> Supported =
        new HashSet<AdActionType> { AdActionType.AddNegativeKeyword, AdActionType.PauseAd };

    private readonly SklikAccountResolver _accounts;
    private readonly SklikPauseAdOperation _pauseAd;
    private readonly SklikNegativeKeywordOperation _negativeKeywords;
    private readonly ILogger<SklikActionExecutor> _logger;

    public SklikActionExecutor(
        ISklikDrakClientFactory clientFactory, IOptions<SklikSettings> options, ILogger<SklikActionExecutor> logger)
        : this(clientFactory.CreateWriteClient(), options.Value, logger)
    {
    }

    internal SklikActionExecutor(ISklikDrakClient client, SklikSettings settings, ILogger<SklikActionExecutor> logger)
    {
        _accounts = new SklikAccountResolver(client, settings);
        _pauseAd = new SklikPauseAdOperation(client);
        _negativeKeywords = new SklikNegativeKeywordOperation(client, settings.ListPageSize, settings.ReportPageSize);
        _logger = logger;
    }

    public AdPlatform Platform => AdPlatform.Sklik;

    public IReadOnlySet<AdActionType> SupportedActions => Supported;

    public async Task<AdTargetState> ReadCurrentAsync(AdAction action, CancellationToken ct)
    {
        await ValidateAsync(action, ct);
        return action.Type == AdActionType.PauseAd
            ? await _pauseAd.ReadAsync(action.TargetExternalId, ct)
            : await _negativeKeywords.ReadAsync(SklikNegativeKeyword.From(action), ct);
    }

    public async Task<AdExecutionResult> ExecuteAsync(AdAction action, CancellationToken ct)
    {
        await ValidateAsync(action, ct);
        return await RunAsync(action, action.OldValue, () => action.Type == AdActionType.PauseAd
            ? _pauseAd.PauseAsync(action.TargetExternalId, ct)
            : _negativeKeywords.AddAsync(SklikNegativeKeyword.From(action), ct));
    }

    public async Task<AdExecutionResult> RevertAsync(AdAction action, AdExecutionResult original, CancellationToken ct)
    {
        await ValidateAsync(action, ct);
        return await RunAsync(action, action.NewValue, () => action.Type == AdActionType.PauseAd
            ? _pauseAd.ResumeAsync(original.PlatformResourceId ?? action.TargetExternalId, ct)
            : _negativeKeywords.RemoveAsync(SklikNegativeKeyword.From(action), ct));
    }

    private async Task<AdExecutionResult> RunAsync(AdAction action, string beforeValue, Func<Task<AdExecutionResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (SklikApiException ex) when (ex.IsPlatformRejection)
        {
            _logger.LogWarning("Sklik rejected {ActionType} on {TargetLevel} {TargetId}: {Error}",
                action.Type, action.TargetLevel, action.TargetExternalId, ex.Message);
            return new AdExecutionResult(AdExecutionOutcome.Failed, beforeValue, null, null, ex.RawJson, ex.Message);
        }
    }

    private async Task ValidateAsync(AdAction action, CancellationToken ct)
    {
        if (action.Platform != AdPlatform.Sklik)
        {
            throw new ArgumentException($"Action for {action.Platform} sent to the Sklik executor.", nameof(action));
        }

        if (!Supported.Contains(action.Type))
        {
            throw new NotSupportedException($"The Sklik executor does not support {action.Type}.");
        }

        if (action.Type == AdActionType.PauseAd && action.TargetLevel != AdEntityLevel.Ad)
        {
            throw new ArgumentException($"PauseAd targets an ad, not {action.TargetLevel}.", nameof(action));
        }

        await _accounts.EnsureAsync(action.AccountExternalId, ct);
    }
}
```

- [ ] **Step 4: Register the executor**

In `SklikAdapterServiceCollectionExtensions.AddSklikAdapter`, add `using Anela.Heblo.Adapters.Sklik.Execution;` and, directly after `services.AddScoped<IAdPlatformReadSource, SklikReadSource>();`:

```csharp
        // Safe to register: nothing calls it until core C3+C4 are live and an admin enables execution (spec 6.6).
        services.AddScoped<IAdActionExecutor, SklikActionExecutor>();
```

- [ ] **Step 5: Run the whole Sklik test project**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj --no-build -p:UseSharedCompilation=false`
Expected: `Passed!` — all Part B tests, Tasks 9–10 tests, 10 executor tests, 1 new registration test and every test inherited from `AdActionExecutorContractTests`. If an inherited contract test fails, fix the executor or fake (fake changes must stay faithful to the Drak docs); never edit the TestKit here.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Sklik backend/test/Anela.Heblo.Adapters.Sklik.Tests
git commit -m "feat: sklik action executor for negative keywords and ad pause

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 12: Write-side docs, manual smoke procedure, validation, PR

**Files:**
- Modify: `docs/integrations/sklik-api.md` (append §11 and §12)
- Modify: `docs/processes/sync-ads-sklik.md` (summary, Purpose, Code entry points, `verified_at`)
- Modify (generated): `docs/processes/INDEX.md`

**Interfaces:**
- Consumes: Tasks 9–11.
- Produces: PR `feat/marketing-sklik-executor` → `main`; a smoke procedure Ondrej runs himself.

- [ ] **Step 1: Append the write-side sections to `docs/integrations/sklik-api.md`**

````markdown
## 11. Write methods Heblo uses (`SklikActionExecutor`)

All writes go through the `Sklik.Write` HTTP client, which **never retries**. Drak batch writes are all-or-nothing.

| Action | Level | Read current state | Execute | Revert |
|---|---|---|---|---|
| `PauseAd` | Ad | `ads.list` `{ids:[ad]}` → `status` (`active` → `Enabled`, `suspend` → `Paused`; `deleted` or missing → not existing) | `ads.update [[{id, status:"suspend", adType}]]` | `ads.update [[{id, status:"active", adType}]]` |
| `AddNegativeKeyword` | AdGroup | `groups.list {ids:[group]}` (exists) + `keywords.negative.list {group:{ids}, isDeleted:false}` | `keywords.negative.create [[{name, groupId, matchType}]]`, then re-list for the id | `keywords.negative.remove [groupId, [{name, matchType}]]` |
| `AddNegativeKeyword` | Campaign | `campaigns.list {ids:[campaign]}` + `keywords.campaign.negative.createReport/readReport` | read the campaign's negatives, append, `campaigns.update [[{id, negativeKeywords:[…all…]}]]` | read, drop the matching one, `campaigns.update` with the rest |

Match types: `Exact` → `negativeExact` (query must equal the keyword), `Phrase` → `negativePhrase` (keyword words in
this order inside the query), `Broad` → `negativeBroad` (all keyword words, any order). Presence is checked on trimmed
text, case-insensitively, with the same match type.

Assumptions **to be confirmed by the manual smoke (§12) before execution is enabled for Sklik**:
1. `campaigns.update` `negativeKeywords` **replaces** the campaign's whole negative list (Heblo always sends the full list).
2. `ads.update` with only `status` (+ unchanged `adType`) keeps the same ad id — the docs say ads are re-created only when
   creative fields change; if `newAdIds` comes back, Heblo stores the new id as `PlatformResourceId` and reverts that one.
3. `ads.update` needs `adType` for non-`eta` ads (it defaults to `eta`), so Heblo echoes the listed `adType`.

Campaign-level negatives are a read-modify-write: a UI edit between Heblo's read and write is lost. Proposals should
prefer ad-group-level negatives.

## 12. Manual smoke procedure (Ondrej runs it; agents never do)

Preconditions: core C3 + C4 (+ C5 for the web UI) deployed to **staging**; `Sklik--ApiToken` (and `Sklik--UserId` if
needed) set in `kv-heblo-stg`; staging app restarted; execution kill switch **on for Sklik only**.

Setup in the Sklik UI (all inside a **paused** campaign, so nothing spends money):
1. Create campaign `HEBLO-SMOKE` (search), status *pozastaveno*. Add campaign negative keyword `heblo-keep` (broad).
2. Inside it, create ad group `HEBLO-SMOKE-GROUP` with one keyword and one active text ad.
3. Wait for the next ads sync (or trigger the daily sync job in Recurring Jobs) so the entities exist in Heblo.

Checks (each: submit the proposal in Heblo, approve in the web UI, verify in the Sklik UI, then revert in Heblo and verify again):
1. **Group negative** — `AddNegativeKeyword` on `HEBLO-SMOKE-GROUP`, text `heblo-smoke-group`, `Phrase`.
   Expect the group's negatives to show `"heblo-smoke-group"`; after revert it is gone.
2. **Campaign negative** — `AddNegativeKeyword` on `HEBLO-SMOKE`, text `heblo-smoke-campaign`, `Exact`.
   Expect `[heblo-smoke-campaign]` **and** `heblo-keep` still present (confirms assumption 1). After revert only
   `heblo-keep` remains. If `heblo-keep` disappeared, or the call failed with `keyword_duplicate`, the replace
   assumption is wrong: disable Sklik execution and report.
3. **Pause ad** — `PauseAd` on the smoke ad. Expect it *pozastavena* with the **same ad id** (confirms assumption 2);
   after revert it is active again.
4. In Heblo's activity view, each action shows `Executed` / `Reverted`, and the ad change sync records the changes.

Record the date and outcome here (Runtime facts in `docs/processes/sync-ads-sklik.md` too), then delete `HEBLO-SMOKE`.
````

- [ ] **Step 2: Update the process doc**

In `docs/processes/sync-ads-sklik.md`:
- `summary:` append ` Also executes approved AddNegativeKeyword / PauseAd proposals (SklikActionExecutor).`
- `verified_at:` set to the quoted output of `git rev-parse --short=9 HEAD`.
- Under `## Purpose` add the paragraph: `It also executes approved Sklik proposals (AddNegativeKeyword at ad group or campaign level, PauseAd) for the core's execution job; nothing calls it until an admin enables execution. Write methods and the manual smoke procedure: docs/integrations/sklik-api.md §11–§12.`
- Under `## Known quirks` add: `- Campaign-level negative keywords are written as the campaign's whole list (campaigns.update); a concurrent UI edit between read and write is lost.`
- Under `## Code entry points` add: `- backend/src/Adapters/Anela.Heblo.Adapters.Sklik/Execution/SklikActionExecutor.cs — the IAdActionExecutor`

Run: `python3 scripts/process-docs/check.py index && python3 scripts/process-docs/check.py check`
Expected: exit 0, no `ERROR` mentioning `sync-ads-sklik`.

- [ ] **Step 3: Full backend validation**

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet format Anela.Heblo.sln --include backend/src/Adapters/Anela.Heblo.Adapters.Sklik/ backend/test/Anela.Heblo.Adapters.Sklik.Tests/
dotnet format Anela.Heblo.sln --verify-no-changes --include backend/src/Adapters/Anela.Heblo.Adapters.Sklik/ backend/test/Anela.Heblo.Adapters.Sklik.Tests/
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.Sklik.Tests/Anela.Heblo.Adapters.Sklik.Tests.csproj --no-build -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "Category!=Playwright&Category!=Integration"
```

Expected: `0 Error(s)` twice, `--verify-no-changes` exits 0, all tests pass.

- [ ] **Step 4: Commit, push, open the PR**

```bash
git add docs/integrations/sklik-api.md docs/processes/sync-ads-sklik.md docs/processes/INDEX.md
git commit -m "docs: sklik write methods and manual smoke procedure

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git push -u origin feat/marketing-sklik-executor
gh pr create --base main --title "feat: Sklik action executor — negative keywords and ad pause (WS3)" --body "$(cat <<'EOF'
## Summary
- `SklikActionExecutor : IAdActionExecutor` for `AddNegativeKeyword` (ad group via `keywords.negative.create/remove`, campaign via `campaigns.update` full-list replace) and `PauseAd` (`ads.update` suspend/active).
- Platform rejections → `Failed` (session stripped from the stored response); auth/transport errors thrown; writes never retried.
- Contract tests on a stateful fake Drak server; `docs/integrations/sklik-api.md` §11 (write methods, match-type mapping) and §12 (manual smoke).
- Registered with the read source; inert until core C3+C4 are live and an admin enables Sklik execution.

## Before enabling Sklik execution
Run the manual smoke in `docs/integrations/sklik-api.md` §12 on staging. It confirms the two unverified assumptions:
`campaigns.update.negativeKeywords` replaces the list, and a status-only `ads.update` keeps the ad id. No live write was run by the agent.

## Deviations from the spec
- Campaign-level negatives are a non-atomic read-modify-write (Drak has no add/remove for them).
- Group negatives get their id by re-listing after create; revert removes by name + match type.
- This PR depends on the Sklik read-source PR (shared client/settings).

## Test plan
- [x] `Anela.Heblo.Adapters.Sklik.Tests` incl. inherited `AdActionExecutorContractTests`
- [x] `Anela.Heblo.Tests` (non-integration)
- [ ] Ondrej: manual smoke §12 on staging

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
gh pr checks --watch
```

Expected: PR created; CI (backend, frontend, docker) green. An empty check list right after creation means "not registered yet" — wait and re-run.
