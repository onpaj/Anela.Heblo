# Marketing Google Ads (WS1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the MarketingAds backbone a working Google Ads platform: prove access (Part A), ship `GoogleAdsReadSource : IAdPlatformReadSource` (Part B), then ship `GoogleAdsActionExecutor : IAdActionExecutor` for `AddNegativeKeyword` and `PauseAd` (Part C).

**Architecture:** A thin, injectable transport (`IGoogleAdsApiClient`) talks to the Google Ads **REST API v25** over `HttpClient` with an OAuth refresh-token provider and Polly retries limited to transient failures. The read source and executor sit on top of it and are pure mapping/orchestration code, so tests use recorded search-response JSON fixtures (read side) and a stateful in-memory fake (write side). Registration is gated on `AdSettingsGuard.IsConfigured(...)` and never throws.

**Tech Stack:** .NET 8, `System.Text.Json` / `System.Text.Json.Nodes`, `IHttpClientFactory`, Polly 8.4.1, xUnit 2.9.2, FluentAssertions 6.12.0, `Microsoft.Extensions.TimeProvider.Testing`, Python 3 stdlib (spike only).

**Spec:** `docs/superpowers/specs/2026-10-07-marketing-agents-platform-design.md` (sections 4.2, 4.3, 9, 12 are binding) and its context `docs/handoff/marketing-agents-platform.md`.

---

## Before you start

You are a fresh agent with no conversation context. Do this first, in order:

