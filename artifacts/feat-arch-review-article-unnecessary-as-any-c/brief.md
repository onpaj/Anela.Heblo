## Module
Article

## Finding

In `frontend/src/api/hooks/useArticles.ts` (lines 173–181), the `useGetArticleQuery` hook casts each `ArticleSourceDto` to `any` in order to access four of its properties:

```typescript
// frontend/src/api/hooks/useArticles.ts lines 173-181
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const raw = s as any;
return {
  title: s.title ?? '',
  url: s.url ?? null,
  type: s.type ?? '',
  knowledgeBaseChunkId: (raw.knowledgeBaseChunkId as string | null) ?? null,
  confidence: (raw.confidence as number | null) ?? null,
  excerpt: (raw.excerpt as string | null) ?? null,
  validationNote: (raw.validationNote as string | null) ?? null,
};
```

All four properties (`knowledgeBaseChunkId`, `confidence`, `excerpt`, `validationNote`) are present and correctly typed in the generated TypeScript client:

```typescript
// frontend/src/api/generated/api-client.ts lines 13220-13277
export class ArticleSourceDto implements IArticleSourceDto {
    title?: string;
    url?: string | undefined;
    type?: string;
    knowledgeBaseChunkId?: string | undefined;   // present
    confidence?: number | undefined;              // present
    excerpt?: string | undefined;                 // present
    validationNote?: string | undefined;          // present
    ...
}
```

The `as any` cast and the `eslint-disable` comment are therefore stale — most likely added when the generated client was missing these properties and never cleaned up after regeneration.

## Why it matters

- The cast suppresses TypeScript's type checker for the entire mapping block; a future property rename or type change in `ArticleSourceDto` will silently compile without error.
- The `// eslint-disable-next-line @typescript-eslint/no-explicit-any` comment signals to reviewers that there is a real gap, even though there is not.
- It sets a precedent for `as any` usage elsewhere in the file.

## Suggested fix

Remove the `as any` cast and access all properties directly through the typed `s` variable:

```typescript
return {
  title: s.title ?? '',
  url: s.url ?? null,
  type: s.type ?? '',
  knowledgeBaseChunkId: s.knowledgeBaseChunkId ?? null,
  confidence: s.confidence ?? null,
  excerpt: s.excerpt ?? null,
  validationNote: s.validationNote ?? null,
};
```

Also remove the `// eslint-disable-next-line @typescript-eslint/no-explicit-any` comment on line 173.

---
_Filed by daily arch-review routine on 2026-06-07._