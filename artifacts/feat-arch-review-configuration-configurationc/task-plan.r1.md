# Remove Unused ASPNETCORE_ENVIRONMENT Constant Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete the unused `ConfigurationConstants.ASPNETCORE_ENVIRONMENT` constant from the backend Domain project to eliminate dead code and a misleading centralization signal.

**Architecture:** Surgical, single-line deletion in `ConfigurationConstants.cs`. No new files, no contract changes, no DI changes, no runtime behavior change. The five raw-string call sites (`DiagnosticsController`, `E2ETestController`, `CostOptimizedTelemetryProcessor`, `DesignTimeDbContextFactory`, `GetConfigurationHandler`) are explicitly preserved per spec FR-4 and tracked for follow-up cleanup.

**Tech Stack:** C# / .NET, `dotnet build`, `dotnet test`, ripgrep (`rg`) for usage verification, git.

---

## File Structure

**Modified files (1):**
- `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs` — remove the `ASPNETCORE_ENVIRONMENT` constant declaration (currently at line 9 per spec FR-1; verify exact line at execution time since file may have shifted).

**Created files:** None.

**Test files:** None added. This is a pure-deletion change verified by existing build + test suite. No new behavior is introduced, so no new tests are appropriate (TDD does not apply to deletion of dead code with zero callers — there is no behavior to specify).

**Preserved files (explicitly NOT modified per spec FR-4):**
- `backend/src/Anela.Heblo.API/Controllers/DiagnosticsController.cs`
- `backend/src/Anela.Heblo.API/Controllers/E2ETestController.cs`
- `backend/src/Anela.Heblo.API/Telemetry/CostOptimizedTelemetryProcessor.cs`
- `backend/src/Anela.Heblo.Persistence/DesignTimeDbContextFactory.cs`
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`

---

### Task 1: Verify zero callers of the constant

**Files:**
- Read-only verification: `backend/` (entire backend tree)

This task satisfies spec FR-2 (verify zero references before deletion). It also produces the evidence to paste into the PR description.

- [ ] **Step 1: Run literal-string grep for qualified accessors**

Run:
```bash
rg -n 'ConfigurationConstants\.ASPNETCORE_ENVIRONMENT' --type cs backend/
```

Expected: zero matches (no output, exit code 1). If any match appears, **STOP** — the spec premise is violated. Investigate the caller, document it, and re-scope before proceeding.

- [ ] **Step 2: Run regex grep for `nameof(...)` reflection-style references**

Run:
```bash
rg -n 'nameof\(ConfigurationConstants\.ASPNETCORE_ENVIRONMENT\)' --type cs backend/
```

Expected: zero matches (no output, exit code 1). Same stop-condition as Step 1 if any match appears.

- [ ] **Step 3: Sanity-check the constant declaration still exists where the spec claims**

Run:
```bash
rg -n 'ASPNETCORE_ENVIRONMENT' backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs
```

Expected: exactly one match — the declaration line, e.g.:
```
9:    public const string ASPNETCORE_ENVIRONMENT = "ASPNETCORE_ENVIRONMENT";
```

Note the actual line number returned — you'll need it for Task 2. If the file does not contain this declaration, the work has already been done (or the file moved) — **STOP** and verify with `git log`.

- [ ] **Step 4: Capture grep output for the PR description**

Save the exact commands and (empty) output from Steps 1 and 2 to a scratch note for inclusion in the PR description later. Format:

```
$ rg -n 'ConfigurationConstants\.ASPNETCORE_ENVIRONMENT' --type cs backend/
(no output — zero matches)

