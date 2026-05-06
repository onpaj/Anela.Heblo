All 37 tasks are complete. Here's the final state:

**Backend (0 test failures, 2,932 passing, format clean):**
- `Article` domain: `PrecisionScore`, `StyleScore`, `FeedbackComment` fields + EF migration + filtered index
- `SubmitArticleFeedback` handler: ownership check by Name, 422/409/403/404 guards
- `GetArticleFeedbackList` handler: parallel `Task.WhenAll(paged, stats)`, sort/page allowlists
- `ArticleSource` enriched during generation: `KnowledgeBaseChunkId`, `Confidence`, `Excerpt`, `ValidationNote`
- `GET /api/articles/{id}` returns new source and feedback fields
- `POST /api/articles/{id}/feedback` and `GET /api/articles/feedback/list` endpoints

**Frontend (946 tests passing, build clean, lint clean):**
- `useArticles.ts`: extended `ArticleSource` + `ArticleDetail` interfaces, new hooks (`useSubmitArticleFeedbackMutation(articleId)`, `useArticleFeedbackListQuery`)
- `ScoreRow` + `RagFeedbackForm` shared components (6 tests)
- `KnowledgeBaseSearchAskTab` refactored to use `RagFeedbackForm` (no duplicate logic)
- `ArticleSourceList`: web sources → anchors, KB sources with chunkId → click-to-open `ChunkDetailModal`, orphan KB → plain text (5 tests)
- `ArticleFeedbackSection`: form when unrated, read-only summary when rated, hidden for non-Generated articles (4 tests)
- `ArticleDetail` wired up with both new components

**Manual QA (Task 37)** requires you to run `dotnet ef database update` and test the flow in the running app.