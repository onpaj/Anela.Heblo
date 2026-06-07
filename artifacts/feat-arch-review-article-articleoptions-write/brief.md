## Module
Article

## Finding
`ArticleOptions.WriteArticleSystemPromptTemplate` is named as though it configures the **system** prompt sent to the AI, but `WriteArticleStep` uses it exclusively as the **user** message:

```csharp
// WriteArticleStep.cs:102-113
private string BuildUserMessage(ArticlePipelineContext context)
{
    ...
    return _options.WriteArticleSystemPromptTemplate   // ← named "SystemPrompt…" but sent as ChatRole.User
        .Replace("{topic}", article.Topic)
        ...
}
```

The actual system prompt is a hardcoded constant in the same class:

```csharp
// WriteArticleStep.cs:86-92
private const string SystemInstruction =
    """
    Jsi zkušený redaktor kosmetického obsahu. Píšeš výhradně v češtině.
    Odpověz POUZE validním JSON bez markdown nebo code fences.
    ...
    """;
```

`SystemInstruction` is **not** exposed via `ArticleOptions` and cannot be overridden via `appsettings.json`. Conversely, the configurable property `WriteArticleSystemPromptTemplate` controls the user turn, not the system turn.

The same naming confusion exists in the other pipeline steps:
- `ArticleOptions.QueryPlannerSystemPrompt` — is sent as `ChatRole.System` ✅
- `ArticleOptions.AggregateFactsSystemPrompt` — is sent as `ChatRole.System` ✅
- `ArticleOptions.ValidateFactsSystemPrompt` — is sent as `ChatRole.System` ✅
- `ArticleOptions.WriteArticleSystemPromptTemplate` — is sent as `ChatRole.User` ❌

Three out of four properties correctly match their role; only the write step is inverted.

## Why it matters
- An operator who wants to tune article writing will find `WriteArticleSystemPromptTemplate` in `appsettings.json`, assume it controls the system-level persona/instruction, and edit it — without effect on the persona. Their change controls the *user turn* (the article brief), not the AI's behaviour mode.
- Conversely, the hardcoded `SystemInstruction` cannot be changed without a code deploy, even though the intent of `ArticleOptions` is to make prompts configurable.
- Breaking consistency with the three other correctly-named options (`QueryPlannerSystemPrompt`, `AggregateFactsSystemPrompt`, `ValidateFactsSystemPrompt`) makes the configuration surface harder to reason about.

## Suggested fix
Two-part fix — either is sufficient; doing both is cleanest:

**Option A: rename to match actual usage**
```csharp
// ArticleOptions.cs
public string WriteArticleUserPromptTemplate { get; set; } = ...;
```
Update the one reference in `WriteArticleStep.BuildUserMessage`. No behaviour change.

**Option B: make the system prompt configurable too** (recommended)
```csharp
// ArticleOptions.cs
public string WriteArticleSystemPrompt { get; set; } = /* the current SystemInstruction value */;
public string WriteArticleUserPromptTemplate { get; set; } = ...;
```
In `WriteArticleStep`, replace the hardcoded `SystemInstruction` constant with `_options.WriteArticleSystemPrompt`, consistent with how the other three steps work.

---
_Filed by daily arch-review routine on 2026-05-25._