# Marketing agents platform — measurement backbone + proposal/approval layer

- **Date:** 2026-10-07
- **Status:** Draft for review
- **Context:** [`docs/handoff/marketing-agents-platform.md`](../../handoff/marketing-agents-platform.md) —
  the brainstorm outcome. Its section 2 (decisions) and section 3 (security requirements) are
  binding and are not repeated here in full.

## 1. Goal and scope

Heblo becomes the eyes and the hands of AI marketing agents that run outside Heblo: it syncs
ad-platform data at management granularity, accepts structured **proposals** from agents and
people over MCP, lets authorised humans approve them, executes approved proposals through its own
platform adapters, and records everything — including changes made outside Heblo.

**In scope:** sub-projects 1 (measurement backbone) and 6 (proposal & approval layer) of the
handoff, for Google Ads, Meta Ads and Sklik.

**Out of scope:** the agents themselves, order-level attribution (GA4/UTM), budget/bid/new-campaign
actions (the engine is built to accept them later; no executor ships for them), any change to live
ad accounts outside an approved proposal.

**Success criteria**

1. Every day Heblo holds yesterday's (and a rolling lookback of) cost, clicks, impressions,
   conversions and conversion value per campaign / ad group / keyword / ad, plus search terms
   (Google, Sklik), for all three platforms.
2. Every change made to an ad account — by Heblo, the agency, a person in the platform UI or a
   platform's auto-applied recommendation — appears in Heblo within a day, flagged *Heblo* or
   *out-of-band*.
3. An agent with its own service identity can read that data and submit a proposal over MCP, and
   **cannot** approve anything.
4. The chief of marketing can list pending proposals and approve/reject them by id + version in
   Claude (MCP) or in the Heblo web UI; Heblo executes, verifies the before-state, and logs the
   result. Every write is revertible from the stored before-state.
5. A blended reality check (real revenue ÷ total ad spend) is available to agents (MCP) and to
   people (Metabase).

## 2. Decomposition into workspaces

The work is split into four independently implemented workspaces. Each gets its own
implementation plan and its own Orca worktree.

| WS | Name | Delivers | Depends on |
|---|---|---|---|
| 0 | `marketing-core` | Shared contracts, `ads` schema, sync orchestration, change detection, proposal/approval layer, identity, MCP tools, UI | — |
| 1 | `marketing-google-ads` | Access spike, `GoogleAdsReadSource`, `GoogleAdsActionExecutor` | Core **PR C1** |
| 2 | `marketing-meta` | Access spike, `MetaAdsReadSource`, `MetaAdsActionExecutor` | Core **PR C1** |
| 3 | `marketing-sklik` | Access spike, new Sklik adapter, `SklikReadSource`, `SklikActionExecutor` | Core **PR C1** |

### 2.1 Sequencing

```
WS0  C1 contracts ──► C2 sync+detection ──► C3 proposals ──► C4 identity+MCP ──► C5 UI
         │
         ├──► WS1  spike ─► read source PR ─► executor PR
         ├──► WS2  spike ─► read source PR ─► executor PR
         └──► WS3  spike ─► read source PR ─► executor PR
```

- **Spikes start immediately** in WS1–3; they need no core code.
- **C1 is deliberately small** (interfaces, value types, `ads` schema, fakes, contract-test base
  classes) and is merged first. Platform implementation PRs branch from `main` after C1 lands.
- A platform read source becomes live data as soon as both its PR and **C2** are merged.
- A platform executor can only ever run after **C3 + C4** are merged *and* an admin enables
  execution (section 6.6). Merging an executor earlier is safe: nothing calls it.

### 2.2 Core PRs

| PR | Contents |
|---|---|
| C1 | `AdPlatform`, value types, `IAdPlatformReadSource`, `IAdActionExecutor`, `AdAction` model, `AdsDbContext` + `ads` schema + migration, fake source/executor, abstract contract-test classes, ADR-008 |
| C2 | Daily sync job and change-history sync job over all registered sources, upsert of entities/facts/search terms, snapshot-diff fallback, out-of-band matcher, Metabase `v_*` views incl. blended view + grants, process docs |
| C3 | Proposal aggregate + versions, lifecycle, limits engine, autonomy settings, kill switch, execution pipeline (Hangfire), revert, append-only audit log, agent runs |
| C4 | New `Feature` values and seed groups, agent service identity, channel (`azp`) check, MCP tool surface |
| C5 | Approval inbox, audit/out-of-band/agent-run views, settings page (autonomy, limits, kill switch) |

