# Implementation: verify-full-build-and-format

## What was implemented

Nothing. This is a **verification-only** task — the final gate for the arch-review refactor
that moved `LeafletDocumentSummary` out of
`UseCases/GetLeafletDocuments/GetLeafletDocumentsRequest.cs` into its own file under the
Leaflet module's shared `Contracts/` folder.

Every checklist step was executed for real against the worktree at commit `34932bf99`.
**All six steps were run — none were skipped or substituted with reasoning.** Real terminal
output is pasted verbatim below.

**Result: all steps pass. No source-file changes were produced, so Step 6 committed nothing.**

## Files created/modified

None. `git status --short` at the end of the task shows only the pipeline's own
`artifacts/feat-4105/state.json` checkpoint update:

```
$ git status --short
 M artifacts/feat-4105/state.json
```

Steps 2 (`dotnet format`) and 4 (client regeneration) both produced **zero** file changes,
so per the task context Step 6 has nothing to commit — the two prior task commits
(`9105ba8ef`, and the move commit before it) already contain the complete change.

## Verification performed

### Step 1 — Full backend build ✅ PASS

The task context says `cd backend && dotnet build`, but there is no `.sln` under `backend/`
(`backend/` contains only `src/`, `test/`, `tools/`); the solution file is
`Anela.Heblo.sln` at the worktree root and it covers the entire backend. Built that instead:

```
$ dotnet build Anela.Heblo.sln
EXIT=0
```

Verbatim summary lines from the build log:

```
Build succeeded.
    ...
    81 Warning(s)
    0 Error(s)

Time Elapsed 00:00:23.72
```

The preceding clean (cold) build of the same solution reported the full warning set:

```
    252 Warning(s)
    0 Error(s)

Time Elapsed 00:02:26.42
```

**0 errors in both runs.** All 252 warnings are pre-existing nullable-reference (`CS8602`,
`CS8618`, `CS8604`, `CS8601`, `CS8600`, `CS8619`, `CS8620`, `CS8625`) warnings in test and
domain files unrelated to Leaflet (Journal, Purchase, Manufacture, Catalog, Photobank,
Smartsupp, Ledger, …). **No warning is attributable to the moved type** — no warning
references `Contracts/LeafletDocumentSummary.cs`, `GetLeafletDocumentsRequest.cs`,
`GetLeafletDocumentsHandler.cs`, `UploadLeafletHandler.cs`, or `UploadLeafletResponse.cs`.

### Step 2 — Format check ✅ PASS

```
$ dotnet format Anela.Heblo.sln --verify-no-changes
FORMAT_EXIT=0
```

The command produced **completely empty output** and exit code 0 — i.e. **zero formatting
violations** anywhere in the solution, including the new
`backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/LeafletDocumentSummary.cs`.
Because there were no violations, the remedial `dotnet format` (without
`--verify-no-changes`) described in the task context was **not needed** and was not run,
and there is nothing to re-stage.

### Step 3 — Full backend test run ✅ PASS (one unrelated flaky test, see below)

Run with the build-first / `--no-build` workaround because concurrent `dotnet test` runs
across sibling worktrees deadlock the shared compiler, and with `--filter
"Category!=Integration"` because CI excludes Integration tests (they require a running
podman/docker daemon).

**Exact command run:**

```
$ dotnet test Anela.Heblo.sln --no-build -p:UseSharedCompilation=false --filter "Category!=Integration"
TEST_EXIT=1
```

Verbatim per-assembly summary lines — **all eight test projects in the solution**:

