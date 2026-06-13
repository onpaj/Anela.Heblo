Plan saved to `artifacts/feat-arch-review-manufacture-productionactivi/plan.r1.md`.

**Summary:** 5 tasks, ~24 steps, all bite-sized with exact paths, complete code blocks, and verifiable command outputs.

- **Task 1** — adds `Microsoft.Extensions.TimeProvider.Testing` 8.0.1 to the test csproj (arch-review amendment #1).
- **Task 2** — production-code refactor + lockstep test-fixture migration; keeps all 12 existing assertions passing.
- **Task 3** — two boundary tests for `IsInActiveProduction` pinning inclusive equality (`m.Date >= cutoffDate`).
- **Task 4** — two boundary tests for `CalculateAverageProductionFrequency` (one record at the boundary is included → 15-day interval; one tick before → `PositiveInfinity`).
- **Task 5** — `dotnet format` + full backend build + Manufacture test slice + grep gate for any surviving `DateTime.UtcNow`.

Each FR/NFR and arch-review amendment is mapped to a specific task step in the spec-coverage cross-check at the end of the plan.