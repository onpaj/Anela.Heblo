# Implementation: final-validation

## What was implemented

This is a validation-only task — no feature code was changed. All seven gates from the
task spec were executed for real in the worktree
(`/home/user/worktrees/feature-4042-Post-Mcp-35-8-Bad-Request-Rate-59-165-In-P7d-No-Di`)
on branch `feature/4042-Post-Mcp-35-8-Bad-Request-Rate-59-165-In-P7d-No-Di`.

- **Step 1 — Backend build**: `dotnet build` (run against `Anela.Heblo.sln` at the repo
  root, since `backend/` itself has no `.sln`/`.csproj` at its top level — see Notes).
  **PASS** — `Build succeeded`, 0 errors, 261 pre-existing nullable-reference warnings
  (all in files untouched by this feature).
- **Step 2 — Backend format check**: `dotnet format Anela.Heblo.sln --verify-no-changes`.
  **PASS** — exit code 0, no diffs. No reformatting commit was needed.
- **Step 3 — MCP-namespaced tests**: `dotnet test --filter "FullyQualifiedName~MCP"`.
  **PASS** — 120/120 passed, 0 failed (`Anela.Heblo.Tests.dll`). Confirms all P1–P12
  equivalents, the widened GET assertion, and the `McpTelemetryHelpers` tests are
  present and green (24 `[Fact]`/`[Theory]` cases in
  `McpBadRequestMiddlewareTests.cs`, 6 in `McpTelemetryHelpersTests.cs`).
- **Step 4 — Full backend test suite**: `dotnet test` (whole solution).
  **PASS WITH PRE-EXISTING FAILURES** — 7143 passed / 190 failed / 4 skipped (7343
  total) across all test projects. Every one of the 190 failures is a pre-existing
  environment limitation, not a regression from this feature (see Tests section for
  the breakdown and evidence). Zero failures in any MCP-namespaced test.
- **Step 5 — Frontend gates**: `npm run build` **PASS** (compiled successfully,
  optimized production build produced). `npm run lint` **FAILED with 236 pre-existing
  errors** across ~30 test files, none of which this feature touches (confirmed via
  `git diff --name-only origin/main...HEAD -- frontend/` → 0 files). Pre-existing
  baseline issue, out of scope for this feature.
- **Step 6 — Manual audit**: `git diff --stat origin/main...HEAD` matches the spec's
  expected file set exactly, modulo the pipeline's own `artifacts/feat-4042/**`
  bookkeeping files (brief/spec/design/task-plan/impl/review markdown — expected
  pipeline overhead, not feature code). Confirmed **no diff** to `Program.cs`,
  `ApplicationBuilderExtensions.cs`, `McpModule.cs`, any `appsettings*.json`, any
  feature-flag config, or any file under `frontend/`.
- **Step 7 — PR-description checklist**: confirmed `docs/integrations/mcp-server.md`
  already contains (a) the canonical Kusto query, (b) an explicit statement that
  `McpBadRequestMiddleware` now observes both GET and POST for the `McpBadRequest`
  event, and (c) the `RemoteIp`/`ForwardedHeaders` follow-up note. No commit needed
  for this step (verification only, per spec).

## Files created/modified

None — validation only. No feature code, docs, or tests were changed. No `dotnet
format` commit was needed (Step 2 was already clean). The only uncommitted change in
the working tree is the pipeline's own `artifacts/feat-4042/state.json` (updated by
the harness itself when this task started), which was left untouched.

## Tests

- MCP-namespaced (`--filter "FullyQualifiedName~MCP"`): **120 passed, 0 failed, 0
  skipped** (`Anela.Heblo.Tests.dll`).