```
Passed!  - Failed:     0, Passed:    34, Skipped:     0, Total:    34, Duration: 640 ms - Anela.Heblo.Adapters.HomeAssistant.Tests.dll (net8.0)
Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6, Duration: 334 ms - Anela.Heblo.Adapters.OpenMeteo.Tests.dll (net8.0)
Passed!  - Failed:     0, Passed:    16, Skipped:     0, Total:    16, Duration: 403 ms - Anela.Heblo.Adapters.OpenAI.Tests.dll (net8.0)
Passed!  - Failed:     0, Passed:    11, Skipped:     0, Total:    11, Duration: 83 ms - Anela.Heblo.Adapters.Logeto.Tests.dll (net8.0)
Passed!  - Failed:     0, Passed:    28, Skipped:     0, Total:    28, Duration: 2 s - Anela.Heblo.Adapters.Plaud.Tests.dll (net8.0)
Passed!  - Failed:     0, Passed:   270, Skipped:     0, Total:   270, Duration: 2 s - Anela.Heblo.Adapters.Flexi.Tests.dll (net8.0)
Passed!  - Failed:     0, Passed:   105, Skipped:     0, Total:   105, Duration: 5 s - Anela.Heblo.Adapters.Shoptet.Tests.dll (net8.0)
Failed!  - Failed:     1, Passed:  6825, Skipped:     4, Total:  6830, Duration: 14 s - Anela.Heblo.Tests.dll (net8.0)
```

**Totals: 7295 passed, 1 failed, 4 skipped (7300 total).**

**Scope disclosure:** what was run is the whole solution *minus* tests carrying
`Category=Integration`. This matches the CI gate exactly (CI applies the same
`Category!=Integration` filter); Integration tests need a live podman container host that
is not available in this pipeline worker. Nothing else was narrowed.

#### The single failure is a pre-existing, load-sensitive flake — unrelated to this change

```
Anela.Heblo.Tests.Persistence.Resilience.DbResiliencePipelineProviderTests.Pipeline_AbortsByTotalTimeBudget [FAIL]
  Error Message:
   Expected sw.Elapsed to be less than 5s, but found 5s, 783ms and 893.4µs.
  Stack Trace:
     at Anela.Heblo.Tests.Persistence.Resilience.DbResiliencePipelineProviderTests.Pipeline_AbortsByTotalTimeBudget()
       in .../backend/test/Anela.Heblo.Tests/Persistence/Resilience/DbResiliencePipelineProviderTests.cs:line 195
```

This is a wall-clock stopwatch budget assertion (`sw.Elapsed < 5s`) that overran by 783 ms
while the machine was under concurrent build/test load from sibling worktree workers. It
lives in `Persistence/Resilience/`, touches no Leaflet code, and this branch's diff touches
no resilience or persistence code at all.

**Confirmed flaky by re-running it in isolation:**

```
$ dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
    -p:UseSharedCompilation=false --filter "FullyQualifiedName~DbResiliencePipelineProviderTests"
Passed!  - Failed:     0, Passed:     8, Skipped:     0, Total:     8, Duration: 2 s - Anela.Heblo.Tests.dll (net8.0)
```

#### Leaflet tests specifically — all green

```
$ dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build \
    -p:UseSharedCompilation=false --filter "FullyQualifiedName~Leaflet"
Passed!  - Failed:     0, Passed:   164, Skipped:     3, Total:   167, Duration: 28 s - Anela.Heblo.Tests.dll (net8.0)
```

(The 3 skips are pre-existing `[Fact(Skip=...)]` declarations, not filtered-out tests.)

### Step 4 — OpenAPI/TypeScript client regeneration, zero drift ✅ PASS

Both halves of this step were executed for real: the **actual NSwag regeneration** *and* the
frontend build.

`frontend/node_modules` was absent in this worktree, so the client was first regenerated
directly through the project's own documented target
(`backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`, target `GenerateFrontendClientManual`,
which runs `dotnet nswag run nswag.frontend.json` — note the automatic `GenerateFrontendClient`
`AfterTargets="Build"` target is `Condition="false"`, i.e. disabled, so a plain build does
*not* regenerate the client and the manual target is the correct invocation):

```
$ md5 -q frontend/src/api/generated/api-client.ts      # BEFORE regeneration
82f44562438fab82468026b42ef77769

$ dotnet msbuild backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj -t:GenerateFrontendClientManual
NSWAG_EXIT=0
  ...
  Done.

  Duration: 00:00:08.4550204
  Frontend API client generation completed.

$ md5 -q frontend/src/api/generated/api-client.ts      # AFTER regeneration
82f44562438fab82468026b42ef77769

$ git diff --stat frontend/src/api/generated/api-client.ts
(no output — empty diff)
```

