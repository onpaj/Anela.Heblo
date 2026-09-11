# Code Review: refactor-service-remove-current-user-dependency

## Summary
The implementation removes `ICurrentUserService` from `GiftPackageManufactureService` and its interface, replacing internal user resolution with a caller-supplied `userName` parameter, exactly as specified in the task-context file. The diff matches the spec's prescribed before/after snippets verbatim across all three files, and the resulting build/test state matches what the spec explicitly predicts.

## Review Result: PASS

### task: refactor-service-remove-current-user-dependency

Verification performed during review:
- `IGiftPackageManufactureService.cs`: `userName` parameter inserted in both method signatures at the exact position specified (before `cancellationToken`). Matches spec Step 3 verbatim.
- `GiftPackageManufactureService.cs`: `using Anela.Heblo.Domain.Features.Users;` removed, `_currentUserService` field and constructor parameter removed, both methods updated to accept and use `userName` directly in place of `_currentUserService.GetCurrentUser().Name ?? "System"`. Matches spec Step 4 verbatim (using block, fields, constructor, both method bodies).
- `GiftPackageManufactureServiceTests.cs`: `using Anela.Heblo.Domain.Features.Users;` removed, `_currentUserServiceMock` field/instantiation/constructor-wiring removed, the `GetCurrentUser()` mock setup removed, and the `CreateManufactureAsync` call under test updated to pass `userId` as the new `userName` argument. Matches spec Step 1 verbatim.
- Grepped the entire `GiftPackageManufacture` feature folder and the test file for `_currentUserService`, `ICurrentUserService`, and `CurrentUser(` — zero matches remain. No leftover unused fields/usings/references.
- `dotnet build Anela.Heblo.sln` from the repo root fails with exactly two errors: `CS1503` in `CreateGiftPackageManufactureHandler.cs:21` and `CS1503` in `DisassembleGiftPackageHandler.cs:23`, both "cannot convert from CancellationToken to string" — i.e. the handlers are now missing the new required `userName` argument. This is precisely the outcome the task-context spec's Step 5 documents as expected and correct for this task's scope: those two handlers are explicitly deferred to the separate queued tasks `inject-current-user-into-create-handler` and `inject-current-user-into-disassemble-handler` (both present as task-context files in `artifacts/feat-4074/task-context/`), not part of this task's file list.
- Because the Application project fails to build, the test project (which references it) cannot currently execute — also expected and consistent with the spec, which does not claim the full suite passes until the handler tasks land.

No functional requirement from the spec is unmet, no architecture guideline (ADR-005) is contradicted — to the contrary, this task directly implements it — and no correctness bug was found within this task's scope.

## Docs to Update
None. This is an internal-signature refactor with no change to public behavior, CLI, environment variables, or documented architecture (it brings the code into compliance with the already-documented ADR-005 rather than changing the rule).

## Overall Notes
The task is intentionally one of a three-task chain (service refactor, then two handler updates) and is correctly scoped — it does not attempt to fix the now-stale handler call sites, which is correct per the task-context spec and will be resolved by the next two checkpointed tasks.

**Status:** PASS
