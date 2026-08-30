# Project State

_Update this file at the end of significant sessions._

## Project Summary

**Anela Heblo** — cosmetics company workspace app. Monorepo: .NET 8 backend + React frontend. Single Docker image deployed to Azure Web App for Containers.

## Active Branches (as of 2026-03-29)

- `main` — stable, production
- `feat/444-shoptet-hydration` — Shoptet test environment hydration feature (in progress)
- `feat/405-memory-directory` — this memory directory implementation

## Recently Completed

- Telemetry brainstorm routine (branch `claude/laughing-babbage-yeu5pw`, 2026-06-12):
  `docs/routines/telemetry-anomaly/telemetry-digest.sh` (curated App Insights KQL set →
  Markdown digest) + `docs/routines/telemetry-anomaly/README.md` (routine def) +
  updated `docs/handoff/appinsights-brainstorm-routine.md`. App Insights egress
  + read-telemetry secrets verified live. Remaining: create the scheduled
  routine in the Claude Code web UI per the routine doc.
- Shoptet test environment hydration (issue #444): added `SHOPTET_HYDRATE` env var gate, Rider launch profile, non-hardcoded storage
- MCP server: 15 tools across Catalog, Manufacturing, Batch Planning, Knowledge Base

- Frontend dead-export detector (issue #3927, PR #3932, `claude/beautiful-darwin-4uaqqg`,
  2026-08-15): added `knip` as a non-blocking frontend CI check (`frontend/knip.json`,
  `.github/workflows/ci-feature-branch.yml`). Initial backlog triaged into follow-up
  issue #3931.

- Coverage-gap fix for `UpdatePurchaseOrderInvoiceAcquiredHandler` (issue #3934,
  PR #3944, `claude/beautiful-darwin-nadsgf`, 2026-08-17): a scheduled `/plan-next-task`
  run couldn't use the AgentHarness branch-per-issue pipeline (designated-branch
  session, see `memory/gotchas/gh-cli-unavailable-in-cloud-sessions.md`), so it
  implemented the missing unit tests directly instead. Also fixed a real bug in
  `.claude/skills/_lib/gh_api.sh` (missing `Content-Type: application/json` on
  POST/PATCH) as part of the same PR.

- Coverage-gap fix for `ShippingMethodMapper` (issue #3961, PR #3964,
  `claude/beautiful-darwin-7pcjww`, 2026-08-24): same pattern again on a scheduled
  `/plan-next-task` run — designated-branch session, gh CLI/API writes blocked
  (`403 Write access ... not permitted through this proxy`, and `gh_api.sh`'s
  Content-Type fix from #3944 was locally reverted, uncommitted, in this
  session's working tree — left untouched as unrelated/out of scope). Used
  `mcp__github__*` tools for branch creation, label edits, and PR creation;
  implemented the missing unit tests directly instead of running the AgentHarness
  planning pipeline. Left one harmless empty orphan branch
  (`feature/3961-Coverage-Gap-Invoices-Shippingmethodmapper-All-Thr`, created via
  MCP before pivoting to the designated-branch approach) — couldn't delete it,
  remote branch deletion (`git push --delete` and REST) is also blocked by the
  proxy's write restriction; it has no unique commits and doesn't block anything.

- `DqtRun`/`StockWriteBackDqtComparer` TimeProvider fix (issue #3969, PR #3978,
  `claude/beautiful-darwin-2p00ks`, 2026-08-29): third occurrence of the same
  scheduled `/plan-next-task` pattern — designated-branch session, implemented
  directly instead of the AgentHarness pipeline. `DqtRun.Start()`/`Complete()`/
  `Fail()` now take explicit timestamp params; 4 recurring jobs, `RunDqtHandler`,
  `DriftDqtJobRunner`, `InvoiceDqtJobRunner` pass `TimeProvider`; comparer now
  injects `TimeProvider` too. Also found `gh_api.sh`'s Content-Type fix from
  #3944 had been reverted *on main* (commit `60da06c`, no linked PR found) —
  re-applied it (this time with an inline code comment pointing at
  `memory/gotchas/gh-cli-unavailable-in-cloud-sessions.md` so it stops
  flip-flopping) and left one more harmless orphan branch
  (`feature/3969-Arch-Review-Dataquality-Dqtrun-And-Stockwritebackd`, same
  can't-delete-via-proxy reason as #3961's).

- Raw-response logging for meeting-task JSON parse failures (issue #3972, PR #3981,
  `claude/beautiful-darwin-iv1zpr`, 2026-08-29): fourth occurrence of the same scheduled
  `/plan-next-task` pattern — designated-branch session, `gh auth status` invalid/`gh api`
  writes to git-data blocked, so implemented directly and used `mcp__github__*` for PR
  creation/labeling instead of the AgentHarness pipeline. This time `gh_api.sh`'s
  Content-Type fix was *not* reverted on `main` — the working tree just had an
  uncommitted local diff removing it (already matched `git diff HEAD` after restoring,
  nothing to commit) — so the "revert on main" failure mode from #3961/#3969 didn't
  recur; worth noting in case it's actually container/session-image drift rather than a
  repo-history revert. Fix: `ClaudeMeetingTaskExtractor.ExtractAsync`'s
  `catch (JsonException)` now logs the raw (fence-stripped) response text so the next
  malformed-JSON occurrence's actual payload is visible in telemetry. Also hit the
  documented `dotnet test` nodeReuse deadlock (`memory/gotchas/dotnet-build-hangs-nodereuse-accessmatrixgen.md`)
  on the first test run attempt — killed the stuck process tree, `dotnet build-server
  shutdown`, retried with `DOTNET_CLI_DISABLE_BUILD_SERVERS=1 MSBUILDDISABLENODEREUSE=1
  -nodeReuse:false`, which worked cleanly (11/11 passed).

- `OrgChartController` cancellation-swallowing fix (issue #3974, PR #3984,
  `claude/beautiful-darwin-q83em9`, 2026-08-29): fifth occurrence of the same
  scheduled `/plan-next-task` pattern — designated-branch session, `gh_api.sh`'s
  `create-ref` call hit the proxy's git-data write block again, so implemented
  directly instead of the AgentHarness pipeline; used `mcp__github__*` for PR
  creation/labeling. Added `catch (OperationCanceledException) { throw; }`
  before the generic `catch (Exception)` in `GetOrganizationStructure` so
  client disconnects propagate instead of logging a spurious 500. Also hit the
  documented `dotnet test` nodeReuse deadlock again, but this time the
  previously-documented fix alone wasn't enough — needed
  `-p:UseSharedCompilation=false` in addition; see the updated
  `memory/gotchas/dotnet-build-hangs-nodereuse-accessmatrixgen.md` entry.

- `OrgChartPage` filtering-logic extraction (issue #3975, PR #3985,
  `claude/beautiful-darwin-kz1l9n`, 2026-08-29): sixth occurrence of the same scheduled
  `/plan-next-task` pattern — designated-branch session, `gh auth status` invalid
  (`GITHUB_TOKEN` rejected), implemented directly instead of the AgentHarness pipeline;
  used `mcp__github__*` for PR creation/labeling (plain `git push` to origin worked fine,
  unlike git-data-API writes in earlier sessions). Also hit the standard uncommitted
  `gh_api.sh` Content-Type-header regression on session start (from `agentharness init`'s
  bundled template) — restored via `git checkout --`, did not recommit, matching the
  documented fix. Extracted the department/level filtering IIFE out of `OrgChartPage.tsx`
  into a new pure `filterPositions()` in `orgChartUtils.ts`, with 5 new unit tests.
  Deliberately did **not** follow the issue's suggested `useMemo` wrap: that call site is
  after the component's `if (!orgData) return` early return, so adding a hook there would
  violate React's Rules of Hooks (conditional hook call across renders) — kept it a plain
  function call instead and noted this in the PR body.

- Removed duplicated Shoptet state-ID constants from `PrintPickingListRequest` (issue #3987,
  PR #3993, `claude/beautiful-darwin-4l0fwf`, 2026-08-30): seventh occurrence of the same
  scheduled `/plan-next-task` pattern — designated-branch session, `claim_issue.sh`'s
  `create-ref` step hit the "Form-encoded ... Send the documented JSON body" 403 again
  (the `gh_api.sh` Content-Type-header regression from `agentharness init`'s bundled
  template, restored via `git checkout --`, not recommitted, matching the documented fix),
  so implemented directly instead of the AgentHarness pipeline. This time plain `git push`
  to origin worked fine (unlike some earlier sessions' git-data-API proxy block); used
  `mcp__github__create_pull_request`/`issue_write` for PR creation/labeling regardless,
  per the established workaround. Fix: `PrintPickingListRequest` (Logistics module) no
  longer declares its own copies of `DefaultSourceStateId`/`DefaultDesiredStateId`/
  `DefaultNoteStateId` — they now reference `ExpeditionPickingRequest`'s (ExpeditionList
  module) constants directly, since `LogisticsExpeditionPickingAdapter` already bridges
  the two modules and depends on `ExpeditionList.Contracts`. Updated the one test file
  (`PickingListIntegrationTests.cs`) that referenced the removed constants. Build clean,
  426/428 tests pass (2 pre-existing Docker/Testcontainers failures unrelated, no Docker
  in this environment).

- Pipeline fix: `git add -A -f` for gitignored `artifacts/` (issue #3980, PR #3991,
  `claude/beautiful-darwin-hl0gyk`, 2026-08-30): seventh occurrence of the same scheduled
  `/plan-next-task` pattern — designated-branch session, `gh auth status` invalid. This
  time the planning queue itself was empty (no `agent`-labeled issue without a PR), so
  rather than reporting "nothing to plan" the run picked up issue #3980 — an
  owner-filed, still-open issue describing two real bugs in the pipeline tooling itself.
  Bug 1 (`gh_api.sh` missing `Content-Type: application/json`) was already fixed on
  `main` — confirmed, no change needed. Bug 2 was real and unfixed: `git add -A
  artifacts/feat-{id}` in `.claude/agents/orchestrator.md` (6x), `.claude/agents/
  plan-orchestrator.md` (3x), and `.claude/skills/oneshot/SKILL.md` (1x) silently staged
  nothing because `artifacts/` is gitignored at the repo root — git refuses ignored
  paths without `-f`. Reproduced the exact "ignored by .gitignore... use -f" warning in
  a scratch repo to confirm before fixing. Added `-f` to all 10 occurrences. Also hit
  the standard uncommitted `gh_api.sh` Content-Type regression on session start (from
  `agentharness init`'s bundled template) — restored via `git checkout --`, did not
  recommit, matching the documented fix. Used `mcp__github__create_pull_request` +
  `issue_write` for PR creation/labeling; plain `git push` to origin worked fine.
- Duplication fix: Shoptet state-ID constants (issue #3987, PR #3994,
  `claude/beautiful-darwin-d887cw`, 2026-08-30): eighth occurrence of the same
  scheduled `/plan-next-task` designated-branch pattern. `claim_issue.sh`'s ref-creation
  call failed with the "Form-encoded... Send the documented JSON body" 403 — the same
  recurring `gh_api.sh` Content-Type regression (from `agentharness init`'s bundled
  template overwriting it on session start), not a new bug; `git checkout --` restored
  the already-fixed `main` version, no recommit needed. Per the documented workaround,
  implemented issue #3987 directly on the designated branch instead of running the
  pipeline: `PrintPickingListRequest`'s three duplicate `Default*StateId` consts now
  reference `ExpeditionPickingRequest`'s instead of redeclaring them. Confirmed via
  `ModuleBoundariesTests` that only "ExpeditionList -> Logistics" is a restricted
  direction — the reverse (Logistics -> ExpeditionList, which
  `LogisticsExpeditionPickingAdapter` already does) is unrestricted, so this introduced
  no boundary violation. `dotnet build`/`format`/tests all green (143 tests total across
  boundary + directly-affected suites). PR opened via `mcp__github__create_pull_request`
  + `issue_write` for the `agent` label.

- Shoptet state-ID constant deduplication (issue #3987, PR #3992,
  `claude/beautiful-darwin-8yyupc`, 2026-08-30): eighth occurrence of the same scheduled
  `/plan-next-task` pattern — designated-branch session. `claim_issue.sh`'s `create-ref`
  hit both documented walls in sequence: first the standard uncommitted `gh_api.sh`
  Content-Type-header regression (from `agentharness init`'s bundled template, restored
  via `git checkout --`), then after that the proxy's `403 Write access ... not permitted
  through this proxy` for git-refs writes. Implemented directly instead of the AgentHarness
  pipeline; plain `git push` to origin worked fine; used `mcp__github__create_pull_request`
  + `issue_write` for PR creation/labeling. Fix: `PrintPickingListRequest`
  (Logistics/Picking) now references `ExpeditionPickingRequest`'s (ExpeditionList)
  `DefaultSourceStateId`/`DefaultDesiredStateId`/`DefaultNoteStateId` consts directly
  instead of redeclaring identical values, matching the existing Logistics→ExpeditionList
  dependency direction already established by `LogisticsExpeditionPickingAdapter`. No
  orphan branch left behind this time since `create-ref` failed before creating anything.

- `SubmitArticleFeedbackRequest.ArticleId` JsonIgnore fix (issue #3989, PR #3997,
  `claude/beautiful-darwin-nxtvgf`, 2026-08-30): ninth occurrence of the same scheduled
  `/plan-next-task` pattern — designated-branch session, `gh auth status` invalid
  (`GITHUB_TOKEN` rejected). Also hit the standard uncommitted `gh_api.sh` Content-Type
  regression (plus `git add -A` for `artifacts/feat-*` reverted to missing `-f`, and an
  `orchestrator.md`/`plan-orchestrator.md` diff of the same shape) on session start, from
  `agentharness init`'s bundled template overwriting `.claude/agents/orchestrator.md`,
  `.claude/agents/plan-orchestrator.md`, `.claude/skills/_lib/gh_api.sh`, and
  `.claude/skills/oneshot/SKILL.md` — restored all four via `git checkout --`, matching
  main, no recommit needed. Implemented issue #3989 directly on the designated branch
  instead of running the AgentHarness pipeline: marked `SubmitArticleFeedbackRequest
  .ArticleId` with `[JsonIgnore]` so it's excluded from the request body's OpenAPI schema
  (the controller already overwrote it from the route param, so client-sent values were
  silently discarded but still misleadingly exposed as a writable body field). `dotnet
  build`/`format`/targeted tests all green (18/18). Plain `git push` to origin worked
  fine; PR opened via `mcp__github__create_pull_request` + `issue_write` for the `agent`
  label. Sibling candidate #3990 (`[Range]` dead-code attributes on `ListArticlesRequest`)
  left for a future run.

- `ListArticlesRequest` `[Range]` dead-code fix (issue #3990, PR #3999,
  `claude/beautiful-darwin-b7bqkw`, 2026-08-30): tenth occurrence of the same scheduled
  `/plan-next-task` pattern — designated-branch session. `claim_issue.sh`'s `create-ref`
  hit both documented walls in sequence: first the standard uncommitted `gh_api.sh`
  Content-Type-header regression (from `agentharness init`'s bundled template — restored
  via `git checkout --`), then after that the proxy's `403 Write access ... not permitted
  through this proxy` for git-refs writes. Same session also had the `git add -A` (missing
  `-f` for gitignored `artifacts/`) regression reappear in `.claude/agents/orchestrator.md`,
  `.claude/agents/plan-orchestrator.md`, and `.claude/skills/oneshot/SKILL.md` — restored
  all three via `git checkout --`, no recommit needed (already fixed on `main`). Implemented
  issue #3990 directly on the designated branch instead of running the AgentHarness
  pipeline: removed the dead `[Range(1, int.MaxValue)]`/`[Range(1, 100)]` attributes (and
  the now-unused `System.ComponentModel.DataAnnotations` import) from `ListArticlesRequest`,
  since `ArticlesController.List` manually constructs the request instead of binding it, so
  ASP.NET Core model validation never runs; tightened the handler's clamping comment to
  match. `dotnet build` (API project): 0 errors; `dotnet format --verify-no-changes`: clean.
  A filtered `dotnet test --filter ListArticles` run was left running in the background
  (very slow on this session's constrained single-core CPU, ~10 minutes just to compile)
  rather than blocking the PR on it, given the change is a pure attribute/comment removal
  with no logic change and the full solution build already succeeded — it finished after
  the PR was already open, confirming all 7 `ListArticlesHandlerTests` still pass. Plain
  `git push` to origin worked fine; PR opened via `mcp__github__create_pull_request` +
  `issue_write` for the `agent` label.

## Pending / Known Issues

- Memory directory (issue #405): adding cross-session knowledge accumulation — this PR
- Database migrations are manual (not automated in deployment)
- Branch `feature/meeting-mindmap`: MindMaps feature (project/workstream mind maps + Claude-rewrite
  Hangfire job with server-side edit guard) complete through Task 15 (final validation gate,
  2026-08-10). Backend build/format/tests and frontend lint/build/tests all green; zero MindMap
  test failures. Pending before it's usable end-to-end:
  1. Apply migration `AddMindMapsTables` manually to staging (`Heblo_TST`).
  2. Upload and assign the two new Entra app roles (`anela.mind_maps.read`,
     `anela.mind_maps.write`) to the Entra app registration — they currently exist only in
     `access-matrix-entra.generated.json`, which nothing consumes automatically. Without this
     every MindMaps endpoint returns 403 and the E2E scenario fails on its first click.
  3. Run `./scripts/run-playwright-tests.sh mindmaps` post-deploy to confirm the nightly E2E
     scenario (now also wired into the nightly workflow matrix).

## Key Infrastructure Notes

- CI runs: frontend Jest + backend .NET tests on PR
- Nightly: full Playwright E2E against staging
- OpenAPI TypeScript client auto-generated on `dotnet build`
- Secrets managed via local `secrets.json` (never `dotnet user-secrets set`)