**The regenerated file is byte-identical to the committed one (same MD5), and the diff is
empty.** This is direct evidence for spec **FR-3 (zero API-shape drift)** rather than an
inference: NSwag re-derived the OpenAPI document from the freshly built assemblies and
emitted exactly the same TypeScript. The C# namespace change is invisible to the OpenAPI
schema because NSwag keys schemas on the type's simple name (`LeafletDocumentSummary`),
which is unchanged, as is its member shape. `LeafletDocumentSummary` still appears in
`frontend/src/api/generated/api-client.ts` in all its prior roles (the
`GetLeafletDocumentsResponse.documents` array element at lines 25987/26036, the exported
class at 26043, the `ILeafletDocumentSummary` interface at 26093, and
`UploadLeafletResponse.document` at 26233).

`npm ci` was then run so the frontend build could also be executed. Plain `npm ci` failed on
a pre-existing upstream peer-dependency conflict (`ERESOLVE`, unrelated to this branch);
`--legacy-peer-deps` installed cleanly:

```
$ npm ci --legacy-peer-deps --no-audit --no-fund
NPMCI_EXIT=0
added 1736 packages in 13s
```

```
$ CI=false npm run build
FEBUILD_EXIT=0
Compiled successfully.

File sizes after gzip:

  1.34 MB   build/static/js/main.2607cd4e.js
  24.54 kB  build/static/css/main.773ccd16.css
```

Gated on `CI=false npm run build` (the real CRA/TypeScript compile) rather than
`npx tsc --noEmit`, which false-greens in this repo. `git status` after the build remains
clean apart from `state.json` — the build output and `node_modules` are gitignored.

### Step 5 — Frontend lint ✅ PASS (at unchanged pre-existing baseline)

```
$ npm run lint          # eslint src --ext .ts,.tsx
LINT_EXIT=1
✖ 249 problems (236 errors, 13 warnings)
  7 errors and 0 warnings potentially fixable with the `--fix` option.
```

**This non-zero exit is the repository's pre-existing baseline, not a regression from this
change** — and that is demonstrated mechanically, not asserted:

1. This branch changes **zero** frontend files. `git diff --name-only <merge-base
   origin/main>...HEAD -- frontend/` returns **empty output**. The entire branch diff is 5
   backend `.cs` files + 1 backend test `.cs` file + `artifacts/`.
2. The 249 problems span **43 distinct files**, and the intersection of that 43-file set
   with the set of files this branch modified is **empty** (verified with `comm -12` over
   the two sorted lists — zero overlap lines).

The reported violations are long-standing project-wide rules
(`testing-library/no-node-access`, `import/first`, `testing-library/prefer-find-by`) in test
files such as `ThemeContext.test.tsx`, `OvertimePage.test.tsx`, and
`features/leaflet-generator/__tests__/LeafletForm.test.tsx`. The last one is worth calling
out explicitly because its path contains "leaflet": it is the **leaflet-*generator*** UI
feature, a different frontend module, its two `no-node-access` errors at line 90 are
pre-existing, and it is untouched by this branch.

### Step 6 — Final commit ➖ NOT APPLICABLE

The task context conditions this step on "only if Step 2 or Step 4 produced file changes to
stage". Step 2 produced no changes (format check clean, empty output) and Step 4 produced no
changes (regenerated client byte-identical, empty diff). `git status --short` confirms the
only modified path is the pipeline's own `artifacts/feat-4105/state.json`. **Nothing to
commit for this task.**

## Tests

No test files were created or modified — this is a verification task. The existing suite was
executed as described in Step 3: **7295 passed / 1 failed (unrelated pre-existing flake,
green on isolated re-run) / 4 skipped**, including **164 passing Leaflet tests**.

Note for the record: the previous unit's artifact claimed "154 passing Leaflet tests". The
independently measured figure under `FullyQualifiedName~Leaflet` is **164 passed, 3 skipped,
167 total**. The difference is a filter-scope discrepancy in the earlier report, not a
regression — the count is higher, not lower, and zero Leaflet tests fail.

## How to verify

From the worktree root:

