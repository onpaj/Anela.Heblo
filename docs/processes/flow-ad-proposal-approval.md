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
verified_at: "a4c5231e8"
related: [module-marketing-ads, flow-google-ads-action-execution, sync-ads-change-history, sync-ads-daily]
---

# Ad proposal → approval → execution

## Purpose
Lets AI agents (and people) change live ad accounts (Google Ads, Meta, Sklik) only through a
structured, human-approved and fully audited path. The chief of marketing will approve in the web UI
(`/marketing/ads/proposals`, PR C5) or via Claude/MCP (PR C4). Admins tune autonomy, limits and the
kill switch over `/api/marketing-ads/settings` (UI in C5). Nothing in C3 has a Heblo page yet.

## Trigger
- Submit: `POST /api/marketing-ads/proposals` (`Marketing_Ads` Write). Revise: `POST /api/marketing-ads/proposals/{id}/revisions` (creator only). C4 adds MCP `SubmitAdProposal` / `ReviseAdProposal`.
- Read: `GET /api/marketing-ads/proposals` (filters `status`, `platform`, `mine`, paging), `GET /api/marketing-ads/proposals/{id}`; `GET /api/marketing-ads/activity/audit`, `GET /api/marketing-ads/activity/agent-runs` (`Marketing_Ads` Read).
- Approve / reject: `POST /api/marketing-ads/proposals/{id}/approve|reject` with `{ version }` (`Marketing_AdApprovals` Write).
- Revert: `POST /api/marketing-ads/proposals/{id}/revert` (web only, `Marketing_AdApprovals` Admin), synchronous.
- Settings: `GET /api/marketing-ads/settings`; `PUT .../settings/autonomy|limits|kill-switch` (web only, `Marketing_AdApprovals` Admin).
- Execution: Hangfire fire-and-forget `AdProposalExecutionJob.RunAsync(id)`, enqueued after the final approval (human or autonomy). `[AutomaticRetry(Attempts = 0)]`, `[DisableConcurrentExecution(600)]`.
- Expiry: recurring job `ads-proposal-expiry` (category Marketing, "Ads Proposal Expiry"), cron `Ads:ExpiryCronExpression` (default `*/15 * * * *`) in `Ads:ExpiryTimeZone` (default `Europe/Prague`), `[AutomaticRetry(Attempts = 0)]`, `[DisableConcurrentExecution(300)]`, honours the Recurring Jobs on/off switch.
- Agent runs: `StartAgentRun` / `FinishAgentRun` MediatR handlers exist but have no HTTP endpoint; C4 exposes them over MCP.
- States: Pending → Approved → Executing → Executed | PartiallyExecuted | Failed; Executed | PartiallyExecuted → Reverted; Pending → Rejected | Expired. A revision keeps Pending at version + 1.

