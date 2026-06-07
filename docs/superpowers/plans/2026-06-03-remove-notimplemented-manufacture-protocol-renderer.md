# Remove Dead `NotImplementedManufactureProtocolRenderer` DI Placeholder Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the dead `NotImplementedManufactureProtocolRenderer` DI placeholder (registration + class) from the Application layer so missing-binding bugs surface at container build time instead of as `NotImplementedException` at HTTP request time.

**Architecture:** Two-line edit in `ManufactureModule.cs` + one file deletion. The real implementation `QuestPdfManufactureProtocolRenderer` is already registered in `API/Extensions/ServiceCollectionExtensions.cs:152` and remains the single registration site. The existing `CompositionRootTests.ServiceContainer_ValidateOnBuild_NoLifetimeMismatchesOrUnresolvableServices` test guards the production composition root with `ValidateOnBuild=true` + `ValidateScopes=true` and is the regression gate for this change.

**Tech Stack:** .NET 8, C#, Microsoft.Extensions.DependencyInjection, xUnit, FluentAssertions, `WebApplicationFactory<Program>`.

---

## File Structure

**Files to edit:**
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs` — delete lines 73–74 (comment + `AddScoped` registration).

**Files to delete:**
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/NotImplementedManufactureProtocolRenderer.cs` — entire file.

**Files NOT to modify (confirmed by repo grep):**
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/IManufactureProtocolRenderer.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/GetManufactureProtocolHandler.cs`
- `backend/src/Anela.Heblo.API/PDFPrints/QuestPdfManufactureProtocolRenderer.cs`
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureProtocolHandlerTests.cs` (uses `Mock<IManufactureProtocolRenderer>`)
- `backend/test/Anela.Heblo.Tests/Infrastructure/CompositionRootTests.cs` (existing regression gate)
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`
- Any file under `docs/` (no references to `NotImplementedManufactureProtocolRenderer` exist; "Phase 6" references in docs belong to unrelated features per arch-review).

---

## Task 1: Establish baseline — confirm prod composition root currently validates green

**Files:**
- Touch: none (read-only verification)

**Rationale:** Before removing the placeholder we must prove the regression gate (`CompositionRootTests`) currently passes. This is the test that will validate FR-4 / NFR-3 (fail-fast invariant preserved post-change). If it is red before we change anything, we stop and investigate — we do not proceed with deletion on a red baseline.

- [ ] **Step 1: Restore + build the solution from worktree root**

Run from `/Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-arch-review-manufacture-stale-notimpleme`:

```bash
dotnet build backend/backend.sln -c Debug
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

If the build fails with errors unrelated to this feature, stop and report. Do not proceed.

- [ ] **Step 2: Run the composition-root regression test on the unchanged tree**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName=Anela.Heblo.Tests.Infrastructure.CompositionRootTests.ServiceContainer_ValidateOnBuild_NoLifetimeMismatchesOrUnresolvableServices" \
  --no-build -c Debug
```

Expected: `Passed!  - Failed: 0, Passed: 1, Skipped: 0`.

If this test fails on the unchanged tree, stop. Do not proceed — the test must be green before we use it as a regression gate.

- [ ] **Step 3: Run the manufacture protocol handler tests on the unchanged tree**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Manufacture.GetManufactureProtocolHandlerTests" \
  --no-build -c Debug
```

Expected: `Passed!  - Failed: 0, Passed: N, Skipped: 0` where N > 0.

If this test fails on the unchanged tree, stop. Do not proceed.

- [ ] **Step 4: Confirm the only references to the placeholder are the two files we plan to touch/delete**

```bash
grep -rn "NotImplementedManufactureProtocolRenderer" backend
```

Expected output (exactly two lines):

```
backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/NotImplementedManufactureProtocolRenderer.cs:6:internal sealed class NotImplementedManufactureProtocolRenderer : IManufactureProtocolRenderer
backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs:74:        services.AddScoped<IManufactureProtocolRenderer, NotImplementedManufactureProtocolRenderer>();
```

