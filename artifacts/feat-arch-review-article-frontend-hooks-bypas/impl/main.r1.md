Implementation complete. Here's a summary:

---

**All 4 tasks completed and reviewed (spec + quality) with 4 implementation commits:**

| Task | Commit | Status |
|------|--------|--------|
| `useArticleTraceQuery` → `articles_GetTrace` | `a171fbd1` | ✅ |
| `useArticleFeedbackListQuery` → `articles_FeedbackList` | `631b195c` | ✅ |
| Remove stale `as any` casts in `useGetArticleQuery` | `852d55c8` | ✅ |
| Add dated TODO above `useSubmitArticleFeedbackMutation` raw-fetch | `3f66ca25` | ✅ |

**What changed:**
- `frontend/src/api/hooks/useArticleTrace.ts` — raw fetch replaced with `client.articles_GetTrace(id)`, explicit per-field step projection
- `frontend/src/api/hooks/useArticles.ts` — `useArticleFeedbackListQuery` uses `client.articles_FeedbackList(...)` with full DTO projection; `useGetArticleQuery` has three stale `(response as any)` casts removed; `useSubmitArticleFeedbackMutation` gets a single dated `// TODO` comment

**Concern:** Pre-existing Jest/Babel config issue prevents test execution in this environment (`import type` syntax errors affecting all 29 suites on this branch, not just modified files). TypeScript strict build and lint both pass cleanly. No consumer files required modification.