# Implementation: inject-current-user-into-create-handler

## What was implemented
`CreateGiftPackageManufactureHandler` now injects `ICurrentUserService`, resolves `_currentUserService.GetCurrentUser()` once at the top of `Handle()`, and forwards `user.Name ?? "System"` as the new `userName` argument to `IGiftPackageManufactureService.CreateManufactureAsync`, matching the ADR-005-compliant pattern established in `CreateNewTransportBoxHandler`. This is task 2 of the 3-task chain (service refactor already completed; this handler now becomes the identity-resolution boundary for the create use case; the disassemble handler is deferred to the next task).

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/CreateGiftPackageManufacture/CreateGiftPackageManufactureHandler.cs` — injects `ICurrentUserService`, resolves the current user once, passes `user.Name ?? "System"` as the 4th argument to `CreateManufactureAsync`.
- `backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/CreateGiftPackageManufactureHandlerTests.cs` — new test file (none existed before) covering the resolved-username-forwarded case and the null-name-falls-back-to-"System" case.

## Tests
`CreateGiftPackageManufactureHandlerTests.cs`:
- `Handle_ForwardsResolvedUserName_ToCreateManufactureAsync` — verifies the resolved `CurrentUser.Name` is forwarded verbatim to the service call.
- `Handle_FallsBackToSystem_WhenCurrentUserNameIsNull` — verifies `"System"` is used when `CurrentUser.Name` is null.

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CreateGiftPackageManufactureHandlerTests"
```
Both tests pass (verified in isolation with `DisassembleGiftPackageHandler.cs`/`DisassembleGiftPackageHandlerTests.cs` temporarily patched to their post-refactor shape and reverted afterward — see Notes).

## Notes
As documented by the prior task's review (`review/refactor-service-remove-current-user-dependency.r1.md`), `dotnet build`/`dotnet test` against the full solution/test project currently fails with a pre-existing, expected, out-of-scope `CS1503` in `DisassembleGiftPackageHandler.cs` (and, transitively, in the still-unmodified `DisassembleGiftPackageHandlerTests.cs`) — both deferred to the separate queued task `inject-current-user-into-disassemble-handler`. This task's own file list does not include either of those files, so they were left untouched. To confirm this task's change compiles and its own tests pass, the disassemble handler and its test were temporarily patched in a scratch/local-only way to their known post-refactor shape (matching the next task's own spec verbatim), the filtered test suite was run and passed 2/2, and both files were then reverted via `git checkout --` to their exact pre-task committed state (confirmed via `git diff` showing no changes) before committing this task's own changes.

## PR Summary
`CreateGiftPackageManufactureHandler` now resolves the current user itself and passes the resolved name to `IGiftPackageManufactureService.CreateManufactureAsync`, completing the ADR-005 identity-resolution boundary move for the create-gift-package-manufacture use case (the service-level refactor landed in a prior task; the disassemble handler is a separate follow-up task). A new test file covers both the normal-username and null-username-falls-back-to-"System" paths.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/CreateGiftPackageManufacture/CreateGiftPackageManufactureHandler.cs` — injects `ICurrentUserService`, resolves and forwards the user name
- `backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/CreateGiftPackageManufactureHandlerTests.cs` — new test file

## Status
DONE
