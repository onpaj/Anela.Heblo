# Code Review: extract-new-to-opened-side-effect

## Summary
The implementation successfully extracts the `New -> Opened` side-effect logic from `ChangeTransportBoxStateHandler.HandleNewToOpened()` into a new `NewToOpenedSideEffect` class implementing `ITransportBoxTransitionSideEffect`. The extraction is behavior-preserving with comprehensive test coverage (6 tests, all passing). Handler remains untouched as required, and no DI registration is present as this is an intermediate refactor step.

## Review Result: PASS

### task: extract-new-to-opened-side-effect
**Status:** PASS

All acceptance criteria satisfied:
- `NewToOpenedSideEffect` class correctly implements `ITransportBoxTransitionSideEffect` interface
- `Supports(TransportBoxState.New, TransportBoxState.Opened)` returns true; all other state pairs return false
- `ExecuteAsync` covers all three outcomes with appropriate error handling and return values:
  - Missing/empty `BoxCode` → `RequiredFieldMissing` error with `Params["field"] = "BoxCode"`
  - Duplicate active box code → `TransportBoxDuplicateActiveBoxFound` error with `Params["code"] = normalized code`
  - Valid code → closes stale `Stocked` boxes and returns null
- Test suite is complete with 6 test cases covering both positive and negative scenarios
- All tests pass (6/6), plus regression check passes (21/21 handler tests still pass)
- Build succeeds with no errors; code formatting is clean
- Handler (`ChangeTransportBoxStateHandler.cs`) remains untouched (verified via git diff)
- No DI registration added (intentionally deferred to `register-di` task as specified)

**Verification:**
- `dotnet build` → 0 errors
- `dotnet test --filter NewToOpenedSideEffectTests` → Passed! 6/6
- `dotnet test --filter ChangeTransportBoxStateHandlerTests` → Passed! 21/21 (regression)
- `dotnet format --verify-no-changes` → clean on both files

## Docs to Update
No documentation changes required for this intermediate refactor step. Future documentation updates will occur when the handler rewiring and DI registration tasks are completed.

## Overall Notes
- The one unused `using Anela.Heblo.Application.Features.Logistics.Contracts;` in the test file is noted and deferred (acceptable on a green, format-clean file)
- `GetPagedListAsync` method signature verification is correct: the implementation uses 4 named parameters with defaults for the remaining 4, matching the real signature (8 total: skip, take, code, state, productCode, sortBy, sortDescending, isActiveFilter)
- Behavior extraction is confirmed byte-for-byte identical to the original private method
- Temporary duplication between `NewToOpenedSideEffect.ExecuteAsync` and the handler's `HandleNewToOpened` is expected until `refactor-handler-orchestration` removes the private method
