---

# Implementation: Remove unnecessary `as any` cast in useGetArticleQuery

## What was implemented

Removed the stale `as any` cast and its `eslint-disable` comment from the `sources` mapping block in `useGetArticleQuery`. Switched from an explicit-return block body with an intermediate `raw` variable to an implicit-return parenthesized object literal that accesses all seven properties directly on the typed `s: ArticleSourceDto` parameter. All `??` fallbacks are preserved exactly.

## Files created/modified

- `frontend/src/api/hooks/useArticles.ts` — removed `// eslint-disable-next-line @typescript-eslint/no-explicit-any`, removed `const raw = s as any;`, replaced `(raw.knowledgeBaseChunkId as string | null) ?? null` pattern with `s.knowledgeBaseChunkId ?? null` for all four previously-cast properties

## Tests

- No test files modified.
- All 10 existing unit tests in `frontend/src/api/hooks/__tests__/useArticles.test.ts` passed without changes (NFR-3: pure refactor with no behavioral delta).

## How to verify

```bash
cd frontend
grep -n "as any" src/api/hooks/useArticles.ts      # must return empty
grep -n "eslint-disable" src/api/hooks/useArticles.ts  # must return empty
npm run build   # no TypeScript errors
npm run lint    # no warnings in useArticles.ts
npm test -- useArticles --watchAll=false            # all 10 tests pass
```

## Notes

No deviations. The generated client at `api-client.ts` already declares all four properties (`knowledgeBaseChunkId`, `confidence`, `excerpt`, `validationNote`) as optional typed fields on `ArticleSourceDto`, confirming the arch review's premise. The implicit-return style matches the surrounding hooks in the same file (`useListArticlesQuery`, `useArticleFeedbackListQuery`).

## PR Summary

Removed a stale `as any` cast from the `useGetArticleQuery` sources mapping. The cast was added when the generated NSwag client was missing four `ArticleSourceDto` properties; those properties are now present and correctly typed, making direct property access type-safe. The `eslint-disable` directive that suppressed the linter for the cast is removed as well.

The refactor converts a two-step explicit-return block (cast to `any`, then re-cast each property) to a single implicit-return parenthesized object literal matching the style used by every other mapping in the same file. Runtime behavior is identical — all `??` fallbacks are preserved.

### Changes
- `frontend/src/api/hooks/useArticles.ts` — removed `eslint-disable` comment, removed `const raw = s as any`, switched sources `.map` callback to implicit return with direct typed property access

## Status
DONE