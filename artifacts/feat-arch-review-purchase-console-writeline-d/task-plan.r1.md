# Remove Debug `Console.WriteLine` from `PurchaseOrder.AddLine()` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete the leftover `// Debug logging` comment and `Console.WriteLine(...)` call from `PurchaseOrder.AddLine()` to restore Clean Architecture compliance (zero infrastructure I/O in the Domain layer).

**Architecture:** Pure deletion in a single Domain-layer file. No replacement logging, no analyzers, no signature or behavior changes. The MediatR handlers that call `AddLine()` already have `ILogger<T>` available if telemetry is ever genuinely needed — that is explicitly out of scope. A separate Domain-wide `Console.*` scan is run as a verification (FR-2) but no other files are modified.

**Tech Stack:** .NET 8, C#, xUnit (existing `PurchaseOrderTests` regression coverage), `dotnet build` + `dotnet format` + `dotnet test` for validation.

---

## File Structure

**Modified files:**
- `backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrder.cs` — remove lines 71–73 (the `// Debug logging` comment, the `Console.WriteLine(...)` call, and the now-redundant trailing blank line) so the method body around `_lines.Add(line);` matches the layout of sibling methods (`RemoveLine`, `UpdateLine`).

**Verified (read-only) files:**
- `backend/test/Anela.Heblo.Tests/Domain/Purchase/PurchaseOrderTests.cs` — existing behavioral tests for `AddLine` must continue to pass without modification.
- All files under `backend/src/Anela.Heblo.Domain/` — scanned for any other `Console.*` call (FR-2). Findings (or absence) are recorded in the commit/PR description only; no files are edited as part of this verification.

**Created files:** none.

---

## Pre-Flight Context

The file currently contains, around line 61–76 of `backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrder.cs`:

```csharp
    public void AddLine(string materialId, string materialName, decimal quantity, decimal unitPrice, string? notes)
    {
        if (!IsEditable)
        {
            throw new InvalidOperationException("Cannot add lines to completed orders");
        }

        var line = new PurchaseOrderLine(Id, materialId, materialName, quantity, unitPrice, notes);
        _lines.Add(line);

        // Debug logging
        Console.WriteLine($"Added line {line.Id} to purchase order {Id}. Total lines: {_lines.Count}");

        UpdatedBy = CreatedBy;
        UpdatedAt = DateTime.UtcNow;
    }
```

After the edit it must read exactly:

```csharp
    public void AddLine(string materialId, string materialName, decimal quantity, decimal unitPrice, string? notes)
    {
        if (!IsEditable)
        {
            throw new InvalidOperationException("Cannot add lines to completed orders");
        }

        var line = new PurchaseOrderLine(Id, materialId, materialName, quantity, unitPrice, notes);
        _lines.Add(line);

        UpdatedBy = CreatedBy;
        UpdatedAt = DateTime.UtcNow;
    }
```

Notes on the diff:
- The deletion removes three source lines: the `// Debug logging` comment, the `Console.WriteLine(...)` statement, and the blank line immediately following the `Console.WriteLine`.
- Exactly **one** blank line remains between `_lines.Add(line);` and `UpdatedBy = CreatedBy;` — matching the spacing convention used elsewhere in the file.
- No other characters in the method or file change. Indentation (8 spaces) is preserved.

---

## Task 1: Confirm baseline state of the file

**Files:**
- Read: `backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrder.cs`

- [ ] **Step 1: Read the target region and confirm the exact text to remove**

Run:

```bash
sed -n '61,76p' backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrder.cs
```

Expected output (verbatim):

```csharp
    public void AddLine(string materialId, string materialName, decimal quantity, decimal unitPrice, string? notes)
    {
        if (!IsEditable)
        {
            throw new InvalidOperationException("Cannot add lines to completed orders");
        }

        var line = new PurchaseOrderLine(Id, materialId, materialName, quantity, unitPrice, notes);
        _lines.Add(line);

        // Debug logging
        Console.WriteLine($"Added line {line.Id} to purchase order {Id}. Total lines: {_lines.Count}");

        UpdatedBy = CreatedBy;
        UpdatedAt = DateTime.UtcNow;
    }
```

If the output differs (different line numbers, different surrounding lines, or the `Console.WriteLine` is no longer present), **stop and re-read the full file** before proceeding — the line numbers in this plan may have shifted because of an earlier commit, and the `old_string`/`new_string` payload in Task 3 must be updated to match what is actually on disk.

- [ ] **Step 2: Confirm `Console.WriteLine` is the only `Console.*` call in the file**

Run:

```bash
grep -n "Console\." backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrder.cs
```

Expected output (exactly one line):

```
72:        Console.WriteLine($"Added line {line.Id} to purchase order {Id}. Total lines: {_lines.Count}");
```