$ rg -n 'nameof\(ConfigurationConstants\.ASPNETCORE_ENVIRONMENT\)' --type cs backend/
(no output — zero matches)
```

This evidence satisfies spec FR-2's "Search is documented in the PR description" acceptance criterion.

---

### Task 2: Establish a baseline green build and test run

**Files:**
- Read-only: full backend solution.

You need a known-good baseline before deleting anything. If the baseline is already red, you can't tell whether your change broke it.

- [ ] **Step 1: Restore and build the solution**

Run from `backend/`:
```bash
dotnet build
```

Expected: build succeeds. Note the warning count.

If the build fails on a clean checkout, **STOP** — the workspace is in a bad state. Investigate (likely a missing dependency or branch issue) before proceeding.

- [ ] **Step 2: Run the full backend test suite**

Run from `backend/`:
```bash
dotnet test
```

Expected: all tests pass. Note the test count and any pre-existing skips.

If tests fail on baseline, **STOP** — capture which tests fail and check with the user whether to proceed. You cannot prove FR-3 (no test regressions) without a green baseline.

- [ ] **Step 3: Record baseline numbers**

Note in a scratch file:
- Build warning count: `<N>`
- Test pass count: `<N>`
- Test skip count: `<N>`

You'll compare against these after the change to satisfy spec FR-3.

---

### Task 3: Read the file and confirm exact deletion target

**Files:**
- Read: `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs`

The spec quotes the declaration as line 9, but file contents may have drifted. You need exact context for the deletion to be unambiguous.

- [ ] **Step 1: Read the file**

Use the Read tool to load `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs` in full. Confirm:
1. The file is a `public static class ConfigurationConstants` inside namespace `Anela.Heblo.Domain.Features.Configuration` (or similar — the namespace is whatever the file says; don't change it).
2. There is a comment block `// Environment variable keys` (per arch-review).
3. Under that comment block, there are at least two constants: `APP_VERSION` and `ASPNETCORE_ENVIRONMENT`.
4. The exact line to remove is:
   ```csharp
   public const string ASPNETCORE_ENVIRONMENT = "ASPNETCORE_ENVIRONMENT";
   ```
   (with whatever indentation the surrounding constants use — 4 spaces almost certainly).

- [ ] **Step 2: Confirm no `using` statement is dedicated to this constant**

`ASPNETCORE_ENVIRONMENT` is a `const string` with no dependencies — it cannot orphan a `using`. Scan the top of the file anyway and confirm all `using` directives serve other declarations.

Expected: no `using` statement becomes orphaned by the deletion (satisfies FR-1 acceptance criterion).

- [ ] **Step 3: Confirm the surrounding structure to preserve**

Mentally note (or scratch-note):
- The `// Environment variable keys` comment block stays.
- The `APP_VERSION` constant stays directly under the comment.
- The blank line separating the `// Environment variable keys` block from `// Configuration keys` stays.
- Only the single `ASPNETCORE_ENVIRONMENT` line is removed.

---

### Task 4: Delete the constant

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs`

- [ ] **Step 1: Apply the edit**

Use the Edit tool with `old_string` set to the exact line you confirmed in Task 3 Step 1 — include enough surrounding context to make it unique (the preceding `APP_VERSION` line is a safe anchor). Example shape:

```csharp
    public const string APP_VERSION = "APP_VERSION";
    public const string ASPNETCORE_ENVIRONMENT = "ASPNETCORE_ENVIRONMENT";
```

Replace with:

```csharp
    public const string APP_VERSION = "APP_VERSION";
```

(Use whatever indentation/spacing the file actually has — copy it verbatim from Task 3 Step 1's read output. Do not invent indentation.)

- [ ] **Step 2: Verify the file post-edit by re-reading it**

Use the Read tool to load the file again. Confirm:
1. The `ASPNETCORE_ENVIRONMENT` line is gone.
2. The `APP_VERSION` line is intact, directly under `// Environment variable keys`.
3. The `// Configuration keys` block and everything below are unchanged.
4. The class brace and namespace brace still close cleanly (no stray braces, no missing braces).
5. No trailing whitespace issue or accidentally-deleted blank line that would make the file's formatting drift from the rest of the project.

If any of the above is wrong, use Edit to repair. Do not skip this re-read — it's the only check between you and a broken file.

---

### Task 5: Verify deletion via grep

**Files:**
- Read-only verification.

- [ ] **Step 1: Confirm the constant declaration is gone from the file**

Run:
```bash
rg -n 'ASPNETCORE_ENVIRONMENT' backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs
```

Expected: zero matches (no output, exit code 1).

If a match still appears, your edit didn't land — return to Task 4.

- [ ] **Step 2: Confirm no qualified accessor exists anywhere in the solution**

Run:
```bash
rg -n 'ConfigurationConstants\.ASPNETCORE_ENVIRONMENT' --type cs backend/
```

Expected: zero matches.

If any match appears, something pulled the symbol in between Task 1 and now — investigate before continuing.

- [ ] **Step 3: Confirm the raw-string sites are untouched**

Run:
```bash
rg -n '"ASPNETCORE_ENVIRONMENT"' --type cs backend/
```

