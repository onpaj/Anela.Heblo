# Fix `useArticleFeedbackListQuery` Type/Field Mismatches Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename the frontend article-feedback list summary fields (`generatedAt` → `createdAt`, `hasFeedback` → `hasComment`) and drop the phantom `feedbackComment` field so the `ArticleFeedbackSummary` interface and `useArticleFeedbackListQuery` mapping reflect the actual backend payload, then update the single consumer and tests to compile and pass.

**Architecture:** Strictly frontend TypeScript-only rename. The hook already uses the generated NSwag client `client.articles_FeedbackList`; it returns a typed DTO with the correct backend field names (`createdAt`, `hasComment`, no `feedbackComment` on list rows). Only the hand-written frontend interface and the single consuming adapter are misaligned. The generic `FeedbackRow`/`FeedbackDetail` contract is domain-neutral and is **not** renamed — the article adapter projects from `hasComment` onto `FeedbackRow.hasFeedback` and writes `feedbackComment: null` (since the list endpoint never carries comment text).

**Tech Stack:** React, TypeScript, @tanstack/react-query, jest + @testing-library/react (via react-scripts), NSwag-generated API client.

---

## File Structure

**Files to modify:**

- `frontend/src/api/hooks/useArticles.ts` — `ArticleFeedbackSummary` interface (rename + remove `feedbackComment`); per-item mapping inside `useArticleFeedbackListQuery`.
- `frontend/src/components/feedback/adapters/useArticleFeedbackAdapter.ts` — read `article.createdAt` / `article.hasComment`; pass `feedbackComment: null` into `FeedbackDetail` (list endpoint never provides it).
- `frontend/src/components/feedback/adapters/__tests__/useArticleFeedbackAdapter.test.ts` — rename mock-fixture fields (`generatedAt` → `createdAt`, `hasFeedback` → `hasComment`, drop `feedbackComment`); rename two test descriptions that mention `generatedAt`.

**Files to create:**

- `frontend/src/api/hooks/__tests__/useArticles.test.ts` — new hook-level tests covering the mapping for: populated `items`, missing `items`, and `hasComment` preservation. Follows the pattern of `__tests__/useJournal.test.ts` and `__tests__/useKnowledgeBase.test.ts`: mock `getAuthenticatedApiClient` to return a stub whose `articles_FeedbackList` returns crafted DTOs.

**Files explicitly NOT touched** (per arch-review Decisions 1 & 2):

- `frontend/src/components/feedback/types.ts` — generic `FeedbackRow.hasFeedback` and `FeedbackDetail.feedbackComment` stay as-is (domain-neutral).
- `frontend/src/components/feedback/adapters/useLeafletFeedbackAdapter.ts`, `useKbFeedbackAdapter.ts` and their tests.
- `frontend/src/components/feedback/GenericFeedbackTable.tsx`, `GenericFeedbackDetailModal.tsx`, `GenericFeedbackFilters.tsx`.
- `frontend/src/features/articles/ArticleFeedbackSection.tsx` — reads `article.feedbackComment` from `ArticleDetail` (the detail type, not the list summary); it stays.
- `frontend/src/api/hooks/useArticles.ts` — `ArticleDetail.feedbackComment` and `useGetArticleQuery` mapping stay. Only `ArticleFeedbackSummary` and `useArticleFeedbackListQuery` change.
- `frontend/src/api/generated/api-client.ts` — generated, not manually edited.
- Any backend file.

---

## Task 1: Add hook-level mapping tests (RED)

**Files:**
- Create: `frontend/src/api/hooks/__tests__/useArticles.test.ts`

The new tests will fail to compile (or fail at runtime) because today’s `ArticleFeedbackSummary` exposes `generatedAt`/`hasFeedback`/`feedbackComment`, not `createdAt`/`hasComment`. That’s the RED step we want before the rename in Task 2.

- [ ] **Step 1: Create the new test file with three mapping tests**

Create `frontend/src/api/hooks/__tests__/useArticles.test.ts`:

