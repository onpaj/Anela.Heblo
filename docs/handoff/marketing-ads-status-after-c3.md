# Handoff: Marketing ads platform — status after Core C3 (2026-10-14)

Read this first in a fresh session. It continues `docs/handoff/marketing-ads-status-2026-10-14.md`
(the WS1 status). That file's §1–§4 still hold; this one replaces its §2 and §5. Verified against
`origin/main` at `bda142975` on 2026-10-14; re-verify with
`git log origin/main --oneline | grep -iE "marketing|ads"` before trusting it.

Binding background, unchanged:
- Spec: `docs/superpowers/specs/2026-10-07-marketing-agents-platform-design.md` (§2 sequencing, §12 names)
- Original handoff: `docs/handoff/marketing-agents-platform.md` (§2 decisions and §3 security are binding)
- Plans: `docs/superpowers/plans/2026-10-07-marketing-*.md` (one per PR / workspace)

## 1. What is merged since the WS1 status

| PR | What | Plan |
|---|---|---|
| #4499 | WS1 status handoff + Google Ads validate-only smoke test result | — |
| #4503 | Core **C3**: proposals, versions, limits, autonomy, kill switch, execution job, revert, append-only audit, agent runs, REST endpoints | `marketing-core-c3-proposals` |

C3 is in release **v3.164.0**. Its migration `AddMarketingAdsProposals` applies at startup
(`MigrateDatabaseAsync` runs on every boot). Behaviour reference:
`docs/processes/flow-ad-proposal-approval.md`.

## 2. State after deployment: nothing can execute yet, by design

- `DenyAllApprovalChannelResolver` is registered, so approve, reject, revert and every settings
  change return 403 `MarketingAdsApprovalChannelDenied`. There is one exception: an admin can
  always turn the kill switch **off**.
- Autonomy is seeded `ProposeOnly` everywhere, and the kill switch is seeded **disabled**.
- The `GoogleAdsActionExecutor` is wired into the job, but no proposal can reach `Approved`.
- C2's change-history sync now uses the real `IAdExecutionLookup` (`AdExecutionLookup`) over
  C3's tables. Changes Heblo made are matched as Heblo's, so they are not flagged as out-of-band.

## 3. What is not started

| Item | State |
|---|---|
| Core **C4**: agent identity, real `ApprovalChannelResolver`, groups, `/mcp` rate limit, MCP tools | **next on the critical path**; plan `marketing-core-c4-identity-mcp` |
| Core **C5**: approval inbox UI, audit/out-of-band/agent-run views, settings page | blocked on C4 |
| WS2 **Meta** / WS3 **Sklik** | unchanged since the WS1 status (stale branches at `171dcf86a`; branch fresh from `origin/main`) |

## 4. What a C4 implementer must know from C3

The C4 plan's precondition greps (its Task 0) should pass as written. These details are not in
the C4 plan text, and the code on main wins:

- **Resolver contract** (`Application/Features/MarketingAds/Proposals/Approval/`):
  `ApprovalChannelResolution` is a class with `Allowed`, `Channel`, `DenyReason`, and factories
  `Allow(channel)` / `Deny(reason)`. It also has `IsHumanChannelAllowed` and
  `HumanChannelDenyReason`.
- **Approve/reject accept only `Web` or `Mcp`.** A resolution with `Allowed = true` and `System`
  or an unset channel (0) is refused with 403. The real resolver must always set `Channel`.
- **Revert and every settings change require `Channel == Web`** (`AdWebAdminGuard.Check`) plus
  the `Marketing_AdApprovals` Admin role. Turning the kill switch off uses
  `AdWebAdminGuard.CheckAdmin` (identity + Admin role only, no resolver), so the C4 resolver
  never gates it. Keep it that way.
- **Limits by channel:** `Web` is checked against the full per-proposal maxima (50 negatives /
  10 pauses). Every other channel, including `System` autonomy, uses `McpMaxActionsPerProposal`
  (20 / 3) and the empties-an-ad-group rule.
- **`Auto` autonomy behaves exactly like `AutoWithinLimits`.** Spec 6.4 does not define `Auto`.
- **Error codes 4001–4017 are taken** (`MarketingAds*`; 39XX belongs to Reporting). New C4 codes
  start at 4018. Each new code also needs the `ErrorHandlingTests` module-range bucket and an
  `i18n.ts` Czech translation.
- **409 `MarketingAdsConcurrentChange` means a lost race only** (Revision token or duplicate
  unique key). Any other failed save discards the tracked changes and surfaces as a 500. MCP
  tools should map 409 to a "reload and retry" message, and other failures should not be
  reported as a race.