Expected: matches in exactly the five files listed in spec FR-4:
- `backend/src/Anela.Heblo.API/Controllers/DiagnosticsController.cs` (lines 31, 44, 86, 108 per spec — line numbers may have shifted, just confirm the file appears)
- `backend/src/Anela.Heblo.API/Controllers/E2ETestController.cs` (line 51 per spec)
- `backend/src/Anela.Heblo.API/Telemetry/CostOptimizedTelemetryProcessor.cs` (line 95 per spec)
- `backend/src/Anela.Heblo.Persistence/DesignTimeDbContextFactory.cs` (line 17 per spec)
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` (line 63 per spec)

This proves spec FR-4 was respected — no preserved file was inadvertently modified.

If matches appear in any OTHER file, you accidentally introduced or modified something. Investigate.

---

### Task 6: Rebuild and confirm zero warnings/errors

**Files:**
- Read-only: full backend solution.

This step satisfies spec FR-3 acceptance criterion: "`dotnet build` succeeds for the entire solution with no new warnings or errors."

- [ ] **Step 1: Run a clean build**

Run from `backend/`:
```bash
dotnet build
```

Expected: build succeeds (`Build succeeded.`). Warning count matches the baseline from Task 2 Step 3.

- [ ] **Step 2: Compare warning count against baseline**

If the warning count went UP, investigate which warning was introduced. A `CS0649` (unused field) or `CS0414` (assigned but never used) is unlikely from a deletion but check anyway.

If the warning count went DOWN, that's fine — possibly a `CS0414` was emitted on the now-removed constant and disappeared.

If the warning count is unchanged, proceed.

- [ ] **Step 3: If build fails, diagnose**

The only way a delete can break the build is if Task 1 missed a caller (e.g., a code-generated file, a `nameof()` call outside the patterns checked, or a string interpolation referencing the symbol via reflection). The compiler error message will name the file and line. Restore the constant, document the missed caller, and re-evaluate the spec premise — do NOT just re-add the constant and ship a no-op PR without disclosure.

---

### Task 7: Run the full test suite and confirm parity

**Files:**
- Read-only: full backend test suite.

This step satisfies spec FR-3 acceptance criterion: "The full backend test suite passes (`dotnet test`)."

- [ ] **Step 1: Run all tests**

Run from `backend/`:
```bash
dotnet test
```

Expected: all tests pass. Pass count and skip count match the baseline from Task 2 Step 3.

- [ ] **Step 2: If any test fails, diagnose**

A deletion of a zero-caller `const string` cannot break a passing test through normal code paths. Possible (unlikely) explanations if a test fails:
- A test was already flaky and happened to fail this run — re-run once to confirm.
- A test reads source files via reflection or file I/O and counts symbols — investigate, but treat as a test-design issue, not a real regression.
- The baseline was actually red and you missed it — go back to Task 2 Step 2 and re-verify.

Do NOT modify tests to make them pass. Spec FR-3 requires that existing tests continue to pass on their own merits.

- [ ] **Step 3: Record post-change numbers**

Note:
- Build warning count post-change: `<N>` (must equal or be less than baseline)
- Test pass count post-change: `<N>` (must equal baseline)
- Test skip count post-change: `<N>` (must equal baseline)

These three numbers go in the PR description as evidence for FR-3.

---

### Task 8: Commit the change

**Files:**
- Staged: `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs`

- [ ] **Step 1: Review the staged diff**

Run from the repository root:
```bash
git diff backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs
```

Expected: exactly one line removed, the `ASPNETCORE_ENVIRONMENT` declaration. No other lines changed. No trailing-whitespace noise. No reformatted unrelated lines.

If the diff shows more than the one intended line, **STOP** and clean it up before committing. A "remove one constant" PR with editor reformatting noise is much harder to review and revert.

- [ ] **Step 2: Stage and commit**

Run:
```bash
git add backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs
git commit -m "$(cat <<'EOF'
refactor: remove unused ASPNETCORE_ENVIRONMENT constant

The constant ConfigurationConstants.ASPNETCORE_ENVIRONMENT had zero
qualified accessors across the solution. Every call site reading the
environment name either uses IHostEnvironment.EnvironmentName (the
preferred DI-aware approach) or calls Environment.GetEnvironmentVariable
with the raw string literal. The constant provided no centralization
value and falsely implied a pattern that does not exist.

Verification (zero matches):
  rg -n 'ConfigurationConstants\.ASPNETCORE_ENVIRONMENT' --type cs
  rg -n 'nameof\(ConfigurationConstants\.ASPNETCORE_ENVIRONMENT\)' --type cs

