# Spec — feat-3594

## Problem
`GatherContextStep.ExecuteAsync` (backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs) writes a mismatched snippet count to the `ArticleGenerationStep.OutputJson` trace: it deduplicates web snippets by URL for `context.ContextSnippets` (line 65–67, consumed by `AggregateFactsStep`/`WriteArticleStep`), but the trace payload on line 70 concatenates the **raw, pre-deduplication** `webSnippets` list instead of the already-computed `deduplicatedWeb`.

## Scope
Single-file, single-line fix. No API contract change, no schema change, no behavior change to article generation — only the diagnostic trace payload changes to reflect reality.

## Acceptance criteria
1. `GatherContextStep.cs` line 70 uses `deduplicatedWeb` instead of `webSnippets` when building `allSnippets` for the trace.
2. `context.ContextSnippets` (the actual pipeline data flow) is unchanged — already correct.
3. A regression test asserts the recorded `ArticleGenerationStep.OutputJson.snippets` array length matches the deduplicated count when duplicate web URLs are returned.
4. Existing `GatherContextStepTests` continue to pass unmodified.

## Out of scope
- Any change to `DeduplicateByUrl` semantics.
- Any change to `GetArticleTraceHandler` or the trace API response shape.
