# Remove unnecessary `as any` cast in useGetArticleQuery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove a stale `as any` cast and its `eslint-disable` comment from the `useGetArticleQuery` hook so the four affected properties are accessed through the now-typed `ArticleSourceDto`.

**Architecture:** Pure type-level cleanup in a single React Query hook. The generated NSwag client already declares `knowledgeBaseChunkId`, `confidence`, `excerpt`, and `validationNote` on `ArticleSourceDto`, so the cast is obsolete. Direct typed access on the existing `s: ArticleSourceDto` parameter replaces the `raw` alias. `??` coalescing is preserved exactly so the view-model contract (`null` vs `undefined`) does not change.

**Tech Stack:** TypeScript, React, TanStack Query, NSwag-generated client.

---

## File Structure

This change touches exactly one file:

- **Modify:** `frontend/src/api/hooks/useArticles.ts` (lines 173-185, the inner `.map(s => …)` callback of `useGetArticleQuery`)

No new files. No new tests (NFR-3 — pure refactor with zero behavioral delta; existing test suite covers the hook).

---

## Task 1: Remove `as any` cast and `eslint-disable` directive in `useGetArticleQuery`

**Files:**
- Modify: `frontend/src/api/hooks/useArticles.ts:173-185`

- [ ] **Step 1: Confirm starting state**

Run: `grep -n "as any" frontend/src/api/hooks/useArticles.ts`
Expected output (exactly one line):

```
175:          const raw = s as any;
```

Run: `grep -n "eslint-disable" frontend/src/api/hooks/useArticles.ts`
Expected output (exactly one line):

```
174:          // eslint-disable-next-line @typescript-eslint/no-explicit-any
```

If either grep returns more than one match, STOP — the plan assumes the cast and directive appear nowhere else in this file. Re-read the arch review before continuing.

- [ ] **Step 2: Apply the edit**

Replace the `sources` mapping block (lines 173-185) so that:
- Line 174 (the `eslint-disable-next-line @typescript-eslint/no-explicit-any` comment) is deleted.
- Line 175 (`const raw = s as any;`) is deleted.
- The four properties that previously read from `raw` (`knowledgeBaseChunkId`, `confidence`, `excerpt`, `validationNote`) read from `s` directly with the same `??` fallback.
- The other three properties (`title`, `url`, `type`) are unchanged.
- The inline `as string | null` / `as number | null` casts on the `raw.*` reads are dropped — `s` is already correctly typed.

**Before (lines 173-185):**

```typescript
        sources: (response.sources ?? []).map((s) => {
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
        }),
```

**After:**

```typescript
        sources: (response.sources ?? []).map((s) => ({
          title: s.title ?? '',
          url: s.url ?? null,
          type: s.type ?? '',
          knowledgeBaseChunkId: s.knowledgeBaseChunkId ?? null,
          confidence: s.confidence ?? null,
          excerpt: s.excerpt ?? null,
          validationNote: s.validationNote ?? null,
        })),
```

Notes on the after-state:
- The arrow function body is now an implicit-return parenthesized object (the previous `{ const raw …; return {…}; }` form was only needed because of the cast). This is the same shape as every other mapping in this hook (e.g. the top-level `response.* ?? …` block at lines 158-189) and matches the project's existing style for trivial DTO→view-model maps.
- Every `??` fallback is preserved verbatim. `s.knowledgeBaseChunkId` is `string | undefined` in the generated client, so `s.knowledgeBaseChunkId ?? null` yields `string | null` — exactly the type declared by the local `ArticleSource` interface (lines 23-31).
- No `any`, no `eslint-disable`, no inline type assertions remain in the block.

- [ ] **Step 3: Verify the cast and directive are gone**

Run: `grep -n "as any" frontend/src/api/hooks/useArticles.ts`
Expected output: (empty — no matches)

Run: `grep -n "eslint-disable" frontend/src/api/hooks/useArticles.ts`
Expected output: (empty — no matches)

If either grep still returns a match, the edit was incomplete. Re-open the file at lines 173-185 and finish the replacement before continuing.

- [ ] **Step 4: Run the TypeScript build**

Run: `cd frontend && npm run build`
Expected: build succeeds with no new TypeScript errors. In particular, no errors mentioning `ArticleSourceDto`, `knowledgeBaseChunkId`, `confidence`, `excerpt`, or `validationNote`.

If the build fails with a "property does not exist on type 'ArticleSourceDto'" error for any of those four properties, the generated client is older than the arch review assumed. STOP and report — do NOT re-add the cast. The fix in that case is to regenerate the client, which is a separate task outside this plan's scope.

- [ ] **Step 5: Run the linter**

Run: `cd frontend && npm run lint`
Expected: passes with no new errors or warnings. Specifically, no `@typescript-eslint/no-explicit-any` warnings on `useArticles.ts` (because no `any` remains) and no "unused eslint-disable directive" warnings (because the directive has been deleted along with the code it covered).

- [ ] **Step 6: Run the existing unit tests for the hook**

Run: `cd frontend && npm test -- useArticles`
Expected: all tests in `frontend/src/api/hooks/__tests__/useArticles.test.ts` pass. No tests need to be added or modified — the returned object shape, keys, types, and null semantics are unchanged.

If a test fails, do not modify the test. Re-read the diff against the "After" code block in Step 2 and confirm the mapping is exactly as specified. A failure here means runtime behavior shifted, which violates FR-3.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/api/hooks/useArticles.ts
git commit -m "refactor: remove obsolete as any cast in useGetArticleQuery sources mapping"
```

The commit message uses `refactor` (not `fix` or `feat`) because there is no behavior change and no bug being fixed — this is a type-safety cleanup tracked by the daily architecture review routine.

---

## Self-Review

**Spec coverage:**
- FR-1 (remove cast, direct typed access): Task 1 Step 2 — `const raw = s as any;` deleted, all seven returned properties read from `s` directly, no `any` remains in the block. ✓
- FR-2 (remove `eslint-disable` directive): Task 1 Step 2 — the `// eslint-disable-next-line @typescript-eslint/no-explicit-any` comment is deleted; Step 5 verifies lint passes. ✓
- FR-3 (preserve null/undefined semantics): Task 1 Step 2 — every `??` fallback is reproduced verbatim (`?? ''` for `title`/`type`, `?? null` for the five nullable fields); Step 6 runs the existing tests to catch any runtime regression. ✓
- NFR-1 (type safety, no `any`): Task 1 Step 3 grep confirms no `any` remains. ✓
- NFR-2 (build & lint): Task 1 Step 4 (`npm run build`) and Step 5 (`npm run lint`). ✓
- NFR-3 (test coverage unchanged): Task 1 Step 6 runs existing tests; no new tests added. ✓
- Arch-review reinforcement 1 ("verify with grep before and after"): Task 1 Step 1 (before) and Step 3 (after). ✓
- Arch-review reinforcement 2 ("run `npm run build`, not just `tsc --noEmit`"): Task 1 Step 4. ✓

**Placeholder scan:** No "TBD", "TODO", "appropriate error handling", "similar to Task N", or other placeholders. Every step shows the exact command or the exact code.

**Type consistency:** The "After" code block in Task 1 Step 2 uses the same property names (`knowledgeBaseChunkId`, `confidence`, `excerpt`, `validationNote`), the same fallback values (`null`, `''`), and the same arrow-function shape as the surrounding mapping. No internal naming drift.
