# Architecture Review: Bank ImportBankStatementHandler coverage gaps

## Summary
Test-only change. No production architecture is affected. All four test cases fit naturally into the existing `ImportBankStatementHandlerTests` class.

## Findings

### No architectural concerns
Adding tests to an existing test class in the same namespace requires no new abstractions, no new dependencies, and no changes to production code. The existing mock setup (`_mockFactory`, `_mockBankClient`, `_mockImportService`, `_mockRepository`, `_mockStateRepository`, `_mockMapper`, `_mockLogger`) already supports all required scenarios.

### Logger assertion pattern
The existing `BackgroundRefreshSchedulerServiceTests` establishes the `It.Is<It.IsAnyType>` pattern for asserting ILogger calls. This pattern should be reused consistently rather than introduced differently.

### Relative dates for watermark tests
Using `DateTime.UtcNow.AddDays(-N)` instead of hardcoded absolute dates makes tests robust over time. The stale-watermark tests must use this approach; `Handle_UsesExistingWatermarkState_WhenAccountKnown` uses a hardcoded `2026-06-09` date which will eventually become ambiguous relative to the default StaleWarningDays.

### UpsertExistingAsync fallback
The null-check fallback in `UpsertExistingAsync` guards against a race condition (row deleted between the existence check and the fetch). The test must set up `GetByTransferIdAsync` to return `null` while `existingTransfers` still reports the statement as a retry.

## Decision
Proceed with the four tests as specified. No production code changes required.