## 3. Data placement (ADR-008)

ADR-007 puts reporting data in `Heblo_V3`, one schema per source, reported only in Metabase. This
design follows it with two deliberate, recorded deviations — written up as **ADR-008** in
`docs/architecture/development_guidelines.md` in PR C1:

1. **One `ads` schema for all three ad platforms**, not one per platform. The value of the
   backbone is a single normalised model that an agent can query the same way for every platform
   and that the limits engine can reason about. Platform-specific fields go to a `jsonb`
   `attributes` column.
2. **MediatR read handlers over `ads` are allowed**, because agents consume them through MCP to
   *manage* accounts. This is operational use, not reporting. Human-facing performance *reports*
   still live in Metabase only; the Heblo UI shows proposals, audit and settings, not charts.

| Data | Where | Owner |
|---|---|---|
| Ad accounts, entities, daily facts, search terms, change events, sync state | `ads` schema, `AdsDbContext` (own `NpgsqlDataSource` + Polly pipeline, `MigrationsHistoryTable` pinned to `ads`, gated on a non-empty connection string — same pattern as `Ga4DbContext`) | WS0 |
| Proposals, versions, audit log, agent runs, autonomy settings, limits, kill switch | `public` schema, `ApplicationDbContext` (operational workflow data) | WS0 |
| Metabase views `v_ads_*`, `v_ads_blended_*` | `ads` schema, granted to `metabase_ro` | WS0 |

## 4. Shared model (PR C1)

### 4.1 Tables in `ads`

| Table | Key columns | Notes |
|---|---|---|
| `ad_accounts` | `id`, `platform`, `external_id`, `name`, `currency`, `time_zone`, `is_managed` | `is_managed=true` only for Anela's own accounts. The limits engine refuses actions on unmanaged accounts. Unique (`platform`, `external_id`). |
| `ad_entities` | `id`, `account_id`, `level`, `external_id`, `parent_id`, `name`, `status`, `attributes jsonb`, `first_seen_at`, `last_seen_at`, `updated_at` | `level` ∈ `Campaign`, `AdGroup`, `Keyword`, `NegativeKeyword`, `Ad`. A Meta ad set is an `AdGroup`. Unique (`account_id`, `level`, `external_id`). Keyword text / match type, budgets, bidding strategy live in `attributes`. |
| `ad_daily_facts` | PK (`entity_id`, `date`) | `impressions`, `clicks`, `cost`, `conversions`, `conversion_value`, `currency`. Stored at every level the platform reports; **queries must aggregate a single level** (summing levels double-counts). Cost is net of VAT, in account currency. |
| `ad_search_term_daily` | PK (`ad_group_entity_id`, `date`, `search_term`, `match_type`) | Same metrics. Google + Sklik only. |
| `ad_change_events` | `id`, `account_id`, `external_event_id`, `occurred_at`, `actor`, `actor_kind`, `entity_id?`, `entity_external_ref`, `change_type`, `old_value jsonb`, `new_value jsonb`, `source`, `origin`, `matched_execution_id?` | `actor_kind` ∈ `Heblo`, `User`, `PlatformAutomation`, `Unknown`. `source` ∈ `PlatformChangeLog`, `SnapshotDiff`. `origin` ∈ `Heblo`, `OutOfBand`. Unique (`account_id`, `source`, `external_event_id`). |
| `sync_state` | PK (`platform`, `account_external_id`, `stream`) | `stream` ∈ `Entities`, `DailyFacts`, `SearchTerms`, `ChangeEvents`; `watermark`, `status`, `last_success_at`, `last_error`. |

### 4.2 Contracts

Internal types (records are fine — they never cross the API boundary). Namespace
`Anela.Heblo.Application.Features.MarketingAds.Contracts` (module name `MarketingAds`).

