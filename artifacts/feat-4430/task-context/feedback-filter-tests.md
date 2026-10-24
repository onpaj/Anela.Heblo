### task: feedback-filter-tests
**Files:** Create `backend/test/Anela.Heblo.Tests/Article/Persistence/ArticleRepositoryFeedbackQueryTests.cs`

- [ ] Create the class (alias `DomainArticle = Anela.Heblo.Domain.Features.Article.Article`; `UseInMemoryDatabase(Guid.NewGuid().ToString())`; `IDisposable`) with a seed helper and fixture set A-E from design.r1.md.
- [ ] Tests: `hasFeedback=null` returns all 5; `true` returns {A,B,C}; `false` returns {D,E} (partial B/C excluded); `requestedBy` exact match returns {A,B,E}; null/whitespace requestedBy returns all; `hasFeedback=false` + requestedBy user2 returns {D}.
- [ ] Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ArticleRepositoryFeedbackQueryTests"` - expect pass.
- [ ] Commit: `test(article): cover feedback filters in ArticleRepository`.

