### task: update-api-client-generation-docs-snippet

## Goal
Update the canonical example in `docs/development/api-client-generation.md` — which uses this exact endpoint as its documented illustration of the "business outcome as HTTP status" pattern — so its `SubmitArticleFeedbackRequest` constructor snippet matches the post-fix shape (no `articleId`).

## Files to change

**Edit:**
- `docs/development/api-client-generation.md`

**Verify only, no change expected:**
- The surrounding explanatory prose in that same section (the description of the business-outcome-as-HTTP-status pattern, the `[ProducesResponseType]` C# snippet above it, the `try/catch`/409-handling explanation below it, and the "escape hatch" section that follows) — none of it changes; only the one object-literal line changes.

**Do not touch:**
- Any other doc file under `docs/` — out of scope.

## Steps

- [ ] **Step 1: Locate the exact line to change**

```bash
grep -n "new SubmitArticleFeedbackRequest" docs/development/api-client-generation.md
```

Expected output: `170:const request = new SubmitArticleFeedbackRequest({ articleId, precisionScore, styleScore, comment });`

- [ ] **Step 2: Update the snippet**

Current (line 170, within the fenced ```typescript block starting at line 166):

```typescript
const request = new SubmitArticleFeedbackRequest({ articleId, precisionScore, styleScore, comment });
```

Change to:

```typescript
const request = new SubmitArticleFeedbackRequest({ precisionScore, styleScore, comment });
```

(Only this line changes — delete `articleId, ` from the object literal. The `import` line above it, the `const client = getAuthenticatedApiClient();` line, and the `try/catch` block below it are untouched.)

- [ ] **Step 3: Confirm no other reference to the old shape remains in the file**

```bash
grep -n "articleId" docs/development/api-client-generation.md
```

Expected: no output (no matches) — the doc no longer mentions `articleId` anywhere, since the code sample was its only occurrence.

- [ ] **Step 4: Commit**

```bash
git add docs/development/api-client-generation.md
git commit -m "docs(api-client-generation): drop articleId from SubmitArticleFeedbackRequest example

Keeps the canonical business-outcome-as-HTTP-status example in sync
with the actual generated client shape after ArticleId was excluded
from the request body contract.

Refs #3989"
```

## Acceptance criteria (full feature)
- `SubmitArticleFeedbackRequest.ArticleId` carries `[JsonIgnore]`; `PrecisionScore`, `StyleScore`, `Comment` are unaffected (FR-1).
- A JSON body with an `articleId` key deserializes with `ArticleId == Guid.Empty` (untouched by the body), proven by `SubmitArticleFeedbackRequestSerializationTests` (FR-1).
- A JSON body without `articleId` behaves identically (FR-1).
- `ArticlesController.SubmitFeedback` and `SubmitArticleFeedbackHandler` are unmodified (FR-1).
- `frontend/src/api/generated/api-client.ts`'s `SubmitArticleFeedbackRequest`/`ISubmitArticleFeedbackRequest` no longer declare `articleId`, confirmed via regeneration + scoped diff, no unrelated type changed (FR-2).
- `useSubmitArticleFeedbackMutation` no longer constructs `articleId` into the request body; `client.articles_SubmitFeedback(articleId, request)`'s route argument is unchanged (FR-3).
- `frontend` builds and type-checks cleanly (FR-3).
- `useArticles.test.ts`'s `toHaveBeenCalledWith` assertion no longer expects `articleId` in the body (FR-3).
- `docs/development/api-client-generation.md`'s example snippet matches the post-fix shape; surrounding prose is untouched (FR-4).
