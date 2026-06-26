# Remove Speculative Async from BuildApplicationConfigurationAsync — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert `GetConfigurationHandler.BuildApplicationConfigurationAsync()` from a fake-async method (terminating with `await Task.CompletedTask`) to a synchronous `BuildApplicationConfiguration()` method, removing speculative async overhead and aligning the method signature with what it actually does.

**Architecture:** This is a pure surgical refactor inside a single Vertical Slice (`Application/Features/Configuration`). The MediatR public `Handle(...)` method keeps its `async Task<GetConfigurationResponse>` signature (framework contract). Only the private helper and its single in-file call site change. No cross-module impact, no contract change, no controller/DI/test-assertion change. A repo-wide grep confirms exactly two references to `BuildApplicationConfigurationAsync` — the declaration and the single call site in `Handle`.

**Tech Stack:** .NET 8, C# (nullable enabled), MediatR, xUnit + FluentAssertions (HTTP integration tests in `GetConfigurationEndpointTests`). No new dependencies introduced.

---

## File Structure

**Modify (exactly one source file):**
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`
  - Line 32: drop `await` from the call to the helper, rename to `BuildApplicationConfiguration()`.
  - Lines 53–73: change the helper signature from `private async Task<ApplicationConfiguration> BuildApplicationConfigurationAsync()` to `private ApplicationConfiguration BuildApplicationConfiguration()`. Delete the `await Task.CompletedTask; // Placeholder for potential async operations` line and the blank line immediately above `return config;` that exists only to separate them.

**Unchanged (must not be touched):**
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationResponse.cs`
- `backend/src/Anela.Heblo.Domain/Features/Configuration/ApplicationConfiguration.cs`
- Any controller / endpoint / DI registration.
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationEndpointTests.cs` (HTTP-level tests; do not name nor reflect on the private helper).

**Out of scope (explicitly do not modify):**
- `GetVersionFromSources()` helper — already synchronous and correct.
- The outer `Handle(...)` try/catch and both `LogDebug` calls — leave verbatim.
- Any naming, file layout, or formatting in adjacent code.

---

## Task 1: Verify pre-change state (baseline)

**Files:**
- Read-only verification of: `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`
- Read-only repo-wide grep for `BuildApplicationConfigurationAsync`.

- [ ] **Step 1: Confirm the helper currently looks exactly like the spec describes**

Open `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` and confirm:
- Line 32: `var appConfig = await BuildApplicationConfigurationAsync();`
- Line 53: `private async Task<ApplicationConfiguration> BuildApplicationConfigurationAsync()`
- Line 70: `await Task.CompletedTask; // Placeholder for potential async operations`
- Line 72: `return config;`

If any of these are not present at those line numbers but the same code is present elsewhere in the same file, continue. If the code is materially different from the spec, STOP and flag the discrepancy — do not invent a new refactor.

- [ ] **Step 2: Confirm only two references to the helper exist in the repo**

Run from the repo root (worktree directory):

```bash
grep -rn "BuildApplicationConfigurationAsync" backend
```

Expected output (exactly two lines):

```
backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs:32:            var appConfig = await BuildApplicationConfigurationAsync();
backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs:53:    private async Task<ApplicationConfiguration> BuildApplicationConfigurationAsync()
```

If any additional reference appears (test file, reflection, another caller), STOP and update the plan to include those call sites. Otherwise, proceed.

- [ ] **Step 3: Confirm baseline build is green**

Run from `backend/`:

```bash
dotnet build
```

Expected: build succeeds, zero errors. If errors exist before the refactor, STOP — those are unrelated and must be addressed independently. Do not proceed with a dirty baseline.

- [ ] **Step 4: Confirm baseline tests are green**

Run from `backend/`:

```bash
dotnet test --filter "FullyQualifiedName~GetConfigurationEndpointTests"
```

Expected: all `GetConfigurationEndpointTests` pass. If any fail before the refactor, STOP — those are unrelated and must be addressed independently.

No commit at the end of Task 1 — this task is verification only.

---

## Task 2: Refactor the helper to be synchronous

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs:53-73`

- [ ] **Step 1: Update the helper signature and remove the placeholder**

Edit `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`. Replace the entire current helper body (lines 53–73):

```csharp
    private async Task<ApplicationConfiguration> BuildApplicationConfigurationAsync()
    {
        // Get version with priority order:
        // 1. APP_VERSION (set by CI/CD pipeline with GitVersion)
        // 2. Assembly informational version
        // 3. Assembly version
        // 4. Fallback to default
        var version = GetVersionFromSources();

        // Get environment
        var environment = _environment.EnvironmentName;

        // Get mock auth setting
        var useMockAuth = _configuration.GetValue<bool>(ConfigurationConstants.USE_MOCK_AUTH, false);

        var config = ApplicationConfiguration.CreateWithDefaults(version, environment, useMockAuth);

        await Task.CompletedTask; // Placeholder for potential async operations

        return config;
    }
