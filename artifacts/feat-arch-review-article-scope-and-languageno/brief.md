## Module
Article

## Finding
`Article.Scope` (e.g. `"overview"`, `"deep-dive"`, `"technical"`, `"beginner"`) and `Article.LanguageNote` are captured in the API request, validated, stored in the database, and surfaced in the UI — but neither ever reaches the writing step that actually produces the article.

**`WriteArticleStep.BuildUserMessage` (`WriteArticleStep.cs:102-113`):**
```csharp
return _options.WriteArticleSystemPromptTemplate
    .Replace("{topic}", article.Topic)
    .Replace("{audience}", article.Audience ?? "obecné publikum")
    .Replace("{length}", article.Length)
    .Replace("{angle}", article.Angle ?? "(nevyspecifikováno)")
    .Replace("{facts}", factsText)
    .Replace("{style_guide}", context.StyleGuideText ?? "");
```

`{scope}` and `{language_note}` are never substituted. The default template in `ArticleOptions.WriteArticleSystemPromptTemplate` (`ArticleOptions.cs:56`) also contains no `{scope}` or `{language_note}` placeholders, so even a custom appsettings override that adds these tokens would silently leave them unreplaced.

`AggregateFactsHandler.BuildUserMessage` does pass `scope` to the fact-aggregation step (`AggregateFactsStep.cs:84-90`), so `scope` is consistently used in earlier pipeline stages but is dropped for the final writing step — the only one whose output the user sees.

**Impact path:**
- User selects "deep-dive" scope → article is generated with exactly the same instruction as "overview".
- User enters a language note ("prefer short sentences, avoid jargon") → silently discarded.

## Why it matters
- User-provided fields have no effect on the final output. The illusion of control degrades trust and makes the form misleading.
- `Article.LanguageNote` exists as a domain concept with a column in the database but is a no-op end-to-end (not even displayed in `ArticleGenerationForm.tsx`), making it dead infrastructure.
- The feature spec (§8.5) explicitly lists `{scope}` and `[Tone note: {language_note}]` as required prompt variables.

## Suggested fix
1. Add `{scope}` and `{language_note}` substitutions to `BuildUserMessage`:
   ```csharp
   return _options.WriteArticleSystemPromptTemplate
       .Replace("{topic}", article.Topic)
       .Replace("{scope}", article.Scope)
       .Replace("{audience}", article.Audience ?? "obecné publikum")
       .Replace("{length}", article.Length)
       .Replace("{angle}", article.Angle ?? "(nevyspecifikováno)")
       .Replace("{language_note}", article.LanguageNote ?? "")
       .Replace("{facts}", factsText)
       .Replace("{style_guide}", context.StyleGuideText ?? "");
   ```
2. Update `ArticleOptions.WriteArticleSystemPromptTemplate` default to include both placeholders.
3. Add a `languageNote` input field to `ArticleGenerationForm.tsx` (or remove the field from the domain/API entirely if intentionally deferred).

---
_Filed by daily arch-review routine on 2026-05-25._