The five raw-string call sites (DiagnosticsController, E2ETestController,
CostOptimizedTelemetryProcessor, DesignTimeDbContextFactory,
GetConfigurationHandler) are preserved intentionally per spec FR-4;
their migration to IHostEnvironment is tracked as a follow-up.
EOF
)"
```

Expected: commit created. Git pre-commit hooks (if any) pass.

- [ ] **Step 3: Verify commit landed cleanly**

Run:
```bash
git log -1 --stat
```

Expected output shape:
```
commit <sha>
Author: ...
Date:   ...

    refactor: remove unused ASPNETCORE_ENVIRONMENT constant
    ...

 backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs | 1 -
 1 file changed, 1 deletion(-)
```

The `1 file changed, 1 deletion(-)` line is the proof of a surgical PR. If you see anything else (insertions, multiple files), the previous tasks went wrong — investigate before pushing.

---

### Task 9: Push and open a PR

**Files:** None modified.

- [ ] **Step 1: Push the branch**

Run:
```bash
git push -u origin HEAD
```

Expected: branch pushed to remote successfully.

- [ ] **Step 2: Open the PR**

Run (using `gh` CLI per user's git-workflow rule):

```bash
gh pr create --title "refactor: remove unused ASPNETCORE_ENVIRONMENT constant" --body "$(cat <<'EOF'
## Summary

- Removes `ConfigurationConstants.ASPNETCORE_ENVIRONMENT` from `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs`. The constant had zero qualified accessors across the solution and provided false reassurance of a centralization pattern that does not actually exist.
- No other files modified. No runtime behavior change.
- Five raw-string call sites are preserved intentionally per spec FR-4; their migration to `IHostEnvironment.EnvironmentName` is tracked as a follow-up.

## Verification

Zero callers of the deleted symbol (run before deletion, satisfies FR-2):

```
$ rg -n 'ConfigurationConstants\.ASPNETCORE_ENVIRONMENT' --type cs backend/
(no output — zero matches)

$ rg -n 'nameof\(ConfigurationConstants\.ASPNETCORE_ENVIRONMENT\)' --type cs backend/
(no output — zero matches)
```

Build and tests (satisfies FR-3):

- `dotnet build` succeeds with no new warnings.
- `dotnet test` passes with the same pass/skip counts as the pre-change baseline.

## Test plan

- [ ] CI build is green.
- [ ] CI test job is green.
- [ ] PR diff shows exactly one file changed, one line deleted.
- [ ] None of the five FR-4-preserved files appear in the diff:
  - `backend/src/Anela.Heblo.API/Controllers/DiagnosticsController.cs`
  - `backend/src/Anela.Heblo.API/Controllers/E2ETestController.cs`
  - `backend/src/Anela.Heblo.API/Telemetry/CostOptimizedTelemetryProcessor.cs`
  - `backend/src/Anela.Heblo.Persistence/DesignTimeDbContextFactory.cs`
  - `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`

## Out of scope

- Migration of the five raw-string call sites to `IHostEnvironment.EnvironmentName`. Each site needs per-context DI verification (`DesignTimeDbContextFactory` is a static EF design-time entry point with no DI scope, etc.); that is tracked as a focused follow-up.
- Audit of other constants in `ConfigurationConstants.cs` (e.g., `DEFAULT_VERSION`, `DEFAULT_ENVIRONMENT`).
- Adding a Roslyn analyzer or `.editorconfig` rule to flag future dead constants.
EOF
)"
```

Expected: PR URL printed to stdout. Report that URL back to the user.

- [ ] **Step 3: Confirm CI starts**

Run:
```bash
gh pr checks
```

Expected: CI checks are queued/running. If checks are missing entirely (no CI configured for this branch), note it and move on — the local `dotnet build` + `dotnet test` from Tasks 6 and 7 are the actual evidence for FR-3.

---

## Spec Coverage Self-Review

| Spec Requirement | Where addressed |
|---|---|
| FR-1: Remove `ASPNETCORE_ENVIRONMENT` declaration; preserve surrounding structure; no orphaned `using`s | Tasks 3, 4 |
| FR-2: Verify zero references; document searches in PR | Tasks 1, 5; Task 9 PR body |
| FR-3: `dotnet build` + `dotnet test` pass with no regressions | Tasks 2 (baseline), 6, 7 |
| FR-4: Five raw-string callers untouched | Task 5 Step 3 (verification); Task 9 PR body (called out) |
| NFR-1 Performance: no impact | N/A — compile-time deletion |
| NFR-2 Security: no impact | N/A — public env-var name |
| NFR-3 Maintainability: reduces noise | Achieved by Task 4 |
| NFR-4 Backwards compatibility: no shim needed | Decision documented in arch-review; no task required |

No spec gaps identified.