If there is more than one match, the spec only covers the one at line 72 — leave any others alone (per the FR-2 "no additional files modified" rule) and proceed.

---

## Task 2: Run the existing `PurchaseOrder` unit tests to establish a green baseline

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Domain/Purchase/PurchaseOrderTests.cs`

- [ ] **Step 1: Run the targeted test fixture before any code change**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Domain.Purchase.PurchaseOrderTests" \
  --nologo
```

Expected: **all tests pass.** Record the pass count; the same count must hold after Task 3.

If any test in `PurchaseOrderTests` fails on `main` before this change, **stop**: the baseline is already broken and this PR is not the right place to fix it. Report the failure and exit.

---

## Task 3: Delete the debug comment and `Console.WriteLine` call

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrder.cs:69-74` (deletes the comment + `Console.WriteLine` + the trailing blank line)

- [ ] **Step 1: Apply the deletion**

Use a single `Edit` call with this exact `old_string` / `new_string`. The block is anchored on the unique `var line = new PurchaseOrderLine(...)` line above and the `UpdatedBy = CreatedBy;` line below so the match is unambiguous.

`old_string`:

```csharp
        var line = new PurchaseOrderLine(Id, materialId, materialName, quantity, unitPrice, notes);
        _lines.Add(line);

        // Debug logging
        Console.WriteLine($"Added line {line.Id} to purchase order {Id}. Total lines: {_lines.Count}");

        UpdatedBy = CreatedBy;
```

`new_string`:

```csharp
        var line = new PurchaseOrderLine(Id, materialId, materialName, quantity, unitPrice, notes);
        _lines.Add(line);

        UpdatedBy = CreatedBy;
```

- [ ] **Step 2: Verify the resulting region**

Run:

```bash
sed -n '61,73p' backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrder.cs
```

Expected output (verbatim):

```csharp
    public void AddLine(string materialId, string materialName, decimal quantity, decimal unitPrice, string? notes)
    {
        if (!IsEditable)
        {
            throw new InvalidOperationException("Cannot add lines to completed orders");
        }

        var line = new PurchaseOrderLine(Id, materialId, materialName, quantity, unitPrice, notes);
        _lines.Add(line);

        UpdatedBy = CreatedBy;
        UpdatedAt = DateTime.UtcNow;
    }
```

If the output does not match (extra blank line, missing blank line, or any other deviation), redo the edit. Do **not** continue with a mismatched layout.

- [ ] **Step 3: Confirm `Console.WriteLine` is gone from the file**

Run:

```bash
grep -n "Console\." backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrder.cs
```

Expected output: **no matches** (the command exits with code 1 and prints nothing).

If any `Console.*` reference remains, the deletion missed something — re-apply.

---

## Task 4: Run the Domain-wide `Console.*` scan required by FR-2

This task does not modify any files. It records a verification result that goes into the commit message and PR description.

- [ ] **Step 1: Scan the entire Domain layer for `Console.*` calls**

Run:

```bash
grep -rn "Console\." backend/src/Anela.Heblo.Domain/ --include="*.cs"
```

Expected output: **no matches** (the command exits with code 1 and prints nothing). The arch review already confirmed this was the only occurrence; this step re-confirms after the deletion.

- [ ] **Step 2: Capture the scan result for the PR description**

If Step 1 produced no output, note for the PR description:

> Domain-layer `Console.*` scan after the deletion: no remaining matches in `backend/src/Anela.Heblo.Domain/`. FR-2 satisfied.

If Step 1 unexpectedly produced output (a `Console.*` call that was not present at arch-review time, e.g. landed via a concurrent merge), **do not delete it**. Per spec FR-2 and the Out-of-Scope section, additional finds are reported only — list each `path:line` in the PR description and continue.

---

## Task 5: Re-run the `PurchaseOrder` tests to confirm no behavioral regression

- [ ] **Step 1: Re-run the same filtered test set as Task 2**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Domain.Purchase.PurchaseOrderTests" \
  --nologo
```

Expected: **same pass count as Task 2**, all green. The deleted line had no return value, raised no exception, and produced no domain event — behavioral parity is the success criterion.

If a test now fails, the most likely cause is an unintended edit elsewhere in the method (e.g. accidentally removing `UpdatedBy = CreatedBy;` or `_lines.Add(line);`). Re-check the region from Task 3 Step 2 before investigating anything else.

---

## Task 6: Run `dotnet format` and `dotnet build` on the Domain project

These are the project's mandatory pre-commit gates (see `CLAUDE.md` → "Validation before completion").

- [ ] **Step 1: Format the edited file**

Run:

```bash
dotnet format backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj \
  --include backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrder.cs \
  --verify-no-changes
```

Expected: exit code `0` with no diff applied (the file is already compliant). If the command reports a change was needed, re-run without `--verify-no-changes` to apply it, then re-check the region from Task 3 Step 2 to ensure formatting did not undo your spacing choice.

