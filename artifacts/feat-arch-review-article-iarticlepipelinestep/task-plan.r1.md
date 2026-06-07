# Remove Unused `IArticlePipelineStep` Interface — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete the dead `IArticlePipelineStep` interface and strip its implementation marker from the five step classes in the Article generation pipeline, with zero behavior change.

**Architecture:** Pure deletion. `GenerateArticleJob` and `ArticleModule` already use concrete step types — the interface is documentation-only. After the change, each `*Step` class becomes a plain concrete service registered and injected by its concrete type. No DI wiring, no method signatures, and no data flow change.

**Tech Stack:** C# / .NET 8, xUnit + FluentAssertions tests, Clean Architecture monorepo. No new dependencies.

---

## File Structure

**Files affected (all in `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/`):**

| File | Action |
|------|--------|
| `IArticlePipelineStep.cs` | **Delete** |
| `PlanQueriesStep.cs` | Edit declaration only: drop `: IArticlePipelineStep` |
| `GatherContextStep.cs` | Edit declaration only: drop `: IArticlePipelineStep` |
| `AggregateFactsStep.cs` | Edit declaration only: drop `: IArticlePipelineStep` |
| `ValidateFactsStep.cs` | Edit declaration only: drop `: IArticlePipelineStep` |
| `WriteArticleStep.cs` | Edit declaration only: drop `: IArticlePipelineStep` |

**Files NOT touched (verified — already concrete-typed):**

