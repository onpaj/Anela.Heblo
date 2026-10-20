# Handoff: Marketing ads platform — status after WS1 (2026-10-14)

Read this first in a fresh session, then the documents it points to. It records where the
marketing agents platform stands after the Google Ads workspace (WS1) finished, and what to
pick up next. Verified against `origin/main` at `e1b74164f` on 2026-10-14; re-verify with
`git log origin/main --oneline | grep -iE "marketing|ads"` before trusting it.

Binding background, unchanged:
- Spec: `docs/superpowers/specs/2026-10-07-marketing-agents-platform-design.md` (§2 sequencing, §12 names)
- Original handoff: `docs/handoff/marketing-agents-platform.md` (§2 decisions and §3 security are binding)
- Plans: `docs/superpowers/plans/2026-10-07-marketing-*.md` (one per PR / workspace)

## 1. What is merged

| PR | What | Plan |
|---|---|---|
| #4452 | Spec + 8 implementation plans | — |
| #4453 | Core **C1**: contracts, `ads` schema, test kit | `marketing-core-c1-contracts` |
| #4454 | WS1 Google Ads **read source** | `marketing-google-ads` Part B |
| #4476 | Core **C2**: daily + change-history sync, out-of-band detection, Metabase views | `marketing-core-c2-sync-detection` |
| #4494 | Ads history backfill tool (`backend/tools/Anela.Heblo.AdsBackfill`) | — (ad hoc) |
| #4495 | WS1 Google Ads **executor** (AddNegativeKeyword, PauseAd) | `marketing-google-ads` Part C |

**WS1 (Google Ads) is code-complete.** Every task in its plan is shipped.

## 2. What is not started

| Item | Worktree | State |
|---|---|---|
| Core **C3**: proposals, approval, limits, kill switch, execution pipeline, revert, audit | none (create one) | not started; **critical path** |
| Core **C4**: Feature values/groups, agent identity, `azp` channel check, MCP tools | — | blocked on C3 |
| Core **C5**: approval inbox UI, audit/out-of-band views, settings page | — | blocked on C4 |
| WS2 **Meta**: spike → read source → executor | `marketing-meta` (branch `feature/marketing-meta`, still at the plan commit `171dcf86a`) | not started; spike needs Ondrej's credentials |
| WS3 **Sklik**: spike → new adapter → read source → executor | `marketing-sklik` (branch `feature/marketing-sklik`, at `171dcf86a`) | not started; spike needs Ondrej's credentials |

Meta and Sklik branches are stale (a week behind main); branch fresh from `origin/main` instead.

## 3. Google Ads executor: what it does and does not do yet

- `GoogleAdsActionExecutor` is registered but **nothing calls it** until C3 (execution pipeline)
  and C4 (approval channel) are merged and an admin turns the kill switch on (spec §6.6).
- Validate-only is forced outside the `Production` host environment, regardless of
  `GoogleAds:ValidateOnly` from appsettings, App Settings or Key Vault (staging shares the live account).
  In production `appsettings.Production.json` sets it to `false`.
- Behaviour reference: `docs/integrations/google-ads-api.md` §12 and
  `docs/processes/flow-google-ads-action-execution.md`.

## 4. Open WS1 follow-ups (small, mostly Ondrej)

1. ~~**Manual validate-only smoke test**~~ — **done 2026-10-14** (run by Claude at Ondrej's request):
   `Passed: 1` for PauseAd on ad `148426617964~754549495678` and AddNegativeKeyword on campaign
   `19788670940` (DSA | All); `change_event` showed 0 events that day afterwards.
2. **Dedicated Google user** — Heblo acts as `ondra@anela.cz` (ADMIN). Recommended: a `heblo-ads@`
   user with Standard access, new refresh token, update `GoogleAds--OAuth2RefreshToken` and
   `GoogleAds--HebloUserEmail` in both vaults. Owner decision; not blocking.
3. **Confirm live data in prod** — after the C2 deploy, check that `sync-ads-daily` and
   `sync-ads-change-history` populate the `ads` schema for Google Ads (process docs list the
   runtime facts to record). Record findings in those docs' *Runtime facts*.

## 5. Recommended next step: Core C3

C3 is the only thing between the merged executor and a real, approved write; C4 and C5 queue
behind it. Start it in a fresh worktree from `origin/main`:

1. Read `CLAUDE.md`, spec §4.3, §6, §10, §12, original handoff §2–3, then
   `docs/superpowers/plans/2026-10-07-marketing-core-c3-proposals.md` in full.
2. Check the plan's preconditions against what C1/C2 actually shipped
   (`backend/src/Anela.Heblo.Application/Features/MarketingAds/`); C2 may have diverged from the
   plan text — the code on main wins, note deviations in the PR.
3. Execute with `superpowers:subagent-driven-development`, one PR.

Things a C3 implementer must know from WS1:
- `AdExecutionResult` with `Outcome = Failed` and an error starting `ValidateOnly:`
  (`GoogleAdsExecutionResults.ValidateOnlyMarker`) means "Google accepted, nothing changed".
  The pipeline must not treat it as a platform rejection worth retrying, nor as applied.
- Executors throw only for transport/auth (`GoogleAdsApiException`); platform rejections come
  back as `Failed`. A thrown mutate may still have been applied (5xx/timeout are not retried) —
  the pipeline should re-read state (`ReadCurrentAsync`) before retrying anything.
- Revert of a negative keyword needs the original result's `PlatformResourceId`; persist it.
  A create that returned 200 without a resource name is reported `Failed` with a
  "verify in the account" message — surface it to the human, do not retry blindly.

Alternative if Ondrej has credentials ready: WS2 Meta or WS3 Sklik spikes (Part A of their
plans) can run in parallel with C3; they need no core code.

## 6. Gotchas collected along the way

- Process docs: any change to a sync/flow updates its `docs/processes/` doc in the same PR, then
  `python3 scripts/process-docs/check.py index`.
- `dotnet test` in Conductor/Orca worktrees: build first, then `--no-build -p:UseSharedCompilation=false`.
- Mutate-path unit tests use `StatefulFakeGoogleAdsApi` (honours `validateOnly`), so a
  "nothing was created" assertion alone is vacuous — also assert the request flag.
- Google Ads API version is v25 over REST; the old `Google.Ads.GoogleAds` 21.1.0 SDK is only used
  by the untouched billing importer.
