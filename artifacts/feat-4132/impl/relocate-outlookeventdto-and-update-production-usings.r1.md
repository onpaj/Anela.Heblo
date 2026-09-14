# Implementation: relocate-outlookeventdto-and-update-production-usings

## What was implemented
Moved `OutlookEventDto` (and its two nested Graph-shape classes `GraphEventBody`,
`GraphEventDateTime`) from `Anela.Heblo.Application.Features.Marketing.Services`
to `Anela.Heblo.Application.Features.Marketing.Infrastructure`, using `git mv` to
preserve file history, and changed only the `namespace` line inside the moved
file. Updated every production consumer's `using` directives to add the new
`Infrastructure` namespace (keeping the existing `Services` using wherever the
file still needs other `Services` symbols), and swapped it outright in the one
file that used only `OutlookEventDto` from `Services`.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Marketing/Infrastructure/OutlookEventDto.cs` — moved here via `git mv` (rename tracked, 95% similarity); namespace changed to `...Marketing.Infrastructure`.
- `backend/src/Anela.Heblo.Application/Features/Marketing/Services/IOutlookCalendarSync.cs` — added `using ...Marketing.Infrastructure;`.
- `backend/src/Anela.Heblo.Application/Features/Marketing/Services/NoOpOutlookCalendarSync.cs` — added `using ...Marketing.Infrastructure;`.
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/ImportFromOutlook/OutlookEventImportMapper.cs` — added `using ...Marketing.Infrastructure;` (kept `Services` for `SyncActor`).
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookCalendarSyncService.cs` — added `using ...Marketing.Infrastructure;` (kept `Services` for `IOutlookCalendarSync`, `IMarketingCategoryMapper`, `OutlookCalendarSyncException`).
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookInternalDtos.cs` — **swapped** the `using` from `...Marketing.Services` to `...Marketing.Infrastructure` (this file used only `OutlookEventDto`, no other `Services` symbol).
- `backend/src/Anela.Heblo.Application/Features/Marketing/Services/MarketingCalendarSyncService.cs` — **not in the task-context's file list**; added `using ...Marketing.Infrastructure;`. This file lives in the `Services` namespace itself, the same namespace `OutlookEventDto` used to live in, so it previously needed no explicit using for it at all. The task-context's inventory missed this consumer; the Application project failed to build (`CS0246: The type or namespace name 'OutlookEventDto' could not be found`, 8 errors across 4 usage sites) until this file also got the new using. This is the smallest possible fix (one using line, no other change) and was required to meet the task's own stated acceptance criterion (`dotnet build` → `0 Error(s)`).

## Tests
No test files touched in this task — that is task 2 (`update-outlookeventdto-test-usings`), not yet started.

## How to verify
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj   # Build succeeded, 0 Error(s)
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Anela.Heblo.Adapters.Microsoft365.csproj  # Build succeeded, 0 Error(s)
grep -n "Marketing.Services" backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookInternalDtos.cs  # no matches
```
Both builds ran clean (pre-existing nullable-reference warnings only, unrelated to this change). `dotnet format` was run scoped to all seven touched production files across both projects and made no changes (already compliant).

Note: the test project (`Anela.Heblo.Tests`) is expected to still fail to build at
this point — `ImportFromOutlookHandlerTests.cs` and
`MarketingCalendarSyncServiceTests.cs` still reference `OutlookEventDto` without
the new using. That is task 2's job, not this one's; this task's own acceptance
criteria (steps 1–11 in the task-context) only cover the production build.

## Notes
- One deviation from the task-context's literal file list: `MarketingCalendarSyncService.cs` also needed the new using (see above) — a missed consumer in the plan's inventory, not a scope expansion. No other line in that file was touched.
- No `.csproj` file was edited — the move is a namespace-only change within the same project (`Anela.Heblo.Application`), consistent with FR-1 in `spec.r1.md`.
- No behavior changes: every edit is either a `git mv` + namespace line, or a `using` addition/swap. No method body, signature, or logic changed anywhere.

## PR Summary
Moved `OutlookEventDto` (a Microsoft Graph wire-format DTO used only by the
Outlook calendar sync adapter and its Application-layer consumers) out of the
`Marketing.Services` namespace into `Marketing.Infrastructure`, where it
belongs alongside other external-integration shapes, per arch-review finding
#4132. Updated every production file that references it — five files gain a
new `using`, one file's `using` is swapped outright (it referenced nothing
else from `Services`), and one file the original plan's consumer inventory
missed (`MarketingCalendarSyncService.cs`, which is itself in the `Services`
namespace and so never needed an explicit using before) also picked up the
new using once the build revealed the gap.

### Changes
- `Infrastructure/OutlookEventDto.cs` (moved, namespace changed)
- `Services/IOutlookCalendarSync.cs`, `Services/NoOpOutlookCalendarSync.cs`, `Services/MarketingCalendarSyncService.cs`, `UseCases/ImportFromOutlook/OutlookEventImportMapper.cs` (using added)
- `Adapters/.../OutlookCalendarSyncService.cs` (using added), `Adapters/.../OutlookInternalDtos.cs` (using swapped)