- `backend/src/Anela.Heblo.Application/Features/Article/ArticleModule.cs` — registers each step under its concrete type.
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/GenerateArticleJob.cs` — injects concrete step types and invokes them in fixed order.
- `backend/test/Anela.Heblo.Tests/Article/Pipeline/*StepTests.cs` and `GenerateArticleJobTests.cs` — confirmed via grep to contain zero references to `IArticlePipelineStep`.
- `docs/superpowers/plans/2026-05-08-article-generation-metadata.md` — historical plan record, frozen artifact, do not edit (per arch-review spec amendment).

**Single commit covers all edits** (per NFR-4 reversibility and arch-review Decision 2). Do not split into multiple commits and do not bundle unrelated changes.

---

## Pre-flight Verification

### Task 0: Confirm baseline state

**Files:**
- Inspect: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/`
- Inspect: `backend/src/Anela.Heblo.Application/Features/Article/ArticleModule.cs`
- Inspect: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/GenerateArticleJob.cs`

- [ ] **Step 0.1: Verify the interface still exists at HEAD**

Run: `ls backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/IArticlePipelineStep.cs`
Expected: file exists.

- [ ] **Step 0.2: Verify the baseline reference set**

Run: `grep -rn "IArticlePipelineStep" backend/ docs/`
Expected output (exactly seven hits, six in code + one historical doc):

```
backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/IArticlePipelineStep.cs: ... interface IArticlePipelineStep
backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/PlanQueriesStep.cs: public class PlanQueriesStep : IArticlePipelineStep
backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs: public class GatherContextStep : IArticlePipelineStep
backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/AggregateFactsStep.cs: public class AggregateFactsStep : IArticlePipelineStep
backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/ValidateFactsStep.cs: public class ValidateFactsStep : IArticlePipelineStep
backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs: public class WriteArticleStep : IArticlePipelineStep
docs/superpowers/plans/2026-05-08-article-generation-metadata.md: <historical reference>
```

If the count is anything other than `interface declaration (1) + 5 step classes (5) + historical plan (1) = 7 hits`, STOP and investigate before editing. New consumers may have been added since the spec was written.

- [ ] **Step 0.3: Establish a clean baseline build and test run**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds, zero errors.

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Article" --no-build`
Expected: all Article-module tests pass.

Record the pre-change test count (you will compare after the edits).

---

## Task 1: Delete the `IArticlePipelineStep` interface file

**Files:**
- Delete: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/IArticlePipelineStep.cs`

- [ ] **Step 1.1: Confirm the file contents before deletion**

The file contains exactly:

```csharp
namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

public interface IArticlePipelineStep
{
    Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct);
}
```

If the file contains anything else (additional methods, doc comments, attributes, sibling types), STOP — the spec assumed a single empty-body interface only. Surface the discrepancy before proceeding.

- [ ] **Step 1.2: Delete the file**

Run: `git rm backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/IArticlePipelineStep.cs`
Expected: file is staged for deletion; `git status` shows `deleted: ...IArticlePipelineStep.cs`.

- [ ] **Step 1.3: Verify the file no longer exists**

Run: `ls backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/IArticlePipelineStep.cs`
Expected: `No such file or directory`.

---

## Task 2: Remove interface marker from `PlanQueriesStep`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/PlanQueriesStep.cs:10`

- [ ] **Step 2.1: Edit the class declaration**

Change the single line at the class declaration. The exact change is:

Old line:

```csharp
public class PlanQueriesStep : IArticlePipelineStep
```

New line:

```csharp
public class PlanQueriesStep
```

No other lines in the file change. Constructor, fields, `ExecuteAsync(...)`, helpers (`BuildFallback`, `QueryPlanOutput`), and `using` directives stay exactly as-is.

- [ ] **Step 2.2: Verify the rest of the file is untouched**

Run: `git diff backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/PlanQueriesStep.cs`
Expected: a single-line removal (`- ...PlanQueriesStep : IArticlePipelineStep`) and a single-line addition (`+ ...PlanQueriesStep`). No other diff hunks.

If `dotnet format` or your editor reformatted the file, revert and redo with a precise edit.

---

## Task 3: Remove interface marker from `GatherContextStep`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs:12`

- [ ] **Step 3.1: Edit the class declaration**

Old line:

```csharp
public class GatherContextStep : IArticlePipelineStep
```

New line:

```csharp
public class GatherContextStep
```

No other lines change.

- [ ] **Step 3.2: Verify the rest of the file is untouched**

Run: `git diff backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs`
Expected: exactly one removal hunk and one addition hunk on the declaration line.

---

## Task 4: Remove interface marker from `AggregateFactsStep`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/AggregateFactsStep.cs:11`

- [ ] **Step 4.1: Edit the class declaration**

Old line:

```csharp
public class AggregateFactsStep : IArticlePipelineStep
```

New line:

```csharp
public class AggregateFactsStep
```

No other lines change.

- [ ] **Step 4.2: Verify the rest of the file is untouched**

Run: `git diff backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/AggregateFactsStep.cs`
Expected: exactly one removal hunk and one addition hunk on the declaration line.

---

## Task 5: Remove interface marker from `ValidateFactsStep`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/ValidateFactsStep.cs:11`

- [ ] **Step 5.1: Edit the class declaration**

Old line:

```csharp
public class ValidateFactsStep : IArticlePipelineStep
```

New line:

```csharp
public class ValidateFactsStep
```

No other lines change.

- [ ] **Step 5.2: Verify the rest of the file is untouched**

Run: `git diff backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/ValidateFactsStep.cs`
Expected: exactly one removal hunk and one addition hunk on the declaration line.

---

## Task 6: Remove interface marker from `WriteArticleStep`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs:15`

- [ ] **Step 6.1: Edit the class declaration**

Old line:

```csharp
public class WriteArticleStep : IArticlePipelineStep
```

New line:

```csharp
public class WriteArticleStep
```

No other lines change.

- [ ] **Step 6.2: Verify the rest of the file is untouched**

Run: `git diff backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs`
Expected: exactly one removal hunk and one addition hunk on the declaration line.

---

## Task 7: Verify no references remain

- [ ] **Step 7.1: Repository-wide grep for the deleted symbol**

Run: `grep -rn "IArticlePipelineStep" backend/`
Expected: zero matches.

Run: `grep -rn "IArticlePipelineStep" docs/superpowers/plans/2026-05-08-article-generation-metadata.md`
Expected: matches in this historical plan file are acceptable and intentionally untouched (per arch-review "Out of Scope" amendment 3). Do not edit this file.

If any hit appears under `backend/` (including tests, generated code, or comments), STOP — find and address the consumer before continuing. The spec assumed zero remaining consumers.

- [ ] **Step 7.2: Confirm the file deletion is staged**

Run: `git status`
Expected output includes:

```
deleted:    backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/IArticlePipelineStep.cs
modified:   backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/AggregateFactsStep.cs
modified:   backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs
modified:   backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/PlanQueriesStep.cs
modified:   backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/ValidateFactsStep.cs
modified:   backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs
```

No other files should appear in the diff.

---

## Task 8: Verify DI registration and job composition are unchanged

This is a guard against accidental drift. The arch-review's FR-3 amendment explicitly requires "functionally and textually equivalent ignoring formatter-induced whitespace; no registered service, lifetime, or registration order changes."

- [ ] **Step 8.1: Re-confirm `ArticleModule.cs` is byte-equivalent to HEAD (modulo whitespace)**

Run: `git diff backend/src/Anela.Heblo.Application/Features/Article/ArticleModule.cs`
Expected: empty diff.

The file should still contain (lines 19-25):

```csharp
services.AddScoped<PipelineStepRecorder>();
services.AddScoped<PlanQueriesStep>();
services.AddScoped<GatherContextStep>();
services.AddScoped<AggregateFactsStep>();
services.AddScoped<ValidateFactsStep>();
services.AddScoped<WriteArticleStep>();
services.AddScoped<GenerateArticleJob>();
```

- [ ] **Step 8.2: Re-confirm `GenerateArticleJob.cs` is byte-equivalent to HEAD**

Run: `git diff backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/GenerateArticleJob.cs`
Expected: empty diff.

The file should still inject the five concrete step types in the constructor (lines 20-27) and invoke them in fixed order inside `RunAsync` (lines 53-61).

---

## Task 9: Build verification

- [ ] **Step 9.1: Build the solution**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds with zero errors and zero new warnings (compared to the pre-change baseline from Task 0.3).

If a `CS0246` "type or namespace name 'IArticlePipelineStep' could not be found" appears anywhere, you missed a reference — go back to Task 7.1 and grep again.

---

## Task 10: Format verification

- [ ] **Step 10.1: Run formatter scoped to touched files only**

Run:

```bash
dotnet format backend/Anela.Heblo.sln --include \
  backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/PlanQueriesStep.cs \
  backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs \
  backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/AggregateFactsStep.cs \
  backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/ValidateFactsStep.cs \
  backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs
```

Expected: no output (formatter is satisfied) or only trivial whitespace adjustments. Do not let the formatter touch any files outside this `--include` list.

- [ ] **Step 10.2: Verify formatter produced no unrelated diff**

Run: `git diff --stat`
Expected: only the six file paths above appear (5 modified + 1 deleted). If any other file appears, STOP and revert that file — the formatter overreached.

---

## Task 11: Test verification

- [ ] **Step 11.1: Run all Article-module tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Article"`
Expected: all tests pass with the same test count as the pre-change baseline (Task 0.3). No skipped, no errored, no new failures.

The test files (`PlanQueriesStepTests`, `GatherContextStepTests`, `AggregateFactsStepTests`, `ValidateFactsStepTests`, `WriteArticleStepTests`, `GenerateArticleJobTests`) instantiate step classes by their concrete type and call `ExecuteAsync` directly, so they should continue to compile and pass without any edits.

- [ ] **Step 11.2: Run the full test project as a regression guard**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: same pass/fail/skip counts as before the change. Any new failure outside the Article module indicates collateral damage and must be diagnosed before commit.

---

## Task 12: Final cross-check

- [ ] **Step 12.1: Re-grep to confirm zero remaining backend references**

Run: `grep -rn "IArticlePipelineStep" backend/`
Expected: zero matches. (Repeat of Task 7.1 — intentional belt-and-braces.)

- [ ] **Step 12.2: Confirm `git diff` covers exactly six files**

Run: `git diff --name-status HEAD`
Expected output (order may vary):

```
D       backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/IArticlePipelineStep.cs
M       backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/AggregateFactsStep.cs
M       backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs
M       backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/PlanQueriesStep.cs
M       backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/ValidateFactsStep.cs
M       backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs
```

Exactly 1 `D` (deletion) and 5 `M` (modifications). Nothing else.

---

## Task 13: Commit

- [ ] **Step 13.1: Stage all six files**

Run:

```bash
git add backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/IArticlePipelineStep.cs \
        backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/PlanQueriesStep.cs \
        backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs \
        backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/AggregateFactsStep.cs \
        backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/ValidateFactsStep.cs \
        backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs
```

- [ ] **Step 13.2: Verify staged changes**

Run: `git status`
Expected: the six paths under "Changes to be committed", nothing under "Changes not staged for commit" except possibly the plan file itself.

- [ ] **Step 13.3: Create the commit**

Run:

```bash
git commit -m "$(cat <<'EOF'
refactor(article): remove unused IArticlePipelineStep interface

The interface was never resolved or injected via the abstraction — DI
registers concrete step types and GenerateArticleJob injects them
directly. Deleting it removes false architectural signal (YAGNI). The
five step classes keep their concrete public ExecuteAsync method; DI
wiring, job composition, and runtime behavior are unchanged.

If runtime step composition is needed later, reintroducing the
interface is a ~6-line change (Option B from the brief).
EOF
)"
```

Expected: a single commit with one file deletion and five file modifications.

- [ ] **Step 13.4: Confirm the commit is clean**

Run: `git show --stat HEAD`
Expected: exactly six files affected (1 deletion, 5 modifications). Diff stat should show small line counts (≈1 line changed per step file, ≈7 lines removed for the interface file).

---

## Acceptance Criteria Trace (Spec → Plan)

| Spec requirement | Covered by |
|---|---|
| FR-1: Delete `IArticlePipelineStep.cs`; no remaining references | Tasks 1, 7.1, 12.1 |
| FR-2: Remove interface marker from 5 step classes; preserve `ExecuteAsync` and all other members | Tasks 2–6 (declaration-only edit, diff verification on each) |
| FR-3 (amended): DI registration and job composition functionally + textually equivalent ignoring whitespace; no lifetime or order changes | Task 8 (empty diff on `ArticleModule.cs` and `GenerateArticleJob.cs`) |
| FR-4 (amended): verify zero test references via grep (no test edits expected) | Task 7.1, 12.1 |
| FR-5: `dotnet build` clean, `dotnet format` no diff on committed files, all tests pass | Tasks 9, 10, 11 |
| NFR-1: no performance impact | Implicit — no runtime change; pipeline still drives concrete services in fixed order |
| NFR-2: no security surface | Implicit — no input, auth, secrets, or boundaries touched |
| NFR-3: maintainability — reader sees concrete pipeline composition only | Achieved via Tasks 1–6 |
| NFR-4: single atomic, easily revertable commit | Task 13 (single commit) |
| Arch-review amendment 3: do not edit historical plan doc | Tasks 7.1, 10.2, 12.2 (verify no unrelated files in diff) |

---

## Out-of-Scope Reminders

Do not, as part of this change:

- Introduce `IEnumerable<IArticlePipelineStep>` injection or any new abstraction (Option B from the brief).
- Add `sealed` modifiers to step classes (arch-review Decision 3).
- Refactor `GenerateArticleJob` into a step-collection runner.
- Rename, move, or restructure step classes or their files.
- Edit `docs/superpowers/plans/2026-05-08-article-generation-metadata.md` or any other historical plan record.
- Reformat unrelated files via a broad `dotnet format` run.
- Add or remove tests beyond what's needed to keep the suite green (no edits expected per Task 7.1 grep).