```typescript
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import { useArticleFeedbackListQuery } from '../useArticles';
import * as clientModule from '../../client';

jest.mock('../../client', () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: {
    articles: ['articles'],
  },
}));

const mockGetAuthenticatedApiClient =
  clientModule.getAuthenticatedApiClient as jest.MockedFunction<
    typeof clientModule.getAuthenticatedApiClient
  >;

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  return React.createElement(
    QueryClientProvider,
    { client: queryClient },
    children,
  );
};

describe('useArticleFeedbackListQuery mapping', () => {
  let mockArticlesFeedbackList: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    mockArticlesFeedbackList = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue({
      articles_FeedbackList: mockArticlesFeedbackList,
    } as any);
  });

  it('maps a populated DTO row to the renamed frontend shape (createdAt, hasComment, no feedbackComment)', async () => {
    mockArticlesFeedbackList.mockResolvedValue({
      items: [
        {
          id: 'art-1',
          topic: 'Topic A',
          title: 'Title A',
          requestedBy: 'user@anela.cz',
          createdAt: new Date('2026-01-15T10:30:00Z'),
          precisionScore: 4,
          styleScore: 5,
          hasComment: true,
        },
      ],
      totalCount: 1,
      page: 1,
      pageSize: 20,
      totalPages: 1,
      stats: {
        totalArticles: 1,
        totalWithFeedback: 1,
        avgPrecisionScore: 4,
        avgStyleScore: 5,
      },
    });

    const { result } = renderHook(() => useArticleFeedbackListQuery({}), {
      wrapper: createWrapper,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data!.articles).toHaveLength(1);
    expect(result.current.data!.articles[0]).toEqual({
      id: 'art-1',
      topic: 'Topic A',
      title: 'Title A',
      requestedBy: 'user@anela.cz',
      createdAt: '2026-01-15T10:30:00.000Z',
      precisionScore: 4,
      styleScore: 5,
      hasComment: true,
    });
    expect(result.current.data!.totalCount).toBe(1);
    expect(result.current.data!.stats).toEqual({
      totalArticles: 1,
      totalWithFeedback: 1,
      avgPrecisionScore: 4,
      avgStyleScore: 5,
    });
  });

  it('returns empty articles and default stats when items and stats are missing from the DTO', async () => {
    mockArticlesFeedbackList.mockResolvedValue({
      totalCount: 0,
      page: 1,
      pageSize: 20,
      totalPages: 0,
    });

    const { result } = renderHook(() => useArticleFeedbackListQuery({}), {
      wrapper: createWrapper,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data!.articles).toEqual([]);
    expect(result.current.data!.stats).toEqual({
      totalArticles: 0,
      totalWithFeedback: 0,
      avgPrecisionScore: null,
      avgStyleScore: null,
    });
  });

  it('preserves hasComment per row (does not synthesize it from other fields) and maps null createdAt', async () => {
    mockArticlesFeedbackList.mockResolvedValue({
      items: [
        {
          id: 'art-with-comment',
          topic: 'T',
          title: null,
          requestedBy: 'u',
          createdAt: undefined,
          precisionScore: null,
          styleScore: null,
          hasComment: true,
        },
        {
          id: 'art-without-comment',
          topic: 'T',
          title: null,
          requestedBy: 'u',
          createdAt: undefined,
          precisionScore: null,
          styleScore: null,
          hasComment: false,
        },
      ],
      totalCount: 2,
      page: 1,
      pageSize: 20,
      totalPages: 1,
    });

    const { result } = renderHook(() => useArticleFeedbackListQuery({}), {
      wrapper: createWrapper,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const [withComment, withoutComment] = result.current.data!.articles;
    expect(withComment.hasComment).toBe(true);
    expect(withComment.createdAt).toBeNull();
    expect(withoutComment.hasComment).toBe(false);
    expect(withoutComment.createdAt).toBeNull();
  });
});
```

- [ ] **Step 2: Run the new tests and verify they fail**

Run from the `frontend/` directory:

```bash
cd frontend && CI=true npx react-scripts test src/api/hooks/__tests__/useArticles.test.ts --watchAll=false
```

Expected: tests FAIL. The current mapping emits `generatedAt`/`hasFeedback`/`feedbackComment`, so the `toEqual` in test 1 will not match and `hasComment`/`createdAt` accesses in tests 1 and 3 will read `undefined`. This is the RED step — we want failure here.

- [ ] **Step 3: Do not commit yet**

