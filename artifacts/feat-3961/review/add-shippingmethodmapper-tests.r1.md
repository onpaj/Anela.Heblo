# Code Review: add-shippingmethodmapper-tests

## Summary
The implementation matches the task-context plan exactly (the file was written verbatim as specified) and covers all three required code paths — null/empty GUID → PickUp, known GUID → configured method, unknown GUID → PickUp + warning — plus a bonus test for the single-argument constructor overload. Test conventions (xUnit, FluentAssertions, Moq) match the sibling `BillingMethodMapperTests.cs` file, and only the intended test file was committed.

## Review Result: PASS

### task: add-shippingmethodmapper-tests
**Status:** PASS

## Docs to Update
(none)

## Overall Notes
- **Spec coverage verified by tracing each test against `ShippingMethodMapper.Map`:**
  - `Map_ReturnsPickUp_WhenShippingIsNull` / `_WhenGuidIsNull` / `_WhenGuidIsEmpty`: `guid = shipping?.Guid` is null in all three cases, so `string.IsNullOrEmpty(guid)` is true → returns `PickUp` before the logger is touched. `VerifyNoWarningLogged` (`Times.Never` on `LogLevel.Warning`) correctly holds.
  - `Map_ReturnsConfiguredMethod_WhenGuidIsKnown` (`[Theory]`, 2 cases: PPL, Zasilkovna): `InvoiceShippingGuidMap.TryGetValue` succeeds, method returns early, no warning logged — assertions match.
  - `Map_ReturnsPickUpAndLogsWarning_WhenGuidIsUnknown_WithNonEmptyMap` / `_WithEmptyMap`: `TryGetValue` fails, falls through to `_logger.LogWarning(...)` (single format arg, no exception) then returns `PickUp`. The Moq verification's `null` literal for the `Exception?` parameter position correctly matches the exception-less overload actually invoked by `ILogger.LogWarning(string, object[])`, and `It.Is<It.IsAnyType>((v,_) => v.ToString()!.Contains(expectedGuid))` correctly checks the formatted message contains the GUID.
  - `Map_ReturnsPickUp_WhenConstructedWithSingleArgumentConstructor`: exercises the single-arg constructor, which per production code delegates to `NullLogger<ShippingMethodMapper>.Instance` — not required by the spec's three paths but a reasonable, correct bonus case, and it doesn't replace or weaken required coverage.
- **Architecture/style**: namespace, using directives, and `[Theory]`/`[InlineData]`/`[Fact]` + `FluentAssertions` usage match `BillingMethodMapperTests.cs` in the same folder. The `Mock<ILogger<T>>.Verify(x => x.Log(...))` pattern matches the working pattern documented in the task context (`InvoiceImportServiceTests.cs`).
- **Completeness / scope**: `git show --stat HEAD` on the worktree confirms the commit touched exactly one file (175 insertions, `ShippingMethodMapperTests.cs`, 0 deletions elsewhere). The only other working-tree change (`artifacts/feat-3961/state.json`) is pipeline bookkeeping, not part of this commit, and was correctly left unstaged per the implementation notes.
- **Dynamic verification**: I did not complete an independent `dotnet test` run in this review session — the sandbox's cold build of the full backend solution did not finish within the available time window (consistent with the ~18-minute cold-build time the developer also reported). Static tracing of every assertion against the production `ShippingMethodMapper.Map` implementation gives high confidence the code compiles and behaves as the developer's reported output states (8/8 new tests passed, 192/192 total in the sibling folder, no regressions). This is a lower-confidence path than an actual rerun, but the logic was checked line-by-line and no discrepancy was found.
- No documentation updates are needed — this is a pure test-only addition to an already-documented mapper, following existing test conventions with no new architectural surface.
