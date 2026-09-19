# Implementation: update-leaflet-consumers

## What was implemented
Fixed the two remaining broken references left after `LeafletDocumentSummary` was moved to
`Anela.Heblo.Application.Features.Leaflet.Contracts` in a prior task. Replaced the stale
`using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocuments;` with
`using Anela.Heblo.Application.Features.Leaflet.Contracts;` in `UploadLeafletResponse.cs` and
`UploadLeafletHandler.cs` (neither file used anything else from the `GetLeafletDocuments`
namespace, so the using was replaced, not added alongside). Added the new `Contracts` using to
`LeafletControllerTests.cs` while keeping the existing `GetLeafletDocuments` using intact, since
that test file still references `GetLeafletDocumentsRequest`/`GetLeafletDocumentsResponse`
elsewhere. No method bodies, property sets, or DTO shapes were changed — this is a pure
namespace/using fix.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletResponse.cs` — replaced stale using with `Anela.Heblo.Application.Features.Leaflet.Contracts`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletHandler.cs` — replaced stale using with `Anela.Heblo.Application.Features.Leaflet.Contracts`
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletControllerTests.cs` — added `using Anela.Heblo.Application.Features.Leaflet.Contracts;` after the `Anela.Heblo.API.Controllers` using, keeping the pre-existing `GetLeafletDocuments` using (still needed for other types in the file)

## Tests
Built `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` (transitively builds Application + API projects): **0 errors**, 240 pre-existing warnings (unrelated to this change).

Ran `dotnet test ... --filter "FullyQualifiedName~Features.Leaflet"`:
```
Passed!  - Failed:     0, Passed:   154, Skipped:     3, Total:   157, Duration: 28 s - Anela.Heblo.Tests.dll (net8.0)
```
The 3 skipped tests are `LeafletDocumentRepositoryTests` (pre-existing DB-dependent skips, unrelated to this change).

`dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --verify-no-changes --include <the 3 touched files>` completed with exit code 0 (no formatting violations).

## How to verify
```
cd /Users/pajgrtondrej/Work/GitHub/worktrees/feature-4105-Arch-Review-Leaflet-Leafletdocumentsummary-Defined
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -p:UseSharedCompilation=false
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~Features.Leaflet"
grep -rn "LeafletDocumentSummary" backend/src backend/test --include="*.cs"
```

## Notes
No deviations from the task spec. `UploadLeafletHandler.cs` did not reference anything else from
the `GetLeafletDocuments` namespace (verified by reading the full file before editing), so the
using was safely replaced rather than added alongside, per Step 2's own guidance. `artifacts/feat-4105/state.json`
shows as modified in git status but was not touched by me in this task — left unstaged per
instructions (orchestrator manages artifacts).

## PR Summary
This is a pure mechanical follow-up to the `move-summary-to-contracts` task. That task moved
`LeafletDocumentSummary` into its own file under `Features/Leaflet/Contracts/` but left two
consumers (`UploadLeafletResponse.cs`, `UploadLeafletHandler.cs`) and one test file
(`LeafletControllerTests.cs`) pointing at the old namespace, breaking the build with 2 CS0246
errors. This task repoints those three files at the new `Contracts` namespace with no behavioral
change. The backend now builds with zero errors and all 157 Leaflet tests pass (154 passed, 3
pre-existing skips, 0 failures).

### Changes
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletResponse.cs` — using fix
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletHandler.cs` — using fix
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletControllerTests.cs` — added using, kept existing one

## Status
DONE