```csharp
public enum AdPlatform { GoogleAds, MetaAds, Sklik }
public enum AdEntityLevel { Campaign, AdGroup, Keyword, NegativeKeyword, Ad }

public interface IAdPlatformReadSource
{
    AdPlatform Platform { get; }
    AdSourceCapabilities Capabilities { get; }            // SearchTerms, ChangeLog, ChangeLogMaxAge
    Task<IReadOnlyList<AdAccountSnapshot>> GetAccountsAsync(CancellationToken ct);
    Task<IReadOnlyList<AdEntitySnapshot>> GetEntitiesAsync(string accountExternalId, CancellationToken ct);
    Task<IReadOnlyList<AdDailyFactRow>> GetDailyFactsAsync(string accountExternalId, DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<AdSearchTermRow>> GetSearchTermsAsync(string accountExternalId, DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<AdChangeEventRow>> GetChangeEventsAsync(string accountExternalId, DateTimeOffset since, CancellationToken ct);
}

public interface IAdActionExecutor
{
    AdPlatform Platform { get; }
    IReadOnlySet<AdActionType> SupportedActions { get; }
    Task<AdTargetState> ReadCurrentAsync(AdAction action, CancellationToken ct);
    Task<AdExecutionResult> ExecuteAsync(AdAction action, CancellationToken ct);
    Task<AdExecutionResult> RevertAsync(AdAction action, AdExecutionResult original, CancellationToken ct);
}
```

- Rows carry **platform external ids** only; the core maps them to `ad_entities.id`.
- Unsupported capabilities return an empty list, never throw (Meta search terms).
- Sources and executors throw on transport/auth errors; the core catches per source so one
  failing platform never blocks the others.
- Each platform registers its source/executor only when its settings are **really** configured:
  a non-empty value that is not a `-- stored in … --` placeholder.

### 4.3 Action model (v1 allowlist)

| `AdActionType` | Target | Payload | "Old value" checked at execution | Platforms |
|---|---|---|---|---|
| `AddNegativeKeyword` | campaign or ad group | `text`, `matchType` | the negative keyword is absent | Google, Sklik |
| `PauseAd` | ad | — | ad status is `Enabled` | Google, Meta, Sklik |

`AdAction` = (`type`, `platform`, `accountExternalId`, `targetExternalId`, `targetLevel`,
`oldValue`, `newValue`, `payload`), all serialised as JSON in the proposal version. New action
types are additive: a new enum value, a payload schema, a renderer and an executor.

### 4.4 Test support shipped in C1

- `FakeAdPlatformReadSource` / `FakeAdActionExecutor` so the core is built and tested with no
  platform present.
