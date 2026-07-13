### task: fix-trace-snippet-source

Change `GatherContextStep.ExecuteAsync` (backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs:70) to build the trace `allSnippets` list from `deduplicatedWeb` instead of `webSnippets`.

Add a regression test in `GatherContextStepTests` asserting the recorded `ArticleGenerationStep.OutputJson` snippet count matches the deduplicated count for duplicate web URLs.

Verify: `dotnet build`, `dotnet test --filter FullyQualifiedName~GatherContextStepTests`, `dotnet format --verify-no-changes` on the touched files.
