# Implementation — fix-trace-snippet-source (r1)

## Change
`backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs:70`

```diff
- var allSnippets = kbSnippets.Concat(webSnippets).ToList();
+ var allSnippets = kbSnippets.Concat(deduplicatedWeb).ToList();
```

## Test
Added `ExecuteAsync_DuplicateWebUrls_TraceOutputMatchesDeduplicatedSnippets` to
`backend/test/Anela.Heblo.Tests/Article/Pipeline/GatherContextStepTests.cs`. It
captures the `ArticleGenerationStep` passed to `IArticleRepository.UpdateStepAsync`,
parses `OutputJson`, and asserts the `snippets` array length is 1 (deduplicated)
rather than 2 (raw), for two web hits sharing the same URL.

## Verification
- `dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — 0 errors.
- `dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~GatherContextStepTests` — 7/7 passed.
- `dotnet format Anela.Heblo.sln --verify-no-changes` on both touched files — no changes needed.
