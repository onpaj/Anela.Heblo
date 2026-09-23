# Implementation: verify-client-diff-and-final-checklist

## What was implemented
This was a verification-only task (no source edits expected). Ran the full
final gate from the task context: confirmed how the TypeScript client is
regenerated, triggered a real regeneration, inspected the resulting diff,
ran a full solution build, ran the Bank-module test suite, and confirmed a
clean working tree. No production or test code was changed.

## Files created/modified
None (inspect-only task, as specified).

## Steps and findings

### Step 1 — How the client is regenerated
`docs/development/api-client-generation.md` says the client is generated
via `dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual`
(or the frontend's `npm run generate-client` prebuild script), not via a
plain `dotnet build`. Confirmed directly in
`backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`: the automatic
`AfterTargets="Build"` client-generation target is present but has
`Condition="false"` ("Automatic client generation is disabled due to build
conflicts. Use 'dotnet msbuild -t:GenerateFrontendClientManual' instead.").
So a plain `dotnet build` of the solution does **not** regenerate the
client — the manual target must be invoked explicitly. This refines (does
not contradict) the doc's "auto-generated on build" framing: it's
auto-generated on the **frontend's** `npm run build`/`npm start` prebuild
step, not on the backend `dotnet build`.

### Step 2 — Full solution build
```
dotnet build Anela.Heblo.sln --nologo
```
(Note: the solution lives at the repo root, not `backend/Anela.Heblo.sln`
— same deviation already documented by the prior task in this plan.)
Result: **Build succeeded, 0 Error(s)**, 170 pre-existing nullable-reference
warnings, none related to this change.

### Step 3 — Client regeneration and diff inspection
Ran `dotnet tool restore` (needed once for the `nswag` local tool) then
```
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
```
which completed successfully ("Frontend API client generation completed.").
```
git status --short frontend/src/api/generated/
git diff -- frontend/src/api/generated/
```
Both empty — **the regenerated client is byte-identical to what was
already committed**.

Investigated per the task's guidance (an empty diff after Step 2 needs
checking that regeneration actually ran): confirmed the regeneration *did*
run (see the tool's own "Executing file 'nswag.frontend.json'..." /
"Done." output), so the empty diff is genuine, not a skipped step.

Root cause of the empty diff: diffed
`frontend/src/api/generated/api-client.ts` at the commit immediately
before this plan's DTO change (`e09f3af4~1`) against the current file —
identical for every `errorType` occurrence, including
`BankStatementImportDto`. NSwag never emitted `errorType` as TypeScript
`readonly` even when the C# property was a get-only computed expression
(`public string? ErrorType => ...`); it always generated a plain settable
class field (`errorType?: string | undefined;`) in `BankStatementImportDto`.
So NFR-4's premise — "the client emits `errorType` as read-only" — turns
out not to hold at the *generated TypeScript* level for this DTO shape;
NSwag's default codegen for a plain class DTO doesn't reflect C#
get-only-ness into `readonly` for this template. The real problem FR-1–FR-3
fixed was still genuine (the DTO coupling to the domain constant, and
runtime write-ability at the C# layer for future consumers), but the
TypeScript client was never actually affected either way. Nothing to
commit; not a regression, not a missed regeneration.

### Step 4 — Commit generated client diff
No diff exists, so there is nothing to commit. (The task's `|| true`
guard exists for exactly this case.)

### Step 5 — Full solution build and full test suite
Build: same as Step 2, 0 errors.

Test suite: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Bank" --nologo` was
attempted several times but the `dotnet test` MSBuild-driven build+run
pipeline reliably hung (10+ minutes, no forward progress, CPU idle) in
this sandboxed session — reproducible with `-p:UseSharedCompilation=false`
and with the Docker-dependent test class filtered out, so it is **not**
Testcontainers/Docker related; it is contention from this session's own
concurrent full-solution rebuild plus a second AgentHarness worker
building a different feature in a sibling worktree at the same time on a
CPU-constrained sandbox, colliding on `dotnet test`'s MSBuild
build-orchestration layer (the `Anela.Heblo.API` project's
`GenerateAccessMatrix` `BeforeTargets="Build"` step forces a full
downstream rebuild on every invocation since it rewrites generated files'
timestamps every time, defeating incremental build).

Worked around by testing the **already-built** DLL directly via
`vstest.console.dll` (bypassing MSBuild's project graph entirely — the
DLL was already fresh from Step 2's successful `dotnet build`):
```
dotnet /usr/local/dotnet/sdk/8.0.425/vstest.console.dll Anela.Heblo.Tests.dll \
  --TestCaseFilter:"FullyQualifiedName~Features.Bank"
```
Result: **Failed: 8, Passed: 119, Skipped: 0, Total: 127, Duration: 2 s**
— completes in seconds once MSBuild is out of the loop, confirming the
`dotnet test` hangs were purely build-orchestration contention, not a
test-level issue. All 8 failures are
`BankStatementImportRepositoryIntegrationTests` (Postgres/Testcontainers,
Docker unavailable in this sandbox: `System.ArgumentException: Docker is
either not running or misconfigured`) — **identical count, identical
class, identical error** to what the previous task
(`make-errortype-settable-and-mapped.r1.md`) already documented for the
same scope before this task ran. No regression.

Also re-ran the two specific required suites directly via vstest for a
clean, isolated confirmation:
```
dotnet /usr/local/dotnet/sdk/8.0.425/vstest.console.dll Anela.Heblo.Tests.dll \
  --TestCaseFilter:"FullyQualifiedName~Anela.Heblo.Tests.Features.Bank.BankMappingProfileTests|FullyQualifiedName~Anela.Heblo.Tests.Features.Bank.GetBankStatementByIdHandlerTests"
```
Result: **Passed: 8, Failed: 0, Skipped: 0, Total: 8, Duration: 77 ms** —
all 4 `BankMappingProfileTests` and all 4 `GetBankStatementByIdHandlerTests`
green.

### Step 6 — Verify clean working tree
```
git status
```
Clean except for this plan's own `artifacts/feat-4279/state.json`
checkpoint bookkeeping (expected pipeline state, not a code change).

## Validation Checklist (all items)
- [x] `dotnet build Anela.Heblo.sln` — succeeds, 0 Error(s).
- [x] `dotnet format Anela.Heblo.sln --include <3 files> --verify-no-changes`
      — exit 0, no diff.
- [x] Bank-scope tests — 119 passed (including all 4
      `BankMappingProfileTests` and all 4 `GetBankStatementByIdHandlerTests`),
      8 pre-existing Docker/Testcontainers failures unrelated to this
      change (verified via direct `vstest.console.dll` run after the
      `dotnet test` MSBuild layer proved unreliable in this sandbox).
- [x] `BankStatementImportDto.cs` contains neither `ImportStatus` nor
      `using Anela.Heblo.Domain.Features.Bank;` (`grep` returns nothing).
- [x] `BankMappingProfile.cs` contains exactly one
      `.ForMember(dest => dest.ErrorType, ...)` call.
- [x] `git diff <merge-base-with-origin/main>...HEAD -- backend/src/Anela.Heblo.Domain/`
      is empty (domain untouched).
- [x] Generated client diff under `frontend/src/api/generated/` — empty;
      confirmed NFR-4's underlying assumption (readonly TS field) never
      actually applied to this DTO shape, so an empty diff is the correct,
      expected outcome, not a missed step.

## How to verify
```bash
dotnet build Anela.Heblo.sln --nologo
dotnet tool restore
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
git diff -- frontend/src/api/generated/   # expect empty
cd backend/test/Anela.Heblo.Tests/bin/Debug/net8.0
dotnet /usr/local/dotnet/sdk/8.0.425/vstest.console.dll Anela.Heblo.Tests.dll \
  --TestCaseFilter:"FullyQualifiedName~Features.Bank"
# expect: Failed: 8 (Docker), Passed: 119
git status   # expect clean (besides artifacts/feat-4279/state.json)
```

## Notes
- No source files were changed by this task, as specified.
- The `dotnet test` CLI hang is an artifact of this specific sandboxed,
  multi-worker session (concurrent full rebuilds contending for shared
  build-server/CPU resources) and is not expected to reproduce in normal
  CI or single-worker local development; it does not indicate any problem
  with the code under test. Documented here so a human reviewer isn't
  puzzled by build logs if the same contention recurs.
- Confirms the prior task's NFR-4 note ("errorType field is expected to
  change from read-only to writable") was based on a reasonable but, on
  closer inspection, incorrect assumption about NSwag's codegen for this
  case — the TypeScript client's behavior for `errorType` is unchanged
  before and after this whole plan's DTO/mapping refactor. The refactor's
  actual value (SRP at the C# layer, removing the DTO's domain coupling,
  the new settability test) stands independent of that.

## PR Summary
Final verification pass for the `BankStatementImportDto.ErrorType`
settability refactor (issue #4279): confirmed the full solution builds
clean, regenerated the OpenAPI/TypeScript client and found it
byte-identical (NSwag never emitted `errorType` as `readonly` for this DTO
shape, so there was nothing for the fix to change at the client level —
documented for the record), and confirmed all Bank-module tests pass
except the 8 pre-existing Postgres/Testcontainers integration tests that
require Docker (unavailable in this sandbox) — identical to the count
already documented by the prior task, so no regression. Working tree is
clean.

### Changes
None — verification-only task.

## Status
DONE
