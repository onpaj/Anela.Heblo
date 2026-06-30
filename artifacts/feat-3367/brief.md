## Module
Article

## Finding
`GetArticleTraceHandler` converts `ArticleGenerationStepStatus` to a plain string instead of exposing it as a typed enum:

**`backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetArticleTrace/GetArticleTraceHandler.cs` line 44:**
```csharp
Status = s.Status.ToString(),  // loses enum type
```

The DTO field is declared as `string`:

**`backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetArticleTrace/GetArticleTraceResponse.cs` line 7:**
```csharp
public string Status { get; set; } = string.Empty;
```

Because the field is `string`, NSwag does not emit a `ArticleGenerationStepStatus` TypeScript enum. The frontend therefore uses magic strings:

**`frontend/src/features/articles/ArticleDebugPanel.tsx` lines 9–13:**
```ts
const STEP_STATUS_COLORS: Record<string, string> = {
  Running: 'bg-blue-100 text-blue-700',
  Succeeded: 'bg-green-100 text-green-700',
  Failed: 'bg-red-100 text-red-700',
};
```

**Lines 40, 51:** `step.status === 'Running'`, `step.status === 'Failed'`

This is inconsistent with `ArticleStatus`, which flows through `GetArticleResponse` as the enum type and correctly generates a TypeScript `ArticleStatus` enum that the frontend imports.

## Why it matters
Magic strings break the refactor-safe contract between backend and frontend. Renaming or adding a step status (e.g. `Skipped`) requires a text-search across files instead of following the compiler. It also prevents exhaustive-switch checking and autocomplete in the frontend, and the `Record<string, string>` type widens the dictionary to accept any key silently.

## Suggested fix
Change the DTO field type to `ArticleGenerationStepStatus` and remove the `.ToString()` call:

```csharp
// GetArticleTraceResponse.cs
public ArticleGenerationStepStatus Status { get; set; }

// GetArticleTraceHandler.cs line 44
Status = s.Status,  // pass enum directly
```

NSwag will then emit a `ArticleGenerationStepStatus` TypeScript enum alongside `ArticleStatus`. Update `ArticleDebugPanel.tsx` to import and use the generated enum:

```ts
import { ArticleGenerationStepStatus } from '../../api/generated/api-client';

const STEP_STATUS_COLORS: Record<ArticleGenerationStepStatus, string> = {
  [ArticleGenerationStepStatus.Running]: '...',
  [ArticleGenerationStepStatus.Succeeded]: '...',
  [ArticleGenerationStepStatus.Failed]: '...',
};
```

---
_Filed by daily arch-review routine on 2026-06-25._