1. Read `CLAUDE.md`, the spec (all of it; section 12 names are binding), the handoff sections 2–3, and this whole plan.
2. Read these to learn house style: `docs/integrations/shoptet-api.md` (integration-doc style), `docs/processes/_TEMPLATE.md` and `docs/processes/sync-ad-platform-transactions.md` (process-doc style; that doc **owns** `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/**`, so every PR here must edit it), `docs/architecture/testing-strategy.md`.
3. Read the existing adapter: everything under `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/`. The billing files (`GoogleAdsInvoiceImportJob.cs`, `GoogleAdsTransactionSource.cs`, `SdkAccountBudgetFetcher.cs`, `IAccountBudgetFetcher.cs`, `RawAccountBudget.cs`, `GoogleAdsAdapterServiceCollectionExtensions.cs`) are **never modified** by this plan.
4. Three parts, three different deliverables:
   - **Part A** — throwaway, read-only access spike. No repo changes, no branch, no PR. Needs Ondrej (the user) to supply credentials and possibly click through one OAuth consent. **If Part A's gate fails, stop and report; do not start Part B.**
   - **Part B** — read-source PR. Prerequisite: core PR **C1** merged on `origin/main` (checked in Task B0).
   - **Part C** — executor PR. Prerequisites: C1 **and the Part B PR** merged on `origin/main` (Part C reuses Part B's transport; see Spec deviations).
5. Build/test rules for this repo (hard-won):
   - Build first, then test with `--no-build`: `dotnet build <project> -p:UseSharedCompilation=false` then `dotnet test <project> --no-build -p:UseSharedCompilation=false`. A test that hangs at 0 % CPU means another worktree is building — wait and retry, do not kill other processes.
   - A failure whose stack trace contradicts the source you can read means stale binaries: `touch` the test project file and rebuild.
   - Format before every commit: `dotnet format Anela.Heblo.sln --include <changed paths>`.
   - The `.sln` is at the repo root (`Anela.Heblo.sln`).
6. You never write to a live Google Ads account, and you never set a production Key Vault secret. Part C's live check is a **manual** procedure Ondrej runs.

## Global Constraints

- Contract names are exactly spec section 12.2: `AdPlatform`, `AdEntityLevel`, `AdEntityStatus`, `KeywordMatchType`, `AdChangeActorKind`, `AdActionType`, `AdExecutionOutcome`, `AdSourceCapabilities`, `AdAccountSnapshot`, `AdEntitySnapshot`, `AdDailyFactRow`, `AdSearchTermRow`, `AdChangeEventRow`, `AdAction`, `AdTargetState`, `AdExecutionResult`, `AdActionValues`, `AdActionPayloadKeys`, `AdSettingsGuard`, `IAdPlatformReadSource`, `IAdActionExecutor`; namespace `Anela.Heblo.Application.Features.MarketingAds.Contracts`. If C1 on main differs, **main wins** — adapt and list the difference in the PR description.
- Test-kit bases: `AdPlatformReadSourceContractTests`, `AdActionExecutorContractTests` in namespace `Anela.Heblo.MarketingAds.TestKit`, project `backend/test/Anela.Heblo.MarketingAds.TestKit/`.
- Platform classes: `GoogleAdsReadSource`, `GoogleAdsActionExecutor` in `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds`; tests in new `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests`; integration doc `docs/integrations/google-ads-api.md`.
- Google Ads REST base `https://googleads.googleapis.com/`, API version from setting `GoogleAds:ApiVersion`, default `v25` (v25 sunsets Aug 2027).
- OAuth scope `https://www.googleapis.com/auth/adwords`; token endpoint `https://oauth2.googleapis.com/token`.
- `change_event`: window ≤ 30 days back, `LIMIT` mandatory and ≤ 10 000.
- Secrets only in Key Vault, `--` separator (`GoogleAds--OAuth2RefreshToken`), staging `kv-heblo-stg`, production `kv-heblo-prod`. Never in App Settings, never in the repo.
- Registration never throws on bad/missing config: it registers nothing.
- Retries: reads retry only transient failures (HTTP 408/429/5xx, transport faults without a status, HttpClient timeouts when the caller's token is not cancelled). Mutates retry **only** HTTP 429.
- `ExecuteAsync`/`RevertAsync` return `Failed` for platform-side rejections and never throw for them; they throw only for transport/auth failures (spec 12.2).
- Executors do not compare old values (the core does). `ReadCurrentAsync` returns `CurrentValue` = `Absent|Present` (negatives) or `Enabled|Paused|Removed|Unknown` (ads).
- Every commit: Conventional Commit subject, blank line, `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Every PR touching owned code updates its `docs/processes/` doc in the same PR and runs `python3 scripts/process-docs/check.py index`.

## Review Focus

1. **Metrics Google leaves out.** REST responses are proto3 JSON: a metric equal to 0 is *omitted*, int64 values arrive as strings. A row with only `impressions` must map to clicks = 0, cost = 0 — not crash, not skip. Pinned in Task B6 (`treats_metrics_google_omits_as_zero`).
2. **Search-term duplicates.** Google returns `EXACT` and `NEAR_EXACT` (and `PHRASE`/`NEAR_PHRASE`) as separate rows; both map to one `KeywordMatchType`, which would collide on the `ad_search_term_daily` primary key. They must be summed into one row. Pinned in Task B6 (`merges_near_exact_into_exact`).
3. **A change-sync watermark older than 30 days.** After an outage the core asks for changes since e.g. 45 days ago; Google rejects the query outright. The source must clamp to the window, warn, and still return the last 30 days. Pinned in Task B7 (`clamps_a_watermark_older_than_the_30_day_window`).
4. **A mutate that failed with 5xx or timed out.** It may already be applied; retrying a create could add a second criterion. Only 429 is retried for mutates. Pinned in Task E1 (`does_not_retry_a_mutate_that_failed_with_503`).
5. **Agent-supplied keyword text and ids reaching GAQL.** Prompt-injected text (quotes, `OR`, newlines) must never be interpolated into a query; ids must be numeric before they are. Pinned in Task E3 (`never_puts_keyword_text_into_a_query`) and Task B3 (`rejects_a_non_numeric_customer_id_without_calling_google`).

## Spec deviations

1. **Developer token is no longer the access gate.** Google sunset developer tokens on 2026-09-09: access levels (Test / Explorer / Basic / Standard) now belong to the Google Cloud project that owns the OAuth client; the `developer-token` header is optional, ignored by v25, and planned to be rejected in a future major version. Spec section 9 asks for a "developer token (Basic access)". This plan: the token is optional in the spike, not part of the registration gate, and not sent by new code. The project needs **Explorer access or higher** (Explorer = 2 880 ops/day on production accounts, ample for ~150 calls/day). The insufficient-access error is now `authorizationError.CLOUD_PROJECT_NOT_APPROVED_FOR_PRODUCTION` (older versions: `DEVELOPER_TOKEN_NOT_APPROVED` / `ACTION_NOT_PERMITTED`).
2. **REST via `HttpClient`, not the `Google.Ads.GoogleAds` SDK.** The repo's SDK 21.1.0 only contains API V16–V18, all sunset; the current SDK (27.x) dropped V18, so upgrading it would force edits to the billing fetcher, which this workspace must not touch. New code uses REST v25; the SDK stays for the (dead) billing importer only.
3. **Contract fixtures are recorded at the search-response level** (the JSON body of `googleAds:search`, consumed through `IGoogleAdsApiClient`), not as raw HTTP exchanges. The HTTP layer has its own stub-handler tests (Task B3).
4. **External-id conventions for Google's composite ids** (spec says only "platform external ids"): Campaign `"{campaignId}"`, AdGroup `"{adGroupId}"`, Keyword `"{adGroupId}~{criterionId}"`, NegativeKeyword `"campaignCriteria/{campaignId}~{criterionId}"` or `"adGroupCriteria/{adGroupId}~{criterionId}"`, Ad `"{adGroupId}~{adId}"`. The executor rebuilds resource names from these.
5. **`validate_only` returns `Failed`.** C1's `AdExecutionOutcome` has no "validated" value. With `GoogleAds:ValidateOnly = true` an accepted request returns `Failed` with an error starting `ValidateOnly:` so nothing downstream believes the account changed. `ValidateOnly` defaults to **true** in `GoogleAdsSettings` and `appsettings.json`; only `appsettings.Production.json` sets it to false (staging and production point at the same live account).
6. **Part C depends on Part B**, not only on C1 (shared transport).
7. **New settings** not named in the spec: `GoogleAds:LoginCustomerId`, `GoogleAds:ApiVersion`, `GoogleAds:HebloUserEmail` (Part B), `GoogleAds:ValidateOnly` (Part C).
8. **Change events on ad-group criteria** are classified `NegativeKeyword` only when the event payload carries `negative: true`; an UPDATE/REMOVE whose payload omits it is reported as `Keyword`. The core then finds no entity for it, but `entity_external_ref` and the raw JSON are kept. Accepted, documented in the integration doc.

## File structure

```
backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/
  GoogleAdsSettings.cs                         (modify: + LoginCustomerId, ApiVersion, HebloUserEmail; C: + ValidateOnly)
  Anela.Heblo.Adapters.GoogleAds.csproj        (modify: + Microsoft.Extensions.Http, Polly, InternalsVisibleTo)
  GoogleAdsMarketingAdsServiceCollectionExtensions.cs   (new: gated registration)
  Api/      GoogleAdsIds, GoogleAdsQuery, IGoogleAdsApiClient, GoogleAdsApiException,
            GoogleAdsErrorParser, IGoogleAdsAccessTokenProvider, GoogleAdsOAuthTokenProvider,
            GoogleAdsRestClient   (C: + GoogleAdsMutateResult, GoogleAdsMutateServices)
  Reporting/ GoogleAdsJson, GoogleAdsMappings, GoogleAdsQueries, GoogleAdsEntityMapper,
             GoogleAdsFactMapper, GoogleAdsChangeEventMapper, GoogleAdsCustomer, GoogleAdsReadSource
  Execution/ (Part C) GoogleAdsExecutorQueries, GoogleAdsMutations, GoogleAdsExecutionResults,
             GoogleAdsActionGuard, GoogleAdsPauseAdHandler, GoogleAdsNegativeKeywordHandler,
             GoogleAdsActionExecutor
backend/src/Anela.Heblo.API/Program.cs         (modify: one line)
backend/src/Anela.Heblo.API/appsettings.json   (modify: GoogleAds section)
backend/src/Anela.Heblo.API/appsettings.Production.json (Part C: GoogleAds.ValidateOnly=false)
backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/   (new project)
  Support/ TestSettings, TestOptionsMonitor, StubHttpMessageHandler, StubHttpClientFactory,
           StaticAccessTokenProvider, FixtureGoogleAdsApiClient, ReadSourceHarness
           (C: StatefulFakeGoogleAdsApi, ExecutorHarness, SmokeFactAttribute, SmokeEnvironment)
  Fixtures/ReadSource/*.json
  Api/, Reporting/, Execution/ test classes, RegistrationTests.cs
docs/integrations/google-ads-api.md            (new, Task B1)
docs/processes/sync-google-ads-campaign-data.md (new, Task B8)
docs/processes/flow-google-ads-action-execution.md (new, Task E5)
docs/processes/sync-ad-platform-transactions.md (modify: runtime facts)
```

---

# Part A — Access spike (throwaway, read-only, no PR)

Everything in Part A lives outside the repo. Scripts go in your session scratchpad directory (the system prompt names it; call it `$SPIKE_DIR` below — if none is named, `SPIKE_DIR=$(mktemp -d)`). Findings that must survive the session go to `~/Work/heblo-marketing-agents/google-ads-spike-findings.md` (no secrets in it, ever). Scripts never print a secret value, never write to Google, and are run with `python3 -I`.

### Task A1: Credentials file check

**Files:**
- Read only: `~/Work/heblo-marketing-agents/google-ads.env` (supplied by Ondrej)
- Create: `$SPIKE_DIR/envcheck.py`

**Interfaces:**
- Consumes: nothing.
- Produces: the env-file variable names every later task uses:

| Variable | Required | Meaning |
|---|---|---|
| `GOOGLE_ADS_CUSTOMER_ID` | yes | Anela's Google Ads **client** account id, e.g. `123-456-7890` |
| `GOOGLE_ADS_LOGIN_CUSTOMER_ID` | only via MCC | Manager account id when the OAuth user reaches the account through a manager |
| `GOOGLE_ADS_OAUTH_CLIENT_ID` | yes | OAuth client id of Anela's Google Cloud project (Google Ads API enabled) |
| `GOOGLE_ADS_OAUTH_CLIENT_SECRET` | yes | its secret |
| `GOOGLE_ADS_REFRESH_TOKEN` | yes (Task A2 can create it) | refresh token of the Google user Heblo will act as |
| `GOOGLE_ADS_HEBLO_USER_EMAIL` | recommended | that user's email |
| `GOOGLE_ADS_DEVELOPER_TOKEN` | no | legacy; optional and ignored by v25 |

- [ ] **Step 1: Ask Ondrej for the file if it is missing**

Run: `test -f ~/Work/heblo-marketing-agents/google-ads.env && echo present || echo missing`

If `missing`, send Ondrej this and wait:

> Please create `~/Work/heblo-marketing-agents/google-ads.env` (chmod 600) with:
> `GOOGLE_ADS_CUSTOMER_ID=` Anela's Google Ads account id · `GOOGLE_ADS_LOGIN_CUSTOMER_ID=` the manager (MCC) id only if your Google user reaches the account through a manager account · `GOOGLE_ADS_OAUTH_CLIENT_ID=` / `GOOGLE_ADS_OAUTH_CLIENT_SECRET=` from an OAuth client of type **Desktop app** in Anela's Google Cloud project with the Google Ads API enabled · `GOOGLE_ADS_REFRESH_TOKEN=` if you already have one (otherwise leave it out, we generate it together) · `GOOGLE_ADS_HEBLO_USER_EMAIL=` the Google user Heblo should act as (ideally a dedicated user with **Standard**, not Admin, access) · `GOOGLE_ADS_DEVELOPER_TOKEN=` only if you have one (no longer required since 2026-09-09).
> Also tell me: what access level does the Google Cloud project show on its "Google Ads API Overview" page in the Cloud Console (Test / Explorer / Basic / Standard)?

- [ ] **Step 2: Write the presence checker**

`$SPIKE_DIR/envcheck.py`:

```python
#!/usr/bin/env python3
"""Reports which google-ads.env variables are set. Never prints a value."""
import sys

REQUIRED = ["GOOGLE_ADS_CUSTOMER_ID", "GOOGLE_ADS_OAUTH_CLIENT_ID",
            "GOOGLE_ADS_OAUTH_CLIENT_SECRET", "GOOGLE_ADS_REFRESH_TOKEN"]
OPTIONAL = ["GOOGLE_ADS_LOGIN_CUSTOMER_ID", "GOOGLE_ADS_HEBLO_USER_EMAIL", "GOOGLE_ADS_DEVELOPER_TOKEN"]


def load_env(path):
    values = {}
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            line = line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            key, value = line.split("=", 1)
            values[key.strip()] = value.strip().strip('"').strip("'")
    return values


if __name__ == "__main__":
    env = load_env(sys.argv[1])
    for key in REQUIRED + OPTIONAL:
        state = "set" if env.get(key) else "MISSING"
        kind = "required" if key in REQUIRED else "optional"
        print(f"{key:34} {kind:9} {state}")
```

- [ ] **Step 3: Run it**

Run: `python3 -I $SPIKE_DIR/envcheck.py ~/Work/heblo-marketing-agents/google-ads.env`
Expected: a table of `set` / `MISSING`, no values. If only `GOOGLE_ADS_REFRESH_TOKEN` is missing → Task A2. If anything else required is missing → ask Ondrej again (Step 1 text) and wait. Otherwise skip A2.

### Task A2: Generate a refresh token with Ondrej (only if missing)

**Files:**
- Create: `$SPIKE_DIR/oauth_loopback.py`
- Modify (append one line): `~/Work/heblo-marketing-agents/google-ads.env`

**Interfaces:**
- Consumes: `GOOGLE_ADS_OAUTH_CLIENT_ID`, `GOOGLE_ADS_OAUTH_CLIENT_SECRET`.
- Produces: `GOOGLE_ADS_REFRESH_TOKEN` in the env file.

- [ ] **Step 1: Tell Ondrej the two preconditions and wait for "ok"**

> 1) The OAuth client must be type **Desktop app** (a "Web application" client works only if `http://127.0.0.1:8765` is an authorised redirect URI). 2) The OAuth consent screen must be **In production** or user type **Internal**: in *Testing* status Google expires refresh tokens after 7 days, and Heblo's sync would then die a week after deployment with `invalid_grant`. I will print a URL; open it, sign in as the Google user Heblo should act as, and approve the "Google Ads" scope.

- [ ] **Step 2: Write the loopback script**

`$SPIKE_DIR/oauth_loopback.py`:

```python
#!/usr/bin/env python3
"""Loopback OAuth (PKCE) for the Google Ads scope. Writes the refresh token into the env file
and never prints it."""
import base64, hashlib, http.server, json, os, secrets, sys, urllib.error, urllib.parse, urllib.request

PORT = 8765
REDIRECT_URI = f"http://127.0.0.1:{PORT}"
SCOPE = "https://www.googleapis.com/auth/adwords"
ENV_KEY = "GOOGLE_ADS_REFRESH_TOKEN"


def load_env(path):
    values = {}
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            line = line.strip()
            if line and not line.startswith("#") and "=" in line:
                key, value = line.split("=", 1)
                values[key.strip()] = value.strip().strip('"').strip("'")
    return values


def write_refresh_token(path, token):
    with open(path, encoding="utf-8") as fh:
        lines = [l for l in fh.read().splitlines() if not l.startswith(ENV_KEY + "=")]
    lines.append(f"{ENV_KEY}={token}")
    with open(path, "w", encoding="utf-8") as fh:
        fh.write("\n".join(lines) + "\n")
    os.chmod(path, 0o600)


class CallbackHandler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        query = urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query)
        if "code" in query or "error" in query:
            self.server.result = query
        self.send_response(200)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        self.end_headers()
        self.wfile.write("Hotovo - you can close this tab.".encode("utf-8"))

    def log_message(self, *args):
        pass


def main(env_path):
    env = load_env(env_path)
    verifier = secrets.token_urlsafe(64)
    challenge = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).rstrip(b"=").decode()
    state = secrets.token_urlsafe(16)
    auth_url = "https://accounts.google.com/o/oauth2/v2/auth?" + urllib.parse.urlencode({
        "client_id": env["GOOGLE_ADS_OAUTH_CLIENT_ID"], "redirect_uri": REDIRECT_URI,
        "response_type": "code", "scope": SCOPE, "access_type": "offline", "prompt": "consent",
        "state": state, "code_challenge": challenge, "code_challenge_method": "S256"})
    print("Open this URL in the browser and approve:\n" + auth_url, flush=True)

    server = http.server.HTTPServer(("127.0.0.1", PORT), CallbackHandler)
    server.result = None
    while server.result is None:
        server.handle_request()
    result = server.result
    if "error" in result:
        sys.exit(f"Consent refused: {result['error'][0]}")
    if result.get("state", [""])[0] != state:
        sys.exit("State mismatch - aborting.")

    data = urllib.parse.urlencode({
        "code": result["code"][0], "client_id": env["GOOGLE_ADS_OAUTH_CLIENT_ID"],
        "client_secret": env["GOOGLE_ADS_OAUTH_CLIENT_SECRET"], "redirect_uri": REDIRECT_URI,
        "grant_type": "authorization_code", "code_verifier": verifier}).encode()
    try:
        with urllib.request.urlopen(urllib.request.Request("https://oauth2.googleapis.com/token", data=data), timeout=30) as resp:
            body = json.load(resp)
    except urllib.error.HTTPError as e:
        err = json.loads(e.read() or b"{}")
        sys.exit(f"Token exchange failed: HTTP {e.code} {err.get('error')} {err.get('error_description')}")
    if "refresh_token" not in body:
        sys.exit("Google returned no refresh_token (consent was not forced?). Revoke the app at myaccount.google.com/permissions and retry.")
    write_refresh_token(env_path, body["refresh_token"])
    print(f"{ENV_KEY} written to {env_path} (value not shown). Granted scope: {body.get('scope')}")


if __name__ == "__main__":
    main(sys.argv[1])
```

- [ ] **Step 3: Run it with Ondrej**

Run (long-running, waits for the browser): `python3 -I $SPIKE_DIR/oauth_loopback.py ~/Work/heblo-marketing-agents/google-ads.env`
Expected: a URL printed; after Ondrej approves, `GOOGLE_ADS_REFRESH_TOKEN written to … (value not shown). Granted scope: https://www.googleapis.com/auth/adwords`. Ask Ondrej which Google account he signed in with and set `GOOGLE_ADS_HEBLO_USER_EMAIL` to it if missing (ask him to add the line; you may add it yourself since an email is not a secret).

- [ ] **Step 4: Re-run Task A1 Step 3** — every required key must now be `set`.

### Task A3: Read-only API spike

**Files:**
- Create: `$SPIKE_DIR/spike.py`

**Interfaces:**
- Consumes: the env file (Task A1 names).
- Produces: console output used by Task A4. Its GAQL and the `change_event` timestamp format (`yyyy-MM-dd HH:mm:ss`) are the ones Part B uses, so a pass here validates them.

- [ ] **Step 1: Write the spike**

`$SPIKE_DIR/spike.py`:

```python
#!/usr/bin/env python3
"""Throwaway, READ-ONLY Google Ads access spike (REST v25). Never prints a secret, never mutates."""
import collections, datetime, json, sys, urllib.error, urllib.parse, urllib.request

API_VERSION = "v25"
BASE = "https://googleads.googleapis.com"
METRICS = "metrics.impressions, metrics.clicks, metrics.cost_micros, metrics.conversions, metrics.conversions_value"
ACCESS_ERRORS = {
    "authorizationError.CLOUD_PROJECT_NOT_APPROVED_FOR_PRODUCTION": "Cloud project has only Test access: apply for Explorer/Basic on the Google Ads API Overview page in Cloud Console.",
    "authorizationError.DEVELOPER_TOKEN_NOT_APPROVED": "Legacy name of the same problem: test-only access.",
    "authorizationError.ACTION_NOT_PERMITTED": "Older API versions' code for test-only access, or the user lacks rights.",
    "authorizationError.USER_PERMISSION_DENIED": "The OAuth user cannot see this customer: wrong account, or access is via a manager and GOOGLE_ADS_LOGIN_CUSTOMER_ID is missing.",
    "authenticationError.NOT_ADS_USER": "The OAuth Google user has no Google Ads access at all.",
    "authorizationError.CUSTOMER_NOT_ENABLED": "The account is cancelled or not set up.",
    "errorInfo.SERVICE_DISABLED": "Google Ads API is not enabled in the OAuth client's Cloud project.",
    "headerError.INVALID_LOGIN_CUSTOMER_ID": "GOOGLE_ADS_LOGIN_CUSTOMER_ID is not a manager of the customer.",
}


def load_env(path):
    values = {}
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            line = line.strip()
            if line and not line.startswith("#") and "=" in line:
                key, value = line.split("=", 1)
                values[key.strip()] = value.strip().strip('"').strip("'")
    return values


def digits(value):
    return (value or "").replace("-", "").strip()


def access_token(env):
    data = urllib.parse.urlencode({
        "grant_type": "refresh_token", "client_id": env["GOOGLE_ADS_OAUTH_CLIENT_ID"],
        "client_secret": env["GOOGLE_ADS_OAUTH_CLIENT_SECRET"],
        "refresh_token": env["GOOGLE_ADS_REFRESH_TOKEN"]}).encode()
    try:
        with urllib.request.urlopen(urllib.request.Request("https://oauth2.googleapis.com/token", data=data), timeout=30) as resp:
            print("[oauth] OK")
            return json.load(resp)["access_token"]
    except urllib.error.HTTPError as e:
        body = json.loads(e.read() or b"{}")
        print(f"[oauth] FAILED HTTP {e.code} error={body.get('error')} description={body.get('error_description')}")
        print("        invalid_grant = refresh token revoked/expired (consent screen in Testing expires it after 7 days).")
        sys.exit(2)


def report_error(name, e):
    raw = e.read().decode("utf-8", "replace")
    try:
        err = json.loads(raw).get("error", {})
    except ValueError:
        err = {"message": raw[:300]}
    codes, request_id = [], None
    for detail in err.get("details", []):
        request_id = detail.get("requestId", request_id)
        for item in detail.get("errors", []):
            codes.extend(f"{k}.{v}" for k, v in item.get("errorCode", {}).items())
        if detail.get("reason"):
            codes.append(f"errorInfo.{detail['reason']}")
    print(f"[{name}] FAILED HTTP {e.code} status={err.get('status')} codes={codes or '-'} requestId={request_id}")
    print(f"        message={err.get('message', '')[:300]}")
    for code in codes:
        if code in ACCESS_ERRORS:
            print(f"        meaning: {ACCESS_ERRORS[code]}")


def search(env, token, name, gaql, max_pages=20):
    cid = digits(env["GOOGLE_ADS_CUSTOMER_ID"])
    headers = {"Authorization": f"Bearer {token}", "Content-Type": "application/json"}
    if digits(env.get("GOOGLE_ADS_LOGIN_CUSTOMER_ID")):
        headers["login-customer-id"] = digits(env["GOOGLE_ADS_LOGIN_CUSTOMER_ID"])
    if env.get("GOOGLE_ADS_DEVELOPER_TOKEN"):
        headers["developer-token"] = env["GOOGLE_ADS_DEVELOPER_TOKEN"]
    url = f"{BASE}/{API_VERSION}/customers/{cid}/googleAds:search"
    rows, page_token = [], None
    for _ in range(max_pages):
        body = {"query": gaql}
        if page_token:
            body["pageToken"] = page_token
        request = urllib.request.Request(url, data=json.dumps(body).encode(), headers=headers, method="POST")
        try:
            with urllib.request.urlopen(request, timeout=60) as resp:
                payload = json.load(resp)
        except urllib.error.HTTPError as e:
            report_error(name, e)
            return None
        rows.extend(payload.get("results", []))
        page_token = payload.get("nextPageToken")
        if not page_token:
            break
    print(f"[{name}] OK rows={len(rows)}")
    return rows


def run(env):
    token = access_token(env)
    yesterday = (datetime.date.today() - datetime.timedelta(days=1)).isoformat()
    now = datetime.datetime.now()
    since = now - datetime.timedelta(days=29)
    fmt = "%Y-%m-%d %H:%M:%S"
    results = {}

    rows = search(env, token, "customer",
                  "SELECT customer.id, customer.descriptive_name, customer.currency_code, customer.time_zone, "
                  "customer.manager, customer.test_account FROM customer LIMIT 1")
    if rows is None:
        return False
    c = rows[0]["customer"]
    print(f"        currency={c.get('currencyCode')} timeZone={c.get('timeZone')} manager={c.get('manager', False)} test={c.get('testAccount', False)}")
    if c.get("manager"):
        search(env, token, "customer_client",
               "SELECT customer_client.id, customer_client.descriptive_name, customer_client.manager "
               "FROM customer_client WHERE customer_client.level <= 1")
        print("        CustomerId is a MANAGER account - Heblo needs the client account id. STOP and ask Ondrej.")
        return False

    rows = search(env, token, "campaign_yesterday",
                  f"SELECT campaign.id, campaign.name, campaign.status, segments.date, {METRICS} "
                  f"FROM campaign WHERE segments.date DURING YESTERDAY")
    results["campaign"] = rows is not None
    if rows is not None:
        cost = sum(int(r.get("metrics", {}).get("costMicros", "0")) for r in rows) / 1_000_000
        conv = sum(float(r.get("metrics", {}).get("conversions", 0)) for r in rows)
        print(f"        campaigns with spend yesterday={len(rows)} cost={cost:.2f} {c.get('currencyCode')} conversions={conv:.2f}")

    rows = search(env, token, "change_event_30d",
                  "SELECT change_event.resource_name, change_event.change_date_time, change_event.change_resource_name, "
                  "change_event.user_email, change_event.client_type, change_event.change_resource_type, "
                  "change_event.resource_change_operation, change_event.changed_fields "
                  f"FROM change_event WHERE change_event.change_date_time >= '{since.strftime(fmt)}' "
                  f"AND change_event.change_date_time <= '{now.strftime(fmt)}' "
                  "ORDER BY change_event.change_date_time ASC LIMIT 1000")
    results["change_event"] = rows is not None
    if rows:
        events = [r["changeEvent"] for r in rows]
        print("        by client_type:", dict(collections.Counter(e.get("clientType") for e in events)))
        print("        by user_email :", dict(collections.Counter(e.get("userEmail", "(none)") for e in events)))
        print("        by resource   :", dict(collections.Counter(e.get("changeResourceType") for e in events)))
        print(f"        first={events[0].get('changeDateTime')} last={events[-1].get('changeDateTime')}")

    rows = search(env, token, "customer_user_access",
                  "SELECT customer_user_access.email_address, customer_user_access.access_role, "
                  "customer_user_access.inviter_user_email_address FROM customer_user_access")
    results["user_access"] = rows is not None
    for r in rows or []:
        a = r["customerUserAccess"]
        print(f"        {a.get('accessRole', '?'):10} {a.get('emailAddress')}")

    rows = search(env, token, "search_terms_yesterday",
                  f"SELECT ad_group.id, search_term_view.search_term, segments.search_term_match_type, segments.date, {METRICS} "
                  f"FROM search_term_view WHERE segments.date = '{yesterday}' LIMIT 50")
    results["search_terms"] = rows is not None
    if rows:
        print("        match types:", dict(collections.Counter(r.get("segments", {}).get("searchTermMatchType") for r in rows)))

    for name, gaql in [
        ("count_campaigns", "SELECT campaign.id, campaign.status FROM campaign"),
        ("count_ad_groups", "SELECT ad_group.id, ad_group.status FROM ad_group"),
        ("count_keywords", "SELECT ad_group_criterion.criterion_id, ad_group_criterion.status FROM ad_group_criterion "
                           "WHERE ad_group_criterion.type = 'KEYWORD' AND ad_group_criterion.negative = FALSE"),
        ("count_ad_group_negatives", "SELECT ad_group_criterion.criterion_id FROM ad_group_criterion "
                                     "WHERE ad_group_criterion.type = 'KEYWORD' AND ad_group_criterion.negative = TRUE"),
        ("count_campaign_negatives", "SELECT campaign_criterion.criterion_id FROM campaign_criterion "
                                     "WHERE campaign_criterion.type = 'KEYWORD' AND campaign_criterion.negative = TRUE"),
        ("count_ads", "SELECT ad_group_ad.ad.id, ad_group_ad.status FROM ad_group_ad"),
    ]:
        rows = search(env, token, name, gaql)
        if rows is not None:
            statuses = collections.Counter(
                next((v.get("status") for v in r.values() if isinstance(v, dict) and "status" in v), "-") for r in rows)
            print(f"        by status: {dict(statuses)}")

    print("\nSUMMARY", results)
    return all(results.values())


if __name__ == "__main__":
    ok = run(load_env(sys.argv[1]))
    sys.exit(0 if ok else 1)
```

- [ ] **Step 2: Run it**

Run: `python3 -I $SPIKE_DIR/spike.py ~/Work/heblo-marketing-agents/google-ads.env`
Expected on success: `[oauth] OK`, `[customer] OK rows=1` with `currency=CZK timeZone=Europe/Prague manager=False`, `[campaign_yesterday] OK`, `[change_event_30d] OK`, `[customer_user_access] OK`, `[search_terms_yesterday] OK`, six `count_*` lines, `SUMMARY {... all True}`, exit code 0.

How to recognise access-level problems in the output (the script prints a `meaning:` line for each):
- `authorizationError.CLOUD_PROJECT_NOT_APPROVED_FOR_PRODUCTION` (HTTP 403) on the **first** search — the Cloud project only has Test access. Older names for the same thing: `DEVELOPER_TOKEN_NOT_APPROVED`, `ACTION_NOT_PERMITTED`.
- `authorizationError.USER_PERMISSION_DENIED` — wrong customer, or a manager login id is needed.
- `errorInfo.SERVICE_DISABLED` — API not enabled in the Cloud project.
- `[oauth] FAILED … invalid_grant` — refresh token dead.
- `customer_user_access` alone failing with `USER_PERMISSION_DENIED` usually means the OAuth user is not an admin; not an API problem — ask Ondrej to read "Admin → Access and security" in the Google Ads UI instead.

### Task A4: Key Vault audit, findings, gate

**Files:**
- Create: `~/Work/heblo-marketing-agents/google-ads-spike-findings.md`

**Interfaces:**
- Consumes: Task A3 output.
- Produces: the findings file Task B1 copies into `docs/integrations/google-ads-api.md`.

- [ ] **Step 1: List GoogleAds secret/setting *names* (never values)**

Run:
```bash
for kv in kv-heblo-stg kv-heblo-prod; do echo "== $kv"; az keyvault secret list --vault-name $kv --query "[?starts_with(name, 'GoogleAds')].name" -o tsv; done
for app in heblo heblo-test; do echo "== $app app settings"; az webapp config appsettings list -g rgHeblo -n $app --query "[?starts_with(name, 'GoogleAds')].name" -o tsv; done
```
Expected (current evidence, 2026-10-07): every block empty. If `az` is not logged in, ask Ondrej to run `az login` and retry; do not skip.

- [ ] **Step 2: Write the findings file**

Fill every line from the A3/A4 output (write "not checked" only if a call was impossible, and say why):

```markdown
# Google Ads access spike — findings (YYYY-MM-DD)

- API version used: v25 (REST, googleAds:search)
- OAuth user (Heblo identity): <email>; its role in the account: <ADMIN|STANDARD|READ_ONLY>
- Cloud project access level (Ondrej, Cloud Console): <Test|Explorer|Basic|Standard>
- Customer: id <digits>, currency <CZK>, time zone <Europe/Prague>, manager <false>, test account <false>
- login-customer-id needed: <yes, MCC id …| no>
- Yesterday: <n> campaigns with spend, cost <x> CZK, conversions <y>
- change_event (29 days, LIMIT 1000): <n> rows; client types {…}; user emails {…}; resource types {…}
- 'yyyy-MM-dd HH:mm:ss' bounds accepted by change_event: <yes|no + error>
- customer_user_access: <list role + domain>; Anela-owned ADMIN present: <yes (emails) | no>
- search_term_view + segments.search_term_match_type works: <yes|no>; match types seen {…}
- Entity counts (all statuses): campaigns <n>, ad groups <n>, keywords <n>, ad-group negatives <n>, campaign negatives <n>, ads <n>
- Key Vault kv-heblo-stg GoogleAds* secrets: <none|names>; kv-heblo-prod: <none|names>; App Settings heblo/heblo-test: <none|names>
- Why ImportedMarketingTransactions is empty: (1) no GoogleAds credentials exist in either Key Vault or App Settings, so the billing job could never authenticate; (2) the job is IsEnabled=false in prod; (3) SdkAccountBudgetFetcher calls API V18, which Google has sunset, so it would fail even with credentials; (4) account_budget returns rows only for monthly-invoicing accounts and only budgets approved in the last 7 days.
- Quirks seen: <anything surprising: omitted fields, value formats, auto-apply user_email text>
```

- [ ] **Step 3: Gate**

Continue to Part B **only if** all hold: OAuth OK; `customer`, `campaign_yesterday`, `change_event_30d`, `search_terms_yesterday` OK; customer is not a manager; Ondrej confirms (from the spike's `customer_user_access` rows or from the Google Ads UI) that at least one **Anela-owned** user (not the agency) has ADMIN.

Otherwise STOP and send Ondrej exactly:

> Google Ads spike stopped. Failed check: <name>. Google said: HTTP <code> <error codes> (requestId <id>). Meaning: <the `meaning:` line>. What I need from you: <one of: apply for Explorer/Basic access for Cloud project <id> on the Google Ads API Overview page; enable the Google Ads API in that project; give Google user <email> access to account <id> (Standard); tell me the manager account id; publish the OAuth consent screen and redo the token step; add an Anela-owned admin user to the account (today only <agency users> are admins)>. Nothing was written anywhere.

No commit in Part A.

---
# Part B — Read source PR

### Task B0: Preconditions and branch

**Files:** none changed.

**Interfaces:**
- Consumes: C1 on `origin/main`.
- Produces: branch `feat/google-ads-read-source`.

- [ ] **Step 1: Verify C1 is merged**

Run:
```bash
git fetch origin
git cat-file -e origin/main:backend/src/Anela.Heblo.Application/Features/MarketingAds/Contracts/IAdPlatformReadSource.cs && echo C1-contracts-present
git cat-file -e origin/main:backend/test/Anela.Heblo.MarketingAds.TestKit/Anela.Heblo.MarketingAds.TestKit.csproj && echo testkit-present
```
Expected: both `…-present` lines. If either is missing: STOP and tell Ondrej "Part B waits for core PR C1 (contracts + test kit) to be merged on main."

- [ ] **Step 2: Verify Part A passed**

Run: `test -f ~/Work/heblo-marketing-agents/google-ads-spike-findings.md && echo findings-present`
Expected: `findings-present`, and the file's gate conditions (Task A4 Step 3) all hold. If not, run Part A first.

- [ ] **Step 3: Branch**

Run: `git switch -c feat/google-ads-read-source origin/main`

- [ ] **Step 4: Read what C1 actually shipped**

Run:
```bash
ls backend/src/Anela.Heblo.Application/Features/MarketingAds/Contracts/
cat backend/src/Anela.Heblo.Application/Features/MarketingAds/Contracts/{IAdPlatformReadSource,AdSettingsGuard,AdEntitySnapshot,AdChangeEventRow,AdSearchTermRow,AdDailyFactRow}.cs
ls backend/test/Anela.Heblo.MarketingAds.TestKit/ && cat backend/test/Anela.Heblo.MarketingAds.TestKit/*.csproj backend/test/Anela.Heblo.MarketingAds.TestKit/AdPlatformReadSourceContractTests.cs
ls docs/processes | grep -i -E "ads|marketing"
```
Compare with spec 12.2/12.3 and the code in this plan. Expected extras from C1's plan: `AdPlatformReadSourceContractTests.ChangeEventsSince` (= FixtureDate − 7 days, 00:00 UTC; this plan's change-event fixture is after it) and `AdActionExecutorContractTests.MissingTargetExternalId = "heblo-contract-missing-target"` (this plan's executor treats it as a malformed target → not found / `Failed`). Where they differ (extra abstract members, renamed properties, different xUnit version), follow main, adapt the plan's code, and note it for the PR description. Note the names of C2's process docs if they exist (used in Task B8 `related`).

### Task B1: Integration doc and billing-doc facts

**Files:**
- Create: `docs/integrations/google-ads-api.md`
- Modify: `docs/processes/sync-ad-platform-transactions.md` (Runtime facts, Known quirks)

**Interfaces:**
- Consumes: `~/Work/heblo-marketing-agents/google-ads-spike-findings.md`.
- Produces: the documented API facts every later task relies on (CLAUDE.md: findings are documented before use).

- [ ] **Step 1: Write the integration doc**

Create `docs/integrations/google-ads-api.md` with this content. Section 10 rows are copied from the findings file (they are measured values, so they come from there):

````markdown
# Google Ads API — Integration Findings

> **Living document.** Every new finding about the Google Ads API MUST be added here before code relies on it.
> Reference: https://developers.google.com/google-ads/api/docs/start · Field reference: https://developers.google.com/google-ads/api/fields/v25/overview
> Enum/proto source of truth: https://github.com/googleapis/googleapis/tree/master/google/ads/googleads/v25

## 1. Overview

Two independent code paths in `Anela.Heblo.Adapters.GoogleAds`:

| Path | Transport | Purpose | State |
|---|---|---|---|
| `SdkAccountBudgetFetcher` → `GoogleAdsInvoiceImportJob` | `Google.Ads.GoogleAds` SDK 21.1.0, API **V18** | billing (`account_budget`) | dead: V18 is sunset, no credentials deployed, job disabled (see §11) |
| `GoogleAdsReadSource`, `GoogleAdsActionExecutor` (`Api/`, `Reporting/`, `Execution/`) | REST over `HttpClient`, API **v25** | MarketingAds backbone: entities, daily facts, search terms, change history, v1 actions | this document |

SDK 21.1.0 contains only V16–V18. The current SDK (27.x) supports v22–v25 and no longer has V18, so upgrading it means rewriting the billing fetcher. The marketing code therefore uses REST and leaves the SDK alone.

## 2. Authentication and access

- OAuth 2.0 refresh-token flow. `POST https://oauth2.googleapis.com/token` with `grant_type=refresh_token`, `client_id`, `client_secret`, `refresh_token` → `access_token` (≈ 3 600 s). Heblo caches it until 2 minutes before expiry.
- Scope: `https://www.googleapis.com/auth/adwords`. The refresh token belongs to one Google user; Heblo acts with that user's rights. Least privilege: a dedicated user with **Standard** access to the client account (setting `GoogleAds:HebloUserEmail`).
- The OAuth consent screen must be **In production** or **Internal**. In *Testing* status Google expires refresh tokens after 7 days → `invalid_grant`.
- **Developer tokens were sunset on 2026-09-09.** Access levels now belong to the Google Cloud project that owns the OAuth client (Cloud Console → Google Ads API Overview). The `developer-token` header is optional and ignored by v25; Google plans to reject it in a future major version, so Heblo's REST code does not send it.
- Access levels: Test (test accounts only), **Explorer** (production, 2 880 ops/day), Basic (15 000 ops/day), Standard (unlimited). Heblo needs Explorer or higher (≈ 150 calls/day).
- `login-customer-id` header: only when the user reaches the account through a manager (MCC). Setting `GoogleAds:LoginCustomerId`; empty = header omitted. `GoogleAds:CustomerId` must be the **client** account; the read source refuses a manager account.

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
- `change_date_time` is in the **account time zone** (`customer.time_zone`, Europe/Prague), format `yyyy-MM-dd HH:mm:ss[.ffffff]`. Heblo converts both ways.
- `resource_name` = `customers/{id}/changeEvents/{timestampMicros}~{commandIndex}~{mutateIndex}`; the part after `changeEvents/` is Heblo's `ExternalEventId`.
- `old_resource` / `new_resource` hold only the changed fields of the resource (`{"adGroupAd": {"status": "PAUSED"}}`). An ad-group criterion UPDATE/REMOVE may therefore lack `negative`, and is then reported as level `Keyword` even when it is a negative.
- Google Ads Editor changes are not reported by `change_event`.
- Actor mapping (`client_type` → `AdChangeActorKind`):

| client_type | ActorKind |
|---|---|
| `GOOGLE_ADS_API` and `user_email` = `GoogleAds:HebloUserEmail` (case-insensitive) | `Heblo` |
| `GOOGLE_ADS_RECOMMENDATIONS`, `GOOGLE_ADS_RECOMMENDATIONS_SUBSCRIPTION`, `GOOGLE_ADS_AUTOMATED_RULE`, `INTERNAL_TOOL` | `PlatformAutomation` |
| `GOOGLE_ADS_WEB_CLIENT`, `GOOGLE_ADS_EDITOR`, `GOOGLE_ADS_MOBILE_APP`, `GOOGLE_ADS_BULK_UPLOAD`, `GOOGLE_ADS_SCRIPTS`, other `GOOGLE_ADS_API` | `User` |
| `OTHER`, `UNKNOWN`, `UNSPECIFIED`, missing | `Unknown` |

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

## 10. Spike results (Part A, <date from findings>)

<copy every bullet from ~/Work/heblo-marketing-agents/google-ads-spike-findings.md, replacing personal email addresses of agency staff with "agency user" and keeping Anela addresses>

## 11. Why `ImportedMarketingTransactions` is empty

Copied from the spike: no GoogleAds credentials in kv-heblo-stg, kv-heblo-prod or App Settings; job `google-ads-invoice-import` disabled in prod; the fetcher targets sunset API V18; `account_budget` only covers monthly-invoicing accounts and only budgets approved in the last 7 days. The billing importer is left untouched; spend comes from the MarketingAds backbone.

## 12. Manual smoke procedure (executor)

Added by the executor PR.
````

In section 10 and the date in its heading, insert the measured values from the findings file before committing — the doc must contain no `<…>` markers when committed. Section 12's single line is replaced in Task E5.

- [ ] **Step 2: Add facts to the billing process doc**

In `docs/processes/sync-ad-platform-transactions.md`, append to `## Runtime facts`:

```markdown
- No `GoogleAds*` secrets exist in `kv-heblo-stg` or `kv-heblo-prod`, and no `GoogleAds*` App Settings on `heblo` / `heblo-test`, so the Google billing job could never authenticate — Google Ads access spike (`docs/integrations/google-ads-api.md` §10–11) — <date>.
- Google sunset Ads API developer tokens on 2026-09-09 (access now follows the OAuth client's Cloud project); `GoogleAds:DeveloperToken` is no longer an access gate — Google Ads API docs — 2026-10-07.
```

and replace the Known-quirks bullet that starts with `**Google Ads API version pinned to v18**` with:

```markdown
- **Google Ads API version pinned to v18** (`Services.V18.GoogleAdsService`). V18 is sunset, and SDK 21.1.0 contains nothing newer than V18, so the Google billing import cannot work without rewriting the fetcher. The MarketingAds backbone (`sync-google-ads-campaign-data`) uses REST v25 instead and does not touch this path.
```

Set `verified_at` to the current short SHA: `git rev-parse --short=9 HEAD`.

- [ ] **Step 3: Validate and commit**

Run: `grep -n "<" docs/integrations/google-ads-api.md | grep -v "^.*<category>\|<CODE>\|<bool>" ; python3 scripts/process-docs/check.py check`
Expected: no leftover `<…>` placeholders except the literal `<category>`, `<CODE>`, `<bool>` in §3/§8; checker exits 0 (warnings about other docs are fine).

```bash
git add docs/integrations/google-ads-api.md docs/processes/sync-ad-platform-transactions.md
git commit -m "docs: document Google Ads API access findings

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task B2: Test project, settings, ids, OAuth token provider

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Anela.Heblo.Adapters.GoogleAds.csproj`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsSettings.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Api/GoogleAdsIds.cs`, `Api/GoogleAdsApiException.cs`, `Api/IGoogleAdsAccessTokenProvider.cs`, `Api/GoogleAdsOAuthTokenProvider.cs`
- Create: `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Anela.Heblo.Adapters.GoogleAds.Tests.csproj`
- Create: `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Support/{TestSettings,TestOptionsMonitor,StubHttpMessageHandler,StubHttpClientFactory}.cs`
- Test: `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Api/GoogleAdsOAuthTokenProviderTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `GoogleAdsSettings.LoginCustomerId : string`, `.ApiVersion : string = "v25"`, `.HebloUserEmail : string`
  - `internal static class GoogleAdsIds { string NormalizeCustomerId(string?); bool IsNumericId(string?); string RequireNumericId(string?, string paramName); bool TryParseComposite(string?, out string first, out string second); }`
  - `internal sealed class GoogleAdsApiException : Exception { HttpStatusCode? StatusCode; string? ErrorCode; string? RequestId; bool IsTransient; }`
  - `internal interface IGoogleAdsAccessTokenProvider { Task<string> GetAccessTokenAsync(CancellationToken ct); }`
  - `internal sealed class GoogleAdsOAuthTokenProvider` with `const string HttpClientName = "GoogleAdsOAuth"`
  - test support: `TestSettings.Create(Action<GoogleAdsSettings>?)`, `TestSettings.CustomerId = "1234567890"`, `TestSettings.Now`, `TestOptionsMonitor<T>`, `StubHttpMessageHandler`, `RecordedRequest`, `StubHttpClientFactory`

- [ ] **Step 1: Adapter csproj**

In `Anela.Heblo.Adapters.GoogleAds.csproj`, add to the package `ItemGroup`:

```xml
    <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.0" />
    <PackageReference Include="Polly" Version="8.4.1" />
```

and to the `InternalsVisibleTo` `ItemGroup`:

```xml
    <InternalsVisibleTo Include="Anela.Heblo.Adapters.GoogleAds.Tests" />
```

- [ ] **Step 2: Settings**

Append inside `GoogleAdsSettings` (after `OAuth2RefreshToken`):

```csharp
    /// <summary>
    /// Manager (MCC) customer id sent as the <c>login-customer-id</c> header when Heblo's Google user
    /// reaches the account through a manager. Empty = direct access, header omitted. Used by the
    /// MarketingAds read source and executor only; the billing fetcher keeps its own behaviour.
    /// </summary>
    public string LoginCustomerId { get; set; } = string.Empty;

    /// <summary>Google Ads REST API major version for the MarketingAds read source and executor.</summary>
    public string ApiVersion { get; set; } = "v25";

    /// <summary>
    /// Email of the Google user whose refresh token Heblo uses. change_event rows that this user made
    /// through the API are attributed to Heblo rather than to a person.
    /// </summary>
    public string HebloUserEmail { get; set; } = string.Empty;
```

- [ ] **Step 3: Ids and exception**

`Api/GoogleAdsIds.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Anela.Heblo.Adapters.GoogleAds.Api;

/// <summary>
/// Google Ads ids are numeric. Every id that is interpolated into a URL or a GAQL query goes
/// through here first, so agent-supplied text can never reach either.
/// </summary>
internal static class GoogleAdsIds
{
    private static readonly Regex NumericId = new("^[0-9]{1,20}$", RegexOptions.CultureInvariant);

    public static string NormalizeCustomerId(string? value) =>
        (value ?? string.Empty).Replace("-", string.Empty, StringComparison.Ordinal).Trim();

    public static bool IsNumericId(string? value) => value is not null && NumericId.IsMatch(value);

    public static string RequireNumericId(string? value, string paramName) =>
        IsNumericId(value)
            ? value!
            : throw new ArgumentException($"'{value}' is not a numeric Google Ads id.", paramName);

    /// <summary>Splits "221~441" into its two numeric halves; false for anything else.</summary>
    public static bool TryParseComposite(string? value, out string first, out string second)
    {
        first = string.Empty;
        second = string.Empty;
        var parts = (value ?? string.Empty).Split('~');
        if (parts.Length != 2 || !IsNumericId(parts[0]) || !IsNumericId(parts[1]))
            return false;

        first = parts[0];
        second = parts[1];
        return true;
    }
}
```

`Api/GoogleAdsApiException.cs`:

```csharp
using System.Net;

namespace Anela.Heblo.Adapters.GoogleAds.Api;

/// <summary>
/// A transport, authentication or authorization failure talking to Google Ads. The message never
/// contains credentials. <see cref="IsTransient"/> drives the retry policy.
/// </summary>
internal sealed class GoogleAdsApiException : Exception
{
    public GoogleAdsApiException(
        string message, HttpStatusCode? statusCode, string? errorCode, string? requestId, bool isTransient)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        RequestId = requestId;
        IsTransient = isTransient;
    }

    public HttpStatusCode? StatusCode { get; }
    public string? ErrorCode { get; }
    public string? RequestId { get; }
    public bool IsTransient { get; }
}
```

- [ ] **Step 4: Test project and support classes**

`backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Anela.Heblo.Adapters.GoogleAds.Tests.csproj`. The test kit depends on `xunit.extensibility.core` 2.9.2, so this project **must** use `xunit` 2.9.2 and `xunit.runner.visualstudio` 2.8.2 (`xunit` 2.5.3 pins its core exactly and fails restore with NU1605); if the TestKit csproj on main pins other versions, match it:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net8.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <IsPackable>false</IsPackable>
        <IsTestProject>true</IsTestProject>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="coverlet.collector" Version="6.0.0" />
        <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
        <PackageReference Include="xunit" Version="2.9.2" />
        <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
        <PackageReference Include="FluentAssertions" Version="6.12.0" />
        <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="8.10.0" />
    </ItemGroup>

    <ItemGroup>
        <Using Include="Xunit" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\..\src\Adapters\Anela.Heblo.Adapters.GoogleAds\Anela.Heblo.Adapters.GoogleAds.csproj" />
        <ProjectReference Include="..\Anela.Heblo.MarketingAds.TestKit\Anela.Heblo.MarketingAds.TestKit.csproj" />
    </ItemGroup>

    <ItemGroup>
        <None Update="Fixtures\**\*.json" CopyToOutputDirectory="PreserveNewest" />
    </ItemGroup>

</Project>
```

Run: `dotnet sln Anela.Heblo.sln add --in-root backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Anela.Heblo.Adapters.GoogleAds.Tests.csproj`
Expected: `Project … added to the solution.`

`Support/TestSettings.cs`:

```csharp
using Anela.Heblo.Adapters.GoogleAds;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Support;

internal static class TestSettings
{
    public const string CustomerId = "1234567890";
    public const string OAuthClientId = "client-id.apps.googleusercontent.com";
    public const string OAuthClientSecret = "client-secret-value";
    public const string RefreshToken = "refresh-token-value";
    public const string HebloUserEmail = "heblo-ads@anela.cz";
    public static readonly DateTimeOffset Now = new(2026, 10, 7, 6, 0, 0, TimeSpan.Zero);

    public static GoogleAdsSettings Create(Action<GoogleAdsSettings>? configure = null)
    {
        var settings = new GoogleAdsSettings
        {
            CustomerId = "123-456-7890",
            OAuth2ClientId = OAuthClientId,
            OAuth2ClientSecret = OAuthClientSecret,
            OAuth2RefreshToken = RefreshToken,
            ApiVersion = "v25",
            HebloUserEmail = HebloUserEmail,
        };
        configure?.Invoke(settings);
        return settings;
    }
}
```

`Support/TestOptionsMonitor.cs`:

```csharp
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Support;

internal sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
{
    public TestOptionsMonitor(T value) => CurrentValue = value;

    public T CurrentValue { get; set; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
```

`Support/StubHttpMessageHandler.cs`:

```csharp
using System.Net;
using System.Text;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Support;

internal sealed record RecordedRequest(
    HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string? Body);

/// <summary>Replays queued responses in order and records every request it saw.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<RecordedRequest> Requests { get; } = new();

    public StubHttpMessageHandler Enqueue(HttpStatusCode status, string body) =>
        Enqueue(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });

    public StubHttpMessageHandler Enqueue(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _responses.Enqueue(respond);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        var headers = request.Headers.ToDictionary(
            h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, headers, body));

        if (_responses.Count == 0)
            throw new InvalidOperationException($"No stubbed response left for {request.RequestUri}.");
        return _responses.Dequeue()(request);
    }
}
```

`Support/StubHttpClientFactory.cs`:

```csharp
namespace Anela.Heblo.Adapters.GoogleAds.Tests.Support;

internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly Dictionary<string, HttpMessageHandler> _handlers = new();

    public StubHttpClientFactory With(string name, HttpMessageHandler handler)
    {
        _handlers[name] = handler;
        return this;
    }

    public HttpClient CreateClient(string name) =>
        _handlers.TryGetValue(name, out var handler)
            ? new HttpClient(handler, disposeHandler: false)
            : throw new InvalidOperationException($"No stub handler for client '{name}'.");
}
```

- [ ] **Step 5: Write the failing token-provider tests**

`Api/GoogleAdsOAuthTokenProviderTests.cs`:

```csharp
using System.Net;
using System.Web;
using Anela.Heblo.Adapters.GoogleAds;
using Anela.Heblo.Adapters.GoogleAds.Api;
using Anela.Heblo.Adapters.GoogleAds.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Api;

public sealed class GoogleAdsOAuthTokenProviderTests
{
    private const string FirstToken = """{"access_token":"ya29.first","expires_in":3599,"token_type":"Bearer"}""";
    private const string SecondToken = """{"access_token":"ya29.second","expires_in":3599,"token_type":"Bearer"}""";

    [Fact]
    public async Task exchanges_the_refresh_token_once_and_reuses_the_access_token()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, FirstToken);
        var provider = Create(handler, new FakeTimeProvider(TestSettings.Now), TestSettings.Create());

        var first = await provider.GetAccessTokenAsync(CancellationToken.None);
        var second = await provider.GetAccessTokenAsync(CancellationToken.None);

        first.Should().Be("ya29.first");
        second.Should().Be("ya29.first");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Uri.Should().Be(new Uri("https://oauth2.googleapis.com/token"));
        var form = HttpUtility.ParseQueryString(handler.Requests[0].Body!);
        form["grant_type"].Should().Be("refresh_token");
        form["refresh_token"].Should().Be(TestSettings.RefreshToken);
        form["client_id"].Should().Be(TestSettings.OAuthClientId);
        form["client_secret"].Should().Be(TestSettings.OAuthClientSecret);
    }

    [Fact]
    public async Task refreshes_two_minutes_before_the_access_token_expires()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, FirstToken)
            .Enqueue(HttpStatusCode.OK, SecondToken);
        var time = new FakeTimeProvider(TestSettings.Now);
        var provider = Create(handler, time, TestSettings.Create());

        await provider.GetAccessTokenAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(3599) - TimeSpan.FromMinutes(2));
        var refreshed = await provider.GetAccessTokenAsync(CancellationToken.None);

        refreshed.Should().Be("ya29.second");
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task fetches_a_new_token_when_the_refresh_token_setting_changes()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, FirstToken)
            .Enqueue(HttpStatusCode.OK, SecondToken);
        var monitor = new TestOptionsMonitor<GoogleAdsSettings>(TestSettings.Create());
        var provider = new GoogleAdsOAuthTokenProvider(
            new StubHttpClientFactory().With(GoogleAdsOAuthTokenProvider.HttpClientName, handler),
            monitor, new FakeTimeProvider(TestSettings.Now));

        await provider.GetAccessTokenAsync(CancellationToken.None);
        monitor.CurrentValue = TestSettings.Create(s => s.OAuth2RefreshToken = "rotated-refresh-token");
        var token = await provider.GetAccessTokenAsync(CancellationToken.None);

        token.Should().Be("ya29.second");
        HttpUtility.ParseQueryString(handler.Requests[1].Body!)["refresh_token"].Should().Be("rotated-refresh-token");
    }

    [Fact]
    public async Task invalid_grant_throws_a_non_transient_error_that_does_not_leak_secrets()
    {
        var handler = new StubHttpMessageHandler().Enqueue(
            HttpStatusCode.BadRequest,
            """{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""");
        var provider = Create(handler, new FakeTimeProvider(TestSettings.Now), TestSettings.Create());

        var act = () => provider.GetAccessTokenAsync(CancellationToken.None);

        var error = (await act.Should().ThrowAsync<GoogleAdsApiException>()).Which;
        error.ErrorCode.Should().Be("oauth.invalid_grant");
        error.IsTransient.Should().BeFalse();
        error.Message.Should().NotContain(TestSettings.RefreshToken).And.NotContain(TestSettings.OAuthClientSecret);
    }

    [Fact]
    public async Task a_5xx_from_the_token_endpoint_is_transient()
    {
        var handler = new StubHttpMessageHandler().Enqueue(HttpStatusCode.ServiceUnavailable, "{}");
        var provider = Create(handler, new FakeTimeProvider(TestSettings.Now), TestSettings.Create());

        var act = () => provider.GetAccessTokenAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<GoogleAdsApiException>()).Which.IsTransient.Should().BeTrue();
    }

    private static GoogleAdsOAuthTokenProvider Create(
        StubHttpMessageHandler handler, TimeProvider time, GoogleAdsSettings settings) =>
        new(new StubHttpClientFactory().With(GoogleAdsOAuthTokenProvider.HttpClientName, handler),
            new TestOptionsMonitor<GoogleAdsSettings>(settings), time);
}
```

- [ ] **Step 6: Run to verify failure**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.GoogleAds.Tests -p:UseSharedCompilation=false`
Expected: FAIL — `CS0246: The type or namespace name 'GoogleAdsOAuthTokenProvider' could not be found`.

- [ ] **Step 7: Implement the token provider**

`Api/IGoogleAdsAccessTokenProvider.cs`:

```csharp
namespace Anela.Heblo.Adapters.GoogleAds.Api;

internal interface IGoogleAdsAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken ct);
}
```

`Api/GoogleAdsOAuthTokenProvider.cs`:

```csharp
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.GoogleAds.Api;

/// <summary>
/// Exchanges the configured refresh token for an access token and caches it until shortly before it
/// expires. A settings change (rotated refresh token in Key Vault) invalidates the cache.
/// </summary>
internal sealed class GoogleAdsOAuthTokenProvider : IGoogleAdsAccessTokenProvider, IDisposable
{
    internal const string HttpClientName = "GoogleAdsOAuth";
    private const int DefaultLifetimeSeconds = 3600;
    private static readonly Uri TokenEndpoint = new("https://oauth2.googleapis.com/token");
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<GoogleAdsSettings> _settings;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CachedToken? _cached;

    public GoogleAdsOAuthTokenProvider(
        IHttpClientFactory httpClientFactory, IOptionsMonitor<GoogleAdsSettings> settings, TimeProvider timeProvider)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _timeProvider = timeProvider;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        var settings = _settings.CurrentValue;
        var fingerprint = Fingerprint(settings);
        if (IsUsable(_cached, fingerprint))
            return _cached!.Value;

        await _gate.WaitAsync(ct);
        try
        {
            if (!IsUsable(_cached, fingerprint))
                _cached = await RefreshAsync(settings, fingerprint, ct);
            return _cached!.Value;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private bool IsUsable(CachedToken? token, string fingerprint) =>
        token is not null
        && token.Fingerprint == fingerprint
        && _timeProvider.GetUtcNow() < token.ExpiresAt - RefreshMargin;

    private async Task<CachedToken> RefreshAsync(GoogleAdsSettings settings, string fingerprint, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = settings.OAuth2ClientId,
            ["client_secret"] = settings.OAuth2ClientSecret,
            ["refresh_token"] = settings.OAuth2RefreshToken,
        });
        using var response = await _httpClientFactory.CreateClient(HttpClientName).PostAsync(TokenEndpoint, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw Failure(response.StatusCode, body);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var token = root.TryGetProperty("access_token", out var t) ? t.GetString() : null;
        if (string.IsNullOrEmpty(token))
            throw new GoogleAdsApiException(
                "Google OAuth token response had no access_token.", response.StatusCode, "oauth.no_access_token", null, isTransient: false);

        var lifetime = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds)
            ? seconds
            : DefaultLifetimeSeconds;
        return new CachedToken(token, _timeProvider.GetUtcNow().AddSeconds(lifetime), fingerprint);
    }

    private static GoogleAdsApiException Failure(HttpStatusCode status, string body)
    {
        var code = ReadOAuthErrorCode(body) ?? "unknown";
        return new GoogleAdsApiException(
            $"Google OAuth token refresh failed: HTTP {(int)status}, error '{code}'. 'invalid_grant' means the refresh " +
            "token was revoked or expired (a consent screen left in Testing status expires it after 7 days).",
            status, $"oauth.{code}", null, isTransient: (int)status >= 500);
    }

    private static string? ReadOAuthErrorCode(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("error", out var error)
                   && error.ValueKind == JsonValueKind.String
                ? error.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Hashed so the cache key never holds the secret in clear text.
    private static string Fingerprint(GoogleAdsSettings s) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{s.OAuth2ClientId}|{s.OAuth2ClientSecret}|{s.OAuth2RefreshToken}")));

    private sealed record CachedToken(string Value, DateTimeOffset ExpiresAt, string Fingerprint);
}
```

- [ ] **Step 8: Run to verify pass**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Adapters.GoogleAds.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.GoogleAds.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~GoogleAdsOAuthTokenProviderTests"
```
Expected: `Passed!  - Failed: 0, Passed: 5`.

- [ ] **Step 9: Format and commit**

```bash
dotnet format Anela.Heblo.sln --include backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/ backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/
git add Anela.Heblo.sln backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds backend/test/Anela.Heblo.Adapters.GoogleAds.Tests
git commit -m "feat: add Google Ads OAuth token provider and test project

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task B3: REST search client

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Api/GoogleAdsQuery.cs`, `Api/IGoogleAdsApiClient.cs`, `Api/GoogleAdsErrorParser.cs`, `Api/GoogleAdsRestClient.cs`
- Create: `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Support/StaticAccessTokenProvider.cs`
- Test: `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Api/GoogleAdsRestClientTests.cs`, `Api/GoogleAdsErrorParserTests.cs`

**Interfaces:**
- Consumes: `GoogleAdsIds`, `GoogleAdsApiException`, `IGoogleAdsAccessTokenProvider`, `GoogleAdsSettings` (Task B2).
- Produces:
  - `internal sealed record GoogleAdsQuery(string Name, string Gaql)`
  - `internal interface IGoogleAdsApiClient { Task<IReadOnlyList<JsonElement>> SearchAsync(string customerId, GoogleAdsQuery query, CancellationToken ct); }` (Part C adds `MutateAsync`)
  - `internal sealed record GoogleAdsError(string? ErrorCode, string Message, string? RequestId)`; `GoogleAdsErrorParser.Parse(string body) : GoogleAdsError`
  - `internal sealed class GoogleAdsRestClient : IGoogleAdsApiClient` — `const string HttpClientName = "GoogleAdsApi"`, public DI ctor `(IHttpClientFactory, IGoogleAdsAccessTokenProvider, IOptionsMonitor<GoogleAdsSettings>, ILogger<GoogleAdsRestClient>)`, internal test ctor adding `TimeSpan retryDelay`; `internal static GoogleAdsApiException ToException(HttpStatusCode, string body, string operation)`; `internal static bool IsTransient(Exception?, CancellationToken)`

- [ ] **Step 1: Write the failing error-parser tests**

`Api/GoogleAdsErrorParserTests.cs`:

```csharp
using Anela.Heblo.Adapters.GoogleAds.Api;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Api;

public sealed class GoogleAdsErrorParserTests
{
    [Fact]
    public void reads_the_first_google_ads_failure_as_category_and_code()
    {
        const string body = """
            {"error":{"code":403,"message":"The caller does not have permission","status":"PERMISSION_DENIED",
              "details":[{"@type":"type.googleapis.com/google.ads.googleads.v25.errors.GoogleAdsFailure",
                "errors":[{"errorCode":{"authorizationError":"CLOUD_PROJECT_NOT_APPROVED_FOR_PRODUCTION"},
                           "message":"The Google Cloud project is only approved for use with test accounts."}],
                "requestId":"req-123"}]}}
            """;

        var error = GoogleAdsErrorParser.Parse(body);

        error.ErrorCode.Should().Be("authorizationError.CLOUD_PROJECT_NOT_APPROVED_FOR_PRODUCTION");
        error.Message.Should().Be("The Google Cloud project is only approved for use with test accounts.");
        error.RequestId.Should().Be("req-123");
    }

    [Fact]
    public void falls_back_to_the_error_info_reason_when_there_is_no_google_ads_failure()
    {
        const string body = """
            {"error":{"code":403,"message":"Google Ads API has not been used in project 42","status":"PERMISSION_DENIED",
              "details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"SERVICE_DISABLED"}]}}
            """;

        GoogleAdsErrorParser.Parse(body).ErrorCode.Should().Be("errorInfo.SERVICE_DISABLED");
    }

    [Fact]
    public void falls_back_to_the_status_and_survives_non_json()
    {
        GoogleAdsErrorParser.Parse("""{"error":{"code":503,"message":"busy","status":"UNAVAILABLE"}}""")
            .ErrorCode.Should().Be("status.UNAVAILABLE");

        var html = GoogleAdsErrorParser.Parse("<html>Bad Gateway</html>");
        html.ErrorCode.Should().BeNull();
        html.Message.Should().Be("<html>Bad Gateway</html>");
    }
}
```

- [ ] **Step 2: Write the failing client tests**

`Support/StaticAccessTokenProvider.cs`:

```csharp
using Anela.Heblo.Adapters.GoogleAds.Api;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Support;

internal sealed class StaticAccessTokenProvider : IGoogleAdsAccessTokenProvider
{
    public const string Token = "test-access-token";

    public Task<string> GetAccessTokenAsync(CancellationToken ct) => Task.FromResult(Token);
}
```

`Api/GoogleAdsRestClientTests.cs`:

```csharp
using System.Net;
using System.Text.Json.Nodes;
using Anela.Heblo.Adapters.GoogleAds;
using Anela.Heblo.Adapters.GoogleAds.Api;
using Anela.Heblo.Adapters.GoogleAds.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Api;

public sealed class GoogleAdsRestClientTests
{
    private static readonly GoogleAdsQuery Query = new("campaigns", "SELECT campaign.id FROM campaign");
    private const string OneRow = """{"results":[{"campaign":{"resourceName":"customers/1234567890/campaigns/111","id":"111"}}]}""";
    private const string Forbidden = """
        {"error":{"code":403,"message":"denied","status":"PERMISSION_DENIED","details":[{
          "@type":"type.googleapis.com/google.ads.googleads.v25.errors.GoogleAdsFailure",
          "errors":[{"errorCode":{"authorizationError":"USER_PERMISSION_DENIED"},"message":"User doesn't have permission."}],
          "requestId":"r-1"}]}}
        """;

    [Fact]
    public async Task posts_the_query_to_the_versioned_search_endpoint_with_bearer_and_login_header()
    {
        var (client, handler) = Create(s => s.LoginCustomerId = "111-222-3333");
        handler.Enqueue(HttpStatusCode.OK, OneRow);

        var rows = await client.SearchAsync(TestSettings.CustomerId, Query, CancellationToken.None);

        rows.Should().ContainSingle();
        rows[0].GetProperty("campaign").GetProperty("id").GetString().Should().Be("111");
        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.Should().Be(new Uri("https://googleads.googleapis.com/v25/customers/1234567890/googleAds:search"));
        request.Headers["Authorization"].Should().Be($"Bearer {StaticAccessTokenProvider.Token}");
        request.Headers["login-customer-id"].Should().Be("1112223333");
        request.Headers.Should().NotContainKey("developer-token");
        JsonNode.Parse(request.Body!)!["query"]!.GetValue<string>().Should().Be(Query.Gaql);
    }

    [Fact]
    public async Task omits_the_login_customer_id_header_for_direct_access()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, OneRow);

        await client.SearchAsync(TestSettings.CustomerId, Query, CancellationToken.None);

        handler.Requests.Single().Headers.Should().NotContainKey("login-customer-id");
    }

    [Fact]
    public async Task follows_next_page_tokens_and_concatenates_the_pages()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """{"results":[{"campaign":{"id":"1"}}],"nextPageToken":"p2"}""")
               .Enqueue(HttpStatusCode.OK, """{"results":[{"campaign":{"id":"2"}}]}""");

        var rows = await client.SearchAsync(TestSettings.CustomerId, Query, CancellationToken.None);

        rows.Select(r => r.GetProperty("campaign").GetProperty("id").GetString()).Should().Equal("1", "2");
        JsonNode.Parse(handler.Requests[0].Body!)!["pageToken"].Should().BeNull();
        JsonNode.Parse(handler.Requests[1].Body!)!["pageToken"]!.GetValue<string>().Should().Be("p2");
    }

    [Fact]
    public async Task an_empty_result_has_no_results_key_and_yields_no_rows()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """{"fieldMask":"campaign.id"}""");

        var rows = await client.SearchAsync(TestSettings.CustomerId, Query, CancellationToken.None);

        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task retries_a_503_and_then_succeeds()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, """{"error":{"code":503,"status":"UNAVAILABLE","message":"busy"}}""")
               .Enqueue(HttpStatusCode.OK, OneRow);

        var rows = await client.SearchAsync(TestSettings.CustomerId, Query, CancellationToken.None);

        rows.Should().ContainSingle();
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task does_not_retry_a_403_and_surfaces_the_google_error_code()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.Forbidden, Forbidden);

        var act = () => client.SearchAsync(TestSettings.CustomerId, Query, CancellationToken.None);

        var error = (await act.Should().ThrowAsync<GoogleAdsApiException>()).Which;
        error.ErrorCode.Should().Be("authorizationError.USER_PERMISSION_DENIED");
        error.RequestId.Should().Be("r-1");
        error.IsTransient.Should().BeFalse();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task retries_an_http_client_timeout()
    {
        var (client, handler) = Create();
        handler.Enqueue(_ => throw new TaskCanceledException("The request was canceled due to HttpClient.Timeout"))
               .Enqueue(HttpStatusCode.OK, OneRow);

        var rows = await client.SearchAsync(TestSettings.CustomerId, Query, CancellationToken.None);

        rows.Should().ContainSingle();
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task does_not_retry_when_the_caller_cancels()
    {
        var (client, handler) = Create();
        using var cts = new CancellationTokenSource();
        handler.Enqueue(_ =>
        {
            cts.Cancel();
            throw new TaskCanceledException();
        });

        var act = () => client.SearchAsync(TestSettings.CustomerId, Query, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task rejects_a_non_numeric_customer_id_without_calling_google()
    {
        var (client, handler) = Create();

        var act = () => client.SearchAsync("123/../../evil", Query, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        handler.Requests.Should().BeEmpty();
    }

    internal static (GoogleAdsRestClient Client, StubHttpMessageHandler Handler) Create(
        Action<GoogleAdsSettings>? configure = null)
    {
        var handler = new StubHttpMessageHandler();
        var client = new GoogleAdsRestClient(
            new StubHttpClientFactory().With(GoogleAdsRestClient.HttpClientName, handler),
            new StaticAccessTokenProvider(),
            new TestOptionsMonitor<GoogleAdsSettings>(TestSettings.Create(configure)),
            NullLogger<GoogleAdsRestClient>.Instance,
            retryDelay: TimeSpan.Zero);
        return (client, handler);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.GoogleAds.Tests -p:UseSharedCompilation=false`
Expected: FAIL — `CS0246` for `GoogleAdsQuery`, `GoogleAdsErrorParser`, `GoogleAdsRestClient`.

- [ ] **Step 4: Implement**

`Api/GoogleAdsQuery.cs`:

```csharp
namespace Anela.Heblo.Adapters.GoogleAds.Api;

/// <summary>A GAQL query plus a stable name used in logs and to route test fixtures.</summary>
internal sealed record GoogleAdsQuery(string Name, string Gaql);
```

`Api/IGoogleAdsApiClient.cs`:

```csharp
using System.Text.Json;

namespace Anela.Heblo.Adapters.GoogleAds.Api;

/// <summary>
/// The only seam between Heblo and Google Ads. Tests replace it with recorded JSON fixtures
/// (read side) or a stateful fake (write side).
/// </summary>
internal interface IGoogleAdsApiClient
{
    /// <summary>
    /// Runs a GAQL query over every page and returns the <c>results</c> rows as detached elements.
    /// Throws <see cref="GoogleAdsApiException"/> on any non-2xx after retrying transient failures.
    /// </summary>
    Task<IReadOnlyList<JsonElement>> SearchAsync(string customerId, GoogleAdsQuery query, CancellationToken ct);
}
```

`Api/GoogleAdsErrorParser.cs`:

```csharp
using System.Text.Json;

namespace Anela.Heblo.Adapters.GoogleAds.Api;

internal sealed record GoogleAdsError(string? ErrorCode, string Message, string? RequestId);

/// <summary>
/// Reads Google's error envelope {"error":{"code","message","status","details":[...]}}. The first
/// GoogleAdsFailure error wins ("authorizationError.USER_PERMISSION_DENIED"); an ErrorInfo reason
/// ("errorInfo.SERVICE_DISABLED") is the fallback, then the gRPC status ("status.UNAVAILABLE").
/// </summary>
internal static class GoogleAdsErrorParser
{
    private const int MaxRawLength = 300;

    public static GoogleAdsError Parse(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("error", out var error)
                   && error.ValueKind == JsonValueKind.Object
                ? FromEnvelope(error)
                : new GoogleAdsError(null, Truncate(body), null);
        }
        catch (JsonException)
        {
            return new GoogleAdsError(null, Truncate(body), null);
        }
    }

    private static GoogleAdsError FromEnvelope(JsonElement error)
    {
        var message = StringProperty(error, "message") ?? string.Empty;
        var details = error.TryGetProperty("details", out var d) && d.ValueKind == JsonValueKind.Array
            ? d.EnumerateArray().ToList()
            : new List<JsonElement>();

        var requestId = details.Select(x => StringProperty(x, "requestId")).FirstOrDefault(x => x is not null);
        var failure = details.SelectMany(FailureErrors).FirstOrDefault();
        if (failure.ValueKind == JsonValueKind.Object)
            return new GoogleAdsError(FailureCode(failure), StringProperty(failure, "message") ?? message, requestId);

        var reason = details.Select(x => StringProperty(x, "reason")).FirstOrDefault(x => x is not null);
        var status = StringProperty(error, "status");
        var code = reason is not null ? $"errorInfo.{reason}" : status is not null ? $"status.{status}" : null;
        return new GoogleAdsError(code, message, requestId);
    }

    private static IEnumerable<JsonElement> FailureErrors(JsonElement detail) =>
        detail.ValueKind == JsonValueKind.Object
        && detail.TryGetProperty("errors", out var errors)
        && errors.ValueKind == JsonValueKind.Array
            ? errors.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

    private static string? FailureCode(JsonElement failure)
    {
        if (!failure.TryGetProperty("errorCode", out var errorCode) || errorCode.ValueKind != JsonValueKind.Object)
            return null;
        var first = errorCode.EnumerateObject().FirstOrDefault();
        return first.Value.ValueKind == JsonValueKind.String ? $"{first.Name}.{first.Value.GetString()}" : null;
    }

    private static string? StringProperty(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Truncate(string body) => body.Length <= MaxRawLength ? body : body[..MaxRawLength];
}
```

`Api/GoogleAdsRestClient.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Anela.Heblo.Adapters.GoogleAds.Api;

/// <summary>Google Ads REST transport: auth headers, paging, error parsing and transient retries.</summary>
internal sealed class GoogleAdsRestClient : IGoogleAdsApiClient
{
    internal const string HttpClientName = "GoogleAdsApi";
    internal static readonly Uri BaseAddress = new("https://googleads.googleapis.com/");
    private const int MaxPages = 100; // 10 000 rows per page; guards against an endless nextPageToken loop
    private const int MaxRetryAttempts = 3;
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(2);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGoogleAdsAccessTokenProvider _tokens;
    private readonly IOptionsMonitor<GoogleAdsSettings> _settings;
    private readonly ILogger<GoogleAdsRestClient> _logger;
    private readonly ResiliencePipeline _searchPipeline;

    public GoogleAdsRestClient(
        IHttpClientFactory httpClientFactory,
        IGoogleAdsAccessTokenProvider tokens,
        IOptionsMonitor<GoogleAdsSettings> settings,
        ILogger<GoogleAdsRestClient> logger)
        : this(httpClientFactory, tokens, settings, logger, DefaultRetryDelay)
    {
    }

    internal GoogleAdsRestClient(
        IHttpClientFactory httpClientFactory,
        IGoogleAdsAccessTokenProvider tokens,
        IOptionsMonitor<GoogleAdsSettings> settings,
        ILogger<GoogleAdsRestClient> logger,
        TimeSpan retryDelay)
    {
        _httpClientFactory = httpClientFactory;
        _tokens = tokens;
        _settings = settings;
        _logger = logger;
        _searchPipeline = BuildSearchPipeline(retryDelay);
    }

    public async Task<IReadOnlyList<JsonElement>> SearchAsync(string customerId, GoogleAdsQuery query, CancellationToken ct)
    {
        GoogleAdsIds.RequireNumericId(customerId, nameof(customerId));
        var rows = new List<JsonElement>();
        string? pageToken = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var body = new JsonObject { ["query"] = query.Gaql };
            if (pageToken is not null)
                body["pageToken"] = pageToken;

            using var document = await _searchPipeline.ExecuteAsync(
                async innerCt => await PostAsync($"customers/{customerId}/googleAds:search", body, query.Name, innerCt), ct);
            rows.AddRange(ReadResults(document.RootElement));

            pageToken = document.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
            if (string.IsNullOrEmpty(pageToken))
            {
                _logger.LogDebug("GoogleAds: query {Query} returned {Rows} rows", query.Name, rows.Count);
                return rows;
            }
        }

        throw new GoogleAdsApiException(
            $"Google Ads query '{query.Name}' exceeded {MaxPages} pages.", null, "client.too_many_pages", null, isTransient: false);
    }

    internal static GoogleAdsApiException ToException(HttpStatusCode status, string body, string operation)
    {
        var error = GoogleAdsErrorParser.Parse(body);
        var code = (int)status;
        var isTransient = status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || code >= 500;
        return new GoogleAdsApiException(
            $"Google Ads {operation} failed: HTTP {code} {error.ErrorCode ?? "(no error code)"}: {error.Message}",
            status, error.ErrorCode, error.RequestId, isTransient);
    }

    /// <summary>
    /// Only failures worth repeating: Google's transient statuses, transport faults without an HTTP
    /// status, and HttpClient timeouts. TaskCanceledException is an OperationCanceledException, so the
    /// caller's own cancellation is told apart by its token, not by the exception type.
    /// </summary>
    internal static bool IsTransient(Exception? exception, CancellationToken ct) => exception switch
    {
        GoogleAdsApiException api => api.IsTransient,
        HttpRequestException http => http.StatusCode is null
                                     || http.StatusCode == HttpStatusCode.RequestTimeout
                                     || (int)http.StatusCode >= 500,
        OperationCanceledException => !ct.IsCancellationRequested,
        _ => false,
    };

    private static ResiliencePipeline BuildSearchPipeline(TimeSpan delay) =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = MaxRetryAttempts,
                Delay = delay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = delay > TimeSpan.Zero,
                ShouldHandle = args => ValueTask.FromResult(
                    IsTransient(args.Outcome.Exception, args.Context.CancellationToken)),
            })
            .Build();

    private static IEnumerable<JsonElement> ReadResults(JsonElement root) =>
        root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array
            ? results.EnumerateArray().Select(r => r.Clone()).ToList()
            : Enumerable.Empty<JsonElement>();

    private async Task<JsonDocument> PostAsync(string path, JsonObject body, string operation, CancellationToken ct)
    {
        using var request = await CreateRequestAsync(path, body, ct);
        using var response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (response.IsSuccessStatusCode)
            return JsonDocument.Parse(text);

        var error = ToException(response.StatusCode, text, operation);
        _logger.LogWarning(
            "GoogleAds: {Operation} failed with HTTP {Status} {ErrorCode} (requestId {RequestId})",
            operation, (int)response.StatusCode, error.ErrorCode, error.RequestId);
        throw error;
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(string path, JsonObject body, CancellationToken ct)
    {
        var settings = _settings.CurrentValue;
        var accessToken = await _tokens.GetAccessTokenAsync(ct);
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseAddress, $"{settings.ApiVersion}/{path}"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var loginCustomerId = GoogleAdsIds.NormalizeCustomerId(settings.LoginCustomerId);
        if (loginCustomerId.Length > 0)
            request.Headers.TryAddWithoutValidation("login-customer-id", loginCustomerId);
        return request;
    }
}
```

- [ ] **Step 5: Run to verify pass**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Adapters.GoogleAds.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.GoogleAds.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~Api"
```
Expected: `Passed!  - Failed: 0, Passed: 17` (5 token + 3 parser + 9 client).

- [ ] **Step 6: Format and commit**

```bash
dotnet format Anela.Heblo.sln --include backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/ backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/
git add backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds backend/test/Anela.Heblo.Adapters.GoogleAds.Tests
git commit -m "feat: add Google Ads REST search client with transient-only retries

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task B4: JSON reading and value mappings

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Reporting/GoogleAdsJson.cs`, `Reporting/GoogleAdsMappings.cs`
- Test: `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Reporting/GoogleAdsJsonTests.cs`, `Reporting/GoogleAdsMappingsTests.cs`

**Interfaces:**
- Consumes: contract enums from C1.
- Produces:
  - `GoogleAdsJson`: `JsonElement? Find(JsonElement, params string[])`, `string? String(...)`, `string RequiredString(...)`, `long Int64(...)` (missing → 0), `decimal Decimal(...)` (missing → 0), `decimal Micros(...)`, `bool Bool(...)`, `string? Raw(...)`
  - `GoogleAdsMappings`: `AdEntityStatus Status(string?)`, `KeywordMatchType? KeywordMatch(string?)`, `KeywordMatchType? SearchTermMatch(string?)`, `string ToApiMatchType(KeywordMatchType)`, `AdChangeActorKind ActorKind(string? clientType, string? userEmail, string? hebloUserEmail)`, `(AdEntityLevel? Level, string? ExternalId) EntityRef(string? resourceName, bool isNegativeCriterion)`, `string NegativeKeywordId(string collection, string compositeId)`, constants `CampaignCriteriaCollection = "campaignCriteria"`, `AdGroupCriteriaCollection = "adGroupCriteria"`

- [ ] **Step 1: Write the failing tests**

`Reporting/GoogleAdsJsonTests.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Adapters.GoogleAds.Reporting;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Reporting;

public sealed class GoogleAdsJsonTests
{
    private static readonly JsonElement Row = JsonDocument.Parse("""
        {"campaign":{"id":"111","name":"Brand"},
         "metrics":{"impressions":"1200","costMicros":"432100000","conversions":4.5,"conversionsValue":1.5E3},
         "customer":{"manager":true}}
        """).RootElement;

    [Fact]
    public void reads_int64_strings_doubles_and_micros()
    {
        GoogleAdsJson.Int64(Row, "metrics", "impressions").Should().Be(1200);
        GoogleAdsJson.Micros(Row, "metrics", "costMicros").Should().Be(432.1m);
        GoogleAdsJson.Decimal(Row, "metrics", "conversions").Should().Be(4.5m);
        GoogleAdsJson.Decimal(Row, "metrics", "conversionsValue").Should().Be(1500m);
    }

    [Fact]
    public void treats_omitted_numbers_as_zero_and_omitted_booleans_as_false()
    {
        GoogleAdsJson.Int64(Row, "metrics", "clicks").Should().Be(0);
        GoogleAdsJson.Micros(Row, "metrics", "missingMicros").Should().Be(0m);
        GoogleAdsJson.Bool(Row, "campaign", "manager").Should().BeFalse();
        GoogleAdsJson.Bool(Row, "customer", "manager").Should().BeTrue();
    }

    [Fact]
    public void required_string_names_the_missing_path()
    {
        var act = () => GoogleAdsJson.RequiredString(Row, "adGroup", "id");

        act.Should().Throw<InvalidOperationException>().WithMessage("*adGroup.id*");
    }
}
```

`Reporting/GoogleAdsMappingsTests.cs`:

```csharp
using Anela.Heblo.Adapters.GoogleAds.Reporting;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Reporting;

public sealed class GoogleAdsMappingsTests
{
    private const string Heblo = "heblo-ads@anela.cz";

    [Theory]
    [InlineData("ENABLED", AdEntityStatus.Enabled)]
    [InlineData("PAUSED", AdEntityStatus.Paused)]
    [InlineData("REMOVED", AdEntityStatus.Removed)]
    [InlineData("UNKNOWN", AdEntityStatus.Unknown)]
    [InlineData(null, AdEntityStatus.Unknown)]
    public void maps_statuses(string? api, AdEntityStatus expected) =>
        GoogleAdsMappings.Status(api).Should().Be(expected);

    [Theory]
    [InlineData("EXACT", KeywordMatchType.Exact)]
    [InlineData("PHRASE", KeywordMatchType.Phrase)]
    [InlineData("BROAD", KeywordMatchType.Broad)]
    [InlineData("NEAR_EXACT", null)]
    [InlineData(null, null)]
    public void maps_keyword_match_types(string? api, KeywordMatchType? expected) =>
        GoogleAdsMappings.KeywordMatch(api).Should().Be(expected);

    [Theory]
    [InlineData("EXACT", KeywordMatchType.Exact)]
    [InlineData("NEAR_EXACT", KeywordMatchType.Exact)]
    [InlineData("PHRASE", KeywordMatchType.Phrase)]
    [InlineData("NEAR_PHRASE", KeywordMatchType.Phrase)]
    [InlineData("BROAD", KeywordMatchType.Broad)]
    [InlineData("AI_MAX", null)]
    [InlineData("PERFORMANCE_MAX", null)]
    [InlineData(null, null)]
    public void maps_search_term_match_types(string? api, KeywordMatchType? expected) =>
        GoogleAdsMappings.SearchTermMatch(api).Should().Be(expected);

    [Theory]
    [InlineData("GOOGLE_ADS_RECOMMENDATIONS", null, AdChangeActorKind.PlatformAutomation)]
    [InlineData("GOOGLE_ADS_RECOMMENDATIONS_SUBSCRIPTION", null, AdChangeActorKind.PlatformAutomation)]
    [InlineData("GOOGLE_ADS_AUTOMATED_RULE", "owner@anela.cz", AdChangeActorKind.PlatformAutomation)]
    [InlineData("INTERNAL_TOOL", null, AdChangeActorKind.PlatformAutomation)]
    [InlineData("GOOGLE_ADS_WEB_CLIENT", "specialist@agency.example", AdChangeActorKind.User)]
    [InlineData("GOOGLE_ADS_EDITOR", "specialist@agency.example", AdChangeActorKind.User)]
    [InlineData("GOOGLE_ADS_MOBILE_APP", "owner@anela.cz", AdChangeActorKind.User)]
    [InlineData("GOOGLE_ADS_BULK_UPLOAD", "owner@anela.cz", AdChangeActorKind.User)]
    [InlineData("GOOGLE_ADS_SCRIPTS", "specialist@agency.example", AdChangeActorKind.User)]
    [InlineData("GOOGLE_ADS_API", "heblo-ads@anela.cz", AdChangeActorKind.Heblo)]
    [InlineData("GOOGLE_ADS_API", "HEBLO-ADS@anela.cz", AdChangeActorKind.Heblo)]
    [InlineData("GOOGLE_ADS_API", "tool@agency.example", AdChangeActorKind.User)]
    [InlineData("GOOGLE_ADS_WEB_CLIENT", "heblo-ads@anela.cz", AdChangeActorKind.User)]
    [InlineData("OTHER", "x@example.com", AdChangeActorKind.Unknown)]
    [InlineData("UNSPECIFIED", null, AdChangeActorKind.Unknown)]
    [InlineData(null, null, AdChangeActorKind.Unknown)]
    public void maps_client_types_to_actor_kinds(string? clientType, string? email, AdChangeActorKind expected) =>
        GoogleAdsMappings.ActorKind(clientType, email, Heblo).Should().Be(expected);

    [Fact]
    public void an_api_change_is_never_heblo_when_no_heblo_user_is_configured() =>
        GoogleAdsMappings.ActorKind("GOOGLE_ADS_API", "", "").Should().Be(AdChangeActorKind.User);

    [Theory]
    [InlineData("customers/1/campaigns/111", false, AdEntityLevel.Campaign, "111")]
    [InlineData("customers/1/adGroups/221", false, AdEntityLevel.AdGroup, "221")]
    [InlineData("customers/1/adGroupAds/221~441", false, AdEntityLevel.Ad, "221~441")]
    [InlineData("customers/1/adGroupCriteria/221~331", false, AdEntityLevel.Keyword, "221~331")]
    [InlineData("customers/1/adGroupCriteria/221~341", true, AdEntityLevel.NegativeKeyword, "adGroupCriteria/221~341")]
    [InlineData("customers/1/campaignCriteria/111~351", false, AdEntityLevel.NegativeKeyword, "campaignCriteria/111~351")]
    public void maps_resource_names_to_entity_refs(string resource, bool negative, AdEntityLevel level, string id) =>
        GoogleAdsMappings.EntityRef(resource, negative).Should().Be(((AdEntityLevel?)level, id));

    [Theory]
    [InlineData("customers/1/campaignBudgets/901")]
    [InlineData("not a resource")]
    [InlineData(null)]
    public void unknown_resources_have_no_entity_ref(string? resource) =>
        GoogleAdsMappings.EntityRef(resource, false).Should().Be(((AdEntityLevel?)null, (string?)null));
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.GoogleAds.Tests -p:UseSharedCompilation=false`
Expected: FAIL — `CS0246` for `GoogleAdsJson` / `GoogleAdsMappings`.

- [ ] **Step 3: Implement**

`Reporting/GoogleAdsJson.cs`:

```csharp
using System.Globalization;
using System.Text.Json;

namespace Anela.Heblo.Adapters.GoogleAds.Reporting;

/// <summary>
/// Reads Google Ads REST rows (proto3 JSON): lowerCamelCase names, int64 values as strings, and
/// fields at their default value (0, false, "") omitted entirely — so a missing metric means zero.
/// </summary>
internal static class GoogleAdsJson
{
    private const decimal MicrosPerUnit = 1_000_000m;

    public static JsonElement? Find(JsonElement row, params string[] path)
    {
        var current = row;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
                return null;
            current = next;
        }
        return current;
    }

    public static string? String(JsonElement row, params string[] path) =>
        Find(row, path) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    public static string RequiredString(JsonElement row, params string[] path) =>
        String(row, path)
        ?? throw new InvalidOperationException($"Google Ads row is missing '{string.Join('.', path)}'.");

    public static long Int64(JsonElement row, params string[] path) => Find(row, path) switch
    {
        { ValueKind: JsonValueKind.String } s => long.Parse(s.GetString()!, NumberStyles.Integer, CultureInfo.InvariantCulture),
        { ValueKind: JsonValueKind.Number } n => n.GetInt64(),
        _ => 0,
    };

    public static decimal Decimal(JsonElement row, params string[] path) => Find(row, path) switch
    {
        { ValueKind: JsonValueKind.String } s => decimal.Parse(s.GetString()!, NumberStyles.Float, CultureInfo.InvariantCulture),
        { ValueKind: JsonValueKind.Number } n => decimal.Parse(n.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture),
        _ => 0m,
    };

    public static decimal Micros(JsonElement row, params string[] path) => Int64(row, path) / MicrosPerUnit;

    public static bool Bool(JsonElement row, params string[] path) =>
        Find(row, path) is { ValueKind: JsonValueKind.True };

    public static string? Raw(JsonElement row, params string[] path) => Find(row, path)?.GetRawText();
}
```

`Reporting/GoogleAdsMappings.cs`:

```csharp
using System.Text.RegularExpressions;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.GoogleAds.Reporting;

internal static class GoogleAdsMappings
{
    public const string CampaignCriteriaCollection = "campaignCriteria";
    public const string AdGroupCriteriaCollection = "adGroupCriteria";
    private const string ApiClientType = "GOOGLE_ADS_API";

    private static readonly Regex ResourceName = new(
        @"^customers/\d+/(?<collection>[A-Za-z]+)/(?<id>\d+(~\d+)?)$", RegexOptions.CultureInvariant);

    public static AdEntityStatus Status(string? apiStatus) => apiStatus switch
    {
        "ENABLED" => AdEntityStatus.Enabled,
        "PAUSED" => AdEntityStatus.Paused,
        "REMOVED" => AdEntityStatus.Removed,
        _ => AdEntityStatus.Unknown,
    };

    public static KeywordMatchType? KeywordMatch(string? apiMatchType) => apiMatchType switch
    {
        "EXACT" => KeywordMatchType.Exact,
        "PHRASE" => KeywordMatchType.Phrase,
        "BROAD" => KeywordMatchType.Broad,
        _ => null,
    };

    /// <summary>NEAR_* are Google's close variants; AI_MAX / PERFORMANCE_MAX have no keyword match type.</summary>
    public static KeywordMatchType? SearchTermMatch(string? apiMatchType) => apiMatchType switch
    {
        "EXACT" or "NEAR_EXACT" => KeywordMatchType.Exact,
        "PHRASE" or "NEAR_PHRASE" => KeywordMatchType.Phrase,
        "BROAD" => KeywordMatchType.Broad,
        _ => null,
    };

    public static string ToApiMatchType(KeywordMatchType matchType) => matchType switch
    {
        KeywordMatchType.Exact => "EXACT",
        KeywordMatchType.Phrase => "PHRASE",
        KeywordMatchType.Broad => "BROAD",
        _ => throw new ArgumentOutOfRangeException(nameof(matchType), matchType, "Unknown keyword match type."),
    };

    /// <summary>
    /// Heblo = an API change by Heblo's own Google user. Recommendations, automated rules and Google's
    /// internal tools are the platform acting on its own. Everything a person drives (UI, Editor,
    /// scripts, bulk uploads, other API tools) is a User change.
    /// </summary>
    public static AdChangeActorKind ActorKind(string? clientType, string? userEmail, string? hebloUserEmail)
    {
        if (clientType == ApiClientType
            && !string.IsNullOrWhiteSpace(hebloUserEmail)
            && string.Equals(userEmail, hebloUserEmail, StringComparison.OrdinalIgnoreCase))
            return AdChangeActorKind.Heblo;

        return clientType switch
        {
            "GOOGLE_ADS_RECOMMENDATIONS" or "GOOGLE_ADS_RECOMMENDATIONS_SUBSCRIPTION"
                or "GOOGLE_ADS_AUTOMATED_RULE" or "INTERNAL_TOOL" => AdChangeActorKind.PlatformAutomation,
            "GOOGLE_ADS_WEB_CLIENT" or "GOOGLE_ADS_EDITOR" or "GOOGLE_ADS_MOBILE_APP"
                or "GOOGLE_ADS_BULK_UPLOAD" or "GOOGLE_ADS_SCRIPTS" or ApiClientType => AdChangeActorKind.User,
            _ => AdChangeActorKind.Unknown,
        };
    }

    /// <summary>Maps a change_event resource name to the entity level and Heblo external id (integration doc §6).</summary>
    public static (AdEntityLevel? Level, string? ExternalId) EntityRef(string? resourceName, bool isNegativeCriterion)
    {
        var match = ResourceName.Match(resourceName ?? string.Empty);
        if (!match.Success)
            return (null, null);

        var collection = match.Groups["collection"].Value;
        var id = match.Groups["id"].Value;
        return collection switch
        {
            "campaigns" => (AdEntityLevel.Campaign, id),
            "adGroups" => (AdEntityLevel.AdGroup, id),
            "adGroupAds" => (AdEntityLevel.Ad, id),
            AdGroupCriteriaCollection when isNegativeCriterion => (AdEntityLevel.NegativeKeyword, NegativeKeywordId(collection, id)),
            AdGroupCriteriaCollection => (AdEntityLevel.Keyword, id),
            CampaignCriteriaCollection => (AdEntityLevel.NegativeKeyword, NegativeKeywordId(collection, id)),
            _ => (null, null),
        };
    }

    public static string NegativeKeywordId(string collection, string compositeId) => $"{collection}/{compositeId}";
}
```

- [ ] **Step 4: Run to verify pass**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Adapters.GoogleAds.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.GoogleAds.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~GoogleAdsJsonTests|FullyQualifiedName~GoogleAdsMappingsTests"
```
Expected: all pass (`Failed: 0`).

- [ ] **Step 5: Format and commit**

```bash
dotnet format Anela.Heblo.sln --include backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/ backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/
git add backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds backend/test/Anela.Heblo.Adapters.GoogleAds.Tests
git commit -m "feat: map Google Ads JSON values, statuses and change actors

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task B5: Read source — accounts and entities

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Reporting/GoogleAdsQueries.cs`, `Reporting/GoogleAdsCustomer.cs`, `Reporting/GoogleAdsEntityMapper.cs`, `Reporting/GoogleAdsReadSource.cs`
- Create: `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Support/FixtureGoogleAdsApiClient.cs`, `Support/ReadSourceHarness.cs`
- Create fixtures: `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Fixtures/ReadSource/{customer,campaigns,ad_groups,keywords,ad_group_negative_keywords,campaign_negative_keywords,ads}.json`
- Test: `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Reporting/GoogleAdsReadSourceEntitiesTests.cs`

**Interfaces:**
- Consumes: `IGoogleAdsApiClient`, `GoogleAdsQuery`, `GoogleAdsIds` (B2/B3), `GoogleAdsJson`, `GoogleAdsMappings` (B4); C1 contracts.
- Produces:
  - `internal sealed class GoogleAdsReadSource : IAdPlatformReadSource`, ctor `(IGoogleAdsApiClient api, IOptionsMonitor<GoogleAdsSettings> settings, TimeProvider timeProvider, ILogger<GoogleAdsReadSource> logger)`; `GetAccountsAsync`, `GetEntitiesAsync` implemented here; `GetDailyFactsAsync`, `GetSearchTermsAsync`, `GetChangeEventsAsync` throw `NotImplementedException` until B6/B7.
  - `GoogleAdsQueries` static members `Customer`, `Campaigns`, `AdGroups`, `Keywords`, `AdGroupNegativeKeywords`, `CampaignNegativeKeywords`, `Ads` (query names = fixture file names)
  - `internal sealed record GoogleAdsCustomer(string Id, string Name, string Currency, string TimeZoneId, TimeZoneInfo TimeZone)`
  - Test support: `FixtureGoogleAdsApiClient` (`Calls`, `WithOverride(string name, string json)`), `ReadSourceHarness.Create(IGoogleAdsApiClient, Action<GoogleAdsSettings>?) : (GoogleAdsReadSource Source, FakeTimeProvider Time)`, `ReadSourceHarness.FixtureDate = 2026-10-06`

Fixture data is synthetic, shaped exactly like real `googleAds:search` responses (integration doc §5). Ids: customer `1234567890`; campaigns `111` (ENABLED), `112` (PAUSED); ad groups `221` (in 111), `222` (in 112); keywords `221~331`, `221~332`; ad-group negative `221~341`; campaign negative `111~351`; ads `221~441`, `222~442`.

- [ ] **Step 1: Write the fixtures**

`Fixtures/ReadSource/customer.json` (note: `manager:false` is *absent*, as Google omits defaults):

```json
{
  "results": [
    {
      "customer": {
        "resourceName": "customers/1234567890",
        "id": "1234567890",
        "descriptiveName": "Anela (fixture)",
        "currencyCode": "CZK",
        "timeZone": "Europe/Prague"
      }
    }
  ],
  "fieldMask": "customer.id,customer.descriptiveName,customer.currencyCode,customer.timeZone,customer.manager"
}
```

`Fixtures/ReadSource/campaigns.json`:

```json
{
  "results": [
    {
      "campaign": { "resourceName": "customers/1234567890/campaigns/111", "id": "111", "name": "Brand CZ", "status": "ENABLED", "advertisingChannelType": "SEARCH", "biddingStrategyType": "MAXIMIZE_CONVERSIONS" },
      "campaignBudget": { "resourceName": "customers/1234567890/campaignBudgets/901", "amountMicros": "500000000" }
    },
    {
      "campaign": { "resourceName": "customers/1234567890/campaigns/112", "id": "112", "name": "Generic CZ", "status": "PAUSED", "advertisingChannelType": "SEARCH", "biddingStrategyType": "MANUAL_CPC" },
      "campaignBudget": { "resourceName": "customers/1234567890/campaignBudgets/902", "amountMicros": "150000000" }
    }
  ]
}
```

`Fixtures/ReadSource/ad_groups.json`:

```json
{
  "results": [
    {
      "campaign": { "resourceName": "customers/1234567890/campaigns/111", "id": "111" },
      "adGroup": { "resourceName": "customers/1234567890/adGroups/221", "id": "221", "name": "Brand exact", "status": "ENABLED", "type": "SEARCH_STANDARD" }
    },
    {
      "campaign": { "resourceName": "customers/1234567890/campaigns/112", "id": "112" },
      "adGroup": { "resourceName": "customers/1234567890/adGroups/222", "id": "222", "name": "Generic kosmetika", "status": "PAUSED", "type": "SEARCH_STANDARD" }
    }
  ]
}
```

`Fixtures/ReadSource/keywords.json`:

```json
{
  "results": [
    {
      "adGroup": { "resourceName": "customers/1234567890/adGroups/221", "id": "221" },
      "adGroupCriterion": { "resourceName": "customers/1234567890/adGroupCriteria/221~331", "criterionId": "331", "status": "ENABLED", "keyword": { "text": "anela", "matchType": "EXACT" } }
    },
    {
      "adGroup": { "resourceName": "customers/1234567890/adGroups/221", "id": "221" },
      "adGroupCriterion": { "resourceName": "customers/1234567890/adGroupCriteria/221~332", "criterionId": "332", "status": "PAUSED", "keyword": { "text": "anela kosmetika", "matchType": "PHRASE" } }
    }
  ]
}
```

`Fixtures/ReadSource/ad_group_negative_keywords.json`:

```json
{
  "results": [
    {
      "adGroup": { "resourceName": "customers/1234567890/adGroups/221", "id": "221" },
      "adGroupCriterion": { "resourceName": "customers/1234567890/adGroupCriteria/221~341", "criterionId": "341", "status": "ENABLED", "negative": true, "keyword": { "text": "zdarma", "matchType": "BROAD" } }
    }
  ]
}
```

`Fixtures/ReadSource/campaign_negative_keywords.json`:

```json
{
  "results": [
    {
      "campaign": { "resourceName": "customers/1234567890/campaigns/111", "id": "111" },
      "campaignCriterion": { "resourceName": "customers/1234567890/campaignCriteria/111~351", "criterionId": "351", "status": "ENABLED", "negative": true, "keyword": { "text": "návod", "matchType": "PHRASE" } }
    }
  ]
}
```

`Fixtures/ReadSource/ads.json` (the first ad has no name, like most responsive search ads):

```json
{
  "results": [
    {
      "adGroup": { "resourceName": "customers/1234567890/adGroups/221", "id": "221" },
      "adGroupAd": { "resourceName": "customers/1234567890/adGroupAds/221~441", "status": "ENABLED", "ad": { "resourceName": "customers/1234567890/ads/441", "id": "441", "type": "RESPONSIVE_SEARCH_AD" } }
    },
    {
      "adGroup": { "resourceName": "customers/1234567890/adGroups/222", "id": "222" },
      "adGroupAd": { "resourceName": "customers/1234567890/adGroupAds/222~442", "status": "PAUSED", "ad": { "resourceName": "customers/1234567890/ads/442", "id": "442", "name": "Podzimní akce", "type": "RESPONSIVE_SEARCH_AD" } }
    }
  ]
}
```

- [ ] **Step 2: Test support**

`Support/FixtureGoogleAdsApiClient.cs`:

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;
using Anela.Heblo.Adapters.GoogleAds.Api;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Support;

/// <summary>
/// Answers a search from Fixtures/ReadSource/{query.Name}.json. Like Google, it returns only rows
/// whose segments.date matches the date the query filtered on.
/// </summary>
internal sealed class FixtureGoogleAdsApiClient : IGoogleAdsApiClient
{
    private static readonly Regex DateFilter = new(@"segments\.date = '(\d{4}-\d{2}-\d{2})'");
    private readonly string _directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ReadSource");
    private readonly Dictionary<string, string> _overrides = new();

    public List<(string CustomerId, GoogleAdsQuery Query)> Calls { get; } = new();

    public FixtureGoogleAdsApiClient WithOverride(string queryName, string json)
    {
        _overrides[queryName] = json;
        return this;
    }

    public Task<IReadOnlyList<JsonElement>> SearchAsync(string customerId, GoogleAdsQuery query, CancellationToken ct)
    {
        Calls.Add((customerId, query));
        var json = _overrides.TryGetValue(query.Name, out var overridden) ? overridden : ReadFixture(query.Name);
        using var document = JsonDocument.Parse(json);
        var rows = document.RootElement.TryGetProperty("results", out var results)
            ? results.EnumerateArray().Select(r => r.Clone()).ToList()
            : new List<JsonElement>();

        var date = DateFilter.Match(query.Gaql);
        if (date.Success)
            rows = rows.Where(r => r.GetProperty("segments").GetProperty("date").GetString() == date.Groups[1].Value).ToList();
        return Task.FromResult<IReadOnlyList<JsonElement>>(rows);
    }

    private string ReadFixture(string name)
    {
        var path = Path.Combine(_directory, name + ".json");
        return File.Exists(path)
            ? File.ReadAllText(path)
            : throw new FileNotFoundException($"No fixture for query '{name}'.", path);
    }
}
```

`Support/ReadSourceHarness.cs`:

```csharp
using Anela.Heblo.Adapters.GoogleAds;
using Anela.Heblo.Adapters.GoogleAds.Api;
using Anela.Heblo.Adapters.GoogleAds.Reporting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Support;

internal static class ReadSourceHarness
{
    public static readonly DateOnly FixtureDate = new(2026, 10, 6);

    public static (GoogleAdsReadSource Source, FakeTimeProvider Time) Create(
        IGoogleAdsApiClient api, Action<GoogleAdsSettings>? configure = null)
    {
        var time = new FakeTimeProvider(TestSettings.Now);
        var source = new GoogleAdsReadSource(
            api,
            new TestOptionsMonitor<GoogleAdsSettings>(TestSettings.Create(configure)),
            time,
            NullLogger<GoogleAdsReadSource>.Instance);
        return (source, time);
    }
}
```

- [ ] **Step 3: Write the failing tests**

`Reporting/GoogleAdsReadSourceEntitiesTests.cs`:

```csharp
using Anela.Heblo.Adapters.GoogleAds.Tests.Support;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Reporting;

public sealed class GoogleAdsReadSourceEntitiesTests
{
    [Fact]
    public async Task returns_the_configured_customer_as_the_only_account()
    {
        var (source, _) = ReadSourceHarness.Create(new FixtureGoogleAdsApiClient());

        var accounts = await source.GetAccountsAsync(CancellationToken.None);

        accounts.Should().Equal(new AdAccountSnapshot("1234567890", "Anela (fixture)", "CZK", "Europe/Prague"));
    }

    [Fact]
    public async Task refuses_a_manager_account_with_an_explanation()
    {
        var api = new FixtureGoogleAdsApiClient().WithOverride("customer", """
            {"results":[{"customer":{"id":"1234567890","currencyCode":"CZK","timeZone":"Europe/Prague","manager":true}}]}
            """);
        var (source, _) = ReadSourceHarness.Create(api);

        var act = () => source.GetAccountsAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*manager account*LoginCustomerId*");
    }

    [Fact]
    public async Task maps_every_level_with_its_parent()
    {
        var (source, _) = ReadSourceHarness.Create(new FixtureGoogleAdsApiClient());

        var entities = await source.GetEntitiesAsync(TestSettings.CustomerId, CancellationToken.None);

        entities.Select(e => (e.Level, e.ExternalId, e.ParentLevel, e.ParentExternalId, e.Status)).Should().BeEquivalentTo(new[]
        {
            (AdEntityLevel.Campaign, "111", (AdEntityLevel?)null, (string?)null, AdEntityStatus.Enabled),
            (AdEntityLevel.Campaign, "112", (AdEntityLevel?)null, (string?)null, AdEntityStatus.Paused),
            (AdEntityLevel.AdGroup, "221", (AdEntityLevel?)AdEntityLevel.Campaign, (string?)"111", AdEntityStatus.Enabled),
            (AdEntityLevel.AdGroup, "222", (AdEntityLevel?)AdEntityLevel.Campaign, (string?)"112", AdEntityStatus.Paused),
            (AdEntityLevel.Keyword, "221~331", (AdEntityLevel?)AdEntityLevel.AdGroup, (string?)"221", AdEntityStatus.Enabled),
            (AdEntityLevel.Keyword, "221~332", (AdEntityLevel?)AdEntityLevel.AdGroup, (string?)"221", AdEntityStatus.Paused),
            (AdEntityLevel.NegativeKeyword, "adGroupCriteria/221~341", (AdEntityLevel?)AdEntityLevel.AdGroup, (string?)"221", AdEntityStatus.Enabled),
            (AdEntityLevel.NegativeKeyword, "campaignCriteria/111~351", (AdEntityLevel?)AdEntityLevel.Campaign, (string?)"111", AdEntityStatus.Enabled),
            (AdEntityLevel.Ad, "221~441", (AdEntityLevel?)AdEntityLevel.AdGroup, (string?)"221", AdEntityStatus.Enabled),
            (AdEntityLevel.Ad, "222~442", (AdEntityLevel?)AdEntityLevel.AdGroup, (string?)"222", AdEntityStatus.Paused),
        });
    }

    [Fact]
    public async Task keeps_keyword_text_match_type_budget_and_ad_name_fallback()
    {
        var (source, _) = ReadSourceHarness.Create(new FixtureGoogleAdsApiClient());

        var entities = await source.GetEntitiesAsync(TestSettings.CustomerId, CancellationToken.None);

        var negative = entities.Single(e => e.ExternalId == "campaignCriteria/111~351");
        negative.Name.Should().Be("návod");
        negative.Attributes[AdActionPayloadKeys.Text].Should().Be("návod");
        negative.Attributes[AdActionPayloadKeys.MatchType].Should().Be(nameof(KeywordMatchType.Phrase));

        var brand = entities.Single(e => e.Level == AdEntityLevel.Campaign && e.ExternalId == "111");
        decimal.Parse(brand.Attributes["budgetAmount"]!, System.Globalization.CultureInfo.InvariantCulture).Should().Be(500m);
        brand.Attributes["advertisingChannelType"].Should().Be("SEARCH");

        entities.Single(e => e.ExternalId == "221~441").Name.Should().Be("RESPONSIVE_SEARCH_AD 441");
        entities.Single(e => e.ExternalId == "222~442").Name.Should().Be("Podzimní akce");
    }

    [Fact]
    public async Task sends_every_query_to_the_configured_customer()
    {
        var api = new FixtureGoogleAdsApiClient();
        var (source, _) = ReadSourceHarness.Create(api);

        await source.GetEntitiesAsync("123-456-7890", CancellationToken.None);

        api.Calls.Should().OnlyContain(c => c.CustomerId == TestSettings.CustomerId);
        api.Calls.Select(c => c.Query.Name).Should().Equal(
            "campaigns", "ad_groups", "keywords", "ad_group_negative_keywords", "campaign_negative_keywords", "ads");
    }

    [Fact]
    public async Task refuses_an_account_that_is_not_configured()
    {
        var api = new FixtureGoogleAdsApiClient();
        var (source, _) = ReadSourceHarness.Create(api);

        var act = () => source.GetEntitiesAsync("9999999999", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        api.Calls.Should().BeEmpty();
    }
}
```

- [ ] **Step 4: Run to verify failure**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.GoogleAds.Tests -p:UseSharedCompilation=false`
Expected: FAIL — `CS0246` for `GoogleAdsReadSource`.

- [ ] **Step 5: Implement**

`Reporting/GoogleAdsCustomer.cs`:

```csharp
namespace Anela.Heblo.Adapters.GoogleAds.Reporting;

internal sealed record GoogleAdsCustomer(string Id, string Name, string Currency, string TimeZoneId, TimeZoneInfo TimeZone);
```

`Reporting/GoogleAdsQueries.cs` (B6/B7 append facts, search-term and change-event queries to this class):

```csharp
using Anela.Heblo.Adapters.GoogleAds.Api;

namespace Anela.Heblo.Adapters.GoogleAds.Reporting;

/// <summary>
/// GAQL for the read source. Query names double as fixture file names. Entity queries do not filter
/// REMOVED: facts in the lookback window can belong to an entity removed since.
/// </summary>
internal static class GoogleAdsQueries
{
    public static readonly GoogleAdsQuery Customer = new("customer",
        "SELECT customer.id, customer.descriptive_name, customer.currency_code, customer.time_zone, customer.manager " +
        "FROM customer LIMIT 1");

    public static readonly GoogleAdsQuery Campaigns = new("campaigns",
        "SELECT campaign.id, campaign.name, campaign.status, campaign.advertising_channel_type, " +
        "campaign.bidding_strategy_type, campaign_budget.amount_micros FROM campaign");

    public static readonly GoogleAdsQuery AdGroups = new("ad_groups",
        "SELECT campaign.id, ad_group.id, ad_group.name, ad_group.status, ad_group.type FROM ad_group");

    public static readonly GoogleAdsQuery Keywords = new("keywords",
        "SELECT ad_group.id, ad_group_criterion.criterion_id, ad_group_criterion.status, " +
        "ad_group_criterion.keyword.text, ad_group_criterion.keyword.match_type FROM ad_group_criterion " +
        "WHERE ad_group_criterion.type = 'KEYWORD' AND ad_group_criterion.negative = FALSE");

    public static readonly GoogleAdsQuery AdGroupNegativeKeywords = new("ad_group_negative_keywords",
        "SELECT ad_group.id, ad_group_criterion.criterion_id, ad_group_criterion.status, " +
        "ad_group_criterion.keyword.text, ad_group_criterion.keyword.match_type FROM ad_group_criterion " +
        "WHERE ad_group_criterion.type = 'KEYWORD' AND ad_group_criterion.negative = TRUE");

    public static readonly GoogleAdsQuery CampaignNegativeKeywords = new("campaign_negative_keywords",
        "SELECT campaign.id, campaign_criterion.criterion_id, campaign_criterion.status, " +
        "campaign_criterion.keyword.text, campaign_criterion.keyword.match_type FROM campaign_criterion " +
        "WHERE campaign_criterion.type = 'KEYWORD' AND campaign_criterion.negative = TRUE");

    public static readonly GoogleAdsQuery Ads = new("ads",
        "SELECT ad_group.id, ad_group_ad.ad.id, ad_group_ad.ad.name, ad_group_ad.ad.type, ad_group_ad.status " +
        "FROM ad_group_ad");
}
```

`Reporting/GoogleAdsEntityMapper.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.GoogleAds.Reporting;

/// <summary>Maps entity query rows to snapshots using the external-id conventions of integration doc §6.</summary>
internal static class GoogleAdsEntityMapper
{
    public static AdEntitySnapshot Campaign(JsonElement row) => new(
        AdEntityLevel.Campaign,
        GoogleAdsJson.RequiredString(row, "campaign", "id"),
        null,
        null,
        GoogleAdsJson.String(row, "campaign", "name") ?? string.Empty,
        GoogleAdsMappings.Status(GoogleAdsJson.String(row, "campaign", "status")),
        new Dictionary<string, string?>
        {
            ["advertisingChannelType"] = GoogleAdsJson.String(row, "campaign", "advertisingChannelType"),
            ["biddingStrategyType"] = GoogleAdsJson.String(row, "campaign", "biddingStrategyType"),
            ["budgetAmount"] = GoogleAdsJson.Find(row, "campaignBudget", "amountMicros") is null
                ? null
                : GoogleAdsJson.Micros(row, "campaignBudget", "amountMicros").ToString(CultureInfo.InvariantCulture),
        });

    public static AdEntitySnapshot AdGroup(JsonElement row) => new(
        AdEntityLevel.AdGroup,
        GoogleAdsJson.RequiredString(row, "adGroup", "id"),
        AdEntityLevel.Campaign,
        GoogleAdsJson.RequiredString(row, "campaign", "id"),
        GoogleAdsJson.String(row, "adGroup", "name") ?? string.Empty,
        GoogleAdsMappings.Status(GoogleAdsJson.String(row, "adGroup", "status")),
        new Dictionary<string, string?> { ["adGroupType"] = GoogleAdsJson.String(row, "adGroup", "type") });

    public static AdEntitySnapshot Keyword(JsonElement row)
    {
        var adGroupId = GoogleAdsJson.RequiredString(row, "adGroup", "id");
        return Criterion(row, "adGroupCriterion", AdEntityLevel.Keyword,
            $"{adGroupId}~{GoogleAdsJson.RequiredString(row, "adGroupCriterion", "criterionId")}",
            AdEntityLevel.AdGroup, adGroupId);
    }

    public static AdEntitySnapshot AdGroupNegativeKeyword(JsonElement row)
    {
        var adGroupId = GoogleAdsJson.RequiredString(row, "adGroup", "id");
        var composite = $"{adGroupId}~{GoogleAdsJson.RequiredString(row, "adGroupCriterion", "criterionId")}";
        return Criterion(row, "adGroupCriterion", AdEntityLevel.NegativeKeyword,
            GoogleAdsMappings.NegativeKeywordId(GoogleAdsMappings.AdGroupCriteriaCollection, composite),
            AdEntityLevel.AdGroup, adGroupId);
    }

    public static AdEntitySnapshot CampaignNegativeKeyword(JsonElement row)
    {
        var campaignId = GoogleAdsJson.RequiredString(row, "campaign", "id");
        var composite = $"{campaignId}~{GoogleAdsJson.RequiredString(row, "campaignCriterion", "criterionId")}";
        return Criterion(row, "campaignCriterion", AdEntityLevel.NegativeKeyword,
            GoogleAdsMappings.NegativeKeywordId(GoogleAdsMappings.CampaignCriteriaCollection, composite),
            AdEntityLevel.Campaign, campaignId);
    }

    public static AdEntitySnapshot Ad(JsonElement row)
    {
        var adGroupId = GoogleAdsJson.RequiredString(row, "adGroup", "id");
        var adId = GoogleAdsJson.RequiredString(row, "adGroupAd", "ad", "id");
        var adType = GoogleAdsJson.String(row, "adGroupAd", "ad", "type");
        var name = GoogleAdsJson.String(row, "adGroupAd", "ad", "name");
        return new AdEntitySnapshot(
            AdEntityLevel.Ad,
            $"{adGroupId}~{adId}",
            AdEntityLevel.AdGroup,
            adGroupId,
            string.IsNullOrWhiteSpace(name) ? $"{adType ?? "AD"} {adId}" : name,
            GoogleAdsMappings.Status(GoogleAdsJson.String(row, "adGroupAd", "status")),
            new Dictionary<string, string?> { ["adType"] = adType });
    }

    private static AdEntitySnapshot Criterion(
        JsonElement row, string criterionProperty, AdEntityLevel level, string externalId,
        AdEntityLevel parentLevel, string parentId)
    {
        var text = GoogleAdsJson.String(row, criterionProperty, "keyword", "text") ?? string.Empty;
        return new AdEntitySnapshot(
            level,
            externalId,
            parentLevel,
            parentId,
            text,
            GoogleAdsMappings.Status(GoogleAdsJson.String(row, criterionProperty, "status")),
            new Dictionary<string, string?>
            {
                [AdActionPayloadKeys.Text] = text,
                [AdActionPayloadKeys.MatchType] = GoogleAdsMappings
                    .KeywordMatch(GoogleAdsJson.String(row, criterionProperty, "keyword", "matchType"))?.ToString(),
            });
    }
}
```

`Reporting/GoogleAdsReadSource.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Adapters.GoogleAds.Api;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.GoogleAds.Reporting;

/// <summary>
/// Google Ads side of the MarketingAds backbone. One configured client account; every query goes
/// through <see cref="IGoogleAdsApiClient"/>. Transport/auth errors propagate — the core catches per source.
/// </summary>
internal sealed class GoogleAdsReadSource : IAdPlatformReadSource
{
    internal static readonly TimeSpan ChangeLogMaxAge = TimeSpan.FromDays(30);

    private static readonly (GoogleAdsQuery Query, Func<JsonElement, AdEntitySnapshot> Map)[] EntityQueries =
    {
        (GoogleAdsQueries.Campaigns, GoogleAdsEntityMapper.Campaign),
        (GoogleAdsQueries.AdGroups, GoogleAdsEntityMapper.AdGroup),
        (GoogleAdsQueries.Keywords, GoogleAdsEntityMapper.Keyword),
        (GoogleAdsQueries.AdGroupNegativeKeywords, GoogleAdsEntityMapper.AdGroupNegativeKeyword),
        (GoogleAdsQueries.CampaignNegativeKeywords, GoogleAdsEntityMapper.CampaignNegativeKeyword),
        (GoogleAdsQueries.Ads, GoogleAdsEntityMapper.Ad),
    };

    private readonly IGoogleAdsApiClient _api;
    private readonly IOptionsMonitor<GoogleAdsSettings> _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GoogleAdsReadSource> _logger;
    private readonly Dictionary<string, GoogleAdsCustomer> _customers = new(StringComparer.Ordinal);

    public GoogleAdsReadSource(
        IGoogleAdsApiClient api,
        IOptionsMonitor<GoogleAdsSettings> settings,
        TimeProvider timeProvider,
        ILogger<GoogleAdsReadSource> logger)
    {
        _api = api;
        _settings = settings;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public AdPlatform Platform => AdPlatform.GoogleAds;

    public AdSourceCapabilities Capabilities { get; } =
        new(SearchTerms: true, ChangeLog: true, ChangeLogMaxAge: ChangeLogMaxAge);

    public async Task<IReadOnlyList<AdAccountSnapshot>> GetAccountsAsync(CancellationToken ct)
    {
        var customer = await GetCustomerAsync(ConfiguredCustomerId(), ct);
        return new[] { new AdAccountSnapshot(customer.Id, customer.Name, customer.Currency, customer.TimeZoneId) };
    }

    public async Task<IReadOnlyList<AdEntitySnapshot>> GetEntitiesAsync(string accountExternalId, CancellationToken ct)
    {
        var customerId = RequireConfiguredAccount(accountExternalId);
        var entities = new List<AdEntitySnapshot>();
        foreach (var (query, map) in EntityQueries)
        {
            var rows = await _api.SearchAsync(customerId, query, ct);
            entities.AddRange(rows.Select(map));
        }
        return entities;
    }

    public Task<IReadOnlyList<AdDailyFactRow>> GetDailyFactsAsync(string accountExternalId, DateOnly date, CancellationToken ct) =>
        throw new NotImplementedException("Task B6");

    public Task<IReadOnlyList<AdSearchTermRow>> GetSearchTermsAsync(string accountExternalId, DateOnly date, CancellationToken ct) =>
        throw new NotImplementedException("Task B6");

    public Task<IReadOnlyList<AdChangeEventRow>> GetChangeEventsAsync(string accountExternalId, DateTimeOffset since, CancellationToken ct) =>
        throw new NotImplementedException("Task B7");

    private async Task<GoogleAdsCustomer> GetCustomerAsync(string customerId, CancellationToken ct)
    {
        if (_customers.TryGetValue(customerId, out var cached))
            return cached;

        var rows = await _api.SearchAsync(customerId, GoogleAdsQueries.Customer, ct);
        if (rows.Count != 1)
            throw new InvalidOperationException($"Google Ads customer {customerId} returned {rows.Count} customer rows.");

        var row = rows[0];
        if (GoogleAdsJson.Bool(row, "customer", "manager"))
            throw new InvalidOperationException(
                $"Google Ads customer {customerId} is a manager account. Set GoogleAds:CustomerId to the client " +
                "account and GoogleAds:LoginCustomerId to the manager.");

        var timeZoneId = GoogleAdsJson.RequiredString(row, "customer", "timeZone");
        var customer = new GoogleAdsCustomer(
            GoogleAdsJson.RequiredString(row, "customer", "id"),
            GoogleAdsJson.String(row, "customer", "descriptiveName") ?? customerId,
            GoogleAdsJson.RequiredString(row, "customer", "currencyCode"),
            timeZoneId,
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
        _customers[customerId] = customer;
        return customer;
    }

    private string ConfiguredCustomerId() =>
        GoogleAdsIds.RequireNumericId(
            GoogleAdsIds.NormalizeCustomerId(_settings.CurrentValue.CustomerId), "GoogleAds:CustomerId");

    private string RequireConfiguredAccount(string accountExternalId)
    {
        var configured = ConfiguredCustomerId();
        return GoogleAdsIds.NormalizeCustomerId(accountExternalId) == configured
            ? configured
            : throw new ArgumentException(
                $"Google Ads account '{accountExternalId}' is not the configured account.", nameof(accountExternalId));
    }
}
```

(`_timeProvider` and `_logger` are used from Task B7; the compiler does not warn about unused private readonly fields assigned in the constructor.)

- [ ] **Step 6: Run to verify pass**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Adapters.GoogleAds.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.GoogleAds.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~GoogleAdsReadSourceEntitiesTests"
```
Expected: `Passed: 6, Failed: 0`. If the fixtures are not found, check that `bin/Debug/net8.0/Fixtures/ReadSource/*.json` exists (the csproj `None Update` item).

- [ ] **Step 7: Format and commit**

```bash
dotnet format Anela.Heblo.sln --include backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/ backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/
git add backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds backend/test/Anela.Heblo.Adapters.GoogleAds.Tests
git commit -m "feat: read Google Ads accounts and entities for marketing ads

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task B6: Read source — daily facts and search terms

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Reporting/GoogleAdsQueries.cs`, `Reporting/GoogleAdsReadSource.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Reporting/GoogleAdsFactMapper.cs`
- Create fixtures: `Fixtures/ReadSource/{campaign_facts,ad_group_facts,keyword_facts,ad_facts,search_terms}.json`
- Test: `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Reporting/GoogleAdsReadSourceFactsTests.cs`

**Interfaces:**
- Consumes: B5's `GoogleAdsReadSource`, `GetCustomerAsync`, `RequireConfiguredAccount`.
- Produces:
  - `GoogleAdsQueries.FactQueries(DateOnly date) : IReadOnlyList<(AdEntityLevel Level, GoogleAdsQuery Query)>` (names `campaign_facts`, `ad_group_facts`, `keyword_facts`, `ad_facts`), `GoogleAdsQueries.SearchTerms(DateOnly date)` (name `search_terms`)
  - `GoogleAdsFactMapper.Fact(AdEntityLevel, JsonElement, string currency) : AdDailyFactRow`, `.SearchTerm(JsonElement, string currency) : AdSearchTermRow`, `.MergeDuplicates(IEnumerable<AdSearchTermRow>) : IReadOnlyList<AdSearchTermRow>`

- [ ] **Step 1: Write the fixtures**

`Fixtures/ReadSource/campaign_facts.json` (campaign 112 has only impressions — Google omitted the zero metrics):

```json
{
  "results": [
    {
      "campaign": { "resourceName": "customers/1234567890/campaigns/111", "id": "111" },
      "segments": { "date": "2026-10-06" },
      "metrics": { "impressions": "1200", "clicks": "85", "costMicros": "432100000", "conversions": 4.0, "conversionsValue": 3150.5 }
    },
    {
      "campaign": { "resourceName": "customers/1234567890/campaigns/112", "id": "112" },
      "segments": { "date": "2026-10-06" },
      "metrics": { "impressions": "10" }
    }
  ]
}
```

`Fixtures/ReadSource/ad_group_facts.json`:

```json
{
  "results": [
    {
      "adGroup": { "resourceName": "customers/1234567890/adGroups/221", "id": "221" },
      "segments": { "date": "2026-10-06" },
      "metrics": { "impressions": "1200", "clicks": "85", "costMicros": "432100000", "conversions": 4.0, "conversionsValue": 3150.5 }
    }
  ]
}
```

`Fixtures/ReadSource/keyword_facts.json`:

```json
{
  "results": [
    {
      "adGroup": { "resourceName": "customers/1234567890/adGroups/221", "id": "221" },
      "adGroupCriterion": { "resourceName": "customers/1234567890/adGroupCriteria/221~331", "criterionId": "331" },
      "keywordView": { "resourceName": "customers/1234567890/keywordViews/221~331" },
      "segments": { "date": "2026-10-06" },
      "metrics": { "impressions": "900", "clicks": "70", "costMicros": "350000000", "conversions": 3.0, "conversionsValue": 2400.0 }
    }
  ]
}
```

`Fixtures/ReadSource/ad_facts.json`:

```json
{
  "results": [
    {
      "adGroup": { "resourceName": "customers/1234567890/adGroups/221", "id": "221" },
      "adGroupAd": { "resourceName": "customers/1234567890/adGroupAds/221~441", "ad": { "resourceName": "customers/1234567890/ads/441", "id": "441" } },
      "segments": { "date": "2026-10-06" },
      "metrics": { "impressions": "1200", "clicks": "85", "costMicros": "432100000", "conversions": 4.0, "conversionsValue": 3150.5 }
    }
  ]
}
```

`Fixtures/ReadSource/search_terms.json`:

```json
{
  "results": [
    {
      "adGroup": { "resourceName": "customers/1234567890/adGroups/221", "id": "221" },
      "searchTermView": { "resourceName": "customers/1234567890/searchTermViews/111~221~YW5lbGEga3LDqW0", "searchTerm": "anela krém" },
      "segments": { "date": "2026-10-06", "searchTermMatchType": "EXACT" },
      "metrics": { "impressions": "40", "clicks": "5", "costMicros": "12500000", "conversions": 1.0, "conversionsValue": 450.0 }
    },
    {
      "adGroup": { "resourceName": "customers/1234567890/adGroups/221", "id": "221" },
      "searchTermView": { "resourceName": "customers/1234567890/searchTermViews/111~221~YW5lbGEga3LDqW0", "searchTerm": "anela krém" },
      "segments": { "date": "2026-10-06", "searchTermMatchType": "NEAR_EXACT" },
      "metrics": { "impressions": "10", "clicks": "1", "costMicros": "2500000" }
    },
    {
      "adGroup": { "resourceName": "customers/1234567890/adGroups/221", "id": "221" },
      "searchTermView": { "resourceName": "customers/1234567890/searchTermViews/111~221~a29zbWV0aWthIHpkYXJtYQ", "searchTerm": "kosmetika zdarma" },
      "segments": { "date": "2026-10-06", "searchTermMatchType": "BROAD" },
      "metrics": { "impressions": "30", "clicks": "2", "costMicros": "6000000" }
    },
    {
      "adGroup": { "resourceName": "customers/1234567890/adGroups/221", "id": "221" },
      "searchTermView": { "resourceName": "customers/1234567890/searchTermViews/111~221~cHLDrXJvZG7DrSBrb3NtZXRpa2E", "searchTerm": "přírodní kosmetika" },
      "segments": { "date": "2026-10-06", "searchTermMatchType": "AI_MAX" },
      "metrics": { "impressions": "8", "clicks": "1", "costMicros": "3000000" }
    }
  ]
}
```

- [ ] **Step 2: Write the failing tests**

`Reporting/GoogleAdsReadSourceFactsTests.cs`:

```csharp
using Anela.Heblo.Adapters.GoogleAds.Tests.Support;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Reporting;

public sealed class GoogleAdsReadSourceFactsTests
{
    private static readonly DateOnly Date = ReadSourceHarness.FixtureDate;

    [Fact]
    public async Task returns_facts_for_all_four_levels_in_account_currency()
    {
        var (source, _) = ReadSourceHarness.Create(new FixtureGoogleAdsApiClient());

        var facts = await source.GetDailyFactsAsync(TestSettings.CustomerId, Date, CancellationToken.None);

        facts.Select(f => (f.Level, f.EntityExternalId)).Should().BeEquivalentTo(new[]
        {
            (AdEntityLevel.Campaign, "111"), (AdEntityLevel.Campaign, "112"),
            (AdEntityLevel.AdGroup, "221"), (AdEntityLevel.Keyword, "221~331"), (AdEntityLevel.Ad, "221~441"),
        });
        facts.Should().OnlyContain(f => f.Currency == "CZK" && f.Date == Date);
    }

    [Fact]
    public async Task converts_micros_and_keeps_fractional_conversions()
    {
        var (source, _) = ReadSourceHarness.Create(new FixtureGoogleAdsApiClient());

        var facts = await source.GetDailyFactsAsync(TestSettings.CustomerId, Date, CancellationToken.None);

        facts.Single(f => f.Level == AdEntityLevel.Campaign && f.EntityExternalId == "111").Should().Be(
            new AdDailyFactRow(AdEntityLevel.Campaign, "111", Date, 1200, 85, 432.1m, 4.0m, 3150.5m, "CZK"));
    }

    [Fact]
    public async Task treats_metrics_google_omits_as_zero()
    {
        var (source, _) = ReadSourceHarness.Create(new FixtureGoogleAdsApiClient());

        var facts = await source.GetDailyFactsAsync(TestSettings.CustomerId, Date, CancellationToken.None);

        facts.Single(f => f.EntityExternalId == "112").Should().Be(
            new AdDailyFactRow(AdEntityLevel.Campaign, "112", Date, 10, 0, 0m, 0m, 0m, "CZK"));
    }

    [Fact]
    public async Task filters_every_fact_query_on_the_requested_date()
    {
        var api = new FixtureGoogleAdsApiClient();
        var (source, _) = ReadSourceHarness.Create(api);

        await source.GetDailyFactsAsync(TestSettings.CustomerId, Date, CancellationToken.None);

        api.Calls.Where(c => c.Query.Name.EndsWith("_facts", StringComparison.Ordinal))
            .Should().HaveCount(4)
            .And.OnlyContain(c => c.Query.Gaql.Contains("segments.date = '2026-10-06'"));
    }

    [Fact]
    public async Task a_day_without_traffic_returns_no_facts()
    {
        var (source, _) = ReadSourceHarness.Create(new FixtureGoogleAdsApiClient());

        var facts = await source.GetDailyFactsAsync(TestSettings.CustomerId, Date.AddDays(-1), CancellationToken.None);

        facts.Should().BeEmpty();
    }

    [Fact]
    public async Task merges_near_exact_into_exact()
    {
        var (source, _) = ReadSourceHarness.Create(new FixtureGoogleAdsApiClient());

        var terms = await source.GetSearchTermsAsync(TestSettings.CustomerId, Date, CancellationToken.None);

        terms.Should().HaveCount(3);
        terms.Single(t => t.SearchTerm == "anela krém").Should().Be(new AdSearchTermRow(
            "221", Date, "anela krém", KeywordMatchType.Exact, 50, 6, 15.0m, 1.0m, 450.0m, "CZK"));
    }

    [Fact]
    public async Task keeps_terms_without_a_keyword_match_type_with_a_null_match_type()
    {
        var (source, _) = ReadSourceHarness.Create(new FixtureGoogleAdsApiClient());

        var terms = await source.GetSearchTermsAsync(TestSettings.CustomerId, Date, CancellationToken.None);

        terms.Single(t => t.SearchTerm == "přírodní kosmetika").MatchType.Should().BeNull();
        terms.Single(t => t.SearchTerm == "kosmetika zdarma").MatchType.Should().Be(KeywordMatchType.Broad);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Adapters.GoogleAds.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.GoogleAds.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~GoogleAdsReadSourceFactsTests"
```
Expected: 7 failures with `NotImplementedException: Task B6`.

- [ ] **Step 4: Implement**

Append to `GoogleAdsQueries` (add `using System.Globalization;` and `using Anela.Heblo.Application.Features.MarketingAds.Contracts;`):

```csharp
    private const string Metrics =
        "metrics.impressions, metrics.clicks, metrics.cost_micros, metrics.conversions, metrics.conversions_value";

    public static IReadOnlyList<(AdEntityLevel Level, GoogleAdsQuery Query)> FactQueries(DateOnly date)
    {
        var day = Day(date);
        return new[]
        {
            (AdEntityLevel.Campaign, new GoogleAdsQuery("campaign_facts",
                $"SELECT campaign.id, segments.date, {Metrics} FROM campaign WHERE segments.date = '{day}'")),
            (AdEntityLevel.AdGroup, new GoogleAdsQuery("ad_group_facts",
                $"SELECT ad_group.id, segments.date, {Metrics} FROM ad_group WHERE segments.date = '{day}'")),
            (AdEntityLevel.Keyword, new GoogleAdsQuery("keyword_facts",
                $"SELECT ad_group.id, ad_group_criterion.criterion_id, segments.date, {Metrics} " +
                $"FROM keyword_view WHERE segments.date = '{day}'")),
            (AdEntityLevel.Ad, new GoogleAdsQuery("ad_facts",
                $"SELECT ad_group.id, ad_group_ad.ad.id, segments.date, {Metrics} " +
                $"FROM ad_group_ad WHERE segments.date = '{day}'")),
        };
    }

    public static GoogleAdsQuery SearchTerms(DateOnly date) => new("search_terms",
        $"SELECT ad_group.id, search_term_view.search_term, segments.search_term_match_type, segments.date, {Metrics} " +
        $"FROM search_term_view WHERE segments.date = '{Day(date)}'");

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
```

`Reporting/GoogleAdsFactMapper.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.GoogleAds.Reporting;

internal static class GoogleAdsFactMapper
{
    public static AdDailyFactRow Fact(AdEntityLevel level, JsonElement row, string currency) => new(
        level,
        EntityId(level, row),
        Date(row),
        GoogleAdsJson.Int64(row, "metrics", "impressions"),
        GoogleAdsJson.Int64(row, "metrics", "clicks"),
        GoogleAdsJson.Micros(row, "metrics", "costMicros"),
        GoogleAdsJson.Decimal(row, "metrics", "conversions"),
        GoogleAdsJson.Decimal(row, "metrics", "conversionsValue"),
        currency);

    public static AdSearchTermRow SearchTerm(JsonElement row, string currency) => new(
        GoogleAdsJson.RequiredString(row, "adGroup", "id"),
        Date(row),
        GoogleAdsJson.RequiredString(row, "searchTermView", "searchTerm"),
        GoogleAdsMappings.SearchTermMatch(GoogleAdsJson.String(row, "segments", "searchTermMatchType")),
        GoogleAdsJson.Int64(row, "metrics", "impressions"),
        GoogleAdsJson.Int64(row, "metrics", "clicks"),
        GoogleAdsJson.Micros(row, "metrics", "costMicros"),
        GoogleAdsJson.Decimal(row, "metrics", "conversions"),
        GoogleAdsJson.Decimal(row, "metrics", "conversionsValue"),
        currency);

    /// <summary>
    /// Google reports EXACT and NEAR_EXACT (PHRASE and NEAR_PHRASE) as separate rows. Both map to one
    /// KeywordMatchType, i.e. one natural key in ad_search_term_daily, so they are summed here.
    /// </summary>
    public static IReadOnlyList<AdSearchTermRow> MergeDuplicates(IEnumerable<AdSearchTermRow> rows) =>
        rows.GroupBy(r => (r.AdGroupExternalId, r.Date, r.SearchTerm, r.MatchType))
            .Select(group => group.Skip(1).Aggregate(group.First(), (sum, r) => sum with
            {
                Impressions = sum.Impressions + r.Impressions,
                Clicks = sum.Clicks + r.Clicks,
                Cost = sum.Cost + r.Cost,
                Conversions = sum.Conversions + r.Conversions,
                ConversionValue = sum.ConversionValue + r.ConversionValue,
            }))
            .ToList();

    private static string EntityId(AdEntityLevel level, JsonElement row) => level switch
    {
        AdEntityLevel.Campaign => GoogleAdsJson.RequiredString(row, "campaign", "id"),
        AdEntityLevel.AdGroup => GoogleAdsJson.RequiredString(row, "adGroup", "id"),
        AdEntityLevel.Keyword =>
            $"{GoogleAdsJson.RequiredString(row, "adGroup", "id")}~{GoogleAdsJson.RequiredString(row, "adGroupCriterion", "criterionId")}",
        AdEntityLevel.Ad =>
            $"{GoogleAdsJson.RequiredString(row, "adGroup", "id")}~{GoogleAdsJson.RequiredString(row, "adGroupAd", "ad", "id")}",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Google Ads reports facts per campaign, ad group, keyword and ad."),
    };

    private static DateOnly Date(JsonElement row) =>
        DateOnly.ParseExact(GoogleAdsJson.RequiredString(row, "segments", "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
```

In `GoogleAdsReadSource`, replace the two B6 stubs:

```csharp
    public async Task<IReadOnlyList<AdDailyFactRow>> GetDailyFactsAsync(
        string accountExternalId, DateOnly date, CancellationToken ct)
    {
        var customerId = RequireConfiguredAccount(accountExternalId);
        var currency = (await GetCustomerAsync(customerId, ct)).Currency;
        var facts = new List<AdDailyFactRow>();
        foreach (var (level, query) in GoogleAdsQueries.FactQueries(date))
        {
            var rows = await _api.SearchAsync(customerId, query, ct);
            facts.AddRange(rows.Select(row => GoogleAdsFactMapper.Fact(level, row, currency)));
        }
        return facts;
    }

    public async Task<IReadOnlyList<AdSearchTermRow>> GetSearchTermsAsync(
        string accountExternalId, DateOnly date, CancellationToken ct)
    {
        var customerId = RequireConfiguredAccount(accountExternalId);
        var currency = (await GetCustomerAsync(customerId, ct)).Currency;
        var rows = await _api.SearchAsync(customerId, GoogleAdsQueries.SearchTerms(date), ct);
        return GoogleAdsFactMapper.MergeDuplicates(rows.Select(row => GoogleAdsFactMapper.SearchTerm(row, currency)));
    }
```

- [ ] **Step 5: Run to verify pass**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Adapters.GoogleAds.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.GoogleAds.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~GoogleAdsReadSource"
```
Expected: `Failed: 0` (6 entity + 7 fact tests).

- [ ] **Step 6: Format and commit**

```bash
dotnet format Anela.Heblo.sln --include backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/ backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/
git add backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds backend/test/Anela.Heblo.Adapters.GoogleAds.Tests
git commit -m "feat: read Google Ads daily facts and search terms

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task B7: Read source — change history

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Reporting/GoogleAdsQueries.cs`, `Reporting/GoogleAdsReadSource.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Reporting/GoogleAdsChangeEventMapper.cs`
- Create fixture: `Fixtures/ReadSource/change_events.json`
- Test: `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Reporting/GoogleAdsReadSourceChangeEventsTests.cs`

**Interfaces:**
- Consumes: B4 mappings, B5 source internals, `GoogleAdsSettings.HebloUserEmail`.
- Produces:
  - `GoogleAdsQueries.ChangeEvents(string fromAccountLocal, string toAccountLocal, int limit)` (name `change_events`)
  - `GoogleAdsChangeEventMapper.Map(JsonElement, TimeZoneInfo, string? hebloUserEmail) : AdChangeEventRow`, `.ToUtc(string accountLocal, TimeZoneInfo) : DateTimeOffset`, `.ToAccountLocal(DateTimeOffset, TimeZoneInfo) : string`
  - `GoogleAdsReadSource.ChangeEventLimit = 10_000`

- [ ] **Step 1: Write the fixture**

`Fixtures/ReadSource/change_events.json` — times are Europe/Prague (CEST, UTC+2):

```json
{
  "results": [
    {
      "changeEvent": {
        "resourceName": "customers/1234567890/changeEvents/1759648500123456~0~0",
        "changeDateTime": "2026-10-05 09:15:00.123456",
        "changeResourceType": "CAMPAIGN",
        "changeResourceName": "customers/1234567890/campaigns/111",
        "clientType": "GOOGLE_ADS_RECOMMENDATIONS",
        "resourceChangeOperation": "UPDATE",
        "changedFields": "biddingStrategyType",
        "oldResource": { "campaign": { "biddingStrategyType": "MANUAL_CPC" } },
        "newResource": { "campaign": { "biddingStrategyType": "MAXIMIZE_CONVERSIONS" } }
      }
    },
    {
      "changeEvent": {
        "resourceName": "customers/1234567890/changeEvents/1759665600000000~0~0",
        "changeDateTime": "2026-10-05 14:00:00",
        "changeResourceType": "AD_GROUP_AD",
        "changeResourceName": "customers/1234567890/adGroupAds/221~441",
        "clientType": "GOOGLE_ADS_WEB_CLIENT",
        "userEmail": "specialist@agency.example",
        "resourceChangeOperation": "UPDATE",
        "changedFields": "status",
        "oldResource": { "adGroupAd": { "status": "ENABLED" } },
        "newResource": { "adGroupAd": { "status": "PAUSED" } }
      }
    },
    {
      "changeEvent": {
        "resourceName": "customers/1234567890/changeEvents/1759732200000000~1~0",
        "changeDateTime": "2026-10-06 08:30:00.5",
        "changeResourceType": "AD_GROUP_CRITERION",
        "changeResourceName": "customers/1234567890/adGroupCriteria/221~341",
        "clientType": "GOOGLE_ADS_API",
        "userEmail": "heblo-ads@anela.cz",
        "resourceChangeOperation": "CREATE",
        "changedFields": "adGroup,negative,keyword.text,keyword.matchType",
        "newResource": { "adGroupCriterion": { "negative": true, "keyword": { "text": "zdarma", "matchType": "BROAD" } } }
      }
    }
  ]
}
```

- [ ] **Step 2: Write the failing tests**

`Reporting/GoogleAdsReadSourceChangeEventsTests.cs`:

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;
using Anela.Heblo.Adapters.GoogleAds.Reporting;
using Anela.Heblo.Adapters.GoogleAds.Tests.Support;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Reporting;

public sealed class GoogleAdsReadSourceChangeEventsTests
{
    private static readonly Regex LowerBound = new(@"change_date_time >= '([^']+)'");
    private static readonly Regex UpperBound = new(@"change_date_time <= '([^']+)'");
    private static readonly TimeZoneInfo Prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");

    [Fact]
    public async Task maps_actor_entity_and_change_of_each_event()
    {
        var (source, _) = ReadSourceHarness.Create(new FixtureGoogleAdsApiClient());

        var events = await source.GetChangeEventsAsync(
            TestSettings.CustomerId, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

        events.Should().HaveCount(3);
        var recommendation = events[0];
        recommendation.ExternalEventId.Should().Be("1759648500123456~0~0");
        recommendation.ActorKind.Should().Be(AdChangeActorKind.PlatformAutomation);
        recommendation.Actor.Should().BeNull();
        recommendation.EntityLevel.Should().Be(AdEntityLevel.Campaign);
        recommendation.EntityExternalId.Should().Be("111");
        recommendation.ChangeType.Should().Be("UPDATE:CAMPAIGN");
        JsonDocument.Parse(recommendation.NewValueJson!).RootElement
            .GetProperty("campaign").GetProperty("biddingStrategyType").GetString().Should().Be("MAXIMIZE_CONVERSIONS");

        var pause = events[1];
        (pause.ActorKind, pause.Actor, pause.EntityLevel, pause.EntityExternalId)
            .Should().Be((AdChangeActorKind.User, "specialist@agency.example", (AdEntityLevel?)AdEntityLevel.Ad, "221~441"));

        var heblo = events[2];
        (heblo.ActorKind, heblo.EntityLevel, heblo.EntityExternalId, heblo.ChangeType)
            .Should().Be((AdChangeActorKind.Heblo, (AdEntityLevel?)AdEntityLevel.NegativeKeyword, "adGroupCriteria/221~341", "CREATE:AD_GROUP_CRITERION"));
        heblo.OldValueJson.Should().BeNull();
    }

    [Fact]
    public async Task converts_account_local_times_to_utc()
    {
        var (source, _) = ReadSourceHarness.Create(new FixtureGoogleAdsApiClient());

        var events = await source.GetChangeEventsAsync(
            TestSettings.CustomerId, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

        events[0].OccurredAt.Should().Be(new DateTimeOffset(2026, 10, 5, 7, 15, 0, TimeSpan.Zero).AddTicks(1_234_560));
        events[1].OccurredAt.Should().Be(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
        events[2].OccurredAt.Should().Be(new DateTimeOffset(2026, 10, 6, 6, 30, 0, 500, TimeSpan.Zero));
    }

    [Fact]
    public async Task queries_from_since_to_now_in_account_time_and_drops_older_events()
    {
        var api = new FixtureGoogleAdsApiClient();
        var (source, _) = ReadSourceHarness.Create(api);
        var since = new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);

        var events = await source.GetChangeEventsAsync(TestSettings.CustomerId, since, CancellationToken.None);

        events.Select(e => e.ExternalEventId).Should().Equal("1759665600000000~0~0", "1759732200000000~1~0");
        var gaql = api.Calls.Single(c => c.Query.Name == "change_events").Query.Gaql;
        LowerBound.Match(gaql).Groups[1].Value.Should().Be("2026-10-05 12:00:00");
        UpperBound.Match(gaql).Groups[1].Value.Should().Be("2026-10-07 08:00:00");
        gaql.Should().MatchRegex(@"LIMIT 10000$");
    }

    [Fact]
    public async Task clamps_a_watermark_older_than_the_30_day_window()
    {
        var api = new FixtureGoogleAdsApiClient();
        var (source, _) = ReadSourceHarness.Create(api);

        var events = await source.GetChangeEventsAsync(
            TestSettings.CustomerId, new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

        events.Should().HaveCount(3);
        var gaql = api.Calls.Single(c => c.Query.Name == "change_events").Query.Gaql;
        // now − 30 days + 1 hour = 2026-09-07T07:00Z = 09:00 in Prague (CEST)
        LowerBound.Match(gaql).Groups[1].Value.Should().Be("2026-09-07 09:00:00");
    }

    [Fact]
    public async Task a_watermark_in_the_future_returns_nothing_without_querying_change_events()
    {
        var api = new FixtureGoogleAdsApiClient();
        var (source, _) = ReadSourceHarness.Create(api);

        var events = await source.GetChangeEventsAsync(
            TestSettings.CustomerId, TestSettings.Now.AddMinutes(5), CancellationToken.None);

        events.Should().BeEmpty();
        api.Calls.Should().NotContain(c => c.Query.Name == "change_events");
    }

    [Fact]
    public void converts_winter_time_too()
    {
        GoogleAdsChangeEventMapper.ToUtc("2026-12-01 10:00:00", Prague)
            .Should().Be(new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero));
        GoogleAdsChangeEventMapper.ToAccountLocal(new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero), Prague)
            .Should().Be("2026-12-01 10:00:00");
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Adapters.GoogleAds.Tests -p:UseSharedCompilation=false
```
Expected: FAIL — `CS0103`/`CS0246` for `GoogleAdsChangeEventMapper`.

- [ ] **Step 4: Implement**

Append to `GoogleAdsQueries`:

```csharp
    /// <summary>
    /// change_event demands a date filter starting at most 30 days back and a LIMIT (≤ 10 000).
    /// Bounds are in the account time zone, format yyyy-MM-dd HH:mm:ss.
    /// </summary>
    public static GoogleAdsQuery ChangeEvents(string fromAccountLocal, string toAccountLocal, int limit) => new("change_events",
        "SELECT change_event.resource_name, change_event.change_date_time, change_event.change_resource_name, " +
        "change_event.user_email, change_event.client_type, change_event.change_resource_type, " +
        "change_event.old_resource, change_event.new_resource, change_event.resource_change_operation, " +
        "change_event.changed_fields FROM change_event " +
        $"WHERE change_event.change_date_time >= '{fromAccountLocal}' AND change_event.change_date_time <= '{toAccountLocal}' " +
        $"ORDER BY change_event.change_date_time ASC LIMIT {limit.ToString(CultureInfo.InvariantCulture)}");
```

`Reporting/GoogleAdsChangeEventMapper.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.GoogleAds.Reporting;

internal static class GoogleAdsChangeEventMapper
{
    private const string ChangeEventsMarker = "/changeEvents/";
    private const string AccountLocalFormat = "yyyy-MM-dd HH:mm:ss";
    private static readonly string[] DateTimeFormats = { "yyyy-MM-dd HH:mm:ss.FFFFFF", AccountLocalFormat };

    public static AdChangeEventRow Map(JsonElement row, TimeZoneInfo accountTimeZone, string? hebloUserEmail)
    {
        var userEmail = GoogleAdsJson.String(row, "changeEvent", "userEmail");
        var operation = GoogleAdsJson.String(row, "changeEvent", "resourceChangeOperation") ?? "UNKNOWN";
        var resourceType = GoogleAdsJson.String(row, "changeEvent", "changeResourceType") ?? "UNKNOWN";
        var (level, externalId) = GoogleAdsMappings.EntityRef(
            GoogleAdsJson.String(row, "changeEvent", "changeResourceName"), IsNegativeCriterion(row));

        return new AdChangeEventRow(
            ExternalEventId: EventId(GoogleAdsJson.RequiredString(row, "changeEvent", "resourceName")),
            OccurredAt: ToUtc(GoogleAdsJson.RequiredString(row, "changeEvent", "changeDateTime"), accountTimeZone),
            Actor: string.IsNullOrWhiteSpace(userEmail) ? null : userEmail,
            ActorKind: GoogleAdsMappings.ActorKind(
                GoogleAdsJson.String(row, "changeEvent", "clientType"), userEmail, hebloUserEmail),
            EntityLevel: level,
            EntityExternalId: externalId,
            ChangeType: $"{operation}:{resourceType}",
            OldValueJson: GoogleAdsJson.Raw(row, "changeEvent", "oldResource"),
            NewValueJson: GoogleAdsJson.Raw(row, "changeEvent", "newResource"));
    }

    /// <summary>
    /// Account-local wall-clock time to UTC. GetUtcOffset never throws: an ambiguous autumn hour
    /// resolves to standard time, a skipped spring hour to the standard offset.
    /// </summary>
    public static DateTimeOffset ToUtc(string accountLocal, TimeZoneInfo timeZone)
    {
        var parsed = DateTime.ParseExact(accountLocal, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None);
        var local = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, timeZone.GetUtcOffset(local)).ToUniversalTime();
    }

    public static string ToAccountLocal(DateTimeOffset instant, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTime(instant, timeZone).ToString(AccountLocalFormat, CultureInfo.InvariantCulture);

    private static string EventId(string resourceName)
    {
        var index = resourceName.IndexOf(ChangeEventsMarker, StringComparison.Ordinal);
        return index < 0 ? resourceName : resourceName[(index + ChangeEventsMarker.Length)..];
    }

    // old/new resources hold only changed fields, so "negative" is present on CREATE but may be absent on UPDATE/REMOVE.
    private static bool IsNegativeCriterion(JsonElement row) =>
        GoogleAdsJson.Bool(row, "changeEvent", "newResource", "adGroupCriterion", "negative")
        || GoogleAdsJson.Bool(row, "changeEvent", "oldResource", "adGroupCriterion", "negative");
}
```

In `GoogleAdsReadSource`, add the constants next to `ChangeLogMaxAge`:

```csharp
    internal const int ChangeEventLimit = 10_000;

    // change_event refuses a window starting more than 30 days back; one hour of slack absorbs clock skew.
    private static readonly TimeSpan ChangeLogSafetyMargin = TimeSpan.FromHours(1);
```

and replace the B7 stub:

```csharp
    public async Task<IReadOnlyList<AdChangeEventRow>> GetChangeEventsAsync(
        string accountExternalId, DateTimeOffset since, CancellationToken ct)
    {
        var customerId = RequireConfiguredAccount(accountExternalId);
        var customer = await GetCustomerAsync(customerId, ct);
        var now = _timeProvider.GetUtcNow();
        var from = ClampToChangeLogWindow(since, now);
        if (from >= now)
            return Array.Empty<AdChangeEventRow>();

        var query = GoogleAdsQueries.ChangeEvents(
            GoogleAdsChangeEventMapper.ToAccountLocal(from, customer.TimeZone),
            GoogleAdsChangeEventMapper.ToAccountLocal(now, customer.TimeZone),
            ChangeEventLimit);
        var rows = await _api.SearchAsync(customerId, query, ct);
        if (rows.Count >= ChangeEventLimit)
            _logger.LogWarning(
                "GoogleAds: change_event hit the {Limit}-row limit for {CustomerId} from {From:o}; the next run continues from the newest event returned",
                ChangeEventLimit, customerId, from);

        var hebloUserEmail = _settings.CurrentValue.HebloUserEmail;
        return rows.Select(row => GoogleAdsChangeEventMapper.Map(row, customer.TimeZone, hebloUserEmail))
                   .Where(e => e.OccurredAt >= since)
                   .ToList();
    }

    private DateTimeOffset ClampToChangeLogWindow(DateTimeOffset since, DateTimeOffset now)
    {
        var floor = now - ChangeLogMaxAge + ChangeLogSafetyMargin;
        if (since >= floor)
            return since;

        _logger.LogWarning(
            "GoogleAds: change history requested since {Since:o}, but change_event reaches back only 30 days; reading from {Floor:o}. Older changes cannot be recovered from Google",
            since, floor);
        return floor;
    }
```

- [ ] **Step 5: Run to verify pass**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Adapters.GoogleAds.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.GoogleAds.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~Reporting"
```
Expected: `Failed: 0`.

- [ ] **Step 6: Format and commit**

```bash
dotnet format Anela.Heblo.sln --include backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/ backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/
git add backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds backend/test/Anela.Heblo.Adapters.GoogleAds.Tests
git commit -m "feat: read Google Ads change history with actor attribution

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task B8: Contract tests, gated registration, configuration, process doc

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsMarketingAdsServiceCollectionExtensions.cs`
- Modify: `backend/src/Anela.Heblo.API/Program.cs` (one line after `builder.Services.AddGoogleAdsAdapter(builder.Configuration);`, currently line 125)
- Modify: `backend/src/Anela.Heblo.API/appsettings.json` (`GoogleAds` section, currently lines 110–116)
- Create: `docs/processes/sync-google-ads-campaign-data.md`
- Test: `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Reporting/GoogleAdsReadSourceContractTests.cs`, `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/RegistrationTests.cs`

**Interfaces:**
- Consumes: everything from B2–B7; `AdSettingsGuard.IsConfigured(params string?[])` and `AdPlatformReadSourceContractTests` from C1.
- Produces: `public static IServiceCollection AddGoogleAdsMarketingAds(this IServiceCollection, IConfiguration)`; `internal static bool IsConfigured(GoogleAdsSettings)`. Part C adds the executor registration inside `AddGoogleAdsMarketingAds`.

- [ ] **Step 1: Write the failing tests**

`Reporting/GoogleAdsReadSourceContractTests.cs` (if C1's base declares other abstract members, implement them from the fixtures and note it in the PR):

```csharp
using Anela.Heblo.Adapters.GoogleAds.Tests.Support;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.MarketingAds.TestKit;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Reporting;

public sealed class GoogleAdsReadSourceContractTests : AdPlatformReadSourceContractTests
{
    protected override IAdPlatformReadSource CreateSource() =>
        ReadSourceHarness.Create(new FixtureGoogleAdsApiClient()).Source;

    protected override string AccountExternalId => TestSettings.CustomerId;

    protected override DateOnly FixtureDate => ReadSourceHarness.FixtureDate;
}
```

`RegistrationTests.cs`:

```csharp
using Anela.Heblo.Adapters.GoogleAds;
using Anela.Heblo.Adapters.GoogleAds.Reporting;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.GoogleAds.Tests;

public sealed class RegistrationTests
{
    private static Dictionary<string, string?> Configured() => new()
    {
        ["GoogleAds:CustomerId"] = "123-456-7890",
        ["GoogleAds:OAuth2ClientId"] = "client-id.apps.googleusercontent.com",
        ["GoogleAds:OAuth2ClientSecret"] = "client-secret-value",
        ["GoogleAds:OAuth2RefreshToken"] = "refresh-token-value",
    };

    private static IServiceCollection Register(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddGoogleAdsMarketingAds(configuration);
    }

    [Fact]
    public void registers_nothing_with_the_repository_placeholders()
    {
        var settings = new Dictionary<string, string?>
        {
            ["GoogleAds:CustomerId"] = "XXX-XXX-XXXX",
            ["GoogleAds:OAuth2ClientId"] = "-- stored in secrets.json --",
            ["GoogleAds:OAuth2ClientSecret"] = "-- stored in secrets.json --",
            ["GoogleAds:OAuth2RefreshToken"] = "-- stored in secrets.json --",
        };

        Register(settings).Should().BeEmpty("an unconfigured environment must stay inert");
    }

    [Theory]
    [InlineData("GoogleAds:CustomerId")]
    [InlineData("GoogleAds:OAuth2ClientId")]
    [InlineData("GoogleAds:OAuth2ClientSecret")]
    [InlineData("GoogleAds:OAuth2RefreshToken")]
    public void registers_nothing_when_one_credential_is_missing(string key)
    {
        var settings = Configured();
        settings[key] = "";

        Register(settings).Should().BeEmpty();
    }

    [Fact]
    public void registers_nothing_for_a_non_numeric_customer_or_login_id()
    {
        var badCustomer = Configured();
        badCustomer["GoogleAds:CustomerId"] = "anela";
        var badLogin = Configured();
        badLogin["GoogleAds:LoginCustomerId"] = "manager";

        Register(badCustomer).Should().BeEmpty();
        Register(badLogin).Should().BeEmpty();
    }

    [Fact]
    public void does_not_require_a_developer_token()
    {
        Register(Configured()).Should().Contain(d => d.ServiceType == typeof(IAdPlatformReadSource));
    }

    [Fact]
    public void resolves_the_read_source_without_calling_google()
    {
        var services = Register(Configured());
        services.AddLogging();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var sources = scope.ServiceProvider.GetServices<IAdPlatformReadSource>().ToList();

        sources.Should().ContainSingle().Which.Should().BeOfType<GoogleAdsReadSource>()
            .Which.Platform.Should().Be(AdPlatform.GoogleAds);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.GoogleAds.Tests -p:UseSharedCompilation=false`
Expected: FAIL — `CS1061: 'IServiceCollection' does not contain a definition for 'AddGoogleAdsMarketingAds'`.

- [ ] **Step 3: Implement the registration**

`GoogleAdsMarketingAdsServiceCollectionExtensions.cs`:

```csharp
using Anela.Heblo.Adapters.GoogleAds.Api;
using Anela.Heblo.Adapters.GoogleAds.Reporting;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Anela.Heblo.Adapters.GoogleAds;

public static class GoogleAdsMarketingAdsServiceCollectionExtensions
{
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(100);
    private static readonly TimeSpan OAuthTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Registers the Google Ads platform for the MarketingAds backbone, but only when the account and
    /// OAuth credentials are really configured: AdSettingsGuard rejects blanks, "-- stored in … --" and
    /// "XXX" placeholders. Otherwise nothing is registered and nothing throws, so a missing or
    /// mistyped Key Vault secret can never stop the app from booting. Nothing here calls Google.
    /// The developer token is not required (Google sunset developer tokens on 2026-09-09).
    /// </summary>
    public static IServiceCollection AddGoogleAdsMarketingAds(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(GoogleAdsSettings.ConfigurationKey);
        var settings = section.Get<GoogleAdsSettings>() ?? new GoogleAdsSettings();
        if (!IsConfigured(settings))
            return services;

        services.Configure<GoogleAdsSettings>(section);
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient(GoogleAdsRestClient.HttpClientName, client => client.Timeout = ApiTimeout);
        services.AddHttpClient(GoogleAdsOAuthTokenProvider.HttpClientName, client => client.Timeout = OAuthTimeout);
        services.AddSingleton<IGoogleAdsAccessTokenProvider, GoogleAdsOAuthTokenProvider>();
        services.AddSingleton<IGoogleAdsApiClient, GoogleAdsRestClient>();
        services.AddScoped<IAdPlatformReadSource, GoogleAdsReadSource>();
        return services;
    }

    internal static bool IsConfigured(GoogleAdsSettings settings)
    {
        var loginCustomerId = GoogleAdsIds.NormalizeCustomerId(settings.LoginCustomerId);
        return AdSettingsGuard.IsConfigured(
                   settings.CustomerId, settings.OAuth2ClientId, settings.OAuth2ClientSecret, settings.OAuth2RefreshToken)
               && GoogleAdsIds.IsNumericId(GoogleAdsIds.NormalizeCustomerId(settings.CustomerId))
               && (loginCustomerId.Length == 0 || GoogleAdsIds.IsNumericId(loginCustomerId));
    }
}
```

(The test project's in-memory `ConfigurationBuilder` comes transitively from the adapter; if the build reports `CS0246 ConfigurationBuilder`, add `<PackageReference Include="Microsoft.Extensions.Configuration" Version="8.0.0" />` to the test csproj.)

- [ ] **Step 4: Wire it into the API and configuration**

In `backend/src/Anela.Heblo.API/Program.cs`, directly after `builder.Services.AddGoogleAdsAdapter(builder.Configuration);` add:

```csharp
        builder.Services.AddGoogleAdsMarketingAds(builder.Configuration);
```

In `backend/src/Anela.Heblo.API/appsettings.json`, make the `GoogleAds` section:

```json
  "GoogleAds": {
    "CustomerId": "XXX-XXX-XXXX",
    "LoginCustomerId": "",
    "ApiVersion": "v25",
    "HebloUserEmail": "",
    "DeveloperToken": "-- stored in secrets.json --",
    "OAuth2ClientId": "-- stored in secrets.json --",
    "OAuth2ClientSecret": "-- stored in secrets.json --",
    "OAuth2RefreshToken": "-- stored in secrets.json --"
  },
```

- [ ] **Step 5: Run to verify pass**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Adapters.GoogleAds.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.GoogleAds.Tests --no-build -p:UseSharedCompilation=false
```
Expected: every test passes, including the inherited contract tests (`GoogleAdsReadSourceContractTests.*`). If a contract test fails, fix the source or fixtures — never the base class.

- [ ] **Step 6: Process doc**

Create `docs/processes/sync-google-ads-campaign-data.md`. `verified_at` = output of `git rev-parse --short=9 HEAD`. `related` lists C2's process docs `sync-ads-daily` and `sync-ads-change-history` (planned names; confirm with the Task B0 Step 4 listing and drop any that do not exist — every name must exist in `docs/processes/`). The `marketing-ads` module overview `module-marketing-ads.md` comes from C2.

```markdown
---
process: sync-google-ads-campaign-data
kind: sync
module: marketing-ads
summary: Google Ads read source for the MarketingAds backbone — account, campaign/ad group/keyword/negative/ad entities, daily facts, search terms and change history pulled over the Google Ads REST API v25.
owns:
  - backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Api/**
  - backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Reporting/**
  - backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsMarketingAdsServiceCollectionExtensions.cs
verified_at: "<short sha>"
related: [sync-ad-platform-transactions, sync-ads-daily, sync-ads-change-history]
---

# Google Ads → MarketingAds (read side)

## Purpose
Gives Heblo's marketing agents and Metabase the Google Ads data needed to *manage* the account,
not only report on it: what exists (campaigns, ad groups, keywords, negatives, ads), what it cost
and earned per day at each level, which search terms triggered ads, and every change made to the
account by Heblo, the agency, a person or Google's auto-applied recommendations.

## Trigger
No job of its own. `GoogleAdsReadSource` is one `IAdPlatformReadSource`; the core MarketingAds
jobs call it: `sync-ads-daily` (05:30 Europe/Prague) and `sync-ads-change-history` (hourly), spec section 5. It exists
only when `AddGoogleAdsMarketingAds` registered it: `GoogleAds:CustomerId` numeric, OAuth client
id/secret and refresh token present and not placeholders (`AdSettingsGuard`), `LoginCustomerId`
empty or numeric. Otherwise the platform is silently absent and the core syncs the others.

## Data flow
1. OAuth refresh token → access token (`oauth2.googleapis.com/token`, cached until 2 min before expiry).
2. `POST googleads.googleapis.com/v25/customers/{CustomerId}/googleAds:search` per GAQL query
   (integration doc §4), all pages.
3. Rows mapped to the C1 contract records (`AdAccountSnapshot`, `AdEntitySnapshot`,
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
- <copy the access-level, entity-count and change-event bullets from docs/integrations/google-ads-api.md §10> — Google Ads access spike — <date>.

## Known quirks
- Google omits zero-valued fields; a missing metric is 0, not an error.
- `change_event` cannot reach back more than 30 days; after a longer outage older changes are lost (warning logged).
- An ad-group criterion UPDATE/REMOVE event may lack `negative` and is then reported at level `Keyword`.
- Staging and production read the same live account (read-only; Explorer quota 2 880 ops/day is shared by the Cloud project).
- The developer token is not used; Google sunset it on 2026-09-09.

## Code entry points
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Reporting/GoogleAdsReadSource.cs` — the five contract methods
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Reporting/GoogleAdsQueries.cs` — every GAQL query
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Api/GoogleAdsRestClient.cs` — transport, paging, retries
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsMarketingAdsServiceCollectionExtensions.cs` — configuration gate
```

Replace the two `<…>` markers (SHA, Runtime-facts bullet) with real values before committing. Then run:

```bash
python3 scripts/process-docs/check.py index
python3 scripts/process-docs/check.py check
```
Expected: `INDEX.md` regenerated with a `marketing-ads` section; `check` reports no errors for `sync-google-ads-campaign-data` or `sync-ad-platform-transactions`.

- [ ] **Step 7: Format and commit**

```bash
dotnet format Anela.Heblo.sln --include backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/ backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/ backend/src/Anela.Heblo.API/Program.cs
git add backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds backend/test/Anela.Heblo.Adapters.GoogleAds.Tests backend/src/Anela.Heblo.API/Program.cs backend/src/Anela.Heblo.API/appsettings.json docs/processes
git commit -m "feat: register Google Ads read source behind a configuration gate

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task B9: Full validation, Key Vault hand-off, PR

**Files:** none new.

**Interfaces:**
- Consumes: all of Part B.
- Produces: an open PR `feat/google-ads-read-source` → `main`, and Ondrej's Key Vault checklist.

- [ ] **Step 1: Whole-solution build and the affected test suites**

Run:
```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.GoogleAds.Tests --no-build -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~GoogleAds|FullyQualifiedName~RecurringJob|FullyQualifiedName~Architecture"
dotnet format Anela.Heblo.sln --verify-no-changes --include backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/ backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/ backend/src/Anela.Heblo.API/Program.cs
python3 scripts/process-docs/check.py check
```
Expected: build `0 Error(s)`; all tests `Failed: 0` (the existing billing tests in `Anela.Heblo.Tests/Adapters/GoogleAds` still pass — proof they were untouched); format exits 0; process check exits 0.

- [ ] **Step 2: Confirm the billing importer is untouched**

Run: `git diff origin/main --stat -- backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsInvoiceImportJob.cs backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsTransactionSource.cs backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/SdkAccountBudgetFetcher.cs backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/IAccountBudgetFetcher.cs backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/RawAccountBudget.cs backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsAdapterServiceCollectionExtensions.cs`
Expected: empty output.

- [ ] **Step 3: Push and open the PR**

```bash
git push -u origin feat/google-ads-read-source
gh pr create --base main --title "feat: Google Ads read source for the marketing ads backbone" --body "$(cat <<'EOF'
## Summary
- `GoogleAdsReadSource : IAdPlatformReadSource` over Google Ads REST v25: account, entities (campaign, ad group, keyword, ad-group and campaign negatives, ad), daily facts at four levels, search terms, change history with actor attribution.
- Thin `IGoogleAdsApiClient` transport (OAuth refresh token, paging, transient-only retries); tests run on recorded search-response fixtures and inherit `AdPlatformReadSourceContractTests`.
- Registration gated on `AdSettingsGuard`; registers nothing and never throws when unconfigured.
- Docs: `docs/integrations/google-ads-api.md` (access-spike findings, why `ImportedMarketingTransactions` is empty), process doc `sync-google-ads-campaign-data`.
- The billing importer is untouched.

## Spec deviations
- Developer token not required (Google sunset developer tokens 2026-09-09; access follows the Cloud project).
- REST instead of the SDK (SDK 21.1.0 only has sunset API versions; upgrading would force billing changes).
- Composite external ids for keywords, negatives and ads (integration doc §6).
<list any C1 differences found in Task B0>

## Key Vault (Ondrej — the agent did not write these)
| Secret | kv-heblo-stg | kv-heblo-prod |
|---|---|---|
| `GoogleAds--CustomerId` | client account id | same |
| `GoogleAds--LoginCustomerId` | only if access is via a manager account | same |
| `GoogleAds--OAuth2ClientId` / `GoogleAds--OAuth2ClientSecret` | Cloud project OAuth client | same |
| `GoogleAds--OAuth2RefreshToken` | refresh token of the Heblo Google user | same |
| `GoogleAds--HebloUserEmail` | that user's email | same |

Before setting them: on staging's Recurring Jobs page make sure `google-ads-invoice-import` is **disabled** — real credentials would let the dead V18 billing job run and fail. After setting: `az webapp restart -g rgHeblo -n heblo-test` (prod: `-n heblo`). Data reaches the `ads` schema only once core PR C2 is merged.

## Test plan
- [x] `dotnet test backend/test/Anela.Heblo.Adapters.GoogleAds.Tests`
- [x] existing GoogleAds billing tests in `Anela.Heblo.Tests` still pass
- [x] `python3 scripts/process-docs/check.py check`
- [ ] After KV secrets on staging: daily sync log shows GoogleAds rows (needs C2)

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```
Replace the `<list …>` line with the actual differences (or delete it if none) before running.

- [ ] **Step 4: Tell Ondrej**

Send the PR link and the Key Vault table from the PR body. Offer to set the **staging** secrets yourself only if he explicitly says so; never set production secrets.

---
# Part C — Executor PR

Part C never writes to a live account. All tests run against `StatefulFakeGoogleAdsApi`; the live check is Ondrej's manual, `validate_only` smoke run (Task E5).

### Task E0: Preconditions and branch

- [ ] **Step 1: Verify C1 and Part B are on main**

Run:
```bash
git fetch origin
git cat-file -e origin/main:backend/src/Anela.Heblo.Application/Features/MarketingAds/Contracts/IAdActionExecutor.cs && echo C1-present
git cat-file -e origin/main:backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Api/IGoogleAdsApiClient.cs && echo partB-present
git show origin/main:backend/test/Anela.Heblo.MarketingAds.TestKit/AdActionExecutorContractTests.cs
```
Expected: both `…-present` lines and the base class source. If Part B is not merged yet, STOP and tell Ondrej "the executor PR waits for the read-source PR". Compare the base class with spec 12.3 and adapt if it differs.

- [ ] **Step 2: Branch**

Run: `git switch -c feat/google-ads-executor origin/main`

### Task E1: Mutate transport

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Api/GoogleAdsMutateResult.cs`, `Api/GoogleAdsMutateServices.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Api/IGoogleAdsApiClient.cs`, `Api/GoogleAdsRestClient.cs`
- Modify: `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Support/FixtureGoogleAdsApiClient.cs`
- Test: `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Api/GoogleAdsRestClientMutateTests.cs`

**Interfaces:**
- Consumes: `GoogleAdsRestClient` internals from B3 (`CreateRequestAsync`, `ToException`, internal ctor with `retryDelay`).
- Produces:
  - `IGoogleAdsApiClient.MutateAsync(string customerId, string service, JsonObject request, CancellationToken ct) : Task<GoogleAdsMutateResult>`
  - `internal sealed record GoogleAdsMutateResult(bool IsSuccess, IReadOnlyList<string> ResourceNames, string ResponseJson, string? ErrorCode, string? ErrorMessage)` with `Success(...)`, `Rejected(...)`, `FromResponse(string)`
  - `internal static class GoogleAdsMutateServices { CampaignCriteria = "campaignCriteria"; AdGroupCriteria = "adGroupCriteria"; AdGroupAds = "adGroupAds"; IReadOnlySet<string> All }`

- [ ] **Step 1: Write the failing tests**

`Api/GoogleAdsRestClientMutateTests.cs`:

```csharp
using System.Net;
using System.Text.Json.Nodes;
using Anela.Heblo.Adapters.GoogleAds.Api;
using Anela.Heblo.Adapters.GoogleAds.Tests.Support;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Api;

public sealed class GoogleAdsRestClientMutateTests
{
    private const string Created = """{"results":[{"resourceName":"customers/1234567890/campaignCriteria/111~9001"}]}""";
    private const string Duplicate = """
        {"error":{"code":400,"message":"Request contains an invalid argument.","status":"INVALID_ARGUMENT","details":[{
          "@type":"type.googleapis.com/google.ads.googleads.v25.errors.GoogleAdsFailure",
          "errors":[{"errorCode":{"criterionError":"KEYWORD_HAS_INVALID_CHARS"},"message":"Keyword has invalid characters."}],
          "requestId":"m-1"}]}}
        """;

    private static JsonObject Request() => new()
    {
        ["operations"] = new JsonArray(new JsonObject { ["remove"] = "customers/1234567890/campaignCriteria/111~9001" }),
        ["partialFailure"] = false,
        ["validateOnly"] = false,
    };

    [Fact]
    public async Task posts_to_the_service_mutate_endpoint_and_returns_resource_names()
    {
        var (client, handler) = GoogleAdsRestClientTests.Create();
        handler.Enqueue(HttpStatusCode.OK, Created);

        var result = await client.MutateAsync(TestSettings.CustomerId, GoogleAdsMutateServices.CampaignCriteria, Request(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.ResourceNames.Should().Equal("customers/1234567890/campaignCriteria/111~9001");
        var request = handler.Requests.Single();
        request.Uri.Should().Be(new Uri("https://googleads.googleapis.com/v25/customers/1234567890/campaignCriteria:mutate"));
        JsonNode.Parse(request.Body!)!["operations"]![0]!["remove"]!.GetValue<string>()
            .Should().Be("customers/1234567890/campaignCriteria/111~9001");
    }

    [Fact]
    public async Task a_validate_only_response_has_no_resource_names()
    {
        var (client, handler) = GoogleAdsRestClientTests.Create();
        handler.Enqueue(HttpStatusCode.OK, "{}");

        var result = await client.MutateAsync(TestSettings.CustomerId, GoogleAdsMutateServices.AdGroupAds, Request(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.ResourceNames.Should().BeEmpty();
    }

    [Fact]
    public async Task returns_a_rejection_instead_of_throwing_for_a_400()
    {
        var (client, handler) = GoogleAdsRestClientTests.Create();
        handler.Enqueue(HttpStatusCode.BadRequest, Duplicate);

        var result = await client.MutateAsync(TestSettings.CustomerId, GoogleAdsMutateServices.CampaignCriteria, Request(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("criterionError.KEYWORD_HAS_INVALID_CHARS");
        result.ErrorMessage.Should().Be("Keyword has invalid characters.");
        JsonNode.Parse(result.ResponseJson)!["error"]!["code"]!.GetValue<int>().Should().Be(400);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task does_not_retry_a_mutate_that_failed_with_503()
    {
        var (client, handler) = GoogleAdsRestClientTests.Create();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, """{"error":{"code":503,"status":"UNAVAILABLE","message":"busy"}}""")
               .Enqueue(HttpStatusCode.OK, Created);

        var act = () => client.MutateAsync(TestSettings.CustomerId, GoogleAdsMutateServices.CampaignCriteria, Request(), CancellationToken.None);

        (await act.Should().ThrowAsync<GoogleAdsApiException>()).Which.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        handler.Requests.Should().ContainSingle("a 5xx mutate may already have been applied");
    }

    [Fact]
    public async Task does_not_retry_a_mutate_that_timed_out()
    {
        var (client, handler) = GoogleAdsRestClientTests.Create();
        handler.Enqueue(_ => throw new TaskCanceledException("HttpClient.Timeout"))
               .Enqueue(HttpStatusCode.OK, Created);

        var act = () => client.MutateAsync(TestSettings.CustomerId, GoogleAdsMutateServices.CampaignCriteria, Request(), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task retries_a_mutate_rejected_with_429()
    {
        var (client, handler) = GoogleAdsRestClientTests.Create();
        handler.Enqueue(HttpStatusCode.TooManyRequests, """{"error":{"code":429,"status":"RESOURCE_EXHAUSTED","message":"slow down"}}""")
               .Enqueue(HttpStatusCode.OK, Created);

        var result = await client.MutateAsync(TestSettings.CustomerId, GoogleAdsMutateServices.CampaignCriteria, Request(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task throws_for_auth_failures()
    {
        var (client, handler) = GoogleAdsRestClientTests.Create();
        handler.Enqueue(HttpStatusCode.Forbidden, """{"error":{"code":403,"status":"PERMISSION_DENIED","message":"no"}}""");

        var act = () => client.MutateAsync(TestSettings.CustomerId, GoogleAdsMutateServices.AdGroupAds, Request(), CancellationToken.None);

        await act.Should().ThrowAsync<GoogleAdsApiException>();
    }

    [Fact]
    public async Task refuses_an_unknown_service_without_calling_google()
    {
        var (client, handler) = GoogleAdsRestClientTests.Create();

        var act = () => client.MutateAsync(TestSettings.CustomerId, "campaignBudgets", Request(), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        handler.Requests.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.GoogleAds.Tests -p:UseSharedCompilation=false`
Expected: FAIL — `CS1061 'GoogleAdsRestClient' does not contain a definition for 'MutateAsync'`.

- [ ] **Step 3: Implement**

`Api/GoogleAdsMutateServices.cs`:

```csharp
namespace Anela.Heblo.Adapters.GoogleAds.Api;

/// <summary>The only mutate services Heblo may call (v1 actions: negative keywords, ad pause).</summary>
internal static class GoogleAdsMutateServices
{
    public const string CampaignCriteria = "campaignCriteria";
    public const string AdGroupCriteria = "adGroupCriteria";
    public const string AdGroupAds = "adGroupAds";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { CampaignCriteria, AdGroupCriteria, AdGroupAds };
}
```

`Api/GoogleAdsMutateResult.cs`:

```csharp
using System.Text.Json;

namespace Anela.Heblo.Adapters.GoogleAds.Api;

internal sealed record GoogleAdsMutateResult(
    bool IsSuccess, IReadOnlyList<string> ResourceNames, string ResponseJson, string? ErrorCode, string? ErrorMessage)
{
    public static GoogleAdsMutateResult Success(IReadOnlyList<string> resourceNames, string responseJson) =>
        new(true, resourceNames, responseJson, null, null);

    public static GoogleAdsMutateResult Rejected(string errorCode, string errorMessage, string responseJson) =>
        new(false, Array.Empty<string>(), responseJson, errorCode, errorMessage);

    /// <summary>{"results":[{"resourceName":"…"}]}; validate-only responses are "{}".</summary>
    public static GoogleAdsMutateResult FromResponse(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        var names = document.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array
            ? results.EnumerateArray()
                .Select(r => r.TryGetProperty("resourceName", out var n) ? n.GetString() : null)
                .OfType<string>()
                .ToList()
            : new List<string>();
        return Success(names, responseJson);
    }
}
```

Add to `IGoogleAdsApiClient` (add `using System.Text.Json.Nodes;`):

```csharp
    /// <summary>
    /// POST customers/{id}/{service}:mutate. Google rejecting the request (HTTP 400/404/409) comes back as a
    /// result with IsSuccess = false; auth and transport failures throw GoogleAdsApiException. Only HTTP 429
    /// is retried: any other failure may already have been applied.
    /// </summary>
    Task<GoogleAdsMutateResult> MutateAsync(string customerId, string service, JsonObject request, CancellationToken ct);
```

In `GoogleAdsRestClient`: add a field `private readonly ResiliencePipeline _mutatePipeline;`, set it in the internal ctor with `_mutatePipeline = BuildMutatePipeline(retryDelay);`, and add:

```csharp
    public async Task<GoogleAdsMutateResult> MutateAsync(
        string customerId, string service, JsonObject request, CancellationToken ct)
    {
        GoogleAdsIds.RequireNumericId(customerId, nameof(customerId));
        if (!GoogleAdsMutateServices.All.Contains(service))
            throw new ArgumentOutOfRangeException(nameof(service), service, "Unsupported Google Ads mutate service.");

        return await _mutatePipeline.ExecuteAsync(async innerCt =>
        {
            using var httpRequest = await CreateRequestAsync($"customers/{customerId}/{service}:mutate", request, innerCt);
            using var response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(httpRequest, innerCt);
            var text = await response.Content.ReadAsStringAsync(innerCt);
            if (response.IsSuccessStatusCode)
                return GoogleAdsMutateResult.FromResponse(text);

            if (IsPlatformRejection(response.StatusCode))
            {
                var error = GoogleAdsErrorParser.Parse(text);
                _logger.LogWarning("GoogleAds: {Service}:mutate rejected: {ErrorCode} (requestId {RequestId})",
                    service, error.ErrorCode, error.RequestId);
                return GoogleAdsMutateResult.Rejected(
                    error.ErrorCode ?? $"http.{(int)response.StatusCode}", error.Message, text);
            }

            throw ToException(response.StatusCode, text, $"{service}:mutate");
        }, ct);
    }

    private static bool IsPlatformRejection(HttpStatusCode status) =>
        status is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Conflict;

    // A mutate that failed with 5xx or timed out may already have been applied; repeating a create
    // could add a second criterion. 429 (RESOURCE_EXHAUSTED) is rejected before processing.
    private static ResiliencePipeline BuildMutatePipeline(TimeSpan delay) =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = MaxRetryAttempts,
                Delay = delay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = delay > TimeSpan.Zero,
                ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Exception is GoogleAdsApiException { StatusCode: HttpStatusCode.TooManyRequests }),
            })
            .Build();
```

In `Support/FixtureGoogleAdsApiClient.cs` add (with `using System.Text.Json.Nodes;`):

```csharp
    public Task<GoogleAdsMutateResult> MutateAsync(string customerId, string service, JsonObject request, CancellationToken ct) =>
        throw new NotSupportedException("Read-source fixtures never mutate.");
```

- [ ] **Step 4: Run to verify pass**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Adapters.GoogleAds.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.GoogleAds.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~Api"
```
Expected: `Failed: 0` (B3 tests still green plus 8 mutate tests).

- [ ] **Step 5: Format and commit**

```bash
dotnet format Anela.Heblo.sln --include backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/ backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/
git add backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds backend/test/Anela.Heblo.Adapters.GoogleAds.Tests
git commit -m "feat: add Google Ads mutate transport that retries only 429

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task E2: Stateful fake and the PauseAd action

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsSettings.cs` (+ `ValidateOnly`)
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Execution/{GoogleAdsExecutorQueries,GoogleAdsMutations,GoogleAdsExecutionResults,GoogleAdsActionGuard,GoogleAdsPauseAdHandler,GoogleAdsActionExecutor}.cs`
- Create: `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Support/StatefulFakeGoogleAdsApi.cs`, `Support/ExecutorHarness.cs`
- Test: `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Execution/GoogleAdsPauseAdTests.cs`

**Interfaces:**
- Consumes: `IGoogleAdsApiClient` incl. `MutateAsync` (E1), `GoogleAdsIds`, `GoogleAdsJson`, `GoogleAdsMappings`.
- Produces:
  - `GoogleAdsSettings.ValidateOnly : bool = true`
  - `internal sealed class GoogleAdsActionExecutor : IAdActionExecutor`, ctor `(IGoogleAdsApiClient, IOptionsMonitor<GoogleAdsSettings>, ILogger<GoogleAdsActionExecutor>)`; dispatches `PauseAd` here and `AddNegativeKeyword` from E3 (returns `Failed`/missing until then)
  - `GoogleAdsExecutionResults.ValidateOnlyMarker = "ValidateOnly:"`, `.Missing`, `.Failed(string, string?)`, `.From(GoogleAdsMutateResult, string before, string after, bool validateOnly)`
  - `GoogleAdsMutations.SetAdStatus(...)`, `.CreateCampaignNegative(...)`, `.CreateAdGroupNegative(...)`, `.Remove(...)`
  - `GoogleAdsExecutorQueries.Campaign/AdGroup/CampaignNegatives/AdGroupNegatives/AdGroupAd` (names `executor_campaign`, `executor_ad_group`, `executor_campaign_negatives`, `executor_ad_group_negatives`, `executor_ad_group_ad`)
  - Test support: `StatefulFakeGoogleAdsApi` (`Campaigns`, `AdGroups`, `AdStatuses`, `Negatives`, `Mutations`, `Queries`, `RejectNextMutate`), `FakeNegativeKeyword`, `ExecutorHarness.SeededApi()`, `.Create(api, validateOnly)`, `.PauseAd(target)`, `.AddNegative(level, target, text, matchType)`

- [ ] **Step 1: Settings**

Append to `GoogleAdsSettings`:

```csharp
    /// <summary>
    /// When true (the default everywhere except production), every mutate is sent with validateOnly:
    /// Google checks it and changes nothing, and the executor reports Failed with a "ValidateOnly:" error.
    /// Staging and production point at the same live account, so only production may write.
    /// </summary>
    public bool ValidateOnly { get; set; } = true;
```

- [ ] **Step 2: Test support**

`Support/StatefulFakeGoogleAdsApi.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Anela.Heblo.Adapters.GoogleAds.Api;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Support;

internal sealed record FakeNegativeKeyword(string Collection, string ParentId, string CriterionId, string Text, string ApiMatchType)
{
    public string ResourceName(string customerId) => $"customers/{customerId}/{Collection}/{ParentId}~{CriterionId}";
}

/// <summary>
/// An in-memory Google Ads account that answers the executor's queries and applies its mutates the
/// way Google does (validateOnly changes nothing), so Execute → ReadCurrent → Revert round-trips.
/// </summary>
internal sealed class StatefulFakeGoogleAdsApi : IGoogleAdsApiClient
{
    private static readonly Regex CampaignIdFilter = new(@"campaign\.id = (\d+)");
    private static readonly Regex AdGroupIdFilter = new(@"ad_group\.id = (\d+)");
    private static readonly Regex AdIdFilter = new(@"ad_group_ad\.ad\.id = (\d+)");
    private static readonly IReadOnlyList<JsonElement> None = Array.Empty<JsonElement>();
    private long _nextCriterionId = 9000;
    private GoogleAdsMutateResult? _nextMutateResult;

    public HashSet<string> Campaigns { get; } = new();
    public HashSet<string> AdGroups { get; } = new();
    public Dictionary<string, string> AdStatuses { get; } = new();
    public List<FakeNegativeKeyword> Negatives { get; } = new();
    public List<(string Service, JsonObject Request)> Mutations { get; } = new();
    public List<GoogleAdsQuery> Queries { get; } = new();

    public void RejectNextMutate(string errorCode, string message) =>
        _nextMutateResult = GoogleAdsMutateResult.Rejected(errorCode, message, """{"error":{"code":400}}""");

    public Task<IReadOnlyList<JsonElement>> SearchAsync(string customerId, GoogleAdsQuery query, CancellationToken ct)
    {
        Queries.Add(query);
        var rows = query.Name switch
        {
            "executor_campaign" => Exists(Campaigns, Id(CampaignIdFilter, query), "campaign"),
            "executor_ad_group" => Exists(AdGroups, Id(AdGroupIdFilter, query), "adGroup"),
            "executor_campaign_negatives" => NegativeRows(customerId, GoogleAdsMutateServices.CampaignCriteria, Id(CampaignIdFilter, query), "campaignCriterion"),
            "executor_ad_group_negatives" => NegativeRows(customerId, GoogleAdsMutateServices.AdGroupCriteria, Id(AdGroupIdFilter, query), "adGroupCriterion"),
            "executor_ad_group_ad" => AdRow(customerId, $"{Id(AdGroupIdFilter, query)}~{Id(AdIdFilter, query)}"),
            _ => throw new NotSupportedException($"The executor fake does not answer '{query.Name}'."),
        };
        return Task.FromResult(rows);
    }

    public Task<GoogleAdsMutateResult> MutateAsync(string customerId, string service, JsonObject request, CancellationToken ct)
    {
        Mutations.Add((service, (JsonObject)request.DeepClone()));
        if (_nextMutateResult is { } rejected)
        {
            _nextMutateResult = null;
            return Task.FromResult(rejected);
        }

        var validateOnly = request["validateOnly"]?.GetValue<bool>() == true;
        var operation = request["operations"]!.AsArray()[0]!.AsObject();
        var resourceName = service == GoogleAdsMutateServices.AdGroupAds
            ? ApplyAdUpdate(operation, validateOnly)
            : operation.ContainsKey("remove")
                ? ApplyRemove(operation, validateOnly)
                : ApplyCreate(customerId, service, operation, validateOnly);

        return Task.FromResult(validateOnly
            ? GoogleAdsMutateResult.Success(Array.Empty<string>(), "{}")
            : GoogleAdsMutateResult.Success(new[] { resourceName },
                new JsonObject { ["results"] = new JsonArray(new JsonObject { ["resourceName"] = resourceName }) }.ToJsonString()));
    }

    private static string Id(Regex filter, GoogleAdsQuery query) =>
        filter.Match(query.Gaql) is { Success: true } match
            ? match.Groups[1].Value
            : throw new InvalidOperationException($"Query '{query.Name}' has no {filter} filter: {query.Gaql}");

    private static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node);

    private static IReadOnlyList<JsonElement> Exists(HashSet<string> ids, string id, string property) =>
        ids.Contains(id)
            ? new[] { Element(new JsonObject { [property] = new JsonObject { ["id"] = id, ["status"] = "ENABLED" } }) }
            : None;

    private IReadOnlyList<JsonElement> NegativeRows(string customerId, string collection, string parentId, string property) =>
        Negatives.Where(n => n.Collection == collection && n.ParentId == parentId)
            .Select(n => Element(new JsonObject
            {
                [property] = new JsonObject
                {
                    ["resourceName"] = n.ResourceName(customerId),
                    ["keyword"] = new JsonObject { ["text"] = n.Text, ["matchType"] = n.ApiMatchType },
                },
            }))
            .ToList();

    private IReadOnlyList<JsonElement> AdRow(string customerId, string adGroupAdId) =>
        AdStatuses.TryGetValue(adGroupAdId, out var status)
            ? new[]
            {
                Element(new JsonObject
                {
                    ["adGroupAd"] = new JsonObject
                    {
                        ["resourceName"] = $"customers/{customerId}/adGroupAds/{adGroupAdId}",
                        ["status"] = status,
                    },
                }),
            }
            : None;

    private string ApplyAdUpdate(JsonObject operation, bool validateOnly)
    {
        var update = operation["update"]!.AsObject();
        var resourceName = update["resourceName"]!.GetValue<string>();
        if (!validateOnly)
            AdStatuses[resourceName[(resourceName.LastIndexOf('/') + 1)..]] = update["status"]!.GetValue<string>();
        return resourceName;
    }

    private string ApplyCreate(string customerId, string service, JsonObject operation, bool validateOnly)
    {
        var create = operation["create"]!.AsObject();
        var parent = (create["campaign"] ?? create["adGroup"])!.GetValue<string>();
        var negative = new FakeNegativeKeyword(
            service,
            parent[(parent.LastIndexOf('/') + 1)..],
            (_nextCriterionId++).ToString(CultureInfo.InvariantCulture),
            create["keyword"]!["text"]!.GetValue<string>(),
            create["keyword"]!["matchType"]!.GetValue<string>());
        if (!validateOnly)
            Negatives.Add(negative);
        return negative.ResourceName(customerId);
    }

    private string ApplyRemove(JsonObject operation, bool validateOnly)
    {
        var resourceName = operation["remove"]!.GetValue<string>();
        if (!validateOnly)
            Negatives.RemoveAll(n => resourceName.EndsWith($"/{n.Collection}/{n.ParentId}~{n.CriterionId}", StringComparison.Ordinal));
        return resourceName;
    }
}
```

`Support/ExecutorHarness.cs`:

```csharp
using Anela.Heblo.Adapters.GoogleAds;
using Anela.Heblo.Adapters.GoogleAds.Execution;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Support;

internal static class ExecutorHarness
{
    /// <summary>Campaign 111 with ad group 221 holding the enabled ad 221~441; no negatives yet.</summary>
    public static StatefulFakeGoogleAdsApi SeededApi()
    {
        var api = new StatefulFakeGoogleAdsApi();
        api.Campaigns.Add("111");
        api.AdGroups.Add("221");
        api.AdStatuses["221~441"] = "ENABLED";
        return api;
    }

    public static GoogleAdsActionExecutor Create(StatefulFakeGoogleAdsApi api, bool validateOnly = false) =>
        new(api,
            new TestOptionsMonitor<GoogleAdsSettings>(TestSettings.Create(s => s.ValidateOnly = validateOnly)),
            NullLogger<GoogleAdsActionExecutor>.Instance);

    public static AdAction PauseAd(string target, string? account = null) => new(
        AdActionType.PauseAd, AdPlatform.GoogleAds, account ?? TestSettings.CustomerId,
        AdEntityLevel.Ad, target, AdActionValues.Enabled, AdActionValues.Paused,
        new Dictionary<string, string>());

    public static AdAction AddNegative(AdEntityLevel level, string target, string text, string matchType) => new(
        AdActionType.AddNegativeKeyword, AdPlatform.GoogleAds, TestSettings.CustomerId,
        level, target, AdActionValues.Absent, AdActionValues.Present,
        new Dictionary<string, string> { [AdActionPayloadKeys.Text] = text, [AdActionPayloadKeys.MatchType] = matchType });
}
```

- [ ] **Step 3: Write the failing tests**

`Execution/GoogleAdsPauseAdTests.cs`:

```csharp
using Anela.Heblo.Adapters.GoogleAds.Tests.Support;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Execution;

public sealed class GoogleAdsPauseAdTests
{
    [Fact]
    public async Task reads_the_current_status_of_the_ad()
    {
        var executor = ExecutorHarness.Create(ExecutorHarness.SeededApi());

        var state = await executor.ReadCurrentAsync(ExecutorHarness.PauseAd("221~441"), CancellationToken.None);

        state.Exists.Should().BeTrue();
        state.CurrentValue.Should().Be(AdActionValues.Enabled);
    }

    [Fact]
    public async Task an_unknown_ad_does_not_exist()
    {
        var executor = ExecutorHarness.Create(ExecutorHarness.SeededApi());

        var state = await executor.ReadCurrentAsync(ExecutorHarness.PauseAd("221~999"), CancellationToken.None);

        state.Should().Be(new AdTargetState(false, null, null));
    }

    [Fact]
    public async Task a_malformed_target_is_missing_without_asking_google()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);

        var state = await executor.ReadCurrentAsync(ExecutorHarness.PauseAd("441 OR 1=1"), CancellationToken.None);

        state.Exists.Should().BeFalse();
        api.Queries.Should().BeEmpty();
    }

    [Fact]
    public async Task pauses_the_ad_with_a_status_only_update()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);

        var result = await executor.ExecuteAsync(ExecutorHarness.PauseAd("221~441"), CancellationToken.None);

        result.Should().Be(new AdExecutionResult(
            AdExecutionOutcome.Succeeded, AdActionValues.Enabled, AdActionValues.Paused,
            "customers/1234567890/adGroupAds/221~441", result.PlatformResponseJson, null));
        var (service, request) = api.Mutations.Single();
        service.Should().Be("adGroupAds");
        request["validateOnly"]!.GetValue<bool>().Should().BeFalse();
        var operation = request["operations"]![0]!;
        operation["updateMask"]!.GetValue<string>().Should().Be("status");
        operation["update"]!["resourceName"]!.GetValue<string>().Should().Be("customers/1234567890/adGroupAds/221~441");
        operation["update"]!["status"]!.GetValue<string>().Should().Be("PAUSED");
        api.AdStatuses["221~441"].Should().Be("PAUSED");
    }

    [Fact]
    public async Task revert_enables_the_ad_again()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);
        var action = ExecutorHarness.PauseAd("221~441");
        var original = await executor.ExecuteAsync(action, CancellationToken.None);

        var reverted = await executor.RevertAsync(action, original, CancellationToken.None);

        reverted.Outcome.Should().Be(AdExecutionOutcome.Succeeded);
        (reverted.BeforeValue, reverted.AfterValue).Should().Be((AdActionValues.Paused, AdActionValues.Enabled));
        (await executor.ReadCurrentAsync(action, CancellationToken.None)).CurrentValue.Should().Be(AdActionValues.Enabled);
    }

    [Fact]
    public async Task a_rejection_is_a_failed_result_carrying_the_google_error()
    {
        var api = ExecutorHarness.SeededApi();
        api.RejectNextMutate("mutateError.RESOURCE_NOT_FOUND", "Resource was not found.");
        var executor = ExecutorHarness.Create(api);

        var result = await executor.ExecuteAsync(ExecutorHarness.PauseAd("221~441"), CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        result.Error.Should().StartWith("mutateError.RESOURCE_NOT_FOUND");
        api.AdStatuses["221~441"].Should().Be("ENABLED");
    }

    [Fact]
    public async Task refuses_an_action_for_another_account_without_calling_google()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);

        var result = await executor.ExecuteAsync(ExecutorHarness.PauseAd("221~441", account: "9999999999"), CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        api.Mutations.Should().BeEmpty();
    }

    [Fact]
    public async Task revert_of_a_failed_pause_does_nothing()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);
        var failed = new AdExecutionResult(AdExecutionOutcome.Failed, null, null, null, null, "x");

        var result = await executor.RevertAsync(ExecutorHarness.PauseAd("221~441"), failed, CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        api.Mutations.Should().BeEmpty();
    }
}
```

- [ ] **Step 4: Run to verify failure**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.GoogleAds.Tests -p:UseSharedCompilation=false`
Expected: FAIL — `CS0246` for `GoogleAdsActionExecutor`.

- [ ] **Step 5: Implement the shared execution pieces**

`Execution/GoogleAdsExecutorQueries.cs`:

```csharp
using Anela.Heblo.Adapters.GoogleAds.Api;

namespace Anela.Heblo.Adapters.GoogleAds.Execution;

/// <summary>Only numeric ids are interpolated (re-checked here); keyword text is matched in C#, never in GAQL.</summary>
internal static class GoogleAdsExecutorQueries
{
    public static GoogleAdsQuery Campaign(string campaignId) => new("executor_campaign",
        $"SELECT campaign.id, campaign.status FROM campaign WHERE campaign.id = {Id(campaignId)}");

    public static GoogleAdsQuery AdGroup(string adGroupId) => new("executor_ad_group",
        $"SELECT ad_group.id, ad_group.status FROM ad_group WHERE ad_group.id = {Id(adGroupId)}");

    public static GoogleAdsQuery CampaignNegatives(string campaignId) => new("executor_campaign_negatives",
        "SELECT campaign_criterion.resource_name, campaign_criterion.keyword.text, campaign_criterion.keyword.match_type " +
        $"FROM campaign_criterion WHERE campaign.id = {Id(campaignId)} AND campaign_criterion.type = 'KEYWORD' " +
        "AND campaign_criterion.negative = TRUE AND campaign_criterion.status != 'REMOVED'");

    public static GoogleAdsQuery AdGroupNegatives(string adGroupId) => new("executor_ad_group_negatives",
        "SELECT ad_group_criterion.resource_name, ad_group_criterion.keyword.text, ad_group_criterion.keyword.match_type " +
        $"FROM ad_group_criterion WHERE ad_group.id = {Id(adGroupId)} AND ad_group_criterion.type = 'KEYWORD' " +
        "AND ad_group_criterion.negative = TRUE AND ad_group_criterion.status != 'REMOVED'");

    public static GoogleAdsQuery AdGroupAd(string adGroupId, string adId) => new("executor_ad_group_ad",
        "SELECT ad_group_ad.resource_name, ad_group_ad.status FROM ad_group_ad " +
        $"WHERE ad_group.id = {Id(adGroupId)} AND ad_group_ad.ad.id = {Id(adId)}");

    private static string Id(string value) => GoogleAdsIds.RequireNumericId(value, nameof(value));
}
```

`Execution/GoogleAdsMutations.cs`:

```csharp
using System.Text.Json.Nodes;
using Anela.Heblo.Adapters.GoogleAds.Reporting;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.GoogleAds.Execution;

/// <summary>Mutate request bodies (integration doc §3). Built as JSON nodes, so text is always escaped.</summary>
internal static class GoogleAdsMutations
{
    public static JsonObject SetAdStatus(string customerId, string adGroupAdId, string apiStatus, bool validateOnly) =>
        Wrap(new JsonObject
        {
            ["updateMask"] = "status",
            ["update"] = new JsonObject
            {
                ["resourceName"] = $"customers/{customerId}/adGroupAds/{adGroupAdId}",
                ["status"] = apiStatus,
            },
        }, validateOnly);

    public static JsonObject CreateCampaignNegative(
        string customerId, string campaignId, string text, KeywordMatchType matchType, bool validateOnly) =>
        Wrap(new JsonObject
        {
            ["create"] = new JsonObject
            {
                ["campaign"] = $"customers/{customerId}/campaigns/{campaignId}",
                ["negative"] = true,
                ["keyword"] = Keyword(text, matchType),
            },
        }, validateOnly);

    public static JsonObject CreateAdGroupNegative(
        string customerId, string adGroupId, string text, KeywordMatchType matchType, bool validateOnly) =>
        Wrap(new JsonObject
        {
            ["create"] = new JsonObject
            {
                ["adGroup"] = $"customers/{customerId}/adGroups/{adGroupId}",
                ["negative"] = true,
                ["keyword"] = Keyword(text, matchType),
            },
        }, validateOnly);

    public static JsonObject Remove(string resourceName, bool validateOnly) =>
        Wrap(new JsonObject { ["remove"] = resourceName }, validateOnly);

    private static JsonObject Keyword(string text, KeywordMatchType matchType) => new()
    {
        ["text"] = text,
        ["matchType"] = GoogleAdsMappings.ToApiMatchType(matchType),
    };

    private static JsonObject Wrap(JsonObject operation, bool validateOnly) => new()
    {
        ["operations"] = new JsonArray(operation),
        ["partialFailure"] = false,
        ["validateOnly"] = validateOnly,
    };
}
```

`Execution/GoogleAdsExecutionResults.cs`:

```csharp
using Anela.Heblo.Adapters.GoogleAds.Api;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.GoogleAds.Execution;

internal static class GoogleAdsExecutionResults
{
    public const string ValidateOnlyMarker = "ValidateOnly:";
    public static readonly AdTargetState Missing = new(false, null, null);

    public static AdExecutionResult Failed(string error, string? responseJson = null) =>
        new(AdExecutionOutcome.Failed, null, null, null, responseJson, error);

    /// <summary>
    /// A validate-only success is reported as Failed: nothing changed in the account, and the
    /// contract has no "validated" outcome, so nothing downstream may believe it did.
    /// </summary>
    public static AdExecutionResult From(GoogleAdsMutateResult response, string before, string after, bool validateOnly)
    {
        if (!response.IsSuccess)
            return Failed($"{response.ErrorCode}: {response.ErrorMessage}", response.ResponseJson);
        if (validateOnly)
            return Failed(
                $"{ValidateOnlyMarker} Google accepted the request; nothing was changed (GoogleAds:ValidateOnly is on).",
                response.ResponseJson);
        return new AdExecutionResult(
            AdExecutionOutcome.Succeeded, before, after, response.ResourceNames.FirstOrDefault(), response.ResponseJson, null);
    }
}
```

`Execution/GoogleAdsActionGuard.cs`:

```csharp
using Anela.Heblo.Adapters.GoogleAds.Api;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.GoogleAds.Execution;

internal static class GoogleAdsActionGuard
{
    /// <returns>null when the action may be sent to Google, otherwise why not.</returns>
    public static string? Validate(AdAction action, string configuredCustomerId) =>
        action.Platform != AdPlatform.GoogleAds
            ? $"Action is for {action.Platform}, not GoogleAds."
            : GoogleAdsIds.NormalizeCustomerId(action.AccountExternalId) != configuredCustomerId
                ? $"Account '{action.AccountExternalId}' is not the configured Google Ads account."
                : null;
}
```

- [ ] **Step 6: Implement PauseAd and the executor**

`Execution/GoogleAdsPauseAdHandler.cs`:

```csharp
using Anela.Heblo.Adapters.GoogleAds.Api;
using Anela.Heblo.Adapters.GoogleAds.Reporting;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.GoogleAds.Execution;

/// <summary>PauseAd: ad_group_ad status ENABLED → PAUSED; revert sets ENABLED. Target = "{adGroupId}~{adId}".</summary>
internal sealed class GoogleAdsPauseAdHandler
{
    private const string ApiEnabled = "ENABLED";
    private const string ApiPaused = "PAUSED";
    private readonly IGoogleAdsApiClient _api;

    public GoogleAdsPauseAdHandler(IGoogleAdsApiClient api) => _api = api;

    public async Task<AdTargetState> ReadCurrentAsync(string customerId, AdAction action, CancellationToken ct)
    {
        if (!GoogleAdsIds.TryParseComposite(action.TargetExternalId, out var adGroupId, out var adId))
            return GoogleAdsExecutionResults.Missing;

        var rows = await _api.SearchAsync(customerId, GoogleAdsExecutorQueries.AdGroupAd(adGroupId, adId), ct);
        if (rows.Count == 0)
            return GoogleAdsExecutionResults.Missing;

        var status = GoogleAdsMappings.Status(GoogleAdsJson.String(rows[0], "adGroupAd", "status"));
        return new AdTargetState(true, status.ToString(), rows[0].GetRawText());
    }

    public Task<AdExecutionResult> ExecuteAsync(string customerId, AdAction action, bool validateOnly, CancellationToken ct) =>
        SetStatusAsync(customerId, action, ApiPaused, AdActionValues.Enabled, AdActionValues.Paused, validateOnly, ct);

    public Task<AdExecutionResult> RevertAsync(
        string customerId, AdAction action, AdExecutionResult original, bool validateOnly, CancellationToken ct) =>
        original.Outcome != AdExecutionOutcome.Succeeded
            ? Task.FromResult(GoogleAdsExecutionResults.Failed("The original pause did not succeed; there is nothing to revert."))
            : SetStatusAsync(customerId, action, ApiEnabled, AdActionValues.Paused, AdActionValues.Enabled, validateOnly, ct);

    private async Task<AdExecutionResult> SetStatusAsync(
        string customerId, AdAction action, string apiStatus, string before, string after, bool validateOnly, CancellationToken ct)
    {
        if (action.TargetLevel != AdEntityLevel.Ad || !GoogleAdsIds.TryParseComposite(action.TargetExternalId, out _, out _))
            return GoogleAdsExecutionResults.Failed(
                $"PauseAd needs an Ad target '<adGroupId>~<adId>', got {action.TargetLevel} '{action.TargetExternalId}'.");

        var request = GoogleAdsMutations.SetAdStatus(customerId, action.TargetExternalId, apiStatus, validateOnly);
        var response = await _api.MutateAsync(customerId, GoogleAdsMutateServices.AdGroupAds, request, ct);
        return GoogleAdsExecutionResults.From(response, before, after, validateOnly);
    }
}
```

`Execution/GoogleAdsActionExecutor.cs` (the `AddNegativeKeyword` arms are wired in Task E3):

```csharp
using Anela.Heblo.Adapters.GoogleAds.Api;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.GoogleAds.Execution;

/// <summary>
/// Google Ads executor for the v1 allowlist. Does not compare old values (the core does). Platform
/// rejections become Failed results; only transport/auth failures throw.
/// </summary>
internal sealed class GoogleAdsActionExecutor : IAdActionExecutor
{
    private static readonly IReadOnlySet<AdActionType> Supported =
        new HashSet<AdActionType> { AdActionType.AddNegativeKeyword, AdActionType.PauseAd };

    private readonly IOptionsMonitor<GoogleAdsSettings> _settings;
    private readonly ILogger<GoogleAdsActionExecutor> _logger;
    private readonly GoogleAdsPauseAdHandler _pauseAd;

    public GoogleAdsActionExecutor(
        IGoogleAdsApiClient api, IOptionsMonitor<GoogleAdsSettings> settings, ILogger<GoogleAdsActionExecutor> logger)
    {
        _settings = settings;
        _logger = logger;
        _pauseAd = new GoogleAdsPauseAdHandler(api);
    }

    public AdPlatform Platform => AdPlatform.GoogleAds;

    public IReadOnlySet<AdActionType> SupportedActions => Supported;

    public async Task<AdTargetState> ReadCurrentAsync(AdAction action, CancellationToken ct)
    {
        var customerId = CustomerId();
        if (GoogleAdsActionGuard.Validate(action, customerId) is not null)
            return GoogleAdsExecutionResults.Missing;

        return action.Type switch
        {
            AdActionType.PauseAd => await _pauseAd.ReadCurrentAsync(customerId, action, ct),
            _ => GoogleAdsExecutionResults.Missing,
        };
    }

    public async Task<AdExecutionResult> ExecuteAsync(AdAction action, CancellationToken ct)
    {
        var customerId = CustomerId();
        var validateOnly = _settings.CurrentValue.ValidateOnly;
        var result = GoogleAdsActionGuard.Validate(action, customerId) is { } invalid
            ? GoogleAdsExecutionResults.Failed(invalid)
            : action.Type switch
            {
                AdActionType.PauseAd => await _pauseAd.ExecuteAsync(customerId, action, validateOnly, ct),
                _ => Unsupported(action),
            };
        Log("Execute", action, result, validateOnly);
        return result;
    }

    public async Task<AdExecutionResult> RevertAsync(AdAction action, AdExecutionResult original, CancellationToken ct)
    {
        var customerId = CustomerId();
        var validateOnly = _settings.CurrentValue.ValidateOnly;
        var result = GoogleAdsActionGuard.Validate(action, customerId) is { } invalid
            ? GoogleAdsExecutionResults.Failed(invalid)
            : action.Type switch
            {
                AdActionType.PauseAd => await _pauseAd.RevertAsync(customerId, action, original, validateOnly, ct),
                _ => Unsupported(action),
            };
        Log("Revert", action, result, validateOnly);
        return result;
    }

    private static AdExecutionResult Unsupported(AdAction action) =>
        GoogleAdsExecutionResults.Failed($"Action type {action.Type} is not supported for Google Ads.");

    private string CustomerId() => GoogleAdsIds.NormalizeCustomerId(_settings.CurrentValue.CustomerId);

    private void Log(string operation, AdAction action, AdExecutionResult result, bool validateOnly) =>
        _logger.LogInformation(
            "GoogleAds: {Operation} {ActionType} on {Level} {Target} -> {Outcome} (validateOnly={ValidateOnly}) {Error}",
            operation, action.Type, action.TargetLevel, action.TargetExternalId, result.Outcome, validateOnly, result.Error);
}
```

- [ ] **Step 7: Run to verify pass**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Adapters.GoogleAds.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.GoogleAds.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~GoogleAdsPauseAdTests"
```
Expected: `Passed: 8, Failed: 0`.

- [ ] **Step 8: Format and commit**

```bash
dotnet format Anela.Heblo.sln --include backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/ backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/
git add backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds backend/test/Anela.Heblo.Adapters.GoogleAds.Tests
git commit -m "feat: pause and re-enable Google Ads ads through the action executor

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task E3: AddNegativeKeyword

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Execution/GoogleAdsNegativeKeywordHandler.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Execution/GoogleAdsActionExecutor.cs`
- Test: `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Execution/GoogleAdsAddNegativeKeywordTests.cs`

**Interfaces:**
- Consumes: E2 execution pieces and fake.
- Produces: `internal sealed class GoogleAdsNegativeKeywordHandler` with `ReadCurrentAsync(string customerId, AdAction, CancellationToken)`, `ExecuteAsync(string customerId, AdAction, bool validateOnly, CancellationToken)`, `RevertAsync(string customerId, AdAction, AdExecutionResult original, bool validateOnly, CancellationToken)` — same shape as `GoogleAdsPauseAdHandler`.

- [ ] **Step 1: Write the failing tests**

`Execution/GoogleAdsAddNegativeKeywordTests.cs`:

```csharp
using Anela.Heblo.Adapters.GoogleAds.Tests.Support;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Execution;

public sealed class GoogleAdsAddNegativeKeywordTests
{
    private static AdAction CampaignNegative(string text = "zdarma", string matchType = "Exact") =>
        ExecutorHarness.AddNegative(AdEntityLevel.Campaign, "111", text, matchType);

    [Fact]
    public async Task is_absent_then_present_after_execute()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);

        var before = await executor.ReadCurrentAsync(CampaignNegative(), CancellationToken.None);
        var result = await executor.ExecuteAsync(CampaignNegative(), CancellationToken.None);
        var after = await executor.ReadCurrentAsync(CampaignNegative(), CancellationToken.None);

        before.Should().Be(new AdTargetState(true, AdActionValues.Absent, null));
        result.Outcome.Should().Be(AdExecutionOutcome.Succeeded);
        result.PlatformResourceId.Should().Be("customers/1234567890/campaignCriteria/111~9000");
        (result.BeforeValue, result.AfterValue).Should().Be((AdActionValues.Absent, AdActionValues.Present));
        after.CurrentValue.Should().Be(AdActionValues.Present);
    }

    [Fact]
    public async Task sends_a_negative_keyword_criterion_for_the_campaign()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);

        await executor.ExecuteAsync(CampaignNegative("  levná   kosmetika ", "Phrase"), CancellationToken.None);

        var (service, request) = api.Mutations.Single();
        service.Should().Be("campaignCriteria");
        var create = request["operations"]![0]!["create"]!;
        create["campaign"]!.GetValue<string>().Should().Be("customers/1234567890/campaigns/111");
        create["negative"]!.GetValue<bool>().Should().BeTrue();
        create["keyword"]!["text"]!.GetValue<string>().Should().Be("levná kosmetika");
        create["keyword"]!["matchType"]!.GetValue<string>().Should().Be("PHRASE");
    }

    [Fact]
    public async Task uses_ad_group_criteria_for_an_ad_group_target()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);
        var action = ExecutorHarness.AddNegative(AdEntityLevel.AdGroup, "221", "zdarma", "Broad");

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        api.Mutations.Single().Service.Should().Be("adGroupCriteria");
        api.Mutations.Single().Request["operations"]![0]!["create"]!["adGroup"]!.GetValue<string>()
            .Should().Be("customers/1234567890/adGroups/221");
        result.PlatformResourceId.Should().StartWith("customers/1234567890/adGroupCriteria/221~");
        (await executor.ReadCurrentAsync(action, CancellationToken.None)).CurrentValue.Should().Be(AdActionValues.Present);
    }

    [Fact]
    public async Task matches_an_existing_negative_ignoring_case_and_extra_spaces_but_not_another_match_type()
    {
        var api = ExecutorHarness.SeededApi();
        api.Negatives.Add(new FakeNegativeKeyword("campaignCriteria", "111", "8000", "Zdarma Doprava", "EXACT"));
        var executor = ExecutorHarness.Create(api);

        var same = await executor.ReadCurrentAsync(CampaignNegative(" zdarma  doprava ", "Exact"), CancellationToken.None);
        var otherType = await executor.ReadCurrentAsync(CampaignNegative("zdarma doprava", "Phrase"), CancellationToken.None);

        same.CurrentValue.Should().Be(AdActionValues.Present);
        otherType.CurrentValue.Should().Be(AdActionValues.Absent);
    }

    [Fact]
    public async Task never_puts_keyword_text_into_a_query()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);
        const string hostile = "x' OR campaign.id = 1 --";
        var action = CampaignNegative(hostile, "Exact");

        await executor.ReadCurrentAsync(action, CancellationToken.None);
        await executor.ExecuteAsync(action, CancellationToken.None);

        api.Queries.Should().NotBeEmpty().And.OnlyContain(q => !q.Gaql.Contains("OR campaign.id = 1"));
        api.Mutations.Single().Request["operations"]![0]!["create"]!["keyword"]!["text"]!.GetValue<string>()
            .Should().Be(hostile);
    }

    [Fact]
    public async Task revert_removes_the_created_criterion()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);
        var original = await executor.ExecuteAsync(CampaignNegative(), CancellationToken.None);

        var reverted = await executor.RevertAsync(CampaignNegative(), original, CancellationToken.None);

        reverted.Outcome.Should().Be(AdExecutionOutcome.Succeeded);
        api.Mutations.Last().Request["operations"]![0]!["remove"]!.GetValue<string>().Should().Be(original.PlatformResourceId);
        (await executor.ReadCurrentAsync(CampaignNegative(), CancellationToken.None)).CurrentValue.Should().Be(AdActionValues.Absent);
    }

    [Theory]
    [InlineData("customers/5555555555/campaignCriteria/111~9000")]
    [InlineData("customers/1234567890/campaignBudgets/901")]
    [InlineData(null)]
    public async Task revert_refuses_a_resource_that_is_not_our_negative_criterion(string? resourceName)
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);
        var original = new AdExecutionResult(AdExecutionOutcome.Succeeded, AdActionValues.Absent, AdActionValues.Present, resourceName, "{}", null);

        var reverted = await executor.RevertAsync(CampaignNegative(), original, CancellationToken.None);

        reverted.Outcome.Should().Be(AdExecutionOutcome.Failed);
        api.Mutations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "Exact")]
    [InlineData("zdarma", "")]
    [InlineData("zdarma", "1")]
    [InlineData("zdarma", "NEAR_EXACT")]
    [InlineData("a b c d e f g h i j k", "Exact")]
    public async Task invalid_payloads_fail_without_calling_google(string text, string matchType)
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);

        var result = await executor.ExecuteAsync(CampaignNegative(text, matchType), CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        api.Mutations.Should().BeEmpty();
    }

    [Fact]
    public async Task text_longer_than_80_characters_fails()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api);

        var result = await executor.ExecuteAsync(CampaignNegative(new string('a', 81)), CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        api.Mutations.Should().BeEmpty();
    }

    [Fact]
    public async Task an_unknown_campaign_does_not_exist()
    {
        var executor = ExecutorHarness.Create(ExecutorHarness.SeededApi());

        var state = await executor.ReadCurrentAsync(
            ExecutorHarness.AddNegative(AdEntityLevel.Campaign, "999", "zdarma", "Exact"), CancellationToken.None);

        state.Should().Be(new AdTargetState(false, null, null));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Adapters.GoogleAds.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.GoogleAds.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~GoogleAdsAddNegativeKeywordTests"
```
Expected: failures — `ReadCurrentAsync` returns `Exists = false` and `ExecuteAsync` returns `Failed: Action type AddNegativeKeyword is not supported`.

- [ ] **Step 3: Implement the handler**

`Execution/GoogleAdsNegativeKeywordHandler.cs`:

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;
using Anela.Heblo.Adapters.GoogleAds.Api;
using Anela.Heblo.Adapters.GoogleAds.Reporting;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.GoogleAds.Execution;

/// <summary>
/// AddNegativeKeyword on a campaign (campaign_criterion) or ad group (ad_group_criterion). Presence is
/// decided in C# — case-insensitive, whitespace-collapsed text plus equal match type — so agent text
/// never reaches GAQL. Revert removes the criterion named by the original PlatformResourceId.
/// </summary>
internal sealed class GoogleAdsNegativeKeywordHandler
{
    private const int MaxKeywordLength = 80;
    private const int MaxKeywordWords = 10;
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);
    private static readonly Regex CriterionResourceName = new(
        @"^customers/(?<customer>\d+)/(?<collection>campaignCriteria|adGroupCriteria)/\d+~\d+$", RegexOptions.CultureInvariant);

    private readonly IGoogleAdsApiClient _api;

    public GoogleAdsNegativeKeywordHandler(IGoogleAdsApiClient api) => _api = api;

    public async Task<AdTargetState> ReadCurrentAsync(string customerId, AdAction action, CancellationToken ct)
    {
        if (TryParse(action, out var target) is not null)
            return GoogleAdsExecutionResults.Missing;

        var parent = await _api.SearchAsync(customerId, target!.ParentQuery, ct);
        if (parent.Count == 0)
            return GoogleAdsExecutionResults.Missing;

        var negatives = await _api.SearchAsync(customerId, target.NegativesQuery, ct);
        var match = negatives.FirstOrDefault(row => target.Matches(row));
        return match.ValueKind == JsonValueKind.Undefined
            ? new AdTargetState(true, AdActionValues.Absent, null)
            : new AdTargetState(true, AdActionValues.Present, match.GetRawText());
    }

    public async Task<AdExecutionResult> ExecuteAsync(string customerId, AdAction action, bool validateOnly, CancellationToken ct)
    {
        if (TryParse(action, out var target) is { } error)
            return GoogleAdsExecutionResults.Failed(error);

        var request = target!.Level == AdEntityLevel.Campaign
            ? GoogleAdsMutations.CreateCampaignNegative(customerId, target.ParentId, target.Text, target.MatchType, validateOnly)
            : GoogleAdsMutations.CreateAdGroupNegative(customerId, target.ParentId, target.Text, target.MatchType, validateOnly);
        var response = await _api.MutateAsync(customerId, target.Service, request, ct);
        return GoogleAdsExecutionResults.From(response, AdActionValues.Absent, AdActionValues.Present, validateOnly);
    }

    public async Task<AdExecutionResult> RevertAsync(
        string customerId, AdAction action, AdExecutionResult original, bool validateOnly, CancellationToken ct)
    {
        if (original.Outcome != AdExecutionOutcome.Succeeded || original.PlatformResourceId is null)
            return GoogleAdsExecutionResults.Failed("The negative keyword was never created; there is nothing to remove.");

        var match = CriterionResourceName.Match(original.PlatformResourceId);
        if (!match.Success || match.Groups["customer"].Value != customerId)
            return GoogleAdsExecutionResults.Failed(
                $"'{original.PlatformResourceId}' is not a negative keyword criterion of account {customerId}.");

        var response = await _api.MutateAsync(
            customerId, match.Groups["collection"].Value, GoogleAdsMutations.Remove(original.PlatformResourceId, validateOnly), ct);
        return GoogleAdsExecutionResults.From(response, AdActionValues.Present, AdActionValues.Absent, validateOnly);
    }

    /// <returns>null and a target when the action is well-formed; otherwise the reason it is not.</returns>
    private static string? TryParse(AdAction action, out NegativeKeywordTarget? target)
    {
        target = null;
        if (action.TargetLevel is not (AdEntityLevel.Campaign or AdEntityLevel.AdGroup))
            return $"AddNegativeKeyword targets a Campaign or AdGroup, got {action.TargetLevel}.";
        if (!GoogleAdsIds.IsNumericId(action.TargetExternalId))
            return $"Target '{action.TargetExternalId}' is not a numeric Google Ads id.";

        var text = Normalize(action.Payload.TryGetValue(AdActionPayloadKeys.Text, out var t) ? t : null);
        if (text.Length is 0 or > MaxKeywordLength || text.Split(' ').Length > MaxKeywordWords)
            return $"Negative keyword text must be 1-{MaxKeywordLength} characters and at most {MaxKeywordWords} words.";

        var matchTypeName = action.Payload.TryGetValue(AdActionPayloadKeys.MatchType, out var m) ? m : null;
        if (matchTypeName is null || !Enum.GetNames<KeywordMatchType>().Contains(matchTypeName))
            return $"Unknown match type '{matchTypeName}'; expected one of {string.Join(", ", Enum.GetNames<KeywordMatchType>())}.";

        target = new NegativeKeywordTarget(
            action.TargetLevel, action.TargetExternalId, text, Enum.Parse<KeywordMatchType>(matchTypeName));
        return null;
    }

    private static string Normalize(string? value) => Whitespace.Replace((value ?? string.Empty).Trim(), " ");

    private sealed record NegativeKeywordTarget(AdEntityLevel Level, string ParentId, string Text, KeywordMatchType MatchType)
    {
        private string CriterionProperty => Level == AdEntityLevel.Campaign ? "campaignCriterion" : "adGroupCriterion";

        public string Service => Level == AdEntityLevel.Campaign
            ? GoogleAdsMutateServices.CampaignCriteria
            : GoogleAdsMutateServices.AdGroupCriteria;

        public GoogleAdsQuery ParentQuery => Level == AdEntityLevel.Campaign
            ? GoogleAdsExecutorQueries.Campaign(ParentId)
            : GoogleAdsExecutorQueries.AdGroup(ParentId);

        public GoogleAdsQuery NegativesQuery => Level == AdEntityLevel.Campaign
            ? GoogleAdsExecutorQueries.CampaignNegatives(ParentId)
            : GoogleAdsExecutorQueries.AdGroupNegatives(ParentId);

        public bool Matches(JsonElement row) =>
            string.Equals(Normalize(GoogleAdsJson.String(row, CriterionProperty, "keyword", "text")), Text, StringComparison.OrdinalIgnoreCase)
            && GoogleAdsMappings.KeywordMatch(GoogleAdsJson.String(row, CriterionProperty, "keyword", "matchType")) == MatchType;
    }
}
```

- [ ] **Step 4: Wire it into the executor**

In `GoogleAdsActionExecutor`: add the field `private readonly GoogleAdsNegativeKeywordHandler _negativeKeyword;`, initialise it in the constructor with `_negativeKeyword = new GoogleAdsNegativeKeywordHandler(api);`, and add one arm to each of the three `switch` expressions, directly after the `PauseAd` arm:

```csharp
            AdActionType.AddNegativeKeyword => await _negativeKeyword.ReadCurrentAsync(customerId, action, ct),
```
```csharp
                AdActionType.AddNegativeKeyword => await _negativeKeyword.ExecuteAsync(customerId, action, validateOnly, ct),
```
```csharp
                AdActionType.AddNegativeKeyword => await _negativeKeyword.RevertAsync(customerId, action, original, validateOnly, ct),
```

- [ ] **Step 5: Run to verify pass**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Adapters.GoogleAds.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.GoogleAds.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~Execution"
```
Expected: `Failed: 0`.

- [ ] **Step 6: Format and commit**

```bash
dotnet format Anela.Heblo.sln --include backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/ backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/
git add backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds backend/test/Anela.Heblo.Adapters.GoogleAds.Tests
git commit -m "feat: add and revert Google Ads negative keywords through the action executor

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task E4: validate_only, contract tests, registration, configuration

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsMarketingAdsServiceCollectionExtensions.cs`
- Modify: `backend/src/Anela.Heblo.API/appsettings.json` (`GoogleAds.ValidateOnly`), `backend/src/Anela.Heblo.API/appsettings.Production.json`
- Modify: `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/RegistrationTests.cs`
- Test: `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Execution/GoogleAdsValidateOnlyTests.cs`, `Execution/GoogleAdsActionExecutorContractTests.cs`

**Interfaces:**
- Consumes: E2/E3 executor; `AdActionExecutorContractTests` from C1.
- Produces: `IAdActionExecutor` registered (scoped) by `AddGoogleAdsMarketingAds`.

- [ ] **Step 1: Write the failing tests**

`Execution/GoogleAdsValidateOnlyTests.cs`:

```csharp
using Anela.Heblo.Adapters.GoogleAds.Execution;
using Anela.Heblo.Adapters.GoogleAds.Tests.Support;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Execution;

public sealed class GoogleAdsValidateOnlyTests
{
    [Fact]
    public async Task a_validated_pause_reports_failed_with_the_marker_and_changes_nothing()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api, validateOnly: true);

        var result = await executor.ExecuteAsync(ExecutorHarness.PauseAd("221~441"), CancellationToken.None);

        result.Outcome.Should().Be(AdExecutionOutcome.Failed);
        result.Error.Should().StartWith(GoogleAdsExecutionResults.ValidateOnlyMarker);
        result.PlatformResourceId.Should().BeNull();
        api.Mutations.Single().Request["validateOnly"]!.GetValue<bool>().Should().BeTrue();
        api.AdStatuses["221~441"].Should().Be("ENABLED");
    }

    [Fact]
    public async Task a_validated_negative_keyword_is_not_created()
    {
        var api = ExecutorHarness.SeededApi();
        var executor = ExecutorHarness.Create(api, validateOnly: true);
        var action = ExecutorHarness.AddNegative(AdEntityLevel.Campaign, "111", "zdarma", "Exact");

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        result.Error.Should().StartWith(GoogleAdsExecutionResults.ValidateOnlyMarker);
        api.Negatives.Should().BeEmpty();
        (await executor.ReadCurrentAsync(action, CancellationToken.None)).CurrentValue.Should().Be(AdActionValues.Absent);
    }

    [Fact]
    public async Task a_rejection_in_validate_only_mode_reports_the_google_error_not_the_marker()
    {
        var api = ExecutorHarness.SeededApi();
        api.RejectNextMutate("policyViolationError.POLICY_ERROR", "Policy violation.");
        var executor = ExecutorHarness.Create(api, validateOnly: true);

        var result = await executor.ExecuteAsync(ExecutorHarness.PauseAd("221~441"), CancellationToken.None);

        result.Error.Should().StartWith("policyViolationError.POLICY_ERROR");
    }
}
```

`Execution/GoogleAdsActionExecutorContractTests.cs`:

```csharp
using Anela.Heblo.Adapters.GoogleAds.Tests.Support;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.MarketingAds.TestKit;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Execution;

public sealed class GoogleAdsActionExecutorContractTests : AdActionExecutorContractTests
{
    protected override IAdActionExecutor CreateExecutor() => ExecutorHarness.Create(ExecutorHarness.SeededApi());

    protected override AdAction SamplePauseAd() => ExecutorHarness.PauseAd("221~441");

    protected override AdAction? SampleAddNegativeKeyword() =>
        ExecutorHarness.AddNegative(AdEntityLevel.Campaign, "111", "zdarma", nameof(KeywordMatchType.Exact));
}
```

Add to `RegistrationTests.cs` (with `using Anela.Heblo.Adapters.GoogleAds.Execution;` and `using Microsoft.Extensions.Options;`):

```csharp
    [Fact]
    public void registers_the_executor_with_validate_only_on_unless_configured_off()
    {
        var services = Register(Configured());
        services.AddLogging();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetServices<IAdActionExecutor>().Should().ContainSingle()
            .Which.Should().BeOfType<GoogleAdsActionExecutor>();
        provider.GetRequiredService<IOptionsMonitor<GoogleAdsSettings>>().CurrentValue.ValidateOnly.Should().BeTrue();
    }

    [Fact]
    public void registers_no_executor_when_unconfigured()
    {
        var settings = Configured();
        settings["GoogleAds:OAuth2RefreshToken"] = "-- stored in Key Vault --";

        Register(settings).Should().NotContain(d => d.ServiceType == typeof(IAdActionExecutor));
    }
```

- [ ] **Step 2: Run to verify failure**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Adapters.GoogleAds.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.GoogleAds.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~RegistrationTests|FullyQualifiedName~ValidateOnly|FullyQualifiedName~ExecutorContract"
```
Expected: `registers_the_executor_with_validate_only_on_unless_configured_off` FAILS (no `IAdActionExecutor` registered); the validate-only and contract tests already pass (they exercise E2/E3 code). If a contract test fails, fix the executor or the fake — never the base class.

- [ ] **Step 3: Register the executor**

In `AddGoogleAdsMarketingAds`, after the read-source line, add (and `using Anela.Heblo.Adapters.GoogleAds.Execution;`):

```csharp
        services.AddScoped<IAdActionExecutor, GoogleAdsActionExecutor>();
```

and extend its XML summary with: `Also registers the executor; nothing calls it until the core's execution pipeline (C3) is enabled by an admin, and GoogleAds:ValidateOnly defaults to true.`

- [ ] **Step 4: Configuration**

In `appsettings.json` add `"ValidateOnly": true,` to the `GoogleAds` section (after `"HebloUserEmail": "",`).

In `appsettings.Production.json` add a top-level section (keep the file's existing order; put it after `"BackgroundServices"`):

```json
  "GoogleAds": {
    "ValidateOnly": false
  },
```

Run: `python3 -c "import json;[json.load(open(p)) for p in ['backend/src/Anela.Heblo.API/appsettings.json','backend/src/Anela.Heblo.API/appsettings.Production.json']];print('json ok')"`
Expected: `json ok`.

- [ ] **Step 5: Run to verify pass**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Adapters.GoogleAds.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.GoogleAds.Tests --no-build -p:UseSharedCompilation=false
```
Expected: `Failed: 0` for the whole project.

- [ ] **Step 6: Format and commit**

```bash
dotnet format Anela.Heblo.sln --include backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/ backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/
git add backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds backend/test/Anela.Heblo.Adapters.GoogleAds.Tests backend/src/Anela.Heblo.API/appsettings.json backend/src/Anela.Heblo.API/appsettings.Production.json
git commit -m "feat: register Google Ads executor with validate-only on outside production

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task E5: Manual smoke test, docs, PR

**Files:**
- Create: `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Execution/SmokeFactAttribute.cs`, `Execution/SmokeEnvironment.cs`, `Execution/GoogleAdsValidateOnlySmokeTests.cs`
- Modify: `docs/integrations/google-ads-api.md` (§12), `docs/processes/sync-ad-platform-transactions.md` (`verified_at` only)
- Create: `docs/processes/flow-google-ads-action-execution.md`

**Interfaces:**
- Consumes: `AddGoogleAdsMarketingAds`, the env-file names from Task A1.
- Produces: a skipped-by-default test Ondrej runs against the live account in validate-only mode; the PR.

- [ ] **Step 1: Smoke test (skipped unless Ondrej opts in)**

`Execution/SmokeFactAttribute.cs`:

```csharp
namespace Anela.Heblo.Adapters.GoogleAds.Tests.Execution;

/// <summary>A Fact that runs only when HEBLO_GOOGLE_ADS_SMOKE_ENV points at a google-ads.env file.</summary>
public sealed class SmokeFactAttribute : FactAttribute
{
    public const string EnvFileVariable = "HEBLO_GOOGLE_ADS_SMOKE_ENV";

    public SmokeFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvFileVariable)))
            Skip = $"Manual smoke test: set {EnvFileVariable} (see docs/integrations/google-ads-api.md §12).";
    }
}
```

`Execution/SmokeEnvironment.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Execution;

/// <summary>Builds the real DI graph from the spike's env file, with ValidateOnly forced on.</summary>
internal sealed record SmokeEnvironment(string CustomerId, string AdGroupAdId, string CampaignId, IReadOnlyDictionary<string, string?> Configuration)
{
    public const string AdVariable = "HEBLO_GOOGLE_ADS_SMOKE_AD";
    public const string CampaignVariable = "HEBLO_GOOGLE_ADS_SMOKE_CAMPAIGN";

    public static SmokeEnvironment Load()
    {
        var env = File.ReadAllLines(Environment.GetEnvironmentVariable(SmokeFactAttribute.EnvFileVariable)!)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#') && line.Contains('='))
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim().Trim('"', '\''));

        var customerId = env["GOOGLE_ADS_CUSTOMER_ID"].Replace("-", string.Empty);
        var configuration = new Dictionary<string, string?>
        {
            ["GoogleAds:CustomerId"] = customerId,
            ["GoogleAds:LoginCustomerId"] = env.GetValueOrDefault("GOOGLE_ADS_LOGIN_CUSTOMER_ID", string.Empty),
            ["GoogleAds:OAuth2ClientId"] = env["GOOGLE_ADS_OAUTH_CLIENT_ID"],
            ["GoogleAds:OAuth2ClientSecret"] = env["GOOGLE_ADS_OAUTH_CLIENT_SECRET"],
            ["GoogleAds:OAuth2RefreshToken"] = env["GOOGLE_ADS_REFRESH_TOKEN"],
            ["GoogleAds:ValidateOnly"] = "true",
        };
        return new SmokeEnvironment(
            customerId,
            Environment.GetEnvironmentVariable(AdVariable) ?? throw new InvalidOperationException($"Set {AdVariable}=<adGroupId>~<adId>."),
            Environment.GetEnvironmentVariable(CampaignVariable) ?? throw new InvalidOperationException($"Set {CampaignVariable}=<campaignId>."),
            configuration);
    }

    public ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddGoogleAdsMarketingAds(new ConfigurationBuilder().AddInMemoryCollection(Configuration).Build());
        return services.BuildServiceProvider(validateScopes: true);
    }
}
```

`Execution/GoogleAdsValidateOnlySmokeTests.cs`:

```csharp
using Anela.Heblo.Adapters.GoogleAds.Execution;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Execution;

/// <summary>
/// Live, validate-only check against the real account. Category=Integration keeps it out of CI; the
/// SmokeFact skips it unless HEBLO_GOOGLE_ADS_SMOKE_ENV is set. Nothing is changed in the account.
/// </summary>
[Trait("Category", "Integration")]
public sealed class GoogleAdsValidateOnlySmokeTests
{
    [SmokeFact]
    public async Task google_accepts_both_actions_in_validate_only_mode_and_nothing_changes()
    {
        var env = SmokeEnvironment.Load();
        using var provider = env.BuildProvider();
        provider.GetRequiredService<IOptionsMonitor<GoogleAdsSettings>>().CurrentValue.ValidateOnly
            .Should().BeTrue("the smoke test must never write");
        using var scope = provider.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<IAdActionExecutor>();

        var pause = new AdAction(AdActionType.PauseAd, AdPlatform.GoogleAds, env.CustomerId, AdEntityLevel.Ad,
            env.AdGroupAdId, AdActionValues.Enabled, AdActionValues.Paused, new Dictionary<string, string>());
        var negative = new AdAction(AdActionType.AddNegativeKeyword, AdPlatform.GoogleAds, env.CustomerId,
            AdEntityLevel.Campaign, env.CampaignId, AdActionValues.Absent, AdActionValues.Present,
            new Dictionary<string, string>
            {
                [AdActionPayloadKeys.Text] = "heblo smoke test",
                [AdActionPayloadKeys.MatchType] = nameof(KeywordMatchType.Exact),
            });

        foreach (var action in new[] { pause, negative })
        {
            var before = await executor.ReadCurrentAsync(action, CancellationToken.None);
            before.Exists.Should().BeTrue($"{action.Type} target {action.TargetExternalId} must exist");

            var result = await executor.ExecuteAsync(action, CancellationToken.None);
            result.Error.Should().StartWith(GoogleAdsExecutionResults.ValidateOnlyMarker,
                "Google must accept the {0} request; got: {1}", action.Type, result.Error);

            (await executor.ReadCurrentAsync(action, CancellationToken.None)).CurrentValue
                .Should().Be(before.CurrentValue, "validate-only must not change the account");
        }
    }
}
```

Run:
```bash
dotnet build backend/test/Anela.Heblo.Adapters.GoogleAds.Tests -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.GoogleAds.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~Smoke"
```
Expected: `Skipped: 1` (you never set the variable yourself).

- [ ] **Step 2: Integration doc §12**

Replace §12 of `docs/integrations/google-ads-api.md` with:

```markdown
## 12. Manual smoke procedure (executor, validate-only)

Run by Ondrej, never by an agent. Nothing in the account changes: the test forces
`GoogleAds:ValidateOnly = true`, Google validates each request and discards it.

1. Pick an **enabled** ad (`ad_group.id~ad_group_ad.ad.id`, e.g. from Google Ads UI → Ads, columns
   "Ad group ID" and "Ad ID") and a campaign id.
2. From the repo root:
   ```bash
   export HEBLO_GOOGLE_ADS_SMOKE_ENV=~/Work/heblo-marketing-agents/google-ads.env
   export HEBLO_GOOGLE_ADS_SMOKE_AD=<adGroupId>~<adId>
   export HEBLO_GOOGLE_ADS_SMOKE_CAMPAIGN=<campaignId>
   dotnet build backend/test/Anela.Heblo.Adapters.GoogleAds.Tests -p:UseSharedCompilation=false
   dotnet test backend/test/Anela.Heblo.Adapters.GoogleAds.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~GoogleAdsValidateOnlySmokeTests"
   ```
3. Expected: `Passed: 1`. A failure message carries Google's error code (§8) — e.g.
   `authorizationError.USER_PERMISSION_DENIED` means the Heblo user has read-only access and needs Standard.
4. Check Google Ads → Change history: no entry was created.

Live writes happen only through an approved proposal in production, with the kill switch on
(spec 6.6) and `GoogleAds:ValidateOnly = false` (production only).
```

- [ ] **Step 3: Process doc for the executor**

Create `docs/processes/flow-google-ads-action-execution.md` (`verified_at` = `git rev-parse --short=9 HEAD`; add the core C3 execution process doc name to `related` if `ls docs/processes | grep -i -E "ads|proposal"` shows one):

```markdown
---
process: flow-google-ads-action-execution
kind: workflow
module: marketing-ads
summary: Executes approved Google Ads proposal actions — add/remove a negative keyword, pause/re-enable an ad — through the Google Ads REST API v25, validate-only outside production.
owns:
  - backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Execution/**
verified_at: "<short sha>"
related: [sync-google-ads-campaign-data]
---

# Google Ads action execution

## Purpose
The "hands" for Google Ads: once a marketing proposal is approved, Heblo — not the agent — makes
the change in the account, records the before-state and can revert it.

## Trigger
Called by the core proposal execution job (spec 6.2) per action after final approval, and by the
admin-only revert in the web UI. Nothing calls it until the core execution pipeline is merged and
an admin turns execution on (kill switch, spec 6.6).

## Data flow
1. `ReadCurrentAsync` → GAQL on the target (integration doc §4 `executor_*`) → `AdTargetState`
   (`Absent|Present` for negatives, `Enabled|Paused|Removed` for ads). The core compares it to the action's old value.
2. `ExecuteAsync` → `campaignCriteria:mutate` / `adGroupCriteria:mutate` (create negative) or
   `adGroupAds:mutate` (status PAUSED, `updateMask: status`).
3. `RevertAsync` → remove the criterion named by the stored `PlatformResourceId`, or set the ad back to ENABLED.

## Logic & formulas
- Negative keyword presence: same match type and same text, case-insensitive, whitespace collapsed; compared in C#, never in GAQL.
- Text 1–80 characters, at most 10 words; match type must be a `KeywordMatchType` name.
- Platform rejections (HTTP 400/404/409) → `Failed` with Google's error code; auth/transport failures throw.
- Mutates retry only HTTP 429. A 5xx or timeout is not retried (the change may have been applied; the change sync will show it).
- `GoogleAds:ValidateOnly = true` → request sent with `validateOnly`, result `Failed` with an error starting `ValidateOnly:`.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `GoogleAds:ValidateOnly` | `true` (`false` only in `appsettings.Production.json`) | dry-run every mutate |
| other `GoogleAds:*` | see `sync-google-ads-campaign-data` | shared with the read source |

## Runtime facts
None yet — no live write has been made. Record the first production execution here.

## Known quirks
- Staging and production point at the same live Google Ads account; that is why only production may write.
- The executor trusts the core for the old-value check and the limits; it only validates shape, account and ids.

## Code entry points
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Execution/GoogleAdsActionExecutor.cs` — dispatch, validate-only, logging
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Execution/GoogleAdsNegativeKeywordHandler.cs` — negative keywords
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Execution/GoogleAdsPauseAdHandler.cs` — ad pause
- `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Execution/GoogleAdsValidateOnlySmokeTests.cs` — manual live check
```

Replace `<short sha>` before committing. Bump `verified_at` in `docs/processes/sync-ad-platform-transactions.md` (it owns `Adapters.GoogleAds/**`; behaviour unchanged) and, because `Api/` changed, in `docs/processes/sync-google-ads-campaign-data.md` (add one Known-quirks bullet: "Mutates retry only HTTP 429; reads retry 408/429/5xx and timeouts."). Then:

```bash
python3 scripts/process-docs/check.py index
python3 scripts/process-docs/check.py check
```
Expected: exit 0.

- [ ] **Step 4: Full validation**

Run:
```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Adapters.GoogleAds.Tests --no-build -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~GoogleAds|FullyQualifiedName~Architecture"
dotnet format Anela.Heblo.sln --verify-no-changes --include backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/ backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/
git diff origin/main --stat -- backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsInvoiceImportJob.cs backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsTransactionSource.cs backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/SdkAccountBudgetFetcher.cs backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsAdapterServiceCollectionExtensions.cs
```
Expected: `0 Error(s)`; all tests `Failed: 0` (smoke `Skipped: 1`); format exit 0; the last command prints nothing.

- [ ] **Step 5: Commit, push, PR**

```bash
git add backend/test/Anela.Heblo.Adapters.GoogleAds.Tests docs/integrations/google-ads-api.md docs/processes
git commit -m "docs: Google Ads executor smoke procedure and process doc

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git push -u origin feat/google-ads-executor
gh pr create --base main --title "feat: Google Ads action executor (negative keyword, pause ad)" --body "$(cat <<'EOF'
## Summary
- `GoogleAdsActionExecutor : IAdActionExecutor` for `AddNegativeKeyword` (campaign / ad group) and `PauseAd`; revert removes the criterion by `PlatformResourceId` or re-enables the ad.
- Mutate transport: platform rejections become `Failed`, only HTTP 429 is retried (a 5xx/timeout may already be applied).
- `GoogleAds:ValidateOnly` defaults to **true**; only `appsettings.Production.json` sets it to false. A validated request reports `Failed` with a `ValidateOnly:` error so nothing believes the account changed.
- Keyword text is matched in C#, never interpolated into GAQL; ids are checked numeric.
- Tests run on a stateful fake account and inherit `AdActionExecutorContractTests`. No live write was made.

## Manual smoke (Ondrej)
`docs/integrations/google-ads-api.md` §12 — validate-only, nothing changes in the account.

## Test plan
- [x] `dotnet test backend/test/Anela.Heblo.Adapters.GoogleAds.Tests` (smoke skipped)
- [x] `python3 scripts/process-docs/check.py check`
- [ ] Ondrej: run the §12 smoke test against the live account

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 6: Tell Ondrej** — PR link, and the three `export` lines from §12 so he can run the smoke test.

---

## Self-review record (plan author)

- **Spec coverage:** spike (spec 9.1, handoff 5.1) → Part A; read source with accounts/entities/facts/search terms/change log, settings + KV names, gated registration, contract tests, process doc (spec 4.2, 9.2, 12) → B1–B9; executor for both v1 actions with ReadCurrent/Execute/Revert, contract tests, manual smoke (spec 4.3, 9.3, 12.2) → E1–E5; why `ImportedMarketingTransactions` is empty → A4 + B1.
- **Review Focus coverage:** omitted metrics (B6 `treats_metrics_google_omits_as_zero`), search-term duplicates (B6 `merges_near_exact_into_exact`), 30-day clamp (B7 `clamps_a_watermark_older_than_the_30_day_window`), mutate retries (E1 `does_not_retry_a_mutate_that_failed_with_503`), GAQL injection (E3 `never_puts_keyword_text_into_a_query`, B3 `rejects_a_non_numeric_customer_id_without_calling_google`).
- **Type consistency:** `IGoogleAdsApiClient.SearchAsync/MutateAsync`, `GoogleAdsQuery(Name, Gaql)`, `GoogleAdsMutateResult`, `GoogleAdsExecutionResults.ValidateOnlyMarker`, `ExecutorHarness.AddNegative(level, target, text, matchType:string)` are used with the same signatures in every task.
