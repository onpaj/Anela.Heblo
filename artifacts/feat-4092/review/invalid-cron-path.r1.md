# Code Review: RecurringJobNextRunCalculator coverage — invalid-cron-path

## Summary
The implementation adds the required test method `Calculate_ReturnsNull_AndLogsWarning_WhenCronInvalid` to `RecurringJobNextRunCalculatorTests.cs`, positioned immediately after the sibling timezone-unknown test. The test body matches the specification exactly: it verifies that an unparseable cron string ("not a cron") causes `Calculate` to return `null` and log exactly one Warning-level message. All three tests in the class now pass.

## Review Result: PASS

### task: invalid-cron-path
**Status:** PASS
**Issues:** None

#### Spec Compliance
- ✓ Test method added to correct file: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobNextRunCalculatorTests.cs`
- ✓ Placed immediately after `Calculate_ReturnsNull_AndLogsWarning_WhenTimezoneUnknown` as specified
- ✓ Method signature and body match specification verbatim (lines 67–92)
- ✓ Tests the invalid-cron path with input `"not a cron"`
- ✓ Assertions correct: `result.Should().BeNull()` and logger verification for exactly one Warning log
- ✓ No production code modified; only test code added (surgical change)

#### Test Quality
- ✓ Follows AAA (Arrange-Act-Assert) pattern
- ✓ Mirrors structure of sibling test for consistency
- ✓ Uses correct mocking (Moq) and assertion libraries (FluentAssertions)
- ✓ Clear, descriptive test name following naming conventions
- ✓ Proper setup of logger mock and all required parameters

#### Execution
- ✓ Developer verified all 3 tests pass: `Passed! - Failed: 0, Passed: 3, Skipped: 0`
- ✓ Build succeeded with 0 errors (240 pre-existing warnings unrelated to this change)
- ✓ Test command matches specification: filter on `RecurringJobNextRunCalculatorTests`

#### Architecture Adherence
- ✓ Test follows established patterns within the class
- ✓ Uses standard testing stack (Xunit, Moq, FluentAssertions)
- ✓ Correctly covers the error-handling path (CrontabException caught and logged)

## Overall Notes
This is a straightforward, correct test addition with no issues. The implementation is surgical (only the test file touched), the test is identical to the specification, and verification confirms all three tests pass. Ready to merge.