```bash
dotnet build Anela.Heblo.sln                                   # expect: Build succeeded. / 0 Error(s)
dotnet format Anela.Heblo.sln --verify-no-changes              # expect: exit 0, no output
dotnet test Anela.Heblo.sln --no-build -p:UseSharedCompilation=false --filter "Category!=Integration"
md5 -q frontend/src/api/generated/api-client.ts                # note the hash
dotnet msbuild backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj -t:GenerateFrontendClientManual
md5 -q frontend/src/api/generated/api-client.ts                # expect: identical hash
git diff --stat frontend/src/api/generated/api-client.ts       # expect: empty
```

Always `dotnet build` before `dotnet test --no-build`; concurrent `dotnet test` runs across
sibling worktrees deadlock the shared compiler at 0% CPU.

## Notes

- **Command-path deviations (all necessary, none narrowing scope):** the task context's
  `cd backend && dotnet build` / `dotnet test` cannot work as written — there is no solution
  file under `backend/`. `Anela.Heblo.sln` at the worktree root was used instead; it covers
  the whole backend, so coverage is a superset, not a subset, of what was asked.
- **`--filter "Category!=Integration"`** matches the CI gate. Integration tests require a
  live podman/docker host unavailable here. This is the only exclusion, and it is disclosed
  rather than silently applied.
- **`--no-build -p:UseSharedCompilation=false`** is required in this multi-worktree
  environment; a plain `dotnet test` hangs indefinitely at 0% CPU against concurrent workers.
- **The one test failure is not a stale-binary artifact.** Its stack trace is consistent with
  the source (a stopwatch budget assertion at `DbResiliencePipelineProviderTests.cs:195`),
  and it passes on isolated re-run against the same binaries — so the flake is timing, not
  staleness, and no rebuild was warranted.
- **`npm ci` needed `--legacy-peer-deps`** due to a pre-existing upstream `ERESOLVE` peer
  conflict in `package-lock.json`. Unrelated to this branch; `package.json` and
  `package-lock.json` are untouched by it. Flagged only so a later worker isn't surprised.
- **Frontend lint exits non-zero on `main` as well as here** (249 pre-existing problems
  across 43 files, none in files this branch touches). Cleaning that up is squarely out of
  scope for an arch-review type-relocation task and would violate the repo's "surgical
  changes" rule.
- The arch-review goal is satisfied: `LeafletDocumentSummary` now lives at
  `backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/LeafletDocumentSummary.cs`
  in namespace `Anela.Heblo.Application.Features.Leaflet.Contracts`, all four consumers
  import it from there, it remains a `class` (not a record, per the repo's OpenAPI-generator
  rule), and the public API contract is provably byte-for-byte unchanged.

## PR Summary

Moved `LeafletDocumentSummary` out of the `GetLeafletDocuments` use-case file into the
Leaflet module's shared `Contracts/` folder, so a DTO consumed by two different use cases no
longer forces one use case to depend on another. `UploadLeaflet` previously had to import
`...UseCases.GetLeafletDocuments` purely to reach this type; it now imports
`...Features.Leaflet.Contracts` instead, removing the cross-use-case coupling the
architecture review flagged.

This final task verified the refactor end to end. The full solution builds with 0 errors,
`dotnet format --verify-no-changes` reports no violations, and the backend suite passes
7295 tests (the single failure is a pre-existing wall-clock flake in
`DbResiliencePipelineProviderTests`, unrelated to Leaflet, green on isolated re-run), with
all 164 Leaflet tests passing. Most importantly for the API contract: regenerating the
TypeScript client through NSwag produces a **byte-identical** `api-client.ts` (same MD5,
empty `git diff`), confirming spec FR-3's zero-API-shape-drift requirement directly rather
than by inference. The frontend production build compiles successfully and lint sits at its
unchanged pre-existing baseline. No files were changed by this verification task.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/LeafletDocumentSummary.cs` — new home for the DTO (class, 7 members, unchanged shape)
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletDocuments/GetLeafletDocumentsRequest.cs` — type removed from this file; `Contracts` using added
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletDocuments/GetLeafletDocumentsHandler.cs` — `Contracts` using added
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletHandler.cs` — import repointed from `UseCases.GetLeafletDocuments` to `Contracts`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletResponse.cs` — import repointed from `UseCases.GetLeafletDocuments` to `Contracts`
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletControllerTests.cs` — `Contracts` using added
- (this task) no files changed — verification only

## Status
DONE
