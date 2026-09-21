# Implementation: fix-consumer-using-and-validate

## What was implemented
Removed the stale `using Anela.Heblo.Application.Features.Marketing.UseCases.ImportFromOutlook;`
directive from `MarketingCalendarSyncService.cs`. This import is no longer needed
because `OutlookEventImportMapper` now lives in
`Anela.Heblo.Application.Features.Marketing.Services` (relocated by the prior task,
`relocate-outlook-mapper-to-services`) — the same namespace as
`MarketingCalendarSyncService` itself, so the type resolves without any `using`.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Marketing/Services/MarketingCalendarSyncService.cs` — removed the now-stale `using` line (previously line 8). No other lines touched; the 3 call sites to `OutlookEventImportMapper.HasChanges` / `.ApplyChanges` / `.BuildAction` needed no edit.

## Tests
No new tests were required or written (per spec FR-5, a full-repo grep had already
confirmed no test references the mapper's old namespace). Ran the existing Marketing
regression suite to confirm nothing broke:
- `ImportFromOutlookHandlerTests`
- `MarketingCalendarSyncServiceTests`
- `OutlookCalendarSyncServiceTests`
- `OutlookCalendarSyncServiceTokenTests`

## How to verify
1. `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — 0 errors.
2. `dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --verify-no-changes` — no violations.
3. `dotnet test --filter "FullyQualifiedName~ImportFromOutlookHandlerTests|FullyQualifiedName~MarketingCalendarSyncServiceTests|FullyQualifiedName~OutlookCalendarSyncServiceTests|FullyQualifiedName~OutlookCalendarSyncServiceTokenTests"` — `Passed! - Failed: 0, Passed: 54, Skipped: 0, Total: 54`.
4. `dotnet build Anela.Heblo.sln` (solution file is at the repo root, not under `backend/` — the task context's path was slightly stale) — 0 errors, confirming no other project references the old `UseCases.ImportFromOutlook` namespace for this type.

## Notes
The task context's Step 6 command (`dotnet build backend/Anela.Heblo.sln`) pointed at a
path that doesn't exist; the solution file is actually at the repo root
(`Anela.Heblo.sln`). Ran the build from there instead — same verification intent,
correct path. No other deviations.

## PR Summary
Removed the stale `using Anela.Heblo.Application.Features.Marketing.UseCases.ImportFromOutlook;` directive from `MarketingCalendarSyncService.cs`, completing the cleanup started by relocating `OutlookEventImportMapper` into `Features.Marketing.Services`. The mapper now shares a namespace with its consumer, so the import was unused. Build, format, the Marketing regression suite (54/54 passing), and a full solution build all confirm the change is safe.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Marketing/Services/MarketingCalendarSyncService.cs` — removed unused `using` directive

## Status
DONE
