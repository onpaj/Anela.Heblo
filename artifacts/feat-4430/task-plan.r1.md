# Implementation Plan: ArticleRepository feedback query tests

**Goal:** Cover the tri-state filter, requestedBy filter, sortBy switch, paging and stats in `ArticleRepository`.
**Architecture:** One xUnit class, EF InMemory `ApplicationDbContext`, no production changes.
**Tech stack:** xUnit, FluentAssertions, EF Core InMemory 8.0.8.

### task: feedback-filter-tests
**Files:** Create `backend/test/Anela.Heblo.Tests/Article/Persistence/ArticleRepositoryFeedbackQueryTests.cs`

- [ ] Create the class (alias `DomainArticle = Anela.Heblo.Domain.Features.Article.Article`; `UseInMemoryDatabase(Guid.NewGuid().ToString())`; `IDisposable`) with a seed helper and fixture set A-E from design.r1.md.
- [ ] Tests: `hasFeedback=null` returns all 5; `true` returns {A,B,C}; `false` returns {D,E} (partial B/C excluded); `requestedBy` exact match returns {A,B,E}; null/whitespace requestedBy returns all; `hasFeedback=false` + requestedBy user2 returns {D}.
- [ ] Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ArticleRepositoryFeedbackQueryTests"` - expect pass.
- [ ] Commit: `test(article): cover feedback filters in ArticleRepository`.

### task: feedback-sort-paging-tests
**Files:** Modify the same test file.

- [ ] Tests (Theory or Facts): sortBy `PrecisionScore` asc/desc and `StyleScore` asc/desc over `hasFeedback=true` articles with distinct non-null scores; default sort (`CreatedAt` and unknown value `"Bogus"`) asc/desc by CreatedAt.
- [ ] Paging test: page 2, pageSize 2 over 5 articles returns items 3-4 in order, TotalCount stays 5.
- [ ] Run the filtered test command; expect pass.
- [ ] Commit: `test(article): cover feedback sort and paging`.

### task: feedback-stats-tests
**Files:** Modify the same test file.

- [ ] Empty table: `GetFeedbackStatsAsync` equals `new ArticleFeedbackStats(0, 0, null, null)`.
- [ ] Populated (fixture A-E with precision 4,2 and style 5,3): Total 5, WithFeedback 3, averages computed over non-null values only.
- [ ] If InMemory cannot translate `GroupBy(_ => 1)`, switch only these tests to a Sqlite in-memory connection and note it.
- [ ] Run filtered tests, then `dotnet build` and `dotnet format --verify-no-changes` on the test project.
- [ ] Commit: `test(article): cover feedback stats`.
