# Implementation: document-code-uniqueness-invariant

## What was implemented

Wrote a new memory gotcha documenting the transport-box code-uniqueness invariant established by
this feature (issue #3887): its single owner (`TransportBoxStateRules`), the three consuming call
sites, the two adjacent-but-different concepts that must not be "fixed" to use it, a read-only
detection query for pre-existing duplicate codes, the binding amendment A9 note that the SQL's
`'Closed'`/`'Stocked'` literals are a deliberate unavoidable second copy, and the deferred amendment
A8 follow-up (a DB-level partial unique index) with its full prerequisites.

## Files created/modified

- `memory/gotchas/transport-box-code-uniqueness-single-definition.md` — new. Follows the
  Symptom / Root cause / Fix / Rules / Related files shape of the sibling
  `memory/gotchas/postgres-partial-index-active-states.md`, with the same front-matter shape
  (`name` / `description` / `type: project`).

No source or test files were touched.

## Tests

None targets this file (documentation-only task). Ran the task's Step 2 path-verification check and
the feature-level Step 3 validation gate instead — see "How to verify" below.

## How to verify

```bash
# Step 2: every path referenced in the new file resolves
grep -o 'backend/[a-zA-Z0-9_./-]*' memory/gotchas/transport-box-code-uniqueness-single-definition.md | sort -u | xargs -I{} test -e {} && echo "all paths OK"
grep -o 'frontend/[a-zA-Z0-9_./-]*' memory/gotchas/transport-box-code-uniqueness-single-definition.md | sort -u | xargs -I{} test -e {} && echo "frontend path OK"
grep -o 'memory/[a-zA-Z0-9_./-]*' memory/gotchas/transport-box-code-uniqueness-single-definition.md | sort -u | xargs -I{} test -e {} && echo "memory paths OK"

# Step 3: final full-solution validation gate (per memory/gotchas/dotnet-build-hangs-nodereuse-accessmatrixgen.md,
# built against the root solution — the sln lives at repo root, not under backend/)
dotnet build-server shutdown
DOTNET_CLI_DISABLE_BUILD_SERVERS=1 MSBUILDDISABLENODEREUSE=1 dotnet build Anela.Heblo.sln -nodeReuse:false
DOTNET_CLI_DISABLE_BUILD_SERVERS=1 MSBUILDDISABLENODEREUSE=1 dotnet format Anela.Heblo.sln --verify-no-changes
DOTNET_CLI_DISABLE_BUILD_SERVERS=1 MSBUILDDISABLENODEREUSE=1 dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "Category!=Integration" -nodeReuse:false
DOTNET_CLI_DISABLE_BUILD_SERVERS=1 MSBUILDDISABLENODEREUSE=1 dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBox" -nodeReuse:false
```

Results:
- `dotnet build`: 0 errors (252 pre-existing warnings, none newly introduced — this task adds no
  code). The `AccessMatrixGen` `ContinueOnError` crash is the documented pre-existing harmless
  warning from `memory/gotchas/dotnet-build-hangs-nodereuse-accessmatrixgen.md`, not a regression.
- `dotnet format --verify-no-changes`: exit 0, clean.
- `--filter "Category!=Integration"`: **6218 passed, 0 failed, 4 skipped** (the 4 skips are
  pre-existing, environment-gated Leaflet/Authorization integration-style tests unrelated to this
  change).
- `--filter "FullyQualifiedName~TransportBox"`: **222 passed, 5 failed, 0 skipped**. The 5 failures
  are all `ChangeTransportBoxStateReceiveAtomicityIntegrationTests` / the SQL-shape integration tests
  added by the prior `add-code-occupancy-sql-shape-test` task, failing with
  `System.ArgumentException: Docker is either not running or misconfigured` at
  `PostgresSharedContainerFixture` construction. This is the same environment-wide Docker/Testcontainers
  unavailability already documented in `artifacts/feat-3887/impl/add-code-occupancy-sql-shape-test.r1.md`
  — not a regression introduced by this (documentation-only) task. Both `dotnet build` and
  `dotnet format` were run against the repo-root `Anela.Heblo.sln`, since (unlike the task context's
  literal `cd backend && dotnet build`) no solution file exists inside `backend/` itself; this is
  functionally the same solution the task context intended.

## Notes

No deviations from the task context's content requirements. One environmental note: the task
context's Step 3 commands are written as `cd backend && dotnet build`/`dotnet format Anela.Heblo.sln`,
but `Anela.Heblo.sln` lives at the repository root, not under `backend/` — there is no solution or
project file directly in `backend/`. Ran the equivalent commands against the root `Anela.Heblo.sln`
instead, which is the solution the prior task's own "How to verify" section already referenced the
same way.

## PR Summary
Adds `memory/gotchas/transport-box-code-uniqueness-single-definition.md`, documenting the
transport-box code-uniqueness invariant this feature (issue #3887) fixed: `TransportBoxStateRules`
is now the single, deny-list definition of code occupancy; the memory file records its three
consuming call sites, the two adjacent concepts that must stay separate, a read-only detection
query for pre-existing duplicate codes, why the query's `'Closed'`/`'Stocked'` string literals are a
deliberate unavoidable second copy of the rule (amendment A9), and the deferred DB-level partial
unique index follow-up with its full preconditions (amendment A8). No source or test files changed.

### Changes
- `memory/gotchas/transport-box-code-uniqueness-single-definition.md` — new memory gotcha file

## Status
DONE