- Full backend suite (`dotnet test` on `Anela.Heblo.sln`), by project:
  - `Anela.Heblo.Tests.dll`: 6659 passed / 105 failed / 4 skipped (6768 total)
  - `Anela.Heblo.Adapters.Shoptet.Tests.dll`: 119 passed / 13 failed / 1 skipped (133 total)
  - `Anela.Heblo.Adapters.Flexi.Tests.dll`: 270 passed / 72 failed / 5 skipped (347 total)
  - `Anela.Heblo.Adapters.HomeAssistant.Tests.dll`: 34 passed / 0 failed
  - `Anela.Heblo.Adapters.Plaud.Tests.dll`: 28 passed / 0 failed
  - `Anela.Heblo.Adapters.OpenMeteo.Tests.dll`: 6 passed / 0 failed
  - `Anela.Heblo.Adapters.OpenAI.Tests.dll`: 16 passed / 0 failed
  - `Anela.Heblo.Adapters.Logeto.Tests.dll`: 11 passed / 0 failed
  - **Totals: 7143 passed / 190 failed / 4 skipped / 7343 total.**
  - All 190 failures are pre-existing environment limitations of this sandbox, not
    regressions:
    - The large majority (Postgres-backed `*IntegrationTests`, `*SqlShapeTests`,
      `*RepositoryUpsertIntegrationTests`, `LeafletRepositoryIntegrationTests`,
      `KnowledgeBaseRepositoryIntegrationTests`, etc.) throw
      `System.ArgumentException: Docker is either not running or misconfigured` from
      `Testcontainers.PostgreSql...Validate()` — verified `docker info` in this
      sandbox reports `failed to connect to the docker API at
      unix:///var/run/docker.sock ... no such file or directory` (daemon not
      running here).
    - The Flexi adapter integration failures (`Flexi*ClientIntegrationTests`) fail
      fixture construction with `ArgumentNullException: implementationInstance` /
      missing `FlexiIntegrationTestFixture` data — a live-Flexibee-credentials
      fixture not configured in this sandbox.
    - The Shoptet adapter integration failures
      (`ShoptetTestEnvironmentHydrationTests`, `ShoptetStockClientIntegrationTests`,
      `ShoptetApiInvoiceSourceIntegrationTests`) fail with
      `InvalidOperationException: Missing Shoptet:StatusId:EXP in configuration` —
      live-Shoptet user-secrets not present in this sandbox.
  - **Zero failures are in the MCP namespace or in any file this feature touches.**
- Frontend: `npm run build` succeeded (no test step was in scope per Step 5's
  spec — only build + lint were requested).

## How to verify

```bash
cd /home/user/worktrees/feature-4042-Post-Mcp-35-8-Bad-Request-Rate-59-165-In-P7d-No-Di

# Step 1/2 — build + format (run from repo root; backend/ has no top-level .sln)
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes

# Step 3 — MCP tests
# NOTE: on this sandbox, a plain `dotnet test --filter "FullyQualifiedName~MCP"` run
# against the .sln can hit a known nodeReuse/shared-compilation deadlock right after
# "Generating access matrix artifacts..." (see
# memory/gotchas/dotnet-build-hangs-nodereuse-accessmatrixgen.md). If it hangs with
# every dotnet/MSBuild process frozen on futex_do_wait (check via
# `cat /proc/<pid>/wchan` and frozen `ps -o time=`), kill the tree and rerun with:
dotnet build-server shutdown
MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DISABLE_BUILD_SERVERS=1 \
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MCP" -m:1 -nodeReuse:false -p:UseSharedCompilation=false

# Step 4 — full suite (same deadlock workaround, targets the whole .sln)
dotnet build-server shutdown
MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DISABLE_BUILD_SERVERS=1 \
  dotnet test Anela.Heblo.sln -m:1 -nodeReuse:false -p:UseSharedCompilation=false

# Step 5 — frontend (npm install needs --legacy-peer-deps in this repo, per
# .github/workflows/ci-feature-branch.yml; a bare `npm ci`/`npm install` hits an
# ERESOLVE conflict between knip and @types/node)
cd frontend && npm install --legacy-peer-deps && npm run build && npm run lint

# Step 6 — manual audit
cd .. && git diff --stat origin/main...HEAD
git diff --name-only origin/main...HEAD | grep -E \
  "Program\.cs|ApplicationBuilderExtensions|McpModule\.cs|appsettings|feature.?flag"
git diff --name-only origin/main...HEAD -- frontend/
```

## Notes