## Data flow
1. **Submit** (`SubmitAdProposalHandler`): `EnsureValid` → shape validation (1–50 actions, one type, one account, text safety) → creator kind (`Agent` when `agentRunId` names the caller's own running run, else `Human`) → Google Ads account id normalised (dashes stripped) → actions built with fixed old/new values → limits evaluated as channel **Web** → Denied returns 422 and stores nothing; otherwise `AdProposals` + `AdProposalVersions` (v1) + `AdAuditEvents(Submitted)`; autonomy may auto-approve as `system:autonomy` in the same save, which enqueues execution.
2. **Revise**: creator only, Pending only → new immutable version, earlier approvals void, `ExpiresAt` reset to now + TTL, limits re-evaluated, `Revised` audit, autonomy re-checked.
3. **Approve** (`ApproveAdProposalHandler`): `IApprovalChannelResolver` → Pending + current version (else 409) → not already decided by this principal (409) → limits for that channel/approver → Denied 422, NeedsWebApproval 403, NeedsSecondApprover records a non-final approval, Allowed records the final one → `AdProposalApprovals` + `Approved` audit in one save guarded by `Revision` → final approval enqueues execution (an enqueue failure is logged; the expiry job re-enqueues).
4. **Reject**: same channel/version/decided checks → Rejected + audit.
5. **Execute** (`AdProposalExecutionJob`): claim Approved → Executing with `ExecutionStarted` audit (one save on the `Revision` token; a lost race exits). Per action, in order: kill switch (`AdKillSwitches`) → executor for platform × action type → `ReadCurrentAsync` → compare with `OldValue` (mismatch = StaleState) → `ExecuteAsync` → `AdProposalActionExecutions` row (before value + raw JSON, after value, platform resource id, response JSON, error) + `ActionExecuted` / `ActionFailed` audit, saved per action. Actions that never ran are recorded Skipped. `FinishExecution`: all succeeded = Executed, some = PartiallyExecuted, none = Failed.
6. **Revert** (`RevertAdProposalHandler`): web admin guard → status Executed or PartiallyExecuted (else 409 NotRevertible) → claim via `Revision` (409 on a concurrent change) → every Succeeded, not-yet-reverted execution of the current version, newest first, `IAdActionExecutor.RevertAsync` with the stored result; each row's revert state + `Reverted` / `ActionFailed` audit saved on its own. All reverted → proposal Reverted; anything left → status unchanged, 502 `MarketingAdsRevertFailed` with counts; a retry reverts only what is left.
7. **Expire** (`ExpireAdProposalsHandler`, via the job): Pending with `ExpiresAt <= now` → Expired + `Expired` audit (one save each; a concurrent change skips that one), then Approved proposals whose `UpdatedAt` is older than `Ads:StuckApprovedRequeueMinutes` are re-enqueued (the execution claim makes duplicates a no-op).
8. **C2 link**: `AdExecutionLookup` (`IAdExecutionLookup`) returns Succeeded executions by `ExecutedAt` and successful reverts by `RevertedAt` (`IsRevert = true`) to C2's change-history matcher (`sync-ads-change-history`).

## Logic & formulas
- Limits (`AdLimitSettings`, per platform × action type, 27 seeded rows): `Enabled` 0/1 (Meta `AddNegativeKeyword` seeded 0), `MaxActionsPerProposal` (negatives 50, pauses 10), `McpMaxActionsPerProposal` (20 / 3), `MaxPausedAdsPer7Days` 20 (succeeded pauses of the account in a rolling 7 days + this proposal), `SecondApproverAbove` (seeded = the per-proposal max, i.e. inactive).
- Evaluation order (`AdLimitsEvaluator`): Denied (type disabled, account not managed or unknown, no/mixed actions, too many actions, weekly pause limit) → NeedsWebApproval (only for non-Web channels: MCP over its threshold, or pausing the last enabled ad of an ad group — an ad the `ads` schema does not know counts as emptying it) → NeedsSecondApprover (over `SecondApproverAbove` and no prior approver differs from this one; System never satisfies it) → Allowed.
- Without the `ads` database (`UnavailableAdsReadModel`) every account is unknown, so every submission is Denied.
- Autonomy (`AdAutonomySettings`, 6 rows, all seeded `ProposeOnly`): ProposeOnly never auto-approves; AutoWithinLimits auto-approves only `Allowed` (evaluated as channel System); **`Auto` currently behaves exactly like AutoWithinLimits** — only `Allowed` is auto-approved by System (spec deviation: spec 6.3 binds limits independent of approver, spec 6.4 leaves Auto undefined).
- Kill switch (`AdKillSwitchPolicy`): execution allowed only when the `Global` row exists and is enabled and no platform row disables it. Seeded **disabled**. Checked before **every** action; off = that action and the rest Skipped, so the proposal ends Failed (or PartiallyExecuted if earlier actions succeeded). Revert is not gated by it.
- Approval channel: `DenyAllApprovalChannelResolver` is registered until C4, so approve, reject, revert and every settings change return 403 `MarketingAdsApprovalChannelDenied` — **nothing can be approved yet**, and nothing executes unless autonomy is changed by SQL.
- A platform `Failed` result on one action continues with the next; stale state, kill switch, missing executor, a failed current-state read or a thrown mutate stop the run and skip the rest.
- A thrown `ExecuteAsync` is never retried. One best-effort `ReadCurrentAsync` follows: new value seen → action Succeeded ("Applied — confirmed by re-read after transport error"); otherwise Failed ("may have been applied; verify in the account").
- Revert retry of a `RevertFailed` row first re-reads; if the target already shows the old value it is marked Reverted without a platform call.
- Audit (`AdAuditEvents`) and `AdProposalVersions` are append-only: Postgres trigger `marketing_ads_reject_mutation` rejects UPDATE, DELETE and TRUNCATE.
- Error codes 4001–4017 (`MarketingAds*`).

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `Ads:ProposalTtlHours` | 72 | Pending proposal lifetime; reset by a revision |
| `Ads:ExpiryCronExpression` | `*/15 * * * *` | Expiry job schedule; an invalid value is scheduled with the default (warning logged) and every run then fails in `EnsureValid` |
| `Ads:ExpiryTimeZone` | `Europe/Prague` | Expiry job time zone |
| `Ads:StuckApprovedRequeueMinutes` | 60 | Approved proposals untouched this long are re-enqueued by the expiry job |

All four are validated by `AdProposalOptions.EnsureValid()` at the start of Submit, Revise and every expiry run (no `ValidateOnStart`): a bad value fails those runs, never app startup or job registration. No environment overrides exist in `appsettings*.json`.

## Runtime facts
None yet — fill in after the first staging run (migration `AddMarketingAdsProposals` auto-applies at startup).

## Known quirks
- Until PR C4 deploys, `DenyAllApprovalChannelResolver` blocks every approve/reject/revert/settings change, and the kill switch is seeded off, so nothing executes.
- Outside Production the Google executor runs validate-only: Google accepts the request but nothing changes. Each action ends Failed with `validateOnly: true` in its audit payload, so the proposal ends **Failed**; a revert of it returns 409 `MarketingAdsNotRevertible` (there are no Succeeded rows to revert). The execution is never re-enqueued.
- A thrown mutate is never retried; after one re-read the action is either Succeeded "confirmed by re-read" or Failed "may have been applied — verify". Check the account before resubmitting a Failed one.
- A proposal stuck in Executing (process killed mid-run, or both finalising saves failed — logged Critical) is never auto-resumed; find it with the proposal list `status=Executing` filter and verify the account by hand.
- Two admins reverting at the same moment can both call the platform (only the claim save is guarded); accepted risk, a retry converges via the re-read.
- A negative keyword whose platform resource id is unknown (e.g. Succeeded via re-read, or an id longer than 200 characters not recoverable from the response JSON) cannot be reverted automatically: the row is `RevertFailed` "remove the negative keyword manually".
- Reverts are returned to C2's `IAdExecutionLookup` with `IsRevert = true`, so the platform change a revert makes is matched as Heblo's, not out-of-band.
- Audit rows and proposal versions are protected by Postgres triggers; a DB owner can still disable triggers.
- New permissions (`marketing.ads.*`, `marketing.ad_approvals.*`, `marketing.ad_approvals_via_mcp.read`) are not granted to existing prod groups by the seeder; C4's rollout grants them.
- `StartAgentRun` / `FinishAgentRun` have no HTTP endpoint until C4, so `AgentRuns` stays empty: a submission without `agentRunId` is created by kind `Human`, one naming a run gets 404 `MarketingAdsAgentRunNotFound`.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/Limits/AdLimitsEvaluator.cs` — every limit rule
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/Autonomy/AdAutonomyService.cs` — auto-approval
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/Execution/AdProposalExecutionJob.cs` — execution pipeline
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/UseCases/ApproveAdProposal/ApproveAdProposalHandler.cs` — approval rules
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/UseCases/RevertAdProposal/RevertAdProposalHandler.cs` — revert
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Infrastructure/Jobs/AdProposalExpiryJob.cs` — expiry job registration
- `backend/src/Anela.Heblo.Application/Features/MarketingAds/Proposals/AdProposalOptions.cs` — configuration and `EnsureValid`
- `backend/src/Anela.Heblo.Domain/Features/MarketingAds/AdProposal.cs` — lifecycle
- `backend/src/Anela.Heblo.Persistence/MarketingAds/MarketingAdsSql.cs` — append-only triggers