```

with the synchronous version (no `async`, no `Task<...>`, no `await`, no placeholder line, no `Async` suffix):

```csharp
    private ApplicationConfiguration BuildApplicationConfiguration()
    {
        // Get version with priority order:
        // 1. APP_VERSION (set by CI/CD pipeline with GitVersion)
        // 2. Assembly informational version
        // 3. Assembly version
        // 4. Fallback to default
        var version = GetVersionFromSources();

        // Get environment
        var environment = _environment.EnvironmentName;

        // Get mock auth setting
        var useMockAuth = _configuration.GetValue<bool>(ConfigurationConstants.USE_MOCK_AUTH, false);

        var config = ApplicationConfiguration.CreateWithDefaults(version, environment, useMockAuth);

        return config;
    }
```

Note: the four leading comments (`// Get version with priority order:` through `// 4. Fallback to default`), the three intermediate comments (`// Get environment`, `// Get mock auth setting`), and the `CreateWithDefaults` call are preserved verbatim. Only the `async`, `Task<...>`, `Async` suffix, `await Task.CompletedTask` line, the trailing comment on that line, and the blank line that separated them from `return config;` are removed.

- [ ] **Step 2: Update the single call site in `Handle`**

In the same file, replace line 32:

```csharp
            var appConfig = await BuildApplicationConfigurationAsync();
```

with:

```csharp
            var appConfig = BuildApplicationConfiguration();
```

The line stays at the same indentation level (three indent levels = 12 spaces, inside `try { ... }` inside `Handle`). No other line in `Handle` is touched. The outer `Handle` method retains its `public async Task<GetConfigurationResponse> Handle(...)` signature — that line is NOT modified.

- [ ] **Step 3: Confirm no `BuildApplicationConfigurationAsync` references remain**

Run from the repo root:

```bash
grep -rn "BuildApplicationConfigurationAsync" backend
```

Expected: no output. If any reference remains, fix it before continuing.

- [ ] **Step 4: Confirm no orphan async constructs remain in the helper**

Run from the repo root:

```bash
grep -n "Task.CompletedTask\|await" backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs
```

Expected: no output (the only `await` in the file was on line 32, and the only `Task.CompletedTask` was on line 70 — both removed). If `Task.CompletedTask` or `await` still appears in this file, the edit was incomplete; revisit and fix.

- [ ] **Step 5: Build to confirm the refactor compiles cleanly**

Run from `backend/`:

```bash
dotnet build
```

Expected: build succeeds with zero errors and zero new warnings. Specifically watch for:
- **CS1998** (`async method lacks await`) — if this appears, the `async` modifier was left on the helper while `await` was removed. Per arch-review Risk row and spec §"No CS1998 suppression": do NOT suppress; instead fully remove `async` and `Task<...>` together. Revisit Step 1.
- **CS4014** (`call not awaited`) — if this appears, the call site at line 32 still has an `await` mismatch or the helper still returns `Task<...>`. Revisit Step 2 or Step 1.

If new warnings appear, fix the underlying cause; do not add `#pragma` suppressions.

- [ ] **Step 6: Run the configuration endpoint tests to confirm behavior is preserved**

Run from `backend/`:

```bash
dotnet test --filter "FullyQualifiedName~GetConfigurationEndpointTests"
```

Expected: all tests pass with no assertion changes required. These tests exercise the handler via HTTP through `WebApplicationFactory`, so they cover the externally observable behavior (Version, Environment, UseMockAuth, Timestamp fields, status codes, logging side effects via the request pipeline). If any test fails, do NOT relax assertions — diagnose whether the refactor accidentally changed behavior (it should not) or whether a flaky test surfaced. If the latter, re-run; if the former, revert and re-check Steps 1–2.

- [ ] **Step 7: Run `dotnet format` and verify zero diff on this file**

Run from `backend/`:

```bash
dotnet format --include src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs
```

Expected: command exits 0, no further changes written. Then verify:

```bash
git diff --stat backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs
```

The diff should ONLY show changes to:
- Line 32 (the call site)
- Lines 53–73 (the helper signature, removed `await Task.CompletedTask` line, removed placeholder comment, removed blank separator line)

If `dotnet format` introduces whitespace changes elsewhere in the file, per arch-review Risk row, review them — keep only changes that are within the lines we intentionally modified. Revert any out-of-scope whitespace churn so the diff stays minimal and surgical (CLAUDE.md "Surgical changes" rule).

- [ ] **Step 8: Run full `dotnet build` once more after format**

Run from `backend/`:

```bash
dotnet build
```

Expected: clean build, zero errors, zero new warnings. This is the final pre-commit gate per the project's "Validation before completion" rules (CLAUDE.md).

- [ ] **Step 9: Commit**

Stage and commit only the one modified file:

