# Implementation: replace-loops-with-bulk-replace-calls

## What was implemented
Replaced the two per-item loops in `CreateMarketingActionHandler.Handle` (product association via `AssociateWithProduct` and folder linking via `LinkToFolder`, each guarded by a manual null/empty check and manual `.Distinct()`) with the two bulk-replace domain calls `action.ReplaceProductAssociations(...)` and `action.ReplaceFolderLinks(...)`, matching exactly how `UpdateMarketingActionHandler.Handle` already sets these two associations. No `using System.Linq;` was added — `ImplicitUsings` is enabled for the project, so `.Select` resolves the same way it already does in `UpdateMarketingActionHandler.cs`.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs` — lines 59–65 replaced with the two bulk-replace calls; no other line in the file changed.

## Tests
No new tests written in this unit — the task context scoped this to the mechanical replace only; regression tests for the dedup-behavior changes are covered by the separate task `add-dedup-regression-tests-and-verify-suite`.

## How to verify
```bash
cd /home/user/worktrees/feature-4191-Arch-Review-Marketing-Createmarketingactionhandler
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
grep -n "AssociateWithProduct\|LinkToFolder" backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs
```
Build succeeded with 0 errors (only pre-existing warnings unrelated to this file). The grep for the two per-item methods inside the handler file returns no matches.

## Notes
Followed the task context's exact replacement block verbatim. No deviations. The surrounding `action` construction and the Outlook sync block below were left untouched, per the task's scope.

## PR Summary
Aligned `CreateMarketingActionHandler` with `UpdateMarketingActionHandler` by replacing its two per-item association loops (`AssociateWithProduct`/`LinkToFolder` with manual dedup) with the domain's bulk-replace methods (`ReplaceProductAssociations`/`ReplaceFolderLinks`), so both handlers now set initial/replacement associations through the same domain entry point.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs` — replaced the two per-item loops with `ReplaceProductAssociations` and `ReplaceFolderLinks` calls

## Status
DONE
