# Implementation: inject-current-user-into-disassemble-handler

## What was implemented
`DisassembleGiftPackageHandler` now injects `Anela.Heblo.Domain.Features.Users.ICurrentUserService`,
resolves `_currentUserService.GetCurrentUser()` once at the top of `Handle()` (before the existing
`try` block), and forwards `user.Name ?? "System"` as the new `userName` argument to
`IGiftPackageManufactureService.DisassembleGiftPackageAsync(giftPackageCode, quantity, userName, cancellationToken)`.
The existing `try`/`catch (InvalidOperationException)`/`catch (ArgumentException)` structure is
unchanged (byte-for-byte identical catch bodies), completing the identity-resolution refactor for
this handler per ADR-005.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/UseCases/DisassembleGiftPackage/DisassembleGiftPackageHandler.cs` — injects `ICurrentUserService` via constructor, resolves the current user before the `try` block, passes `user.Name ?? "System"` as the third argument to `DisassembleGiftPackageAsync`.
- `backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/DisassembleGiftPackageHandlerTests.cs` — updated to construct the handler with both `IGiftPackageManufactureService` and `ICurrentUserService` mocks, updated all `DisassembleGiftPackageAsync` setup/verify call sites to the new 4-argument signature, and added a new test for the `"System"` fallback when `CurrentUser.Name` is null.

## Tests
- `DisassembleGiftPackageHandlerTests.cs` (4 tests, all passing):
  - `Handle_ReturnsSuccessWithDisassembly_WhenServiceSucceeds`
  - `Handle_ReturnsInvalidOperation_WhenServiceThrowsInvalidOperationException`
  - `Handle_ReturnsInvalidValue_WhenServiceThrowsArgumentException`
  - `Handle_ForwardsSystemFallback_WhenCurrentUserNameIsNull` (new)

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DisassembleGiftPackageHandlerTests"
dotnet format Anela.Heblo.sln --verify-no-changes   # run from repo root — sln lives there, not under backend/
cd .. && dotnet build Anela.Heblo.sln                # run from repo root
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

## Notes
- Deviation from the task-context's literal step commands: the task-context says to run
  `dotnet format --verify-no-changes` and `dotnet build Anela.Heblo.sln` from inside `backend/`.
  In this checkout `Anela.Heblo.sln` lives at the repo root (`/home/user/worktrees/.../Anela.Heblo.sln`),
  not under `backend/`, so both commands were run from the repo root instead — same commands,
  correct working directory. `dotnet test` commands (which target the `.csproj` file directly) were
  run from `backend/` exactly as specified, since those don't need a solution file.
- `dotnet format Anela.Heblo.sln --verify-no-changes`: clean, exit code 0, no output.
- `dotnet build Anela.Heblo.sln`: 0 errors, 94 pre-existing warnings (none introduced by this change), Time Elapsed 36.71s.
- Full suite (`dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`): 6771 passed, 105 failed, 4 skipped, 6880 total.
  All 105 failures are pre-existing `Testcontainers`/`Docker`/`PostgreSql` integration test failures
  (`Testcontainers.PostgreSql.PostgreSqlBuilder.Build()` throwing because no Docker daemon is
  reachable in this sandbox: `docker ps` fails with "failed to connect to the docker API at
  unix:///var/run/docker.sock ... no such file or directory"). None of the failures touch
  `DisassembleGiftPackageHandlerTests` or `ModuleBoundariesTests` — both pass, confirmed by grepping
  the full test log for those names among the failures (no matches) and confirming
  `ModuleBoundariesTests` produced no failure output.
- Only the two files listed above were staged and committed (`git add <file> <file>`, not `-A`).
  `artifacts/feat-4074/state.json` was already modified in the working tree before this task started
  and was left untouched/uncommitted, per the "commit ONLY these two files" instruction.

## Status
DONE
