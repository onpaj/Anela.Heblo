# Implementation: relocate-outlook-mapper-to-services

## What was implemented
Moved `OutlookEventImportMapper` from
`Features/Marketing/UseCases/ImportFromOutlook/` to `Features/Marketing/Services/`,
using `git mv` to preserve history, and updated its namespace declaration and
`using` directives to match the new location. No method signatures, bodies, or
member ordering were changed.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookEventImportMapper.cs` — new location; namespace changed from `Anela.Heblo.Application.Features.Marketing.UseCases.ImportFromOutlook` to `Anela.Heblo.Application.Features.Marketing.Services`; removed the now-redundant `using Anela.Heblo.Application.Features.Marketing.Services;` (self-referential once the file's own namespace became `Services`).
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/ImportFromOutlook/OutlookEventImportMapper.cs` — deleted (via rename).

## Tests
No test changes required or made for this task — it is a pure structural move. `git status` confirms the change tracked as a rename with content modification (namespace/using only).

## How to verify
```bash
git show --stat HEAD~1 HEAD
git diff HEAD~2 HEAD -- backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookEventImportMapper.cs
```
Confirms: file exists only at the new path, namespace is `Anela.Heblo.Application.Features.Marketing.Services`, `internal static class OutlookEventImportMapper` unchanged, `HasChanges`/`ApplyChanges`/`BuildAction` signatures unchanged.

Note: `MarketingCalendarSyncService.cs` still has a stale
`using Anela.Heblo.Application.Features.Marketing.UseCases.ImportFromOutlook;`
line at this point — that consumer-side cleanup, plus the build/format/test
verification, is explicitly the scope of the next task
(`fix-consumer-using-and-validate`), which itself declares a dependency on this
task having completed first. This task's own acceptance criteria (git status
shows a rename with modifications, not a separate delete+add) are met.

## Notes
Followed the task-context steps exactly: read the original file first, moved it
with `git mv`, made only the two specified textual edits, and committed. No
other `using` lines, the class declaration, or any method body were touched.

## PR Summary
Moved `OutlookEventImportMapper` out of the `UseCases/ImportFromOutlook`
folder into `Services/`, fixing the inverted Services -> UseCases dependency
flagged in issue #4226. The file's namespace was updated to
`Anela.Heblo.Application.Features.Marketing.Services` and its now-redundant
self-referential `using` was removed. Pure structural move — no behavior
change, no signature change.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookEventImportMapper.cs` — moved here from `UseCases/ImportFromOutlook/`, namespace updated

## Status
DONE