- **Deviation from the literal spec commands**: the spec's Step 1–4 commands are
  written as `cd backend && dotnet build` / `dotnet test` / `dotnet format`, but
  `backend/` has no `.sln` or top-level `.csproj` — the solution file
  (`Anela.Heblo.sln`) lives at the repo root (this matches
  `docs/development/setup.md`, which documents these commands without a `cd
  backend` prefix). I ran them against `Anela.Heblo.sln` from the repo root instead;
  this is the only way any of these commands can resolve a project, so it is very
  unlikely to be an intentional deviation — flagging it in case the spec's `cd
  backend` prefix was a slip rather than a real environment difference.
- **Sandbox-only build deadlock (not a feature defect)**: the first attempt at Step 3
  hung indefinitely with every `dotnet`/`MSBuild.dll` process frozen on
  `futex_do_wait` right after `Generating access matrix artifacts...` — exactly the
  documented pattern in `memory/gotchas/dotnet-build-hangs-nodereuse-accessmatrixgen.md`.
  Killed the stuck process tree and reran with `dotnet build-server shutdown` +
  `MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DISABLE_BUILD_SERVERS=1
  -nodeReuse:false -p:UseSharedCompilation=false`, which completed cleanly both times
  (Step 3 and Step 4). This is a pre-existing sandbox/tooling quirk unrelated to this
  feature's diff (it reproduces on the unmodified access-matrix generator step, not
  on any MCP file) — no code was changed to work around it, only the test-invocation
  flags. The access-matrix generator also regenerated its `.generated.cs`/`.ts`/
  `.json` output files as a side effect of the build; `git status` after both test
  runs showed no diff to any of them (only the pipeline's own `state.json` changed),
  so nothing needed to be reverted or committed.
- **Frontend `npm install` needs `--legacy-peer-deps`**: a bare `npm ci`/`npm
  install` fails with an ERESOLVE conflict (`knip@5.88.1` peer `@types/node@">=18"`
  vs. the root project's pinned `@types/node@^16.18.108`). This repo's own CI
  (`.github/workflows/ci-feature-branch.yml`, `ci-main-branch.yml`) always installs
  with `--legacy-peer-deps`, so that flag was used here too — not a feature-related
  issue.
- **Step 4 full-suite failures and Step 5 lint failures are both pre-existing and
  out of scope**, per the task's own instructions to distinguish pre-existing
  failures from regressions before touching anything: this feature's diff contains
  zero frontend files and zero files under `Persistence`/`Adapters.Flexi`/
  `Adapters.Shoptet` beyond what's already accounted for, so none of these 190
  backend test failures or 236 lint errors could have been introduced by it. No fix
  was attempted for either, per the "only fix real regressions from this feature"
  guidance — fixing 190 environment-caused test failures or 236 unrelated lint
  errors would be far outside a validation-only task's scope and would need
  Docker-in-sandbox / live Shoptet & Flexi credentials / a separate lint-debt
  cleanup effort respectively.
- No git commit was made — Step 2's format check was already clean, so there was
  nothing to commit per the task's own instructions.

## PR Summary

Final validation for issue #4042 (MCP `POST /mcp` 400 rate diagnostics) is
complete and the feature is green. Backend build succeeds with 0 errors,
`dotnet format --verify-no-changes` is clean, and all 120 MCP-namespaced tests pass,
confirming `McpBadRequestMiddleware` now emits the same structured `McpBadRequest`
warning-level telemetry (EventId 5931 for GET, 5932 for POST) for both GET and POST
`/mcp` 400 responses, backed by the new shared `McpTelemetryHelpers.TruncateSessionId`
helper. The full backend suite has 190 pre-existing failures, all traced to sandbox
environment gaps (no Docker daemon for Testcontainers-backed Postgres integration
tests; no live Shoptet/Flexi credentials for adapter integration tests) — none touch
the MCP namespace or any file this feature changed. Frontend build succeeds; frontend
lint has 236 pre-existing errors in test files this feature does not touch. A manual
diff audit confirms the changed-file set matches the spec exactly (plus expected
pipeline bookkeeping files), with zero incidental changes to `Program.cs`,
`ApplicationBuilderExtensions.cs`, `McpModule.cs`, `appsettings*.json`, feature-flag
config, or any frontend file. `docs/integrations/mcp-server.md` already carries the
canonical Kusto query, the GET+POST coverage note, and the `RemoteIp`/
`ForwardedHeaders` follow-up note needed for the PR description. No code was changed
by this task.

### Changes
- None (validation only).

## Status
DONE_WITH_CONCERNS
