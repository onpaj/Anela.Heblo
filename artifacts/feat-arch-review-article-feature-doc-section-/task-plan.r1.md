Plan saved to `docs/superpowers/plans/2026-06-14-article-generation-doc-auth-fix.md`.

The plan implements the **arch-review's amended spec**, not the stale original. Key decisions baked in:

- The role to document is `marketing.article.write` for `POST /generate` and `marketing.article.read` for the read endpoints and feedback POST — not `marketing_reader` (the spec's original target, which no longer exists).
- The snippet shows `[FeatureAuthorize(...)]` verbatim from `ArticlesController.cs:15,28`, with no reference to the deleted `AuthorizationConstants` class.
- The fix covers both **section 7 line 143** and **section 14** (the arch-review extended FR-4 to include the section 7 line).
- Task 1 is a hard pre-flight gate: if any quoted source-of-truth reference has drifted further, the implementer must halt rather than make the doc wrong in yet another direction.
- The "test" for this docs-only change is `grep -nE 'marketing_(writer|reader)|article_generator|AuthorizationConstants' docs/features/article-generation.md` returning zero matches (Task 4).

Five tasks total, each with checkbox steps: pre-flight verification → fix section 7 → fix section 14 → document-wide sweep → single conventional commit.