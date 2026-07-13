# Design — feat-3594

No design work needed beyond the spec: swap the source list feeding the trace's `snippets` field from `webSnippets` to the already-deduplicated `deduplicatedWeb`, so the count matches `context.ContextSnippets`.

```csharp
var allSnippets = kbSnippets.Concat(deduplicatedWeb).ToList();
```