The plan commits once at the end (Task 6) after all related changes compile and pass together. Leave the new test file uncommitted for now.

---

## Task 2: Rename `ArticleFeedbackSummary` fields and update the hook mapping (GREEN for Task 1 tests, RED for adapter)

**Files:**
- Modify: `frontend/src/api/hooks/useArticles.ts:78-88, 268-281`

This step renames the public hook contract and updates the per-item mapping accordingly. The hook tests from Task 1 will pass after this step. The adapter file will now fail to compile because it reads `article.generatedAt`/`article.hasFeedback`/`article.feedbackComment`, which no longer exist — that breakage is intentional and is fixed in Task 3.

- [ ] **Step 1: Update the `ArticleFeedbackSummary` interface**

In `frontend/src/api/hooks/useArticles.ts`, replace the current interface (lines 78–88):

```typescript
export interface ArticleFeedbackSummary {
  id: string;
  topic: string;
  title: string | null;
  requestedBy: string;
  generatedAt: string | null;
  precisionScore: number | null;
  styleScore: number | null;
  feedbackComment: string | null;
  hasFeedback: boolean;
}
```

with:

```typescript
export interface ArticleFeedbackSummary {
  id: string;
  topic: string;
  title: string | null;
  requestedBy: string;
  createdAt: string | null;
  precisionScore: number | null;
  styleScore: number | null;
  hasComment: boolean;
}
```

Rationale per spec FR-3: `generatedAt` → `createdAt`, `hasFeedback` → `hasComment`, drop `feedbackComment`. Keep `id: string` (not `string | undefined`); the existing `item.id ?? ''` default handles the NSwag `string | undefined`.

- [ ] **Step 2: Update the per-item mapping inside `useArticleFeedbackListQuery`**

In the same file, replace the mapping block in `useArticleFeedbackListQuery` (current lines 268–281):

```typescript
articles: (data.items ?? []).map((item) => ({
  id: item.id ?? '',
  topic: item.topic ?? '',
  title: item.title ?? null,
  requestedBy: item.requestedBy ?? '',
  generatedAt: item.createdAt?.toISOString() ?? null,
  precisionScore: item.precisionScore ?? null,
  styleScore: item.styleScore ?? null,
  // Backend list endpoint never emits a per-item feedback comment
  // (only the boolean hasComment), so projecting null here is exact
  // behavior preservation, not data loss.
  feedbackComment: null,
  hasFeedback: item.hasComment ?? false,
})),
```

with:

```typescript
articles: (data.items ?? []).map((item) => ({
  id: item.id ?? '',
  topic: item.topic ?? '',
  title: item.title ?? null,
  requestedBy: item.requestedBy ?? '',
  createdAt: item.createdAt?.toISOString() ?? null,
  precisionScore: item.precisionScore ?? null,
  styleScore: item.styleScore ?? null,
  hasComment: item.hasComment ?? false,
})),
```

Leave the surrounding `totalCount` / `page` / `pageSize` / `totalPages` / `stats` defaults exactly as they are (NFR-4 forbids adding helpers/abstractions just for this fix).

- [ ] **Step 3: Run the hook tests and verify they now pass**

```bash
cd frontend && CI=true npx react-scripts test src/api/hooks/__tests__/useArticles.test.ts --watchAll=false
```

Expected: all three tests PASS.

- [ ] **Step 4: Verify the adapter file is now broken (expected)**

```bash
cd frontend && npx tsc --noEmit -p tsconfig.json 2>&1 | grep useArticleFeedbackAdapter
```

Expected: errors mentioning `Property 'generatedAt' does not exist`, `Property 'hasFeedback' does not exist`, `Property 'feedbackComment' does not exist` on the article row type. This confirms Task 3 is the next required step. Do not commit yet.

---

## Task 3: Update the article feedback adapter to read renamed fields

**Files:**
- Modify: `frontend/src/components/feedback/adapters/useArticleFeedbackAdapter.ts:15-25`

The adapter is the only file outside `useArticles.ts` that reads from `ArticleFeedbackSummary`. Per arch-review Decision 1, do **not** rename `FeedbackRow.hasFeedback` (domain-neutral). Per Decision 2, explicitly pass `feedbackComment: null` (the list endpoint never carries it).

- [ ] **Step 1: Update the row-projection block**

