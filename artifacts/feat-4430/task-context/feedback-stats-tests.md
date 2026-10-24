### task: feedback-stats-tests
**Files:** Modify the same test file.

- [ ] Empty table: `GetFeedbackStatsAsync` equals `new ArticleFeedbackStats(0, 0, null, null)`.
- [ ] Populated (fixture A-E with precision 4,2 and style 5,3): Total 5, WithFeedback 3, averages computed over non-null values only.
- [ ] If InMemory cannot translate `GroupBy(_ => 1)`, switch only these tests to a Sqlite in-memory connection and note it.
- [ ] Run filtered tests, then `dotnet build` and `dotnet format --verify-no-changes` on the test project.
- [ ] Commit: `test(article): cover feedback stats`.
