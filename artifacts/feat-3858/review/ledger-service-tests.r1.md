## Review: ledger-service-tests r1
**Status:** PASS

All required scenarios covered (single-flight, no caching of failures/cancels, waiter isolation on cancel, TTL boundaries, QueryCompleted log, FR-5). Deviation (FR-5 test placed in Flexi.Tests, new file beside existing LedgerServiceTests.cs) is justified and documented. Tests pass and are format-clean.
