## Module
InvoiceClassification

## Finding
`ClassificationHistoryRepository` (`backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationHistoryRepository.cs`, lines 81–121) implements a `GetStatisticsAsync(DateTime? fromDate, DateTime? toDate)` method (40 lines including multi-query aggregation and `GroupBy`).

`IClassificationHistoryRepository` (`backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IClassificationHistoryRepository.cs`) does not declare this method. All consuming code accesses the repository via the interface, so `GetStatisticsAsync` is unreachable through the abstraction.

A codebase-wide search confirms the method is never called anywhere. The corresponding types (`ClassificationStatistics`, `RuleUsageStatistic` in Domain; `ClassificationStatisticsDto`, `RuleUsageStatisticDto` in `Contracts/`) appear to have been scaffolded for this feature but there is no use case handler, no controller endpoint, and no frontend hook consuming statistics.

## Why it matters
- Dead code in the persistence layer is invisible to maintainers — developers see `IClassificationHistoryRepository` has no statistics method and conclude the feature doesn't exist, not that it's hidden in the concrete class.
- The orphaned domain types (`ClassificationStatistics`, `RuleUsageStatistic`) and contract types (`ClassificationStatisticsDto`, `RuleUsageStatisticDto`) add noise to the contract surface, appear in the generated OpenAPI client, and mislead readers about available capabilities.
- Any future maintainer adding a statistics feature will duplicate work already sitting here unused.

## Suggested fix
Two options — pick one:

**Option A (complete the feature):** Add `Task GetStatisticsAsync(DateTime? fromDate, DateTime? toDate)` to `IClassificationHistoryRepository`, create a `GetClassificationStatistics` use case (handler + request + response), add a `GET /api/invoice-classification/statistics` controller action, and add a frontend hook.

**Option B (remove dead code):** Delete `GetStatisticsAsync` from `ClassificationHistoryRepository`, and delete the orphaned `ClassificationStatistics`, `RuleUsageStatistic`, `ClassificationStatisticsDto`, and `RuleUsageStatisticDto` types.

---
_Filed by daily arch-review routine on 2026-07-07._