In `frontend/src/components/feedback/adapters/useArticleFeedbackAdapter.ts`, replace the current `rows` mapping (lines 15–25):

```typescript
const rows: FeedbackDetail[] = (query.data?.articles ?? []).map((article) => ({
  id: article.id,
  primaryText: article.title ?? article.topic,
  secondaryText: article.topic,
  createdAt: article.generatedAt ?? '',
  userId: article.requestedBy,
  precisionScore: article.precisionScore,
  styleScore: article.styleScore,
  hasFeedback: article.hasFeedback,
  feedbackComment: article.feedbackComment,
}));
```

with:

```typescript
const rows: FeedbackDetail[] = (query.data?.articles ?? []).map((article) => ({
  id: article.id,
  primaryText: article.title ?? article.topic,
  secondaryText: article.topic,
  createdAt: article.createdAt ?? '',
  userId: article.requestedBy,
  precisionScore: article.precisionScore,
  styleScore: article.styleScore,
  hasFeedback: article.hasComment,
  feedbackComment: null,
}));
```

Leave the `stats` block (lines 27–34) and the `return` block (36–44) unchanged.

- [ ] **Step 2: Verify the adapter compiles**

```bash
cd frontend && npx tsc --noEmit -p tsconfig.json 2>&1 | grep useArticleFeedbackAdapter
```

Expected: no output (no errors). If errors remain, fix them before continuing.

- [ ] **Step 3: Confirm the adapter tests now fail at runtime**

The adapter test fixtures still use the old field names, so its runtime assertions will break:

```bash
cd frontend && CI=true npx react-scripts test src/components/feedback/adapters/__tests__/useArticleFeedbackAdapter.test.ts --watchAll=false
```

Expected: failures in `maps generatedAt to createdAt` and `uses empty string for createdAt when generatedAt is null` (the mocks still have `generatedAt`, so `article.createdAt` reads as `undefined` and the projected `createdAt` becomes `''`). This confirms Task 4 is needed next.

---

## Task 4: Update adapter test fixtures and descriptions

**Files:**
- Modify: `frontend/src/components/feedback/adapters/__tests__/useArticleFeedbackAdapter.test.ts:9-31, 81-89`

Mocks must match the new `ArticleFeedbackSummary` shape. Two test descriptions reference the old `generatedAt` field name and must be renamed too.

- [ ] **Step 1: Update `mockArticle`**

Replace the current `mockArticle` declaration (lines 9–19):

```typescript
const mockArticle = {
  id: 'art-1',
  topic: 'Péče o pleť v létě',
  title: 'Jak pečovat o pleť v letních měsících',
  requestedBy: 'user@anela.cz',
  generatedAt: '2026-01-15T10:30:00Z',
  precisionScore: 4,
  styleScore: 5,
  feedbackComment: 'Skvělý článek.',
  hasFeedback: true,
};
```

with:

```typescript
const mockArticle = {
  id: 'art-1',
  topic: 'Péče o pleť v létě',
  title: 'Jak pečovat o pleť v letních měsících',
  requestedBy: 'user@anela.cz',
  createdAt: '2026-01-15T10:30:00Z',
  precisionScore: 4,
  styleScore: 5,
  hasComment: true,
};
```

- [ ] **Step 2: Update `mockArticleNoTitle`**

Replace the current `mockArticleNoTitle` declaration (lines 21–31):

```typescript
const mockArticleNoTitle = {
  id: 'art-2',
  topic: 'Zimní vlasová péče',
  title: null,
  requestedBy: 'other@anela.cz',
  generatedAt: null,
  precisionScore: null,
  styleScore: null,
  feedbackComment: null,
  hasFeedback: false,
};
```

with:

```typescript
const mockArticleNoTitle = {
  id: 'art-2',
  topic: 'Zimní vlasová péče',
  title: null,
  requestedBy: 'other@anela.cz',
  createdAt: null,
  precisionScore: null,
  styleScore: null,
  hasComment: false,
};
```

- [ ] **Step 3: Rename the two test descriptions that reference `generatedAt`**

Replace:

```typescript
test('maps generatedAt to createdAt', () => {
  const { result } = renderHook(() => useArticleFeedbackAdapter(params));
  expect(result.current.rows[0].createdAt).toBe('2026-01-15T10:30:00Z');
});

test('uses empty string for createdAt when generatedAt is null', () => {
  const { result } = renderHook(() => useArticleFeedbackAdapter(params));
  expect(result.current.rows[1].createdAt).toBe('');
});
```

with:

```typescript
test('maps article createdAt onto row createdAt', () => {
  const { result } = renderHook(() => useArticleFeedbackAdapter(params));
  expect(result.current.rows[0].createdAt).toBe('2026-01-15T10:30:00Z');
});

test('uses empty string for row createdAt when article createdAt is null', () => {
  const { result } = renderHook(() => useArticleFeedbackAdapter(params));
  expect(result.current.rows[1].createdAt).toBe('');
});
```

Leave all other tests in this file unchanged — the `primaryText`/`secondaryText`/`userId`/`stats`/`params translation`/`loading`/`error` tests do not depend on the renamed fields.

- [ ] **Step 4: Run the adapter tests and verify they pass**

```bash
cd frontend && CI=true npx react-scripts test src/components/feedback/adapters/__tests__/useArticleFeedbackAdapter.test.ts --watchAll=false
```

Expected: all adapter tests PASS.

---

## Task 5: Verify no other consumers reference the old field names

**Files:** No edits. This is a guard step to confirm the rename is complete in the article-feedback path and has not leaked into unrelated code.

- [ ] **Step 1: Grep for stale references in feedback-list usage sites**

```bash
cd frontend && grep -rn 'generatedAt\|hasFeedback\|feedbackComment' src/components/feedback/adapters src/api/hooks/useArticles.ts src/api/hooks/__tests__/useArticles.test.ts src/components/feedback/adapters/__tests__/useArticleFeedbackAdapter.test.ts
```

Expected matches (allowed — domain-neutral or unrelated to the article list summary):

- `src/components/feedback/adapters/useArticleFeedbackAdapter.ts` — `hasFeedback: article.hasComment,` (mapping onto the generic row contract; Decision 1).
- `src/components/feedback/adapters/useArticleFeedbackAdapter.ts` — `feedbackComment: null,` (explicit projection onto `FeedbackDetail`; Decision 2).
- `src/components/feedback/adapters/useLeafletFeedbackAdapter.ts` and `useKbFeedbackAdapter.ts` — out of scope; their own `hasFeedback`/`feedbackComment` fields are sourced from different backends and stay.

Expected to NOT match (must be gone):

- Any `generatedAt` in `useArticleFeedbackAdapter.*` or in the article feedback list mapping.
- Any `feedbackComment:` reference inside `useArticles.ts` that targets `ArticleFeedbackSummary` (the `useGetArticleQuery` mapping for `ArticleDetail.feedbackComment` is allowed — that’s the detail endpoint, not the list).
- Any `hasFeedback` on a row read from `useArticleFeedbackListQuery` (the adapter writes `hasFeedback` onto the generic row; that’s allowed).

If any disallowed match is found, fix it before continuing.

- [ ] **Step 2: Grep the wider tree for stale references to the renamed `ArticleFeedbackSummary` fields**

```bash
cd frontend && grep -rn 'useArticleFeedbackListQuery' src
```

Expected: only `useArticleFeedbackAdapter.ts` and `__tests__/useArticles.test.ts` consume the hook. If any other consumer appears, update it to read `createdAt`/`hasComment` (no `feedbackComment`).

---

## Task 6: Run full validation, then commit

**Files:** No edits. This is the standard-validation + single-commit step.

- [ ] **Step 1: Type-check the frontend**

```bash
cd frontend && npx tsc --noEmit -p tsconfig.json
```

Expected: no errors. If the compiler complains about `ArticleFeedbackSummary`, `useArticleFeedbackListQuery`, or `useArticleFeedbackAdapter`, fix before continuing.

- [ ] **Step 2: Run all touched test files together**

```bash
cd frontend && CI=true npx react-scripts test src/api/hooks/__tests__/useArticles.test.ts src/components/feedback/adapters/__tests__/useArticleFeedbackAdapter.test.ts --watchAll=false
```

Expected: every test PASS. No `console.error` about act warnings or missing query data.

- [ ] **Step 3: Build the frontend**

```bash
cd frontend && npm run build
```

Expected: build succeeds. CRA may emit lint warnings — they should not include any unused-variable or type errors introduced by this change.

- [ ] **Step 4: Lint**

```bash
cd frontend && npm run lint
```

Expected: no new errors. Pre-existing warnings unrelated to the changed files are acceptable.

- [ ] **Step 5: Stage only the files this plan changes**

```bash
git add frontend/src/api/hooks/useArticles.ts \
        frontend/src/api/hooks/__tests__/useArticles.test.ts \
        frontend/src/components/feedback/adapters/useArticleFeedbackAdapter.ts \
        frontend/src/components/feedback/adapters/__tests__/useArticleFeedbackAdapter.test.ts
```

Expected: `git status` shows exactly four files staged (one new, three modified). If any other file appears modified, investigate before committing — the plan is meant to be a single-commit, surgical change.

- [ ] **Step 6: Commit**

```bash
git commit -m "$(cat <<'EOF'
fix: align ArticleFeedbackSummary with backend (createdAt, hasComment)

Renames the hand-written ArticleFeedbackSummary fields to match the
backend payload returned by GetArticleFeedbackListHandler / NSwag DTO:
generatedAt -> createdAt, hasFeedback -> hasComment. Removes the phantom
feedbackComment field (the list endpoint never emits it; only the
boolean hasComment).

The single consumer useArticleFeedbackAdapter is updated to read the
renamed fields and to pass feedbackComment: null explicitly into the
generic FeedbackDetail (Decision 2 in the architecture review). The
domain-neutral FeedbackRow.hasFeedback contract is intentionally left
in place (Decision 1) so leaflet and KB adapters remain untouched.

Adds hook-level mapping tests for useArticleFeedbackListQuery covering
populated rows, missing items, and hasComment preservation.
EOF
)"
```

Expected: commit succeeds. `git log -1 --stat` should list exactly the four files above.

---

## Self-Review

**Spec coverage:**

- FR-1 (explicit field-by-field mapping, no untyped passthrough) — Task 2 step 2 keeps the existing explicit mapping; no `as any` cast of the DTO is introduced. Per arch-review amendment 1, FR-1 is satisfied by mapping from the typed NSwag DTO rather than from `response.json()`.
- FR-2 (frontend exposes `articles`, hook reads `raw.items`) — Task 2 step 2 retains `articles: (data.items ?? []).map(...)`. No consumer reads `items` directly (verified in Task 5 step 2).
- FR-3 (rename `generatedAt` → `createdAt`, `hasFeedback` → `hasComment`, drop `feedbackComment` on `ArticleFeedbackSummary`) — Task 2 step 1. `id: string` is kept (arch-review amendment 2).
- FR-4 (consuming components updated; behavior preserved) — Task 3 (adapter rename) + Task 4 (test mocks rename). Per arch-review amendment 3, `FeedbackRow`/`FeedbackDetail` are not renamed; the adapter projects between them.
- FR-5 (unit tests for mapping: populated row, all-nullable row, empty `items`; `hasComment` preserved) — Task 1 covers all three cases plus the `hasComment` preservation assertion. Per arch-review amendment 4, the new tests live in `frontend/src/api/hooks/__tests__/useArticles.test.ts` and the adapter tests are updated separately.
- NFR-1/2/3/4 — Performance (no change), security (no change), backwards-compat (TypeScript-only rename), code consistency (no new abstraction introduced; mapping mirrors `useGetArticleQuery` style).
- Out-of-scope items (NSwag migration of the hook, exposing `feedbackComment` on list, refactoring sibling hooks, backend changes, E2E additions) are not touched by any task.

**Placeholder scan:** No `TBD`/`TODO`/`implement later`/`add appropriate X`/`similar to Task N` strings anywhere in the plan. Every code step contains the full replacement code; every command step contains the exact command and expected outcome.

**Type consistency:** Across tasks, the interface field names (`createdAt`, `hasComment`), the adapter projection (`article.createdAt`, `article.hasComment`, `hasFeedback: article.hasComment`, `feedbackComment: null`), and the test mock keys (`createdAt`, `hasComment`) all match. The hook test uses `result.current.data!.articles[0]` and asserts the same renamed keys. No drift between tasks.
