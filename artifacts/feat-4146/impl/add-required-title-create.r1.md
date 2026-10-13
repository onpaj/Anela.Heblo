# Implementation: add-required-title-create

## What was implemented
Added the `[Required]` data annotation to the `Title` property on `CreateJournalEntryRequest`, matching the existing `[Required]` + `[MaxLength]` pattern already used on `Content` two lines below it in the same class. This closes the gap where `Title` was validated as required by the EF Core configuration and the handler, but not by the request DTO's data annotations — meaning the OpenAPI spec (and therefore the generated TypeScript client) emitted `title` as optional.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/CreateJournalEntryRequest.cs` — added `[Required]` above the existing `[MaxLength(200)]` on the `Title` property. No other property, constructor, or the paired `CreateJournalEntryResponse` class changed.

## Tests
No test files created or modified, per the task context and spec's Out of Scope. Ran the existing `CreateJournalEntryHandlerTests` (7 tests) before and after the change to confirm no regression — this test class calls the handler directly, bypassing MVC model binding, so it is unaffected by the DTO annotation change.

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -c Release --filter "FullyQualifiedName~CreateJournalEntryHandlerTests" -v minimal
```
Expected: `Passed! - Failed: 0, Passed: 7, Skipped: 0, Total: 7`.

## Notes
- Verification note: `dotnet test` (default Debug configuration) triggers a `BeforeTargets="Build"` MSBuild target on `Anela.Heblo.API.csproj` (`GenerateAccessMatrix`) that shells out via `Exec` to `dotnet run --project tools/Anela.Heblo.AccessMatrixGen`. In this sandboxed worktree environment, that nested `dotnet run` invocation reliably hangs after writing its output (confirmed: the tool runs cleanly standalone in ~8s; the hang only occurs when nested inside another active `dotnet build`/`dotnet test` invocation — a nested MSBuild/build-server node contention issue, not a defect in the generator tool or in this change). Worked around by building/testing with `-c Release` for local verification, which skips that Debug-only target (`Condition="'$(Configuration)' == 'Debug'"`). This is an environment-specific workaround for local verification only; it does not change any code and has no bearing on the actual fix. Flagging so the `verify-build-and-contract-regen` task (which runs `dotnet build`/`dotnet test` without a configuration override) is aware of this if it hits the same hang.
- No `using` changes needed — `System.ComponentModel.DataAnnotations` was already imported.

## PR Summary
Added the `[Required]` attribute to `Title` on `CreateJournalEntryRequest`, aligning the request DTO with the handler's and EF Core's existing required-title validation so the generated OpenAPI spec and TypeScript client correctly mark `title` as mandatory instead of optional.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/CreateJournalEntryRequest.cs` — added `[Required]` above `[MaxLength(200)]` on `Title`

## Status
DONE