```bash
git add backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs
git commit -m "refactor: remove speculative async from BuildApplicationConfiguration

The private helper performed no I/O and terminated with await Task.CompletedTask
as a placeholder for hypothetical future async work, violating YAGNI and
imposing per-request async state machine allocation. Rename to
BuildApplicationConfiguration and make synchronous. The public MediatR Handle
method retains its async Task signature per framework contract."
```

Verify:

```bash
git log -1 --stat
```

Expected: one commit, one file changed, small line count (call site delta + helper signature delta + removed placeholder).

---

## Task 3: Final verification across the full validation gate

**Files:** none modified.

- [ ] **Step 1: Full backend build**

Run from `backend/`:

```bash
dotnet build
```

Expected: clean. This is the CLAUDE.md required validation gate for backend changes.

- [ ] **Step 2: Full `dotnet format` check across the solution**

Run from `backend/`:

```bash
dotnet format --verify-no-changes
```

Expected: exits 0 with no diff suggested. If any other file is flagged, that flag is pre-existing tech debt unrelated to this PR — do NOT fix it as part of this refactor (surgical-change rule). Flag in the PR description if non-trivial; otherwise ignore.

- [ ] **Step 3: Re-run the configuration endpoint tests**

Run from `backend/`:

```bash
dotnet test --filter "FullyQualifiedName~GetConfigurationEndpointTests"
```

Expected: all pass.

- [ ] **Step 4: Confirm the public `Handle` signature is unchanged**

Run from the repo root:

```bash
grep -n "public async Task<GetConfigurationResponse> Handle" backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs
```

Expected: exactly one match — the existing `Handle` method signature. This proves the MediatR contract is intact (spec FR-3, arch-review Decision 2).

- [ ] **Step 5: Confirm the new helper signature is exactly the planned one**

Run from the repo root:

```bash
grep -n "private ApplicationConfiguration BuildApplicationConfiguration" backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs
```

Expected: exactly one match, exactly the line `    private ApplicationConfiguration BuildApplicationConfiguration()`. No `Async` suffix, no `Task<...>` return type, no `async` modifier.

- [ ] **Step 6: Confirm there are zero remaining `await` or `Task.CompletedTask` references in the file**

Run from the repo root:

```bash
grep -nE "await|Task\.CompletedTask" backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs
```

Expected: no output.

No commit at the end of Task 3 — this task is verification only. The single commit from Task 2 Step 9 is the entire change set for this PR.

---

## Self-Review

**Spec coverage check (each requirement → task that implements it):**
- FR-1 (rename + de-async the private helper): Task 2 Step 1.
- FR-2 (update the single call site): Task 2 Step 2; Task 2 Step 3 verifies no other call sites remain.
- FR-3 (preserve external behavior — Handle signature, logging, try/catch unchanged): Task 2 Step 1/Step 2 preserve them verbatim; Task 3 Step 4 explicitly verifies the `Handle` signature; Task 2 Step 6 + Task 3 Step 3 verify behavior via the existing HTTP tests.
- FR-4 (existing tests pass without assertion changes): Task 2 Step 6 and Task 3 Step 3 run them; the plan never modifies any test code.
- NFR-1 (no new allocations, no new sync blocking): the refactor only removes a state machine — no new code paths added.
- NFR-2 (no security surface change): no new inputs/outputs/logs introduced.
- NFR-3 (maintainability): signature now matches behavior; Task 2 Step 4 verifies no async leftovers.
- NFR-4 (style / build / format clean): Task 2 Steps 5, 7, 8 and Task 3 Steps 1, 2 cover this.
- Arch-review Decision 1 (full sync, not ValueTask): Task 2 Step 1 uses the exact synchronous signature.
- Arch-review Decision 2 (no rename of file/type/Handle): Task 2 touches only line 32 and lines 53–73.
- Arch-review Decision 3 (preserve try/catch and both LogDebug calls): Task 2 Step 1 instructions explicitly do not touch them; Task 3 Step 4 verifies `Handle` signature still wraps them.
- Arch-review Specification Amendment 1 (no `Async` suffix on the new name): Task 2 Step 1 specifies the new name `BuildApplicationConfiguration` (no `Async`); Task 3 Step 5 verifies it.
- Arch-review Specification Amendment 2 (no CS1998 suppression): Task 2 Step 5 explicitly forbids `#pragma` suppression and routes the engineer back to remove `async` + `Task<...>` together.

**Placeholder scan:** No "TBD", no "TODO", no "implement appropriate ..." — every step has exact file path, exact line numbers, exact code blocks, exact commands, exact expected output.

**Type consistency:** The new helper name `BuildApplicationConfiguration` is used identically in Task 2 Steps 1, 2, 4, and Task 3 Step 5. The return type `ApplicationConfiguration` is consistent throughout. The public `Handle` signature `public async Task<GetConfigurationResponse> Handle(GetConfigurationRequest request, CancellationToken cancellationToken)` is preserved verbatim and verified in Task 3 Step 4.

No gaps found; plan is complete.
