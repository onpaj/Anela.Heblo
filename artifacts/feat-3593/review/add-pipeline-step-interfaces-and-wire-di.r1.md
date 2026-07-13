# Code Review: add-pipeline-step-interfaces-and-wire-di

## Summary
The implementation matches the task spec verbatim: five single-method interfaces (`IPlanQueriesStep`, `IGatherContextStep`, `IAggregateFactsStep`, `IValidateFactsStep`, `IWriteArticleStep`) were added co-located with their implementing classes, `GenerateArticleJob`'s constructor/fields were retyped to the interfaces with the constructor body and `RunAsync` left untouched, and `ArticleModule.cs`'s DI registrations were updated to `AddScoped<IXStep, XStep>()`. This is a clean, low-risk dependency-inversion refactor with no behavioral change.

## Review Result: PASS

### task: add-pipeline-step-interfaces-and-wire-di
**Status:** PASS

## Docs to Update
(none)

## Overall Notes
Verification performed directly against commit `4570e3b` in the worktree:
- `git show 4570e3b --stat` / full diff: exactly the 7 files listed in the spec were touched, diff content matches the spec's before/after snippets character-for-character (interface + `: IXStep` added, DI registrations swapped to two-arg `AddScoped`, `GenerateArticleJob` field/constructor-parameter types changed with constructor body and `RunAsync` untouched).
- `dotnet build Anela.Heblo.sln` from repo root: succeeded, 0 errors, 250 warnings (pre-existing, unrelated to this change — consistent with the developer's summary).
- `dotnet format Anela.Heblo.sln --verify-no-changes --include <7 touched files>`: no diffs reported.
- Re-ran the spec's repo-wide grep sweep for the five concrete step type names outside the touched files: results are exactly the expected set — the five per-step `*StepTests.cs` files and `SourceEnrichmentIntegrationTests.cs`, all constructing concrete classes directly via `new XStep(...)`, which remains valid since implementing an interface is purely additive. No undocumented consumers found.
- Each interface's `ExecuteAsync` signature (`Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct)`) matches the corresponding class's existing method signature exactly, confirmed by direct inspection of all five files.
- `GenerateArticleJobTests.cs` was correctly left untouched — the task spec explicitly scopes that file's rework to a follow-up task, and it still compiles today since concrete step classes satisfy the new interface-typed constructor parameters.
- `git status --short` shows only an unrelated, pre-existing modification to `artifacts/feat-3593/state.json`, which the developer correctly left out of the commit.

This follows arch-review.r1.md's guidance precisely: per-step interfaces (not a shared `IPipelineStep`), interfaces co-located in the same file as their implementation, and DI wiring confined to `ArticleModule.cs`. No functional, architectural, or completeness issues found.
