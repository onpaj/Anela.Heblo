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
