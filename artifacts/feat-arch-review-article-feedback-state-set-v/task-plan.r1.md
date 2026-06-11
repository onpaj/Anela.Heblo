Plan saved to `docs/superpowers/plans/2026-06-08-encapsulate-article-feedback-submission.md`.

**Self-review:**
- **Spec coverage:** FR-1 → Task 2; FR-2 → Task 3; FR-3 → Task 3 Step 2 + Task 4 Step 3; FR-4 → Task 1 (both `_AssignsAllThreeProperties` and `_NullComment_IsAllowed`). Arch-review amendments applied: test path `Domain/Article/ArticleTests.cs` and method placement after `MarkAsFailed`.
- **Placeholders:** none — every code block contains complete content, every command is exact.
- **Type consistency:** `SubmitFeedback(int precisionScore, int styleScore, string? comment)` and property names match across Tasks 1–3.

**Plan shape:** 4 tasks, TDD red→green→refactor, each step 2–5 minutes, three commits + optional format commit. Touches exactly two source files and one new test file — no DI, OpenAPI, migrations, or frontend.