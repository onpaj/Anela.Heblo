# Implementation: add-pipeline-step-interfaces-and-wire-di

## What was implemented
Extracted single-method interfaces (`IPlanQueriesStep`, `IGatherContextStep`, `IAggregateFactsStep`, `IValidateFactsStep`, `IWriteArticleStep`) for the five Article generation pipeline step classes, each exposing `Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct)`. Each concrete step class now implements its corresponding interface. `GenerateArticleJob`'s constructor and field declarations were updated to depend on the interfaces instead of the concrete step classes, and `ArticleModule`'s DI registrations were updated to register each interface-to-implementation mapping (`services.AddScoped<IXStep, XStep>()`) instead of registering the concrete classes directly.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/PlanQueriesStep.cs` — added `IPlanQueriesStep` interface; `PlanQueriesStep` now implements it
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs` — added `IGatherContextStep` interface; `GatherContextStep` now implements it
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/AggregateFactsStep.cs` — added `IAggregateFactsStep` interface; `AggregateFactsStep` now implements it
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/ValidateFactsStep.cs` — added `IValidateFactsStep` interface; `ValidateFactsStep` now implements it
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs` — added `IWriteArticleStep` interface; `WriteArticleStep` now implements it
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/GenerateArticleJob.cs` — field declarations and constructor parameters changed from concrete step types to their interface types; constructor body and `RunAsync` unchanged
- `backend/src/Anela.Heblo.Application/Features/Article/ArticleModule.cs` — DI registrations for the five steps changed from `services.AddScoped<XStep>()` to `services.AddScoped<IXStep, XStep>()`

## Tests
No test files were changed. Verified via `grep` (per task instructions) that the only other references to the five concrete step type names are in `PlanQueriesStepTests.cs`, `GatherContextStepTests.cs`, `AggregateFactsStepTests.cs`, `ValidateFactsStepTests.cs`, `WriteArticleStepTests.cs`, and `SourceEnrichmentIntegrationTests.cs` — all of which construct the concrete classes directly via `new XStep(...)`, which remains valid since implementing an interface is purely additive. `GenerateArticleJobTests.cs` was also excluded from that grep sweep per task instructions (handled in a follow-up task) and continues to compile since concrete classes now satisfy the interface-typed constructor parameters.

## How to verify
```bash
cd backend
dotnet build ../Anela.Heblo.sln   # or: cd .. && dotnet build Anela.Heblo.sln
dotnet format ../Anela.Heblo.sln --verify-no-changes --include <touched files>
```
Both were run from the repo root against `Anela.Heblo.sln` and passed: build completed with 0 errors (250 pre-existing warnings, unrelated to this change), and `dotnet format --verify-no-changes` produced no diffs for the seven touched files.

## Notes
- The task context's example `dotnet build` / `dotnet format` invocations assumed a project/solution file directly under `backend/`, but the actual solution file `Anela.Heblo.sln` lives at the repo root. Both commands were run against that solution file instead; behavior and outcome are unaffected.
- `artifacts/feat-3593/state.json` showed as modified in `git status` before and after this task's changes — it was not touched by this task and was intentionally left out of the commit (matches instructions: only the seven specified files were staged and committed).
- No deviations from the specified interface shapes, file paths, or DI wiring.

## PR Summary
This change introduces single-method interfaces for each of the five steps in the Article generation pipeline (plan queries, gather context, aggregate facts, validate facts, write article) and updates `GenerateArticleJob` to depend on those interfaces rather than concrete classes, with `ArticleModule` registering the corresponding DI mappings. This is a pure dependency-inversion refactor with no behavioral changes — method bodies, orchestration logic, and constructor bodies are untouched; only declared types changed. Verified via `dotnet build` (0 errors) and `dotnet format --verify-no-changes` (no diffs) against the root `Anela.Heblo.sln`. A repo-wide grep confirmed no other consumers depend on the concrete step types in a way this refactor would break, aside from existing per-step unit tests and an integration test that construct the concrete classes directly (still valid, since implementing an interface is additive).

### Changes
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/PlanQueriesStep.cs` — added `IPlanQueriesStep`
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs` — added `IGatherContextStep`
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/AggregateFactsStep.cs` — added `IAggregateFactsStep`
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/ValidateFactsStep.cs` — added `IValidateFactsStep`
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs` — added `IWriteArticleStep`
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/GenerateArticleJob.cs` — constructor/fields now use interface types
- `backend/src/Anela.Heblo.Application/Features/Article/ArticleModule.cs` — DI registrations now map interface to implementation

## Status
DONE