- [ ] **Step 2: Build the Domain project**

Run:

```bash
dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj --nologo
```

Expected: build succeeds with **zero new warnings or errors** attributable to `PurchaseOrder.cs`. Pre-existing warnings in unrelated files are acceptable but should be ignored — do not "fix" them here (surgical change policy).

- [ ] **Step 3: Build the full solution to catch downstream effects**

Run:

```bash
dotnet build backend/Anela.Heblo.sln --nologo
```

Expected: full solution builds. Any failure here would indicate something outside `PurchaseOrder.AddLine()` was relying on the `Console.WriteLine` (extremely unlikely) — investigate before committing.

---

## Task 7: Commit

- [ ] **Step 1: Stage exactly one file**

Run:

```bash
git add backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrder.cs
git status
```

Expected: `git status` shows exactly one modified file (`PurchaseOrder.cs`) staged, and no other staged or unstaged changes. If anything else appears (e.g. an accidental edit from a previous task, or a formatter reflow elsewhere), unstage it and investigate before committing.

- [ ] **Step 2: Verify the staged diff is the intended three-line deletion**

Run:

```bash
git diff --cached backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrder.cs
```

Expected: the diff shows exactly three removed lines and zero added lines:

```diff
         _lines.Add(line);

-        // Debug logging
-        Console.WriteLine($"Added line {line.Id} to purchase order {Id}. Total lines: {_lines.Count}");
-
         UpdatedBy = CreatedBy;
```

If the diff shows anything else (extra context changes, whitespace-only changes elsewhere in the file), revert and redo Task 3.

- [ ] **Step 3: Commit with a conventional-commit message**

Run:

```bash
git commit -m "$(cat <<'EOF'
chore(domain): remove debug Console.WriteLine from PurchaseOrder.AddLine

The `// Debug logging` + Console.WriteLine call in
PurchaseOrder.AddLine() was a leftover that violated the Domain layer's
no-infrastructure-I/O rule and emitted unstructured noise to container
stdout on every line added during CreatePurchaseOrder /
UpdatePurchaseOrder. Removed purely; no replacement logging introduced
(handlers already have ILogger<T> if telemetry is ever required, but
that is a separate concern out of scope here).

FR-2 verification: grep -rn 'Console\\.' backend/src/Anela.Heblo.Domain/
returns no matches after this change.
EOF
)"
```

Expected: commit succeeds. If a pre-commit hook fails, **do not amend** — fix the underlying issue and create a new commit.

---

## Out of Scope (reminder — do not do these in this PR)

These items are explicitly excluded by the spec. If you find yourself tempted to do any of them, stop and re-read the spec's "Out of Scope" section:

- Adding `ILogger.LogDebug(...)` or any other replacement telemetry, anywhere.
- Deleting other `Console.*` calls discovered outside `PurchaseOrder.cs` (only report them).
- Adding a Roslyn `BannedApiAnalyzers` rule to prevent recurrence (worth a follow-up issue, not part of this PR).
- Any other refactor, rename, or comment edit inside `PurchaseOrder.cs` (surgical-change policy).
- Frontend, E2E, migration, or OpenAPI-client regeneration work.

---

## Self-Review

**Spec coverage:**
- FR-1 (delete the `Console.WriteLine` and adjacent `// Debug logging` comment): covered by Task 3.
- FR-1 acceptance — `Console.WriteLine` absent from the file: Task 3 Step 3 grep.
- FR-1 acceptance — `// Debug logging` comment removed: Task 3 Step 2 layout check.
- FR-1 acceptance — no other lines in `AddLine()` modified: Task 3 Step 2 layout check + Task 7 Step 2 diff inspection.
- FR-1 acceptance — existing unit tests still pass: Tasks 2 (baseline) and 5 (post-change).
- FR-1 acceptance — `dotnet build` clean: Task 6 Steps 2 and 3.
- FR-1 acceptance — `dotnet format` compliant: Task 6 Step 1.
- FR-2 (Domain-wide `Console.*` scan, document findings, do not modify other files): Task 4.
- NFR-1 to NFR-4: implicitly satisfied by the deletion; no explicit task needed (no benchmarking, no security review, no observability gap to fill — all called out as no-ops by the spec).
- Out-of-Scope list: re-stated as a reminder block above Tasks.

**Placeholder scan:** none of "TBD", "TODO", "implement later", "add appropriate X", "similar to Task N", or bare "write tests" placeholders. All steps include exact commands, exact expected output, and (where applicable) the exact `old_string`/`new_string` payload.

**Type consistency:** no new types, methods, properties, or signatures introduced. `PurchaseOrder.AddLine(string, string, decimal, decimal, string?)` is the only API referenced and is referenced consistently.