- **MediatR handlers MCP tools wrap:** submit, revise, approve, reject, get/list proposals, list
  audit events, `StartAgentRun` / `FinishAgentRun` (no HTTP endpoint yet), list agent runs, and
  get settings. Revert and settings updates are web-only and must **not** become MCP tools.
- The generator emits an unused `marketing.ad_approvals.read` permission. Spec 7.2 gives
  AdApprovals no Read level, so do not grant it to any group.

## 5. Open items from the C3 review (owner decisions, not done)

The C3 review agents raised these. They were deliberately left out of #4503 because each one
changes documented behaviour:

1. **Approving while the kill switch is off ends the proposal as Failed.** The job claims the
   proposal, every action is Skipped, and `Failed` is terminal, so the proposal has to be
   re-submitted. Because the switch is seeded off, this is the common path in the first weeks.
   Options:
   - (a) check the switch before the claim and leave the proposal `Approved` (the stuck-approved
     requeue picks it up once the switch is on; the old-value check guards stale state);
   - (b) refuse the approval with a clear error while the switch is off;
   - (c) keep the current behaviour.
   Decide before C5 ships the approve button.
2. **A proposal stranded in `Executing`** (process killed mid-run) is never recovered.
   Redelivery now logs a Warning. Its unrecorded pauses keep counting against
   `MaxPausedAdsPer7Days` until someone fixes it by hand. Option: the expiry job finalises
   `Executing` proposals whose `UpdatedAt` is older than the 600 s job lock, marking them
   "Not recorded — verify in the account".
3. **The stuck-approved requeue re-enqueues the same proposal every 15 min.** Duplicates are
   harmless no-ops at the claim, but they add queue noise with `WorkerCount = 1`. Option: stamp
   an enqueue time and skip recently re-enqueued proposals.
4. **The creator may approve their own proposal**, including one their own agent submitted.
   This follows spec 6.3, which only requires the *second* approver to differ. Option: a
   creator-is-not-approver rule in `AdProposalGuards`.
5. **The append-only triggers can be dropped by the role that owns the tables** (the app's DB
   role). Real tamper-proofing would need a separate migration/owner role.

## 6. Recommended next steps

1. **Confirm the deployment** (Ondrej or a session with prod access):
   - The startup log shows `AddMarketingAdsProposals` applied, and `public."AdKillSwitches"` has
     one `Global` row with `ExecutionEnabled = false`.
   - Hangfire `hangfire_heblo` lists the recurring ad proposal expiry job.
   - The next `sync-ads-change-history` run still succeeds now that it reads C3's tables.
   - Record the findings in the *Runtime facts* of `flow-ad-proposal-approval.md` and
     `sync-ads-change-history.md`.
2. **Decide §5 item 1** (kill switch vs. approval) before C5. Items 2–5 can wait.
3. **Start C4** in a fresh worktree from `origin/main`:
   - Read `CLAUDE.md`, spec §7 + §12, original handoff §2–3, this file's §4, then
     `docs/superpowers/plans/2026-10-07-marketing-core-c4-identity-mcp.md` in full.
   - Execute with `superpowers:subagent-driven-development`, one PR.
   - The Entra runbook steps (agent app registration, client secret in the agent runtime, group
     grants on prod/staging, reading `azp` from a real web token) are Ondrej's.
   - `JsonGroupSeeder` is insert-if-missing, so new permissions never reach existing groups
     without the documented grant step.

## 7. Gotchas collected during C3

- **InMemory EF has no transactions.** A save that fails on the concurrency token can still leave
  its inserted rows (e.g. audit events) in the InMemory store. Assert atomicity only in the
  Testcontainers Postgres tests (`Category=Integration`, needs `podman machine start`).
- **InMemory does enforce concurrency tokens**, so race tests over `Revision` work there. Make the
  losing side load *after* the winner's claim, otherwise the test passes vacuously.
- **No `BeginTransaction` in `backend/src`** (CI `scripts/check-no-managed-tx.sh`). Each state
  change is one `SaveChangesAsync` guarded by `Revision`.
- **All `DateTime` columns are `timestamp` without time zone** (`AsUtcTimestamp()`). The global
  converter forces `Kind=Unspecified`, so a `timestamptz` column fails every write.
- **`dotnet test` in Orca worktrees:** build first, then `--no-build -p:UseSharedCompilation=false`.
  `dotnet format --verify-no-changes --include <files>` keeps the format check to the touched files.
