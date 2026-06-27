# Code Review: move-dtos-to-new-module

## Summary
All three DTOs (`RefreshTaskDto`, `RefreshTaskExecutionLogDto`, `RefreshTaskStatusDto`) have been created under `BackgroundRefresh/Contracts/` with the correct namespace `Anela.Heblo.Application.Features.BackgroundRefresh.Contracts`. The `BackgroundJobs/Contracts/` folder retains exactly the three permitted files (`RecurringJobDto.cs`, `UpdateJobCronRequestBody.cs`, `UpdateJobStatusRequestBody.cs`) and no others. DTO fields, types, access modifiers, and class declarations are unchanged from their original form.

## Review Result: PASS

### task: move-dtos-to-new-module
**Status:** PASS

## Overall Notes
- Namespace declarations in all three new files are exactly `Anela.Heblo.Application.Features.BackgroundRefresh.Contracts` — correct.
- No stray files remain in `BackgroundJobs/Contracts/` beyond the three that were meant to stay.
- No content drift detected: property names, types, `required` modifiers, and `init`/`set` accessors are all preserved verbatim.
- The move is a clean surgical operation with no collateral changes.
