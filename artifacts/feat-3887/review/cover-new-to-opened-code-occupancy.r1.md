# Code Review: cover-new-to-opened-code-occupancy

## Summary
Two new `[Fact]` tests were added to `TransportBoxUniquenessTests` that exercise the real `ChangeTransportBoxStateHandler` + `TransportBoxRepository` + in-memory `ApplicationDbContext` stack, confirming that a `New` box cannot take a code held by an existing `Quarantine` or `Error` box (FR-3, the reported bug's path). The diff matches the task spec's prescribed test bodies essentially verbatim, no production code was touched, amendment A3 (no assertion on the rejected box's tracked `Code`) is respected, and both the full test file and the mocked-repository handler test suite were run and pass.

## Review Result: PASS

### task: cover-new-to-opened-code-occupancy
**Status:** PASS

Verification performed independently against the actual diff (not just the impl summary):
- `git diff` shows only `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxUniquenessTests.cs` changed (plus pipeline `state.json`) — `ChangeTransportBoxStateHandler.cs` is untouched, satisfying the "no production file changes" / "handler not modified" requirements.
- `OpenTransportBox_WhenCodeHeldByQuarantinedBox_ShouldPreventDuplicate` builds the Quarantine fixture via `Open("B001", ...)` then `ToQuarantine(...)` exactly as the spec's transition recipe describes, and asserts only `Success`, `ErrorCode == TransportBoxDuplicateActiveBoxFound`, and `Params["code"] == "B001"` — no assertion on the tracked `New` box's `Code`, matching amendment A3.
- `OpenTransportBox_WhenCodeHeldByErroredBox_ShouldPreventDuplicate` mirrors it via `Open("B001", ...)` then `Error(..., "boom")`, matching the spec's `Error` fixture recipe.
- The five pre-existing tests in the file are byte-for-byte unmodified (diff only appends after the last pre-existing test, before `Dispose`).
- Ran independently in this review (not just trusting the impl summary):
  - `dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` → 0 errors.
  - `dotnet test ... --filter "FullyQualifiedName~TransportBoxUniquenessTests"` → 7/7 passed (5 pre-existing + 2 new).
  - `dotnet test ... --filter "FullyQualifiedName~ChangeTransportBoxStateHandlerTests"` → 21/21 passed, confirming the mocked-repository suite is unaffected.
  - `dotnet format Anela.Heblo.sln --include backend/test/.../TransportBoxUniquenessTests.cs` → no output, no formatting issues.
- Step 2 (optional persistence assertion via a second `ApplicationDbContext`) was correctly skipped, and no persistence assertion was added in its absence, per the spec's "if you skip this step, do not assert persistence at all."

No functional requirement is unmet, no architecture guideline is violated, and both required test filters pass.

## Docs to Update
(None — this is a test-only change with no public behavior, CLI, or operational change.)

## Overall Notes
Implementation is a clean, minimal, spec-literal addition. Nothing further needed for this task.

**Status:** PASS
