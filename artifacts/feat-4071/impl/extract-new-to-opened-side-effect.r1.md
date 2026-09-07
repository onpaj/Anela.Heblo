# Implementation: extract-new-to-opened-side-effect

## What was implemented
Extracted the body of `ChangeTransportBoxStateHandler.HandleNewToOpened` into a new standalone class `NewToOpenedSideEffect`, implementing the `ITransportBoxTransitionSideEffect` interface added by the previous task (`create-side-effect-interface`).

The extraction is byte-for-byte behaviour-preserving: the `ExecuteAsync` body is a verbatim copy of the private handler method — same `string.IsNullOrEmpty(request.BoxCode)` guard returning `ErrorCodes.RequiredFieldMissing` with `Params["field"] = "BoxCode"`, same `ToUpper()` normalization plus `IsBoxCodeActiveAsync` duplicate check returning `ErrorCodes.TransportBoxDuplicateActiveBoxFound` with `Params["code"]`, and the same stale-`Stocked`-box close loop (`Close(...)` + `UpdateAsync(...)`) returning `null` on success. `Supports(from, to)` returns true only for `New -> Opened`.

Per the task context, `ChangeTransportBoxStateHandler.cs` was **not** modified — it still owns its private `HandleNewToOpened` and its `CallBackMap`. Rewiring the handler to dispatch through the side-effect collection is the later `refactor-handler-orchestration` task, so the logic is deliberately duplicated on the branch until then. No DI registration was added either (that is the later `register-di` task).

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/NewToOpenedSideEffect.cs` — new class implementing `ITransportBoxTransitionSideEffect`; constructor-injects `ITransportBoxRepository`, `ICurrentUserService`, `TimeProvider`; `Supports` predicate for `New -> Opened` and `ExecuteAsync` carrying the extracted side-effect logic.
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/NewToOpenedSideEffectTests.cs` — new xUnit test class (Moq + FluentAssertions), 6 executed test cases.

## Tests
`backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/NewToOpenedSideEffectTests.cs`:
- `Supports_NewToOpened_ReturnsTrue` — the `New -> Opened` pair is claimed.
- `Supports_AnyOtherPair_ReturnsFalse` — `[Theory]` over `(Opened, Reserve)` and `(New, Quarantine)`; both rejected.
- `ExecuteAsync_MissingBoxCode_ReturnsRequiredFieldMissing` — a request with no `BoxCode` short-circuits with `Success = false`, `ErrorCode = RequiredFieldMissing`, `Params["field"] == "BoxCode"`.
- `ExecuteAsync_DuplicateActiveCode_ReturnsDuplicateActiveBoxFound` — lowercase `"b999"` is normalized to `"B999"` before the `IsBoxCodeActiveAsync` lookup; an active hit short-circuits with `TransportBoxDuplicateActiveBoxFound` and `Params["code"] == "B999"`.
- `ExecuteAsync_ValidCode_ClosesStaleStockedBoxesWithSameCode_ReturnsNull` — a stale `Stocked` box returned by `GetPagedListAsync` is closed and persisted via `UpdateAsync` exactly once, and the side effect returns `null` so the transition continues.

Regression check: the pre-existing `ChangeTransportBoxStateHandlerTests` (21 tests) still pass unchanged, confirming the handler was untouched.

## How to verify
```bash
cd /path/to/worktree
dotnet build                       # 0 errors (253 pre-existing warnings)
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~NewToOpenedSideEffectTests|FullyQualifiedName~ChangeTransportBoxStateHandlerTests"
                                   # Passed! 27/27
dotnet format --verify-no-changes --no-restore   # exit 0
```
Diff check: `git show 22a562e` shows exactly the two new files; `git diff 3519ee4..HEAD -- backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs` is empty.

## Notes
- The task context's illustrative test snippet mocked `ITransportBoxRepository.GetPagedListAsync` with 9 positional arguments; the real signature takes **8** (`skip, take, code, state, productCode, sortBy, sortDescending, isActiveFilter`). The test was written against the real signature using eight `It.IsAny<...>()` matchers, matching the form already used in the sibling `ChangeTransportBoxStateHandlerTests`. All other snippet details (`CurrentUser` 4-arg record ctor, `ErrorCodes` members, `TransportBox.Close(DateTime, string)`, `ChangeTransportBoxStateResponse : BaseResponse`) were verified against source and matched.
- `new TransportBox()` defaults to `State = New`, and `Close` is permitted from `New`/`Received`/`Stocked`, so the stale-box test fixture is valid without extra state setup.
- Deliberate temporary duplication: `HandleNewToOpened` remains on the handler. This is expected until `refactor-handler-orchestration` removes it. Reviewers of the whole feature branch should not flag it at this commit.
- The unused `box` parameter on `ExecuteAsync` is required by the interface contract and is intentionally kept.
- Deferred minor (not fixed, to avoid churn on a green, format-clean file): the test file carries one unused `using Anela.Heblo.Application.Features.Logistics.Contracts;`.
- Both reviewers (spec compliance and code quality) passed on the first round; no fix rounds were needed.

## PR Summary
Extracts the `New -> Opened` transition side effect out of `ChangeTransportBoxStateHandler` into a dedicated `NewToOpenedSideEffect` class implementing `ITransportBoxTransitionSideEffect`. This is the second step of the architecture refactor for issue #4071, which breaks the handler's growing set of private `Handle*` transition methods and its static `CallBackMap` into independently testable, DI-registered side-effect classes.

The move is strictly behaviour-preserving — `ExecuteAsync` is a verbatim copy of the private `HandleNewToOpened` method, keeping the same validation order, error codes, error parameter keys, box-code normalization, and stale-`Stocked`-box closing loop. The handler itself is intentionally left untouched in this change, so there is no runtime behaviour change at all yet: the new class is not registered in DI and nothing dispatches to it. Rewiring the handler and registering the side effects land in follow-up commits on this branch, at which point the now-duplicated private method is deleted.

The new class ships with its own focused unit test suite covering the transition predicate (positive and negative pairs) and all three `ExecuteAsync` outcomes — missing box code, duplicate active code, and the success path that closes stale stocked boxes. The existing `ChangeTransportBoxStateHandlerTests` suite continues to pass unchanged, which is the regression guard proving the handler's behaviour was not disturbed.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/NewToOpenedSideEffect.cs` — new `ITransportBoxTransitionSideEffect` implementation holding the extracted `New -> Opened` side-effect logic, verbatim from the handler's private method.
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/NewToOpenedSideEffectTests.cs` — new unit tests for the `Supports` predicate and all three `ExecuteAsync` outcomes.

## Status
DONE
