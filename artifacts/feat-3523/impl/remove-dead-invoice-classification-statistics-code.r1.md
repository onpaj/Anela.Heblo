# Implementation: remove-dead-invoice-classification-statistics-code

## What was implemented
Removed orphaned dead code from the InvoiceClassification module: the `GetStatisticsAsync`
repository method (never declared on the interface, never wired to any use-case or controller),
its supporting domain types (`ClassificationStatistics`, `RuleUsageStatistic`), their DTOs
(`ClassificationStatisticsDto`, `RuleUsageStatisticDto`), the two AutoMapper `CreateMap` registrations
for them, and the unused `statistics` entry in the frontend `CLASSIFICATION_QUERY_KEYS` constant.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/InvoiceClassificationMappingProfile.cs` — removed the two `CreateMap<ClassificationStatistics, ClassificationStatisticsDto>()` / `CreateMap<RuleUsageStatistic, RuleUsageStatisticDto>()` lines.
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ClassificationStatistics.cs` — deleted (whole file).
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/RuleUsageStatistic.cs` — deleted (whole file).
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Contracts/ClassificationStatisticsDto.cs` — deleted (whole file).
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Contracts/RuleUsageStatisticDto.cs` — deleted (whole file).
- `backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationHistoryRepository.cs` — removed the `GetStatisticsAsync` method (lines 81–121), leaving `AddAsync`, `GetHistoryAsync`, `GetHistoryByInvoiceIdAsync`, `GetPagedHistoryAsync` untouched.
- `frontend/src/api/hooks/useInvoiceClassification.ts` — removed the unused `statistics: ['invoice-classification', 'statistics'] as const,` entry from `CLASSIFICATION_QUERY_KEYS`.

`IClassificationHistoryRepository.cs`, `InvoiceClassificationModule.cs`, `ClassificationStats.tsx`,
`InvoiceClassificationPage.tsx`, and any EF migration/configuration were left untouched, as required.

## Tests
- Zero-match grep across `backend/src backend/test frontend/src` for `GetStatisticsAsync|ClassificationStatistics|RuleUsageStatistic|ClassificationStatisticsDto|RuleUsageStatisticDto` — confirmed no output.
- `dotnet build Anela.Heblo.sln` — succeeded, 0 errors, 254 warnings (all pre-existing, none in the two edited backend files).
- `dotnet format Anela.Heblo.sln --verify-no-changes` — passed with no diffs.
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceClassification"` — 86/86 passed.
- Full backend suite (`dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`) — 5414 passed, 4 skipped, 64 failed. All 64 failures are pre-existing `Docker is either not running or misconfigured` Testcontainers errors (Postgres-backed integration tests), unrelated to this change and reproducible identically on the pre-change tree (verified via `git stash`). No InvoiceClassification test failures.
- `npm run build` (frontend) — compiled successfully.
- `npm run lint` (frontend) — 162 problems (148 errors, 14 warnings), identical count/content to the pre-change baseline (verified via `git stash` + rerun), all in unrelated test files (Testing Library rule violations). `npx eslint src/api/hooks/useInvoiceClassification.ts` on the touched file alone — clean, no output.
- `git diff --stat` against the parent commit — exactly the 7 listed files (4 deletions, 3 edits), no other file touched.

Note: `frontend/node_modules` did not exist in this worktree; had to run `npm install --legacy-peer-deps`
(plain `npm install` hits a pre-existing `react-i18next` peer-dependency conflict with `typescript@4.9.5`,
unrelated to this change) before `npm run build` / `npm run lint` could execute.

## How to verify
1. `grep -rn -E "GetStatisticsAsync|ClassificationStatistics|RuleUsageStatistic|ClassificationStatisticsDto|RuleUsageStatisticDto" backend/src backend/test frontend/src` → no output.
2. `cd backend && dotnet build Anela.Heblo.sln` → 0 errors.
3. `dotnet format Anela.Heblo.sln --verify-no-changes` → no diffs.
4. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceClassification"` → all pass.
5. `cd frontend && npm run build` → compiles successfully.
6. `npm run lint` → same pre-existing problem count as baseline, none in the touched file.
7. `git diff --stat HEAD~1` → only the 7 listed files.

## Notes
- Ran `npm install --legacy-peer-deps` in the frontend worktree purely to obtain `node_modules` for
  local verification (no repo files were changed by this); the underlying `react-i18next`/`typescript`
  peer conflict predates this task and was not touched.
- No comments were added to any surviving file explaining the removal, per scope rules.
- `IClassificationHistoryRepository.cs` was left untouched as instructed — it never declared `GetStatisticsAsync`.

## PR Summary
Removes dead code identified by an architecture review in the InvoiceClassification module: a
`GetStatisticsAsync` repository method that was scaffolded but never wired into the interface,
any use-case, or the controller, plus its supporting domain types (`ClassificationStatistics`,
`RuleUsageStatistic`), DTOs (`ClassificationStatisticsDto`, `RuleUsageStatisticDto`), the two
now-orphaned AutoMapper registrations, and an unused frontend query-key constant
(`CLASSIFICATION_QUERY_KEYS.statistics`). Pure deletion — no behavior change, verified via full
backend build/format/test suite and frontend build/lint.

### Changes
- Deleted `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ClassificationStatistics.cs`
- Deleted `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/RuleUsageStatistic.cs`
- Deleted `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Contracts/ClassificationStatisticsDto.cs`
- Deleted `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Contracts/RuleUsageStatisticDto.cs`
- Edited `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/InvoiceClassificationMappingProfile.cs` (removed 2 `CreateMap` lines)
- Edited `backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationHistoryRepository.cs` (removed `GetStatisticsAsync` method)
- Edited `frontend/src/api/hooks/useInvoiceClassification.ts` (removed unused `statistics` query-key entry)

## Status
DONE
