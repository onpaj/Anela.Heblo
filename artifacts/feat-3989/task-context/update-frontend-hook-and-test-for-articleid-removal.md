### task: update-frontend-hook-and-test-for-articleid-removal

## Goal
Stop `useSubmitArticleFeedbackMutation` from passing `articleId` into the `SubmitArticleFeedbackRequest` constructor (the field no longer exists on the regenerated type), and update the one existing test assertion that currently expects it in the mocked request body.

## Files to change

**Edit:**
- `frontend/src/api/hooks/useArticles.ts`
- `frontend/src/api/hooks/__tests__/useArticles.test.ts`

**Verify only, no change expected:**
- `frontend/src/api/hooks/useArticles.ts` lines other than the `useSubmitArticleFeedbackMutation` body — e.g. `useArticleFeedbackListQuery`, `articleKeys` — untouched.
- The route-level `articleId` parameter/argument: `useSubmitArticleFeedbackMutation(articleId: string)`'s signature, and the call `client.articles_SubmitFeedback(articleId, request)`, and `queryClient.invalidateQueries({ queryKey: articleKeys.detail(articleId) })` — all stay exactly as they are; only the object literal passed into `new SubmitArticleFeedbackRequest({...})` changes.
- The other two tests in the `useSubmitArticleFeedbackMutation` describe block (409 and 500 paths) — they don't assert on the request body shape, so they need no edit and must keep passing.

**Do not touch:**
- `frontend/src/api/generated/api-client.ts` — already regenerated in the previous task; not touched here.

## Steps

- [ ] **Step 1: Confirm the test currently fails to compile / fails against the regenerated type**

```bash
cd frontend && npx tsc --noEmit -p tsconfig.json 2>&1 | grep -i "articleId" | head -20
```

Expected: a TypeScript error pointing at `frontend/src/api/hooks/useArticles.ts` around the `new SubmitArticleFeedbackRequest({ articleId, ... })` call, e.g. `Object literal may only specify known properties, and 'articleId' does not exist in type ...`. This confirms the regenerated client (previous task) has already made the current hook code invalid — the fix below is required, not optional.

- [ ] **Step 2: Remove `articleId` from the request body construction**

In `frontend/src/api/hooks/useArticles.ts`, current code (around line 213-227):

```typescript
export const useSubmitArticleFeedbackMutation = (articleId: string) => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (payload: SubmitArticleFeedbackPayload): Promise<SubmitArticleFeedbackResult> => {
      const client = getAuthenticatedApiClient();
      const request = new SubmitArticleFeedbackRequest({
        articleId,
        precisionScore: payload.precisionScore,
        styleScore: payload.styleScore,
        comment: payload.comment,
      });
```

Change to:

```typescript
export const useSubmitArticleFeedbackMutation = (articleId: string) => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (payload: SubmitArticleFeedbackPayload): Promise<SubmitArticleFeedbackResult> => {
      const client = getAuthenticatedApiClient();
      const request = new SubmitArticleFeedbackRequest({
        precisionScore: payload.precisionScore,
        styleScore: payload.styleScore,
        comment: payload.comment,
      });
```

(Delete only the `articleId,` line inside the object literal. The function signature `useSubmitArticleFeedbackMutation(articleId: string)`, the `client.articles_SubmitFeedback(articleId, request)` call below it, and everything else in the file are unchanged — `articleId` is still in scope and still used as the route argument.)

- [ ] **Step 3: Confirm the TypeScript error is gone**

```bash
cd frontend && npx tsc --noEmit -p tsconfig.json 2>&1 | grep -i "articleId"
```

Expected: no output.

- [ ] **Step 4: Run the existing hook test suite and confirm the known assertion fails**

```bash
cd frontend && npx react-scripts test src/api/hooks/__tests__/useArticles.test.ts --watchAll=false
```

Expected: the suite reports a failure in `useSubmitArticleFeedbackMutation > resolves with parsed body on 2xx (typed generated response with success: true)`, specifically the `toHaveBeenCalledWith` assertion, because the mocked call no longer receives an `articleId` key in the body object — `objectContaining` fails to match since the actual received object at that key is `undefined`/absent while the expectation still requires `articleId: 'article-1'`. The other two tests in the same describe block still pass.

- [ ] **Step 5: Update the test assertion to drop `articleId` from the expected body shape**

In `frontend/src/api/hooks/__tests__/useArticles.test.ts`, current code (lines 321-329):

```typescript
    expect(mockArticlesSubmitFeedback).toHaveBeenCalledWith(
      'article-1',
      expect.objectContaining({
        articleId: 'article-1',
        precisionScore: 4,
        styleScore: 5,
        comment: 'great',
      }),
    );
```

Change to:

```typescript
    expect(mockArticlesSubmitFeedback).toHaveBeenCalledWith(
      'article-1',
      expect.objectContaining({
        precisionScore: 4,
        styleScore: 5,
        comment: 'great',
      }),
    );
```

(Delete only the `articleId: 'article-1',` line. The first positional argument `'article-1'` — the route-level id passed to `client.articles_SubmitFeedback` — is unchanged and stays asserted.)

- [ ] **Step 6: Run the hook test suite again and confirm it passes**

```bash
cd frontend && npx react-scripts test src/api/hooks/__tests__/useArticles.test.ts --watchAll=false
```

Expected: `Tests: 6 passed, 6 total` (3 tests in `useArticleFeedbackListQuery mapping`, 2 in `useArticleFeedbackListQuery parameter passing`... — run `grep -c "it(" frontend/src/api/hooks/__tests__/useArticles.test.ts` beforehand if the exact count is needed; the key expectation is 0 failed, 0 skipped, all `useSubmitArticleFeedbackMutation` tests (3) green).

- [ ] **Step 7: Run the full frontend test suite**

```bash
cd frontend && npm test -- --watchAll=false
```

Expected: all suites pass, no new failures introduced elsewhere.

- [ ] **Step 8: Run the frontend build and lint**

```bash
cd frontend && npm run build
cd frontend && npm run lint
```

Expected: both succeed with no errors (build: no TypeScript compile errors; lint: no new lint errors).

- [ ] **Step 9: Commit**

```bash
git add frontend/src/api/hooks/useArticles.ts frontend/src/api/hooks/__tests__/useArticles.test.ts
git commit -m "fix(article): stop sending articleId in SubmitArticleFeedback request body

The regenerated SubmitArticleFeedbackRequest no longer has an
articleId field (backend now [JsonIgnore]s it), so
useSubmitArticleFeedbackMutation no longer constructs it into the
body. The route-level articleId argument to
client.articles_SubmitFeedback is unchanged. Updates the one existing
test assertion that expected articleId in the mocked request body.

Refs #3989"
```

---
