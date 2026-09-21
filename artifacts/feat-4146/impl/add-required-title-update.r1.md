# Implementation: add-required-title-update

## What was implemented
Added the `[Required]` data annotation to the `Title` property on `UpdateJournalEntryRequest`, matching the existing `[Required]` + `[MaxLength]` pattern already used on `Content` two lines below it in the same class, and mirroring the `add-required-title-create` task's change to the sibling `CreateJournalEntryRequest`. This closes the gap where `Title` was validated as required by the EF Core configuration and the handler, but not by the request DTO's data annotations — meaning the OpenAPI spec (and therefore the generated TypeScript client) emitted `title` as optional.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/UpdateJournalEntryRequest.cs` — added `[Required]` above the existing `[MaxLength(200)]` on the `Title` property. `Id` and every other property, and `UpdateJournalEntryResponse`, are untouched.

## Tests
No test files created or modified, per the task context. Ran the existing `UpdateJournalEntryHandlerTests` (8 tests) before and after the change to confirm no regression — this test class calls the handler directly, bypassing MVC model binding, so it is unaffected by the DTO annotation change.

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -c Release --filter "FullyQualifiedName~UpdateJournalEntryHandlerTests" -v minimal
```
Expected: `Passed! - Failed: 0, Passed: 8, Skipped: 0, Total: 8`.

## Notes
- Same environment note as `add-required-title-create`: verified with `-c Release` to avoid the sandbox-specific hang in the Debug-only `GenerateAccessMatrix` MSBuild target on `Anela.Heblo.API.csproj`. Does not change any code and has no bearing on the actual fix.
- No `using` changes needed — `System.ComponentModel.DataAnnotations` was already imported.

## PR Summary
Added the `[Required]` attribute to `Title` on `UpdateJournalEntryRequest`, aligning the request DTO with the handler's and EF Core's existing required-title validation so the generated OpenAPI spec and TypeScript client correctly mark `title` as mandatory instead of optional.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/UpdateJournalEntryRequest.cs` — added `[Required]` above `[MaxLength(200)]` on `Title`

## Status
DONE
