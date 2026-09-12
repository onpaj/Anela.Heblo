# Code Review: unknown-timezone-path

## Summary
The implementation correctly adds a unit test for the FR-2 unknown-timezone path of `RecurringJobNextRunCalculator.Calculate`. The test verifies that when `TimeZoneInfo.FindSystemTimeZoneById` throws `TimeZoneNotFoundException` for an invalid timezone, the method catches the exception, logs exactly one warning, and returns null. The test is properly placed after the existing `Calculate_ReturnsNull_WhenJobDisabled` test, uses the exact assertion pattern from the task specification, and follows the existing codebase conventions.

## Review Result: PASS

### task: unknown-timezone-path
**Status:** PASS
**Issues:** None

## Verification
- ✓ Test added in correct file: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs`
- ✓ Test placement: Immediately after `Calculate_ReturnsNull_WhenJobDisabled`, as required
- ✓ Test structure: Arrange-Act-Assert pattern, with mocked logger
- ✓ Assertions match spec: `result.Should().BeNull()` and logger Warning verification with `Times.Once`
- ✓ Parameters align with production behavior: `isEnabled: true` to proceed past the first check, `timeZoneId: "Not/A/Real/Zone"` to trigger `TimeZoneNotFoundException`
- ✓ Production code verified: Handles `TimeZoneNotFoundException` in lines 26–31, logs Warning (line 28), returns null (line 30)
- ✓ Test execution: Reported as passing (Passed: 2, Failed: 0)
- ✓ Commit created: fedbdf639 with message `test(background-jobs): cover RecurringJobNextRunCalculator unknown-timezone path`

## Overall Notes
The implementation is minimal, focused, and complete. The test directly targets the FR-2 coverage gap without modification to production code, and the commit is properly attributed.
