Plan saved to `docs/superpowers/plans/2026-06-03-dataqualitystatustile-structured-error-logging.md`.

Three TDD-ordered tasks:
1. **Task 1 (RED)** — add `Mock<ILogger<DataQualityStatusTile>>` to the test fixture and a new `LoadDataAsync_RepositoryThrows_LogsErrorOnce` test that fails to compile because the constructor still takes one parameter.
2. **Task 2 (GREEN)** — inject `ILogger<DataQualityStatusTile>` (appended last per sibling convention), replace the bare `catch` with `catch (Exception ex)` + `_logger.LogError(ex, "Failed to load DataQuality status tile for {TestType}", DqtTestType.IssuedInvoiceComparison)`. Build, run the 4 tests, format, commit.
3. **Task 3** — full DataQuality test slice + final build + clean working tree check.

No `DataQualityModule.cs` edits needed (`RegisterTile<T>()` resolves the new param through DI automatically). Self-review confirms every spec FR/NFR and all three arch-review amendments map to concrete steps.