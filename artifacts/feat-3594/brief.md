## Module
Article

## Finding
`GatherContextStep.ExecuteAsync` deduplicates web snippets before storing them in the pipeline context, but records the **pre-deduplication** list in the `OutputJson` written to `ArticleGenerationStep` (the trace):

```csharp
// backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs:65–71
var deduplicatedWeb = DeduplicateByUrl(webSnippets);

context.ContextSnippets = [.. kbSnippets, .. deduplicatedWeb];   // deduped — used by downstream steps
context.StyleGuideText = styleGuideText;

var allSnippets = kbSnippets.Concat(webSnippets).ToList();        // NOT deduped — written to trace
return (true, (object?)new { snippets = allSnippets, styleGuideLength = styleGuideText?.Length });
```

`allSnippets` concatenates `webSnippets` (raw) instead of `deduplicatedWeb`. The `OutputJson` stored in the `ArticleGenerationStep` row therefore shows a higher snippet count than what the `AggregateFactsStep` and `WriteArticleStep` actually processed. For example, if 3 web queries each returned the same top result URL, the trace logs 3 web snippets but the context carries 1.

## Why it matters
The article trace (`GET /api/Articles/{id}/trace`) is the primary diagnostic tool for investigating why an article cited (or failed to cite) specific sources. When the logged snippet count doesn't match the context that downstream steps received, the trace actively misleads:

- A developer reviewing a low-source-diversity article might check the trace, see "17 snippets gathered", and conclude the steps had plenty of context — when the actual deduplicated context was 9 snippets.
- Conversely, the dedup might be removing too aggressively: the trace won't surface this because it shows the pre-dedup list.

## Suggested fix
Use `deduplicatedWeb` (already computed on line 65) in the trace output, so what's logged matches what the pipeline consumed:

```csharp
// Line 70 — change webSnippets → deduplicatedWeb
var allSnippets = kbSnippets.Concat(deduplicatedWeb).ToList();
return (true, (object?)new { snippets = allSnippets, styleGuideLength = styleGuideText?.Length });
```

One-line change, no behaviour change to article generation itself.

---
_Filed by daily arch-review routine on 2026-07-11._
_Source: GitHub issue #3594._