If any **third** reference exists (e.g. another module's registration, a test fixture, a `docs/` mention), stop and re-scope: there is hidden coupling the spec did not anticipate. Do not proceed with deletion.

- [ ] **Step 5: Confirm no `docs/` references to the placeholder**

```bash
grep -rln "NotImplementedManufactureProtocolRenderer" docs
```

Expected: empty output (no matches).

If matches are found, list them — they need to be cleaned up in Task 4 instead of being skipped.

---

## Task 2: Remove the dead DI registration from `ManufactureModule.cs`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs:73-74`

- [ ] **Step 1: Read the file and confirm the exact target lines**

Open `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs`. Confirm lines 67–76 look like this (anchored around the deletion target):

```csharp
        // Register dashboard tiles
        services.RegisterTile<TodayProductionTile>();
        services.RegisterTile<NextDayProductionTile>();
        services.RegisterTile<ManualActionRequiredTile>();
        services.RegisterTile<ManufactureConditionsTile>();

        // Register protocol renderer placeholder (replaced by QuestPdfManufactureProtocolRenderer in Phase 6)
        services.AddScoped<IManufactureProtocolRenderer, NotImplementedManufactureProtocolRenderer>();

        // Register manufacture error transformation
```

If they don't match exactly, stop — someone else has edited the file since this plan was written. Re-read and adjust before proceeding.

- [ ] **Step 2: Apply the edit — delete the comment, the registration, and the now-orphaned blank line above the next block**

Replace exactly this block:

```csharp
        services.RegisterTile<ManufactureConditionsTile>();

        // Register protocol renderer placeholder (replaced by QuestPdfManufactureProtocolRenderer in Phase 6)
        services.AddScoped<IManufactureProtocolRenderer, NotImplementedManufactureProtocolRenderer>();

        // Register manufacture error transformation
```

With this block (keep a single blank line separating the dashboard-tile block from the error-transformation block):

```csharp
        services.RegisterTile<ManufactureConditionsTile>();

        // Register manufacture error transformation
```

- [ ] **Step 3: Remove the now-unused `using` if applicable**

Check the file's `using` directives at the top. The line:

```csharp
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureProtocol;
```

is **still required** because nothing else in this file uses that namespace? Verify:

```bash
grep -n "GetManufactureProtocol\|IManufactureProtocolRenderer\|ManufactureProtocolData" backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs
```

Expected after the Step 2 edit: empty output (no remaining symbols from that namespace are referenced in this file).

If the output is empty, delete the `using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureProtocol;` line from the top of the file.

If the output is non-empty (something else in this file uses a type from that namespace — unlikely, but verify), leave the `using` alone.

- [ ] **Step 4: Compile to ensure the file still parses**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj -c Debug
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

If the build fails with `CS0246` ("could not find type `NotImplementedManufactureProtocolRenderer`") at any **other** site, that means Step 1's grep in Task 1 missed a reference. Stop, re-scope, do not proceed.

If the build fails with `CS0246` on the `NotImplementedManufactureProtocolRenderer` symbol on a now-deleted line, you did not actually save the edit — re-apply Step 2.

---

## Task 3: Delete the placeholder class file

**Files:**
- Delete: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/NotImplementedManufactureProtocolRenderer.cs`

- [ ] **Step 1: Delete the file**

```bash
rm backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/NotImplementedManufactureProtocolRenderer.cs
```

- [ ] **Step 2: Confirm no remaining references in `backend/`**

```bash
grep -rn "NotImplementedManufactureProtocolRenderer" backend
```

Expected: empty output (no matches anywhere).

If any match remains, you missed a reference. Investigate and remove before proceeding.

- [ ] **Step 3: Build the whole solution**

```bash
dotnet build backend/backend.sln -c Debug
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

If there are warnings introduced by this change, treat them as errors per NFR-2 and fix before proceeding.

---

## Task 4: Validate via existing test gates

**Files:**
- Touch: none (test execution only)

**Rationale:** Per arch-review Decision 2 and Spec Amendment 1, no new test is added for FR-4. The production fail-fast invariant is verified by the existing `CompositionRootTests`. Handler behavior is verified by the existing `GetManufactureProtocolHandlerTests`. Both must continue to pass.

- [ ] **Step 1: Run the composition-root regression test on the changed tree**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName=Anela.Heblo.Tests.Infrastructure.CompositionRootTests.ServiceContainer_ValidateOnBuild_NoLifetimeMismatchesOrUnresolvableServices" \
  --no-build -c Debug
```

Expected: `Passed!  - Failed: 0, Passed: 1, Skipped: 0`.

This is the **primary regression gate** for this change. If it fails with `InvalidOperationException: Unable to resolve service for type 'Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureProtocol.IManufactureProtocolRenderer'`, then `API/Extensions/ServiceCollectionExtensions.cs:152` was not actually executing — that contradicts the arch-review's assumption. Stop and investigate; do not "fix" by re-adding the placeholder.

- [ ] **Step 2: Run all Manufacture feature tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Manufacture" \
  --no-build -c Debug
```

Expected: `Passed!  - Failed: 0, Passed: N, Skipped: 0` where N > 0 and matches the pre-change run count from Task 1 Step 3.

- [ ] **Step 3: Run module-boundary tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Architecture.ModuleBoundariesTests" \
  --no-build -c Debug
```

Expected: `Passed!  - Failed: 0, Passed: N, Skipped: 0`.

Module-boundary tests validate that Application does not depend on infrastructure-bound types. The placeholder was an Application-layer infra stub; removing it should keep the boundary clean.

- [ ] **Step 4: Run the full backend test suite**

```bash
dotnet test backend/backend.sln --no-build -c Debug
```

Expected: `Failed: 0` across all test projects.

If any unrelated test fails, that's a pre-existing condition — verify by checking out `main` briefly to confirm the failure is not caused by this change. If reproduced on `main`, file separately and continue. If only on this branch, stop and investigate.

- [ ] **Step 5: Run `dotnet format` and confirm no whitespace drift on the changed file**

```bash
dotnet format backend/backend.sln --verify-no-changes
```

Expected: exit code 0, no output (NFR-2: "`dotnet format` must produce no diff on the affected files").

If `dotnet format` reports changes on `ManufactureModule.cs`, run `dotnet format backend/backend.sln` (without `--verify-no-changes`) to apply, then re-run `--verify-no-changes` to confirm clean. Stage the formatting result with the functional edit.

---

## Task 5: Commit

**Files:**
- Touch: none (git operations only)

- [ ] **Step 1: Stage exactly the two files in scope**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs \
        backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/NotImplementedManufactureProtocolRenderer.cs
```

- [ ] **Step 2: Verify the staged diff is minimal**

```bash
git diff --staged --stat
```

Expected: exactly two files listed —
- `ManufactureModule.cs` with `-2` (or `-3` if the `using` was also dropped, plus possible `-1` for a blank line consolidation; net deletions, no insertions)
- `NotImplementedManufactureProtocolRenderer.cs` with the full file as deletions.

If any other file is staged, unstage it (`git restore --staged <file>`) and investigate why it changed.

- [ ] **Step 3: Re-confirm the registration is gone and the file is removed**

```bash
git diff --staged --stat
git ls-files backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/NotImplementedManufactureProtocolRenderer.cs
```

Expected: second command returns empty (file is not tracked anymore once staged for deletion).

- [ ] **Step 4: Commit**

```bash
git commit -m "$(cat <<'EOF'
refactor: remove dead NotImplementedManufactureProtocolRenderer DI placeholder

The Application module registered a NotImplementedException-throwing stub
for IManufactureProtocolRenderer that was overridden at runtime by
QuestPdfManufactureProtocolRenderer in the API composition root. The
placeholder turned a missing-binding bug into a deferred 500 at request
time instead of a clear startup failure.

Removed both the registration in ManufactureModule.cs and the placeholder
class itself. The QuestPDF renderer registration in
ServiceCollectionExtensions.cs is now the single source. The existing
CompositionRootTests.ServiceContainer_ValidateOnBuild_NoLifetimeMismatchesOrUnresolvableServices
test, which runs with ValidateOnBuild=true and ValidateScopes=true,
guards against any host losing the renderer binding.
EOF
)"
```

Expected: commit succeeds, hook passes, `git status` clean.

If a pre-commit hook fails (e.g. `dotnet format` re-flags a file), fix the issue, re-stage, and create a **new** commit — do not `--amend`.

- [ ] **Step 5: Final sanity check**

```bash
git status
grep -rn "NotImplementedManufactureProtocolRenderer" backend docs || echo "clean"
```

Expected: `git status` shows clean working tree; the grep prints `clean`.

---

## Self-Review Summary

**Spec coverage:**
- FR-1 (remove registration + Phase 6 comment) → Task 2.
- FR-2 (delete class unconditionally per Amendment 2) → Task 3.
- FR-3 (preserve QuestPDF as the sole registration) → Task 4 Step 1 verifies the prod container resolves the renderer; explicit "do not touch" on `ServiceCollectionExtensions.cs:152` listed in File Structure.
- FR-4 (fail-fast under missing registration, per Amendment 1) → Task 4 Step 1 leverages existing `CompositionRootTests` as the gate; no new test added per arch-review Decision 2.
- FR-5 (documentation cleanup, per Amendment 3) → Task 1 Step 5 + Task 2 Step 2 remove the Phase 6 comment in `ManufactureModule.cs` and confirm no `docs/` references exist.
- NFR-1 (behavior preservation) → Task 4 Steps 2 + 4 (Manufacture + full suite).
- NFR-2 (build & test gates, no warnings, no `dotnet format` drift) → Task 3 Step 3 + Task 4 Steps 4 + 5.
- NFR-3 (fail-fast DI) → Task 4 Step 1.
- NFR-4 (minimal blast radius) → Task 5 Step 2 enforces two-file staged diff.

**Placeholder scan:** All steps have concrete commands, exact code blocks, and exact expected output. No "TBD", "add appropriate error handling", or "similar to Task N" remain.

**Type consistency:** No new types introduced. Interface name `IManufactureProtocolRenderer` and class name `NotImplementedManufactureProtocolRenderer` used consistently across all tasks.
