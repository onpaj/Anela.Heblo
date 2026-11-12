# Handoff: Marketing ads platform — status after Core C4 (2026-10-15)

Read this first in a fresh session. It continues `docs/handoff/marketing-ads-status-after-c3.md`
(docs PR #4506, branch `docs/marketing-ads-status-after-c3`; read it with
`git show origin/docs/marketing-ads-status-after-c3:docs/handoff/marketing-ads-status-after-c3.md`
if #4506 is not merged yet). That file's §5 (owner decisions) and §7 (gotchas) still hold; this
one replaces its §2, §3, §4 and §6. Verified against `origin/main` at `79dd022bc` on 2026-10-15;
re-verify with `git log origin/main --oneline | grep -iE "marketing|ads"` before trusting it.
The code on `main` wins over any plan text.

Binding background, unchanged:
- Spec: `docs/superpowers/specs/2026-10-07-marketing-agents-platform-design.md` (§2 sequencing, §12 names)
- Original handoff: `docs/handoff/marketing-agents-platform.md` (§2 decisions and §3 security are binding)
- Plans: `docs/superpowers/plans/2026-10-07-marketing-*.md` (one per PR / workspace)

## 1. What is merged since the C3 status

| PR | What | Plan |
|---|---|---|
| #4507 | Core **C4**: seed groups, `ICurrentUserService` client claims, real `ApprovalChannelResolver`, `/mcp` agent rate limit, 15 `MarketingAdsMcpTools`, Entra runbook | `marketing-core-c4-identity-mcp` |

C4 is merged as `79dd022bc` (2026-10-15) and ships in the next release after v3.164.0. It has no
migration. The PR description of #4507 lists every ruling made during C4 (R1–R19).

## 2. State after deployment

- **The real `ApprovalChannelResolver` is registered** (`Application/Features/MarketingAds/Proposals/Approval/`).
  Order: app-only token → deny (even `super_user`); `azp` in `Ads:Approval:WebClientIds` → Web;
  `azp` in `Ads:Approval:McpClientIds` or equal to `AzureAd:ClientId` → Mcp (a client id in both
  lists → Mcp); cookie session → Web; any other bearer → deny.
- **Nobody can approve yet in practice.** The new groups (`Marketing_Agent`,
  `Marketing_Specialist`, `Marketing_Chief`) only reach an environment's DB through
  `scripts/seed-authorization.sh <env>` (insert-if-missing), and `Marketer`/`Spravce` need their
  new permissions granted at `/admin/access`. These are Ondrej's steps, documented in
  `docs/integrations/marketing-agents-identity.md` (rollout checklist in #4507). Check whether
  they are done before assuming a user holds `marketing.ad_approvals.write`.
- Autonomy is still seeded `ProposeOnly`, and the kill switch is still seeded **disabled**.
- `/mcp` has 15 marketing ads tools; app-only callers are limited to 60 calls/min per `oid`
  (HTTP 429 + `Retry-After`). People are never limited.

## 3. What is not started

| Item | State |
|---|---|
| Core **C5**: approval inbox, activity view (audit / out-of-band / agent runs), settings page | **next**; plan `marketing-core-c5-ui` |
| Fix: `ServicePrincipalTokenValidator` never verifies the JWT signature (see §6) | not started, separate small PR, **security** |
| WS2 **Meta** / WS3 **Sklik** | unchanged (stale branches at `171dcf86a`; branch fresh from `origin/main`) |

## 4. What a C5 implementer must know (not in the C5 plan text)

The C5 plan was written before C3/C4 existed. Its "Before you start" Step 5 tells you to map
expected names to the generated client; these are the real ones on `main`:

- **Controllers and NSwag prefixes** (three controllers, as the plan guessed):
  - `api/marketing-ads/proposals` → `marketingAdsProposals_ListProposals(status, platform, mine, page, pageSize)`,
    `_GetProposal(id)`, `_SubmitProposal(request)`, `_ReviseProposal(id, request)`,
    `_ApproveProposal(id, ApproveAdProposalRequest)`, `_RejectProposal(id, RejectAdProposalRequest)`,
    `_RevertProposal(id)`.
  - `api/marketing-ads/activity` → `marketingAdsActivity_ListAuditEvents(proposalId, eventType, page, pageSize)`,
    `_ListAgentRuns(page, pageSize)`.
  - `api/marketing-ads/settings` → `marketingAdsSettings_GetSettings()`, `_UpdateAutonomy(UpdateAdAutonomySettingRequest)`,
    `_UpdateLimit(UpdateAdLimitSettingRequest)`, `_SetKillSwitch(SetAdKillSwitchRequest)`.
- **Ids are `int`**, not Guids (`toApiId` stays numeric).
- **409 is typed.** Approve, reject and revert declare `[ProducesResponseType(typeof(<X>Response), 409)]`
  (also 403/404/422 typed), so NSwag throws the parsed DTO, not a `SwaggerException` — exactly the
  plan's Review Focus #2. `MarketingAdsConcurrentChange` means a lost race only.
- **Kill-switch semantics match the plan's assumption** (`Domain/Features/MarketingAds/AdKillSwitchPolicy.cs`):
  execution runs only when the `Global` row exists and is on **and** the platform has no disabling
  override. A missing global row fails closed.
- `GetAdSettings` returns `IsExecutionEnabledGlobally` and, per autonomy row, `ApprovedWithoutEdit`
  and a rate (`ApprovedWithoutEdit / Decided`, null when nothing was decided).
- **Gap — out-of-band change events have no REST endpoint.** C2's `GetAdChangeHistoryRequest`
  (and `GetAdAccountsRequest`) are reachable only through MCP tools. The plan marks the change
  events row "UI (required)" and says to stop if it is missing, and its Global Constraints forbid
  backend source changes. The C5 session has to rule on this (recommended: one thin read-only
  `GET api/marketing-ads/activity/change-events` action on `MarketingAdsActivityController`
  wrapping the existing handler, gated by `marketing.ads.read`, with a controller test, recorded
  as a spec/plan deviation in the PR).
- **Approve/reject are refused for two kinds of caller that C5 will meet first:**
  - Local mock auth (`UseMockAuth`): the mock principal has no `azp`, so it is a bearer with no
    client id → deny. The approve/reject buttons can only be exercised against a real Entra
    login (or with a mocked API in unit tests).
  - The nightly E2E session on Staging: its identity type `E2ETest` is treated as app-only
    (#4507 R16), so approve **and reject** return 403. The C5 plan's E2E (deviation 3: seed a
    proposal, then reject it) cannot pass as written. Options: keep the E2E read-only (inbox,
    diff, reasoning rendering) and assert the 403 path, or test reject only in unit tests.
- **Permissions to gate on** (spec 7.2): `marketing.ads.read`, `marketing.ad_approvals.write`,
  `marketing.ad_approvals.admin`. `marketing.ad_approvals.read` exists in the generated matrix but
  no group holds it (#4507 R1) — never gate on it.
- Revert and every settings change require the Web channel plus Admin; turning the kill switch
  **off** needs only identity + Admin (`AdWebAdminGuard.CheckAdmin`).

## 5. Owner decisions still open (from the C3 handoff §5, not done in C4)

1. **Approving while the kill switch is off ends the proposal as `Failed`.** The C3 handoff said
   to decide this before C5 ships the approve button. It is still undecided. Options: (a) check
   the switch before the claim and leave the proposal `Approved`; (b) refuse approval while the
   switch is off; (c) keep it. Until Ondrej decides, C5 must not change backend behaviour here;
   it can make the risk visible (e.g. a warning near the approve button when
   `IsExecutionEnabledGlobally` is false) and must list the decision in its PR.
2. A proposal stranded in `Executing` is never recovered.
3. The stuck-approved requeue re-enqueues every 15 min.
4. A creator may approve their own proposal.
5. The app's DB role can drop the append-only triggers.

## 6. Follow-ups from the C4 review (not done)

- 🔴 **Staging security hole, pre-existing:** `ServicePrincipalTokenValidator.ValidateAsync` only
  calls `ReadJwtToken`; it checks appid/tid/iss/aud/exp but never the signature. Anonymous
  `POST /api/E2ETest/auth` with a forged JWT yields a `super_user` cookie session on Staging
  (Development too). C4 closed the ads-approval part only. Fix in its own PR: validate with
  `JsonWebTokenHandler` against Entra's signing keys (OpenID configuration). Do this before
  trusting Staging for anything sensitive.
- A SPA token sent to `/mcp` resolves to Web (the channel is chosen from `azp`, not the endpoint).
- No full-pipeline `/mcp` tests (429 after 60 calls; app-only `ApproveAdProposal`).
- REST `/api` has no rate limit for agent tokens.
- Add the prompt-injection risk (the chief's Claude session holds approve rights and reads
  agent/search-term text) to the spec's risks section.
- `ListAdAuditEvents` / `ListAgentRuns` could become MCP read tools.

## 7. Recommended next steps

1. **Ondrej:** run the C4 rollout on Staging (`docs/integrations/marketing-agents-identity.md`):
   verify the SPA `azp`, seed groups, grant Marketer/Spravce, create the agent app + role, smoke
   test. Then decide §5 item 1.
2. **Start C5** in a fresh worktree from `origin/main`:
   - Read `CLAUDE.md`, this file (§4 especially), spec §6, §7.2, §8, §10, §12, then
     `docs/superpowers/plans/2026-10-07-marketing-core-c5-ui.md` in full.
   - Run the plan's "Before you start" checks; map names using §4 above.
   - Execute with `superpowers:subagent-driven-development`, one PR, TDD.
   - FE gate: `CI=false npm run build` + `npm run lint`; tests via `react-scripts test`. In a fresh
     worktree, `cd frontend && npm install --legacy-peer-deps` first.
3. **Separately:** the signature-validation fix from §6 (small backend PR).

## 8. Gotchas collected during C4

- `JsonGroupSeeder` is insert-if-missing: new permissions never reach existing groups without
  the documented grant step. Never use `--reset-group`.
- `scripts/sync-entra-access.sh --apply` replaces the API's app roles and would delete the
  `Marketing.Agent` role created by the runbook.
- C3's `AdProposalExecutionJobTests.RunAsync_TwoDeliveriesClaimConcurrently_OnlyOneExecutes` is
  flaky on a shared InMemory DbContext (failed once, passed 6/6 on rerun).
- `dotnet format` over the whole solution exits non-zero on a pre-existing EF1002 warning in
  `FlexiAnalyticsSyncService.cs`; use `--verify-no-changes --include <changed files>`.