- `AdPlatformReadSourceContractTests<T>` and `AdActionExecutorContractTests<T>` — abstract xUnit
  bases that every platform test project inherits, run against recorded HTTP fixtures. They pin
  the cross-platform semantics (empty-not-throw, external ids non-empty, currency set, cost ≥ 0,
  `ReadCurrent` before `Execute`, `Revert` restores `ReadCurrent`'s value).

## 5. Measurement backbone (PR C2)

- **`AdsDailySyncJob`** — Hangfire recurring, 05:30 Europe/Prague, `[AutomaticRetry(Attempts = 0)]`.
  For every registered source and every managed account: accounts → entities → daily facts and
  search terms for **yesterday plus a 14-day lookback** (`Ads:FactLookbackDays`, conversions are
  attributed late), upserted by natural key. Writes `sync_state` per stream.
- **`AdsChangeSyncJob`** — hourly. Pulls `GetChangeEventsAsync(since watermark)` where
  `Capabilities.ChangeLog`; otherwise the **snapshot-diff fallback** compares each entity's
  `status`, `name` and `attributes` with the previous snapshot and writes a `SnapshotDiff` event
  with `actor_kind = Unknown`.
- **Out-of-band matcher.** A change event is `origin = Heblo` when it matches an executed
  proposal action (same entity, same change, within ±2 h of execution, and — when the platform
  reports an actor — the actor is Heblo's platform user). Everything else is `OutOfBand`.
- **Blended reality check** — `v_ads_blended_daily` / `_monthly`: revenue from `shoptet_raw`
  orders (net of VAT, excluding cancelled) ÷ total ad cost from `ad_daily_facts` at campaign
  level, all platforms. Exposed to agents via an MCP tool and to people via Metabase.
- Metabase views: `v_ads_campaign_monthly`, `v_ads_blended_monthly`, `v_ads_change_events`
  (no customer PII anywhere in `ads`).
- Process docs: `docs/processes/` entries for both jobs + module overview, per `CLAUDE.md`.

## 6. Proposal & approval layer (PR C3)

### 6.1 Aggregate

- `AdProposal`: `id`, `current_version`, `status`, `platform`, `account_external_id`,
  `action_type`, `created_by_principal`, `created_by_kind` (`Agent` | `Human`), `agent_run_id?`,
  `created_at`, `expires_at` (created + 72 h, `Ads:ProposalTtlHours`).
- `AdProposalVersion` (immutable): `proposal_id`, `version`, `actions jsonb` (1–50 `AdAction`s of
  the proposal's single type and account), `reasoning` (agent free text, shown separately),
  `created_by`, `created_at`. A revision inserts a new version; nothing is ever updated in place.
- Human-readable diff is **rendered by Heblo from `actions`**, never from `reasoning`.
- `AdProposalApproval`: `proposal_id`, `version`, `principal`, `channel`, `decision`, `at`. A
  proposal needing a second approver stays `Pending` after the first approval; the second must
  be a different principal on the same version. A revision voids earlier approvals.

### 6.2 Lifecycle

```
Pending ──approve(id,v)──► Approved ──► Executing ──► Executed
   │                                         ├──────► PartiallyExecuted
   │                                         └──────► Failed
   ├──reject(id,v)──► Rejected                Executed/PartiallyExecuted ──revert──► Reverted
   ├──ttl──────────► Expired
   └──revise───────► Pending (version+1, prior approvals void)
```

- Approve/reject must name the **current** version; otherwise 409.
- Execution runs as an enqueued Hangfire job right after the final approval. Actions run
  sequentially; per action: `ReadCurrentAsync` → compare to `oldValue` → on mismatch the action
  fails `StaleState` and the remaining actions are skipped → else `ExecuteAsync`, store the
  before-state and platform response.
- Revert: web UI only, `Marketing_AdApprovals` Admin, uses stored before-state per action.

### 6.3 Limits engine

Pure, unit-tested `IAdLimitsEvaluator.Evaluate(proposal, version, channel, approver) → Allowed |
NeedsSecondApprover | NeedsWebApproval | Denied(reason)`. Rules, all server-side and independent of
who approved:

- action type is on the allowlist and enabled for that platform;
- account is `is_managed`;
- per-proposal max actions (`AddNegativeKeyword` ≤ 50, `PauseAd` ≤ 10);
- per account per rolling 7 days: max paused ads (default 20);
- pausing an ad that would leave its ad group with no enabled ad → `NeedsWebApproval`;
- MCP channel has lower thresholds than web (configurable; v1 default: MCP may approve ≤ 20
  negatives / ≤ 3 paused ads per proposal, above that `NeedsWebApproval`);
- over any "second approver" threshold → `NeedsSecondApprover` (different principal).
  Future budget rules (>20 % change → web only) slot in here.

Limits are rows in `ad_limit_settings` (platform × action type × key → value), seeded with the
defaults above, editable by admins, every change audited.

### 6.4 Autonomy

`ad_autonomy_settings` (platform × action type → `ProposeOnly` | `AutoWithinLimits` | `Auto`),
seeded **`ProposeOnly` everywhere**. In `AutoWithinLimits`, a proposal whose evaluation is
`Allowed` is auto-approved by the system principal (`channel = System`) and audited as such.
Changing a mode needs `Marketing_AdApprovals` Admin. The approval-without-edit rate per action type
is shown next to the setting to inform the decision.

### 6.5 Audit log and agent runs

- `ad_audit_events` — append-only: proposal id, version, event (`Submitted`, `Revised`,
  `Approved`, `Rejected`, `Expired`, `ExecutionStarted`, `ActionExecuted`, `ActionFailed`,
  `Reverted`, `SettingChanged`, `KillSwitchChanged`), actor principal, actor kind, channel
  (`Web` | `Mcp` | `System`), payload, timestamp. Enforced by a Postgres trigger rejecting `UPDATE`
  and `DELETE`, covered by an integration test.
- `agent_runs` — agent name, principal, started/finished, summary, data scopes read, proposals
  created, approximate cost (tokens/CZK), status. Reported by agents over MCP.

### 6.6 Kill switch

A single persisted switch (+ optional per-platform override), checked by the execution job
**immediately before every action**. Default after deployment: **execution disabled** — an admin
turns it on deliberately once a platform executor is live. Flipping it is audited.

## 7. Identity, authorization, MCP (PR C4)

### 7.1 How Heblo authenticates today (verified 2026-10-07)

- The API accepts Entra bearer tokens and cookies (`AuthenticationExtensions.cs`); scopes are never
  enforced, so an **app-only token with a `roles` claim already authenticates**.
- Permissions come from the **database**, not from Entra: `PermissionClaimsTransformation` takes
  the caller's `oid`, `PermissionResolver` auto-creates an `AppUser` for it and resolves its
  `PermissionGroup`s. The one token-based shortcut is the `super_user` app role, which grants
  everything — **it must never be assigned to an agent identity.**
- The in-API MCP server is `MapMcp("/mcp")`; tools gate with `EnsureFeatureAccess`. Its OAuth
  metadata advertises the **API's own client id** (`AzureAd:ClientId`) with `access_as_user`, so
  Claude connectors carry `azp = AzureAd:ClientId`. The web SPA carries `azp =`
  `REACT_APP_AZURE_CLIENT_ID`. The separate HebloMCP (Python) calls REST with a forwarded
  delegated token.
- Nothing reads `azp`/`appid` today.

### 7.2 Features and groups

New entries in `access-matrix.json` (regenerate with `AccessMatrixGen`):

| Feature | Read | Write | Admin |
|---|---|---|---|
| `Marketing_Ads` "Reklamní kampaně" | ad data, proposals, audit | submit / revise proposals, report agent runs | — |
| `Marketing_AdApprovals` "Schvalování kampaní" | — | approve / reject in the **web UI** | autonomy, limits, kill switch, revert |
| `Marketing_AdApprovalsViaMcp` "Schvalování přes Claude" | approve / reject **via MCP** (requires `Marketing_AdApprovals` Write as well) | — | — |

Seed groups: `Marketing_Agent` (Ads W), `Marketer` gains Ads W, `Marketing_Specialist`
(+ AdApprovals W), `Marketing_Chief` (+ AdApprovalsViaMcp R), `Spravce` gains all incl.
AdApprovals Admin. **`JsonGroupSeeder` only inserts missing groups**, so existing prod groups do
not receive new permissions automatically: C4's rollout includes an explicit, reviewed grant step
in the admin UI (or a one-off SQL in the PR description), checked on staging first.

### 7.3 Agent service identity

- New Entra app registration **`heblo-marketing-agent`** (one per agent if we want per-agent
  attribution later; one to start). Client-credentials flow, secret/certificate held by the agent
  runtime, never by Heblo.
- An app role **`Marketing.Agent`** on the Heblo API app registration, assigned to that service
  principal, so its token carries `roles` and authenticates. No other app role.
- Its `oid` becomes an `AppUser` (auto-created on first call) that an admin puts into the
  `Marketing_Agent` group — nothing else.
- Agents call the in-API `/mcp` endpoint with that bearer token (Entra access tokens live 60–90
  min; revocation = disable the service principal). HebloMCP's client-credentials mode is
  out of scope; agents use `/mcp` directly.
- `/mcp` gets an ASP.NET rate limiter partitioned by `oid` for app-only callers
  (`Ads:AgentRateLimitPerMinute`, default 60).

### 7.4 Approval channel and the hard rules

`IApprovalChannelResolver` (unit-tested, used by every approve/reject path):

1. **App-only token** (no `scp` claim, or `idtyp = app`) → **deny**, whatever permissions the
   principal holds. A service identity can never approve.
2. `azp` (v2) or `appid` (v1) ∈ `Ads:Approval:WebClientIds` → channel `Web`; requires
   `Marketing_AdApprovals` Write.
3. `azp`/`appid` ∈ `Ads:Approval:McpClientIds` (default: `AzureAd:ClientId`, plus HebloMCP's
   client id) → channel `Mcp`; requires `Marketing_AdApprovals` Write **and**
   `Marketing_AdApprovalsViaMcp` Read, and the stricter MCP limits apply.
4. A cookie-authenticated session (the web app's own login, incl. the E2E session) → `Web`.
   A bearer token from any other client → **deny**.

Enforcement lives in the MediatR handlers, not in tool exposure, so REST and MCP get the same
rules. `ICurrentUserService` is extended with `ClientAppId` and `IsAppOnly`.

### 7.5 MCP tool surface (`MarketingAdsMcpTools`)

| Tool | Gate | Purpose |
|---|---|---|
| `ListAdAccounts` | Ads R | managed accounts per platform |
| `GetAdPerformance` | Ads R | facts for a platform/account/level/date range, optional parent filter, paged |
| `GetAdSearchTerms` | Ads R | search terms for an ad group/campaign and range |
| `GetBlendedPerformance` | Ads R | real revenue ÷ total ad spend, daily or monthly |
| `GetAdChangeHistory` | Ads R | change events, filter by origin/actor |
| `GetAdGuardrails` | Ads R | allowlist, limits, autonomy modes — so agents propose within them |
| `SubmitAdProposal` | Ads W | returns id, version, limits evaluation |
| `ReviseAdProposal` | Ads W | `id`, `expectedVersion`, new actions/reasoning |
| `ListAdProposals` / `GetAdProposal` | Ads R | filter by status/creator ("mine") |
| `StartAgentRun` / `FinishAgentRun` | Ads W | observability of the agents themselves |
| `ListPendingAdProposals` | AdApprovals W | the chief's "what's pending?", numbered, with rendered diffs |
| `ApproveAdProposal` / `RejectAdProposal` | channel rules 7.4 | `id` + `version` (+ reason on reject) |

All tool outputs render diffs from the structured payload. No tool returns customer PII.

## 8. UI (PR C5)

Marketing section, new pages (light + dark mode, `layout_definition.md`):

- `/marketing/ads/proposals` — approval inbox: pending list, detail with the rendered diff, the
  agent's reasoning in a separate block, limits evaluation, approve/reject (version-bound),
  history tab.
- `/marketing/ads/activity` — audit log, out-of-band changes (filterable by platform/actor) and
  agent runs.
- `/marketing/ads/settings` — autonomy modes, limits, kill switch (admin only).

Performance charts are **not** built in Heblo (ADR-007/008): the pages link to the Metabase
dashboards built on the `v_ads_*` views.

## 9. Platform workspaces (WS1–WS3)

Each platform workspace follows the same three steps.

1. **Access spike (throwaway, read-only, before any code).** One call for yesterday's
   campaign spend and one for change history, run from a scratch script with credentials Ondrej
   supplies in `~/Work/heblo-marketing-agents/<platform>.env`. Confirms Anela owns admin access
   itself (not only through the agency). Findings — access level, limits, quirks — are written to
   `docs/integrations/<platform>-api.md` **before** code relies on them. If the spike fails,
   the workspace stops and reports; it does not code around missing access.
2. **Read source PR** (after C1): `I<Platform>ReadSource` implementation, settings + KV secret
   names, registration gated on real configuration, contract tests on recorded fixtures, process
   doc for the platform stream.
3. **Executor PR** (after C1): `IAdActionExecutor` for the platform's v1 actions, contract tests,
   a documented manual smoke procedure. No live write is ever run by the workspace itself.

| | Google Ads (WS1) | Meta (WS2) | Sklik (WS3) |
|---|---|---|---|
| Existing code | `Adapters.GoogleAds` (SDK `Google.Ads.GoogleAds` 21.1.0, billing only) | `Adapters.MetaAds` (Graph v21, billing only) | none — new `Adapters.Sklik` |
| Credentials | customer id, developer token (**Basic** access needed), OAuth client + refresh token of a Standard-access user, `login-customer-id` if via MCC | `act_…`, system-user token from Anela's Business Manager, `ads_read` (+ `ads_management` for the executor) | API token, client user id if access is via a shared login |
| Read | GAQL on `campaign`, `ad_group`, `ad_group_criterion`, `ad_group_ad`, `search_term_view`, `segments.date` | Insights `level=campaign/adset/ad`, `time_increment=1`; entity lists | Drak JSON API: `campaigns/groups/keywords/ads.list`, stats via `createReport`/`readReport`, search queries report |
| Change log | `change_event` (≤ 30 days back, ≤ 10k rows/query) | `/act_…/activities` | unknown → spike decides; else snapshot-diff fallback |
| Executor | negative keyword criterion (campaign/ad group), `AdGroupAd.status = PAUSED`; tests use `validate_only` | `POST /{ad_id} status=PAUSED` | negative keyword, ad pause via Drak API |

The existing billing importers (`*InvoiceImportJob`) are left untouched; the spike explains why
`ImportedMarketingTransactions` is empty (current finding: no Google/Meta secrets exist in either
Key Vault or App Settings).

## 10. Testing strategy

- **Unit:** limits evaluator (table-driven), lifecycle transitions, out-of-band matcher,
  snapshot-diff, diff renderer, channel resolver, autonomy auto-approval.
- **Contract:** the C1 abstract suites, inherited by each platform on recorded JSON fixtures
  (assertions parse JSON, never substring-match).
- **Integration (Postgres, `Category=Integration`):** `AdsDbContext` migrations + history-table
  pin, upserts, append-only trigger, persistence verified through a second `DbContext`.
- **API/MCP:** approval with web token vs MCP token vs app-only token; stale version → 409;
  service principal can never approve even when granted the feature.
- **E2E (nightly, staging):** approval inbox happy path with fake-executor-seeded data in
  `frontend/test/e2e/marketing/`.
- **Manual:** per-platform smoke — Google `validate_only`, Meta/Sklik on a deliberately paused
  test ad — documented in each integration doc, run by Ondrej.

## 11. Risks

| Risk | Mitigation |
|---|---|
| Anela lacks own admin access / developer token belongs to the agency MCC | Spike first; WS stops and reports |
| Google developer token stuck at Test/Explorer access | Apply for Basic early; read source can be built on fixtures meanwhile |
| Prompt injection via search terms, reviews | Server-side limits independent of approver; agents never write; MCP approvals capped lower |
| Server load (B1ms `heblosql`) | Own small pool for `AdsDbContext`; daily batch off-peak; 14-day lookback bounded |
| Platform conversions differ from reality | Blended reality check next to platform numbers |

## 12. Canonical names (binding for all plans)

Fixed here so the eight implementation plans, written in parallel, agree.

### 12.1 Placement

| What | Where |
|---|---|
| Contracts (enums, records, interfaces, constants, settings guard) | `backend/src/Anela.Heblo.Application/Features/MarketingAds/Contracts/`, namespace `Anela.Heblo.Application.Features.MarketingAds.Contracts`, one type per file |
| Core application module (sync, proposals, MCP handlers) | `backend/src/Anela.Heblo.Application/Features/MarketingAds/` (`MarketingAdsModule.cs`) |
| `ads` schema persistence | new project `backend/src/Anela.Heblo.Persistence.Ads/` (`AdsDbContext`, `SchemaName = "ads"`, `AdsPersistenceModule`, connection key `AdsDatabase:ConnectionString`, KV `AdsDatabase--ConnectionString`) — mirrors `Persistence.Ga4` |
| Proposal / audit / settings entities | `backend/src/Anela.Heblo.Domain/Features/MarketingAds/`, EF config in `backend/src/Anela.Heblo.Persistence/MarketingAds/`, `ApplicationDbContext` |
| MCP tools | `backend/src/Anela.Heblo.API/MCP/Tools/MarketingAdsMcpTools.cs` |
| Shared test kit (fakes + contract-test bases) | new project `backend/test/Anela.Heblo.MarketingAds.TestKit/`, namespace `Anela.Heblo.MarketingAds.TestKit` |
| Platform adapters | `Adapters/Anela.Heblo.Adapters.GoogleAds` (existing), `Adapters/Anela.Heblo.Adapters.MetaAds` (existing), `Adapters/Anela.Heblo.Adapters.Sklik` (new) |
| Platform test projects | new `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests`, `…MetaAds.Tests`, `…Sklik.Tests` |
| Platform classes | `GoogleAdsReadSource` / `GoogleAdsActionExecutor`, `MetaAdsReadSource` / `MetaAdsActionExecutor`, `SklikReadSource` / `SklikActionExecutor` |
| Integration docs | `docs/integrations/google-ads-api.md`, `meta-ads-api.md`, `sklik-api.md` |

### 12.2 Contract types (PR C1 creates exactly these)

```csharp
public enum AdPlatform { GoogleAds = 1, MetaAds = 2, Sklik = 3 }
public enum AdEntityLevel { Campaign = 1, AdGroup = 2, Keyword = 3, NegativeKeyword = 4, Ad = 5 }
public enum AdEntityStatus { Unknown = 0, Enabled = 1, Paused = 2, Removed = 3 }
public enum KeywordMatchType { Exact = 1, Phrase = 2, Broad = 3 }
public enum AdChangeActorKind { Unknown = 0, Heblo = 1, User = 2, PlatformAutomation = 3 }
public enum AdActionType { AddNegativeKeyword = 1, PauseAd = 2 }
public enum AdExecutionOutcome { Succeeded = 1, Failed = 2, StaleState = 3 }

public sealed record AdSourceCapabilities(bool SearchTerms, bool ChangeLog, TimeSpan? ChangeLogMaxAge);
public sealed record AdAccountSnapshot(string ExternalId, string Name, string Currency, string TimeZone);
public sealed record AdEntitySnapshot(
    AdEntityLevel Level, string ExternalId, AdEntityLevel? ParentLevel, string? ParentExternalId,
    string Name, AdEntityStatus Status, IReadOnlyDictionary<string, string?> Attributes);
public sealed record AdDailyFactRow(
    AdEntityLevel Level, string EntityExternalId, DateOnly Date, long Impressions, long Clicks,
    decimal Cost, decimal Conversions, decimal ConversionValue, string Currency);
public sealed record AdSearchTermRow(
    string AdGroupExternalId, DateOnly Date, string SearchTerm, KeywordMatchType? MatchType,
    long Impressions, long Clicks, decimal Cost, decimal Conversions, decimal ConversionValue, string Currency);
public sealed record AdChangeEventRow(
    string ExternalEventId, DateTimeOffset OccurredAt, string? Actor, AdChangeActorKind ActorKind,
    AdEntityLevel? EntityLevel, string? EntityExternalId, string ChangeType,
    string? OldValueJson, string? NewValueJson);

public sealed record AdAction(
    AdActionType Type, AdPlatform Platform, string AccountExternalId,
    AdEntityLevel TargetLevel, string TargetExternalId,
    string OldValue, string NewValue, IReadOnlyDictionary<string, string> Payload);
public sealed record AdTargetState(bool Exists, string? CurrentValue, string? RawJson);
public sealed record AdExecutionResult(
    AdExecutionOutcome Outcome, string? BeforeValue, string? AfterValue,
    string? PlatformResourceId, string? PlatformResponseJson, string? Error);

public static class AdActionValues   { public const string Absent = "Absent", Present = "Present", Enabled = "Enabled", Paused = "Paused"; }
public static class AdActionPayloadKeys { public const string Text = "text", MatchType = "matchType"; }

public static class AdSettingsGuard
{
    // false for null/whitespace, values starting with "--" (e.g. "-- stored in Key Vault --"),
    // and template placeholders containing "XXX" or equal to "your-…"
    public static bool IsConfigured(params string?[] values);
}

public interface IAdPlatformReadSource { /* as section 4.2 */ }
public interface IAdActionExecutor     { /* as section 4.2 */ }
```

Action value conventions: `AddNegativeKeyword` → `TargetLevel` `Campaign` or `AdGroup`,
`OldValue = Absent`, `NewValue = Present`, payload `text` + `matchType` (`KeywordMatchType` name);
`ReadCurrentAsync` returns `CurrentValue = Absent|Present`; `PlatformResourceId` is the created
negative criterion's id (used by revert). `PauseAd` → `TargetLevel = Ad`, `OldValue = Enabled`,
`NewValue = Paused`, empty payload; revert sets it back to `Enabled`. Executors **do not** compare
old values themselves — the core does (section 6.2); `ExecuteAsync` returns `Failed` on a platform
error and never throws for platform-side rejections (it throws only for transport/auth failures).

### 12.3 Test-kit bases (PR C1 creates exactly these)

```csharp
public abstract class AdPlatformReadSourceContractTests
{
    protected abstract IAdPlatformReadSource CreateSource();   // wired to recorded JSON fixtures
    protected abstract string AccountExternalId { get; }
    protected abstract DateOnly FixtureDate { get; }
}
public abstract class AdActionExecutorContractTests
{
    protected abstract IAdActionExecutor CreateExecutor();     // backed by a stateful fake transport
    protected abstract AdAction SamplePauseAd();
    protected abstract AdAction? SampleAddNegativeKeyword();   // null when the platform lacks it
}
public sealed class FakeAdPlatformReadSource : IAdPlatformReadSource { /* settable in-memory data */ }
public sealed class FakeAdActionExecutor : IAdActionExecutor { /* in-memory target state */ }
```
