---
process: flow-ad-proposal-approval
kind: workflow
module: marketing-ads
summary: Agents and people submit versioned ad-change proposals; server-side limits, autonomy and an approval channel decide who may approve; approved proposals execute action by action behind a kill switch, with append-only audit and revert from stored before-state.
owns:
  - backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/**
  - backend/src/Anela.Heblo.Application/Features/MarketingAds/Infrastructure/Jobs/AdProposalExpiryJob.cs
  - backend/src/Anela.Heblo.Domain/Features/MarketingAds/**
  - backend/src/Anela.Heblo.Persistence/MarketingAds/**
  - backend/src/Anela.Heblo.API/Controllers/MarketingAdsProposalsController.cs
  - backend/src/Anela.Heblo.API/Controllers/MarketingAdsSettingsController.cs
  - backend/src/Anela.Heblo.API/Controllers/MarketingAdsActivityController.cs
  - frontend/src/components/marketing/ads/**
  - frontend/src/api/hooks/useMarketingAds*.ts
verified_at: "7f2594a8d"
related: [module-marketing-ads, flow-google-ads-action-execution, sync-ads-change-history, sync-ads-daily]
---

# Ad proposal → approval → execution

## Purpose
Lets AI agents (and people) change live ad accounts (Google Ads, Meta, Sklik) only through a
structured, human-approved and fully audited path. The chief of marketing will approve in the web UI
(`/marketing/ads/proposals`) or via Claude/MCP (15 `MarketingAdsMcpTools`, PR C4). Admins tune autonomy, limits and the
kill switch over `/api/marketing-ads/settings` (`/marketing/ads/settings`).

## Trigger
- Submit: `POST /api/marketing-ads/proposals` (`Marketing_Ads` Write). Revise: `POST /api/marketing-ads/proposals/{id}/revisions` (creator only). MCP: `SubmitAdProposal` / `ReviseAdProposal`.
- Read: `GET /api/marketing-ads/proposals` (filters `status`, `platform`, `mine`, paging), `GET /api/marketing-ads/proposals/{id}`; `GET /api/marketing-ads/activity/audit`, `GET /api/marketing-ads/activity/agent-runs` (`Marketing_Ads` Read).
- Approve / reject: `POST /api/marketing-ads/proposals/{id}/approve|reject` with `{ version }` (`Marketing_AdApprovals` Write); MCP: `ListPendingAdProposals` (the oldest pending proposals first — `ListAdProposalsRequest.OldestFirst`; REST/UI lists stay newest first), `ApproveAdProposal`, `RejectAdProposal`.
- Agents connect to `/mcp` with their own client-credentials token (app role `Marketing.Agent`); `/mcp` rate-limits app-only callers per `oid` to `Ads:AgentRateLimitPerMinute` (default 60/min, HTTP 429). People are not limited. Runbook: `docs/integrations/marketing-agents-identity.md`.
- Web UI: `/marketing/ads/proposals` (approve/reject a named version; 409 shows a stale-version banner and forces "Obnovit detail"; a decision refreshes only the lists, the decided detail and the audit log, and the buttons stay disabled until that detail refetch lands), revert on executed proposals (`Marketing_AdApprovals` Admin). Settings (autonomy, limits, kill switch with confirmation): `/marketing/ads/settings`. Audit, out-of-band changes and agent runs: `/marketing/ads/activity`.
- Revert: `POST /api/marketing-ads/proposals/{id}/revert` (web only, `Marketing_AdApprovals` Admin), synchronous.
- Settings: `GET /api/marketing-ads/settings`; `PUT .../settings/autonomy|limits|kill-switch` (web only, `Marketing_AdApprovals` Admin).
- Execution: Hangfire fire-and-forget `AdProposalExecutionJob.RunAsync(id)`, enqueued after the final approval (human or autonomy). `[AutomaticRetry(Attempts = 0)]`, `[DisableConcurrentExecution(600)]`.
- Expiry: recurring job `ads-proposal-expiry` (category Marketing, "Ads Proposal Expiry"), cron `Ads:ExpiryCronExpression` (default `*/15 * * * *`) in `Ads:ExpiryTimeZone` (default `Europe/Prague`), `[AutomaticRetry(Attempts = 0)]`, `[DisableConcurrentExecution(300)]`, honours the Recurring Jobs on/off switch.
- Agent runs: `StartAgentRun` / `FinishAgentRun` MediatR handlers exist but have no HTTP endpoint; over MCP they are `StartAgentRun` / `FinishAgentRun`.
- States: Pending → Approved → Executing → Executed | PartiallyExecuted | Failed; Executed | PartiallyExecuted → Reverted; Pending → Rejected | Expired. A revision keeps Pending at version + 1.

## Data flow
1. **Submit** (`SubmitAdProposalHandler`): `EnsureValid` → shape validation (1–50 actions, one type, one account, text safety) → creator kind (`Agent` when `agentRunId` names the caller's own running run, else `Human`) → Google Ads account id normalised (dashes stripped) → actions built with fixed old/new values → limits evaluated as channel **Web** → Denied returns 422 and stores nothing; otherwise `AdProposals` + `AdProposalVersions` (v1) + `AdAuditEvents(Submitted)`; autonomy may auto-approve as `system:autonomy` in the same save, which enqueues execution.
2. **Revise**: creator only, Pending only → new immutable version, earlier approvals void, `ExpiresAt` reset to now + TTL, limits re-evaluated, `Revised` audit, autonomy re-checked.
3. **Approve** (`ApproveAdProposalHandler`): `IApprovalChannelResolver` (only Web or Mcp is accepted; System or an unset channel is 403) → Pending + current version (else 409) → not already decided by this principal (409) → limits for that channel/approver → Denied 422, NeedsWebApproval 403, NeedsSecondApprover records a non-final approval, Allowed records the final one → `AdProposalApprovals` + `Approved` audit in one save guarded by `Revision` → final approval enqueues execution (an enqueue failure is logged; the expiry job re-enqueues).
4. **Reject**: same channel/version/decided checks → Rejected + audit.
5. **Execute** (`AdProposalExecutionJob`): claim Approved → Executing with `ExecutionStarted` audit (one save on the `Revision` token; a lost race exits). Then the limits are re-evaluated from the execution perspective (`IAdLimitsContextBuilder.BuildForExecutionAsync`: channel System, principal `system:executor`); **Denied** runs nothing — every action is recorded Skipped "Not executed: the limits deny it at execution time: <reason>" with an `ActionFailed` audit each, and the proposal ends Failed (none succeeded). Otherwise, per action, in order: kill switch (`AdKillSwitches`) → executor for platform × action type → `ReadCurrentAsync` → compare with `OldValue` (mismatch = StaleState) → `ExecuteAsync` → `AdProposalActionExecutions` row (before value + raw JSON, after value, platform resource id, response JSON, error) + `ActionExecuted` / `ActionFailed` audit, saved per action. Actions that never ran are recorded Skipped. `FinishExecution`: all succeeded = Executed, some = PartiallyExecuted, none = Failed.
6. **Revert** (`RevertAdProposalHandler`): web admin guard → status Executed or PartiallyExecuted (else 409 NotRevertible) → claim via `Revision` (409 on a concurrent change) → every Succeeded, not-yet-reverted execution of the current version, newest first, `IAdActionExecutor.RevertAsync` with the stored result; each row's revert state + `Reverted` / `ActionFailed` audit saved on its own, each save bumping `Revision` (a concurrent revert that saved first makes this one stop with 409, never overwrite its row). All reverted → proposal Reverted; anything left → status unchanged, 502 `MarketingAdsRevertFailed` with counts; a retry reverts only what is left.
7. **Expire** (`ExpireAdProposalsHandler`, via the job): Pending with `ExpiresAt <= now` → Expired + `Expired` audit (one save each; a concurrent change skips that one), then Approved proposals whose `UpdatedAt` is older than `Ads:StuckApprovedRequeueMinutes` are re-enqueued (the execution claim makes duplicates a no-op).
8. **C2 link**: `AdExecutionLookup` (`IAdExecutionLookup`) returns Succeeded executions by `ExecutedAt` and successful reverts by `RevertedAt` (`IsRevert = true`) to C2's change-history matcher (`sync-ads-change-history`).

## Logic & formulas
- Limits (`AdLimitSettings`, per platform × action type, 27 seeded rows): `Enabled` 0/1 (Meta `AddNegativeKeyword` seeded 0), `MaxActionsPerProposal` (negatives 50, pauses 10), `McpMaxActionsPerProposal` (20 / 3), `MaxPausedAdsPer7Days` 20 (succeeded pauses of the account in a rolling 7 days + the not-yet-recorded pauses of the account's **other** in-flight proposals + this proposal; in-flight = Approved or Executing at submit/approve, only Executing at execution, so of several approved proposals the earliest to run wins and a later one over the cap is denied when it runs), `SecondApproverAbove` (seeded = the per-proposal max, i.e. inactive).
- Evaluation order (`AdLimitsEvaluator`): Denied (type disabled, account not managed or unknown, no/mixed actions, too many actions, weekly pause limit) → NeedsWebApproval (only for non-Web channels — MCP, System autonomy and an unset channel all use `McpMaxActionsPerProposal`: over that threshold, or pausing the last enabled ad of an ad group — an ad the `ads` schema does not know counts as emptying it) → NeedsSecondApprover (over `SecondApproverAbove` and no prior approver differs from this one; System never satisfies it) → Allowed.
- Without the `ads` database (`UnavailableAdsReadModel`) every account is unknown, so every submission is Denied.
- At execution only **Denied** blocks (action type disabled, account no longer managed/unknown, weekly cap exceeded, …). NeedsWebApproval and NeedsSecondApprover are ignored there: the approvals that made the proposal Approved already satisfied them. The ad-group-occupancy rule is therefore not enforced at execution (it only ever yields NeedsWebApproval); each ad is still guarded by the per-action stale-state check.
- Autonomy (`AdAutonomySettings`, 6 rows, all seeded `ProposeOnly`): ProposeOnly never auto-approves; AutoWithinLimits auto-approves only `Allowed` (evaluated as channel System); **`Auto` currently behaves exactly like AutoWithinLimits** — only `Allowed` is auto-approved by System (spec deviation: spec 6.3 binds limits independent of approver, spec 6.4 leaves Auto undefined).
- Kill switch (`AdKillSwitchPolicy`): execution allowed only when the `Global` row exists and is enabled and no platform row disables it. Seeded **disabled**. Checked before **every** action; off = that action and the rest Skipped, so the proposal ends Failed (or PartiallyExecuted if earlier actions succeeded). Revert is not gated by it.
- **Approval channel** (`ApprovalChannelResolver`, used by every approve/reject handler):
  service identities — app-only tokens (no `scp` or `idtyp = app`) and the E2E test session
  (AuthenticationType `E2ETest`) — are refused before any permission is checked;
  `azp`/`appid` in `Ads:Approval:McpClientIds` or equal to `AzureAd:ClientId` → Mcp (needs
  `marketing.ad_approvals.write` **and** `marketing.ad_approvals_via_mcp.read`; stricter MCP limits
  apply); in `Ads:Approval:WebClientIds` → Web (needs `marketing.ad_approvals.write`); the MCP list
  is checked first, so a client id in both lists is Mcp (the stricter channel); a cookie session →
  Web; any other bearer client → refused. Setup: `docs/integrations/marketing-agents-identity.md`.
  Turning the kill switch **off** needs only `Marketing_AdApprovals` Admin, so a misbehaving resolver can never stop an admin halting execution.
- A platform `Failed` result on one action continues with the next; stale state, kill switch, missing executor, a failed current-state read or a thrown mutate stop the run and skip the rest.
- A thrown `ExecuteAsync` is never retried. One best-effort `ReadCurrentAsync` follows: new value seen → action Succeeded ("Applied — confirmed by re-read after transport error"); otherwise Failed ("may have been applied; verify in the account").
- Revert retry of a `RevertFailed` row first re-reads; if the target already shows the old value it is marked Reverted without a platform call.
- Free text that may hold newlines (proposal reasoning, reject reason, agent-run name and summary) is refused with 400 when it contains U+0000, which Postgres `text`/`jsonb` cannot store; ids, keywords and agent-run scopes refuse every control/format character.
- Audit (`AdAuditEvents`) and `AdProposalVersions` are append-only: Postgres trigger `marketing_ads_reject_mutation` rejects UPDATE, DELETE and TRUNCATE.
- Error codes 4001–4017 (`MarketingAds*`).

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `Ads:ProposalTtlHours` | 72 | Pending proposal lifetime; reset by a revision |
| `Ads:ExpiryCronExpression` | `*/15 * * * *` | Expiry job schedule; an invalid value is scheduled with the default (warning logged) and every run then fails in `EnsureValid` |
| `Ads:ExpiryTimeZone` | `Europe/Prague` | Expiry job time zone |
| `Ads:StuckApprovedRequeueMinutes` | 60 | Approved proposals untouched this long are re-enqueued by the expiry job |
| `Ads:Approval:WebClientIds` | web SPA client id | `azp`/`appid` values treated as the Web channel |
| `Ads:Approval:McpClientIds` | `[]` (+ `AzureAd:ClientId` in code) | extra MCP client ids (e.g. HebloMCP) |
| `Ads:AgentRateLimitPerMinute` | 60 | `/mcp` requests per minute per agent `oid` |

The first four are validated by `AdProposalOptions.EnsureValid()` at the start of Submit, Revise and every expiry run (no `ValidateOnStart`): a bad value fails those runs, never app startup or job registration. No environment overrides exist in `appsettings*.json`.

## Runtime facts
None yet — fill in after the first staging run (migration `AddMarketingAdsProposals` auto-applies at startup).

## Known quirks
- Approving while the kill switch is off ends the proposal Failed (owner decision pending); the web UI warns next to the approve button (`execution-disabled-warning`).
- The nightly E2E session is refused approve/reject on Staging (app-only identity type `E2ETest`), so web approve/reject is covered only by unit tests.
- The kill switch is seeded off, so nothing executes until an admin turns it on. New permission grants and the agent identity are rollout steps (runbook), so a fresh deployment approves nothing until they are done.
- Outside Production the Google executor runs validate-only: Google accepts the request but nothing changes. Each action ends Failed with `validateOnly: true` in its audit payload, so the proposal ends **Failed**; a revert of it returns 409 `MarketingAdsNotRevertible` (there are no Succeeded rows to revert). The execution is never re-enqueued.
- A thrown mutate is never retried; after one re-read the action is either Succeeded "confirmed by re-read" or Failed "may have been applied — verify". Check the account before resubmitting a Failed one.
- Limits are checked at approval and again at execution, not between actions: changing `Enabled` or the managed flag mid-run does not stop the remaining actions (the kill switch, checked before every action, does).
- A proposal stuck in Executing (process killed mid-run, or both finalising saves failed — logged Critical) is never auto-resumed; find it with the proposal list `status=Executing` filter and verify the account by hand.
- 409 `MarketingAdsConcurrentChange` means a lost race only (concurrency token or a duplicate unique key); any other failed save discards the tracked changes and surfaces as a 500.
- A redelivered execution job that finds its proposal still Executing logs a Warning and exits.
- Two admins reverting at the same moment can both call the platform; accepted risk. Every per-row save is guarded by `Revision`, so the request that loses stops with 409 (its platform result is logged, not stored) and a retry converges via the re-read.
- A negative keyword whose platform resource id is not stored (e.g. Succeeded via re-read, or an id longer than 200 characters) may not be revertible automatically: revert tries to recover the resource name from the stored response / re-read JSON (`StoredResourceId.Recover`); if it can't, the row is `RevertFailed` "remove the negative keyword manually".
- Reverts are returned to C2's `IAdExecutionLookup` with `IsRevert = true`, so the platform change a revert makes is matched as Heblo's, not out-of-band.
- Audit rows and proposal versions are protected by Postgres triggers; a DB owner can still disable triggers.
- New permissions (`marketing.ads.*`, `marketing.ad_approvals.*`, `marketing.ad_approvals_via_mcp.read`) are not granted to existing prod groups by the seeder (insert-if-missing); the C4 rollout grants them by hand (runbook section 3). Nobody holds `marketing.ad_approvals.read`.
- On Staging/Development a service-principal token becomes an `E2ETestSession` cookie via `E2ETestController` (or an `X-E2E-Test-Token` request). That identity is always `super_user`, so it can change ads settings and revert, but it counts as a service identity (`IsAppOnly`): approve/reject are refused, so nightly E2E cannot test approving, and `/mcp` rate-limits it per `oid` like an agent. A cookie request that also carries `Authorization: Bearer` is treated as bearer (fails closed).
- A client id configured in both `WebClientIds` and `McpClientIds` (or equal to `AzureAd:ClientId`) resolves to Mcp, so web approvals from it also need `marketing.ad_approvals_via_mcp.read` until the config is fixed.
- `StartAgentRun` / `FinishAgentRun` have no HTTP endpoint (only MCP tools), so `AgentRuns` stays empty until an agent reports runs: a submission without `agentRunId` is created by kind `Human`, one naming a run gets 404 `MarketingAdsAgentRunNotFound`.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/Limits/AdLimitsEvaluator.cs` — every limit rule
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/Autonomy/AdAutonomyService.cs` — auto-approval
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/Execution/AdProposalExecutionJob.cs` — execution pipeline
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/UseCases/ApproveAdProposal/ApproveAdProposalHandler.cs` — approval rules
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/Approval/ApprovalChannelResolver.cs` — approval channel decision
- `backend/src/Anela.Heblo.API/MCP/Tools/MarketingAdsMcpTools.cs` — the 15 MCP tools
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/UseCases/RevertAdProposal/RevertAdProposalHandler.cs` — revert
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Infrastructure/Jobs/AdProposalExpiryJob.cs` — expiry job registration
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/AdProposalOptions.cs` — configuration and `EnsureValid`
- `backend/src/Anela.Heblo.Domain/Features/MarketingAds/AdProposal.cs` — lifecycle
- `backend/src/Anela.Heblo.Persistence/MarketingAds/MarketingAdsSql.cs` — append-only triggers
- `frontend/src/components/marketing/ads/proposals/ProposalDecisionBar.tsx` — version-bound approve/reject/revert and 409/403 handling
- `frontend/src/api/hooks/useMarketingAdsProposals.ts` — REST calls behind the inbox
