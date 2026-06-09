# Specification: Remove unnecessary `as any` cast in useGetArticleQuery

## Summary
Remove a stale `as any` cast and its associated `eslint-disable` comment from the `useGetArticleQuery` hook in `frontend/src/api/hooks/useArticles.ts`. The cast was added when the generated TypeScript client was missing four `ArticleSourceDto` properties; those properties are now present and correctly typed, so direct property access is type-safe.

## Background
The daily architecture review routine flagged the `useGetArticleQuery` hook (lines 173–181) for casting each `ArticleSourceDto` to `any` to access `knowledgeBaseChunkId`, `confidence`, `excerpt`, and `validationNote`. Inspection of the generated client (`frontend/src/api/generated/api-client.ts` lines 13220–13277) confirms all four properties are now declared on `ArticleSourceDto` with correct optional types. The cast and its `eslint-disable` comment are therefore obsolete and actively harmful: they suppress type checking for the entire mapping block, hide future regressions (renames, type changes), and set a precedent for `as any` elsewhere in the file.

## Functional Requirements

### FR-1: Remove the `as any` cast and use direct typed property access
The `raw` intermediate variable cast to `any` must be removed. All seven properties returned by the map callback must be accessed directly from the typed `s: ArticleSourceDto` parameter, preserving the existing null-coalescing fallback behavior.

**Acceptance criteria:**
- The line `const raw = s as any;` is removed from `frontend/src/api/hooks/useArticles.ts`.
- The mapped object returns:
  - `title: s.title ?? ''`
  - `url: s.url ?? null`
  - `type: s.type ?? ''`
  - `knowledgeBaseChunkId: s.knowledgeBaseChunkId ?? null`
  - `confidence: s.confidence ?? null`
  - `excerpt: s.excerpt ?? null`
  - `validationNote: s.validationNote ?? null`
- No `any` types appear in the modified block.
- The returned object shape (keys, value types, null semantics) matches the existing behavior exactly — consumers of `useGetArticleQuery` require no changes.

### FR-2: Remove the obsolete `eslint-disable` directive
The `// eslint-disable-next-line @typescript-eslint/no-explicit-any` comment that previously suppressed the linter for the cast must be removed, since no `any` usage remains.

**Acceptance criteria:**
- The comment `// eslint-disable-next-line @typescript-eslint/no-explicit-any` is removed from the affected block.
- No other `eslint-disable` directives in the file are touched.
- `npm run lint` passes with no new warnings or errors in `useArticles.ts`.

### FR-3: Preserve runtime null/undefined semantics
The generated DTO declares all four properties as optional (`string | undefined`, `number | undefined`). The existing `??` operator coalesces `undefined` to `null` (or `''` for strings on `title`/`type`). This conversion must remain intact so downstream consumers continue to receive `null` rather than `undefined`.

**Acceptance criteria:**
- For each property, when the source value is `undefined`, the returned value matches the prior behavior (`null` for nullable fields, `''` for `title` and `type`).
- For each property, when the source value is defined, the returned value is the source value unchanged.

## Non-Functional Requirements

### NFR-1: Type safety
The change must not introduce or retain any `any` types in the modified block. TypeScript's strict checks (including `noImplicitAny`) must apply to every property access.

### NFR-2: Build & lint
- `npm run build` must succeed with no new TypeScript errors.
- `npm run lint` must succeed with no new ESLint errors or warnings in `useArticles.ts`.

### NFR-3: Test coverage
Existing unit tests covering `useGetArticleQuery` must continue to pass without modification. If no tests cover this mapping, none need to be added for this refactor (the change is a pure type-level cleanup with no behavioral delta).

## Data Model
No changes. The `ArticleSourceDto` type in the generated client already declares the four properties:

```typescript
export class ArticleSourceDto implements IArticleSourceDto {
    title?: string;
    url?: string | undefined;
    type?: string;
    knowledgeBaseChunkId?: string | undefined;
    confidence?: number | undefined;
    excerpt?: string | undefined;
    validationNote?: string | undefined;
    // ...
}
```

The returned mapped object's shape is unchanged.

## API / Interface Design
No public API changes. `useGetArticleQuery`'s hook signature, return type, and runtime output remain identical. The change is internal to the mapping callback.

## Dependencies
- Generated TypeScript client at `frontend/src/api/generated/api-client.ts` (already contains the required properties — no regeneration needed).
- No new packages, services, or features.

## Out of Scope
- Audit or removal of other `as any` casts elsewhere in the codebase.
- Audit or removal of other `eslint-disable` directives elsewhere in `useArticles.ts` or the codebase.
- Changes to `ArticleSourceDto`, the generated client, or the backend contract.
- Adding new unit tests for `useGetArticleQuery` (existing coverage is sufficient for this refactor).
- Refactoring the surrounding query hook structure, caching, or error handling.

## Open Questions
None.

## Status: COMPLETE