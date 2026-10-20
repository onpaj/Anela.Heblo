# Specification: LedgerService coverage gap tests

## Summary
Add unit tests for `LedgerService` (Flexi adapter) covering the cache-hit path and `GetCosts` daily aggregation. Test-only change; no production code changes.

## Background
Issue #3938 reports 52.3% line coverage (threshold 60%). Of four listed gaps, items 2 (internal timeout -> Warning) and 3 (caller abort -> Information) are already covered by `LedgerServiceTests.GetLedgerItems_WhenInternalTimeoutCancels_LogsWarningAndRethrows` and `GetLedgerItems_WhenCallerCancels_LogsInformationAndRethrows`. Remaining gaps: cache hit (existing fixture mocks IMemoryCache to always miss) and GetCosts aggregation (also GetPersonalCosts/GetDirectCosts delegation, and the cache-set-on-miss path).

## Functional Requirements

### FR-1: Cache hit avoids FlexiBee call
**Acceptance criteria:**
- With a real `MemoryCache`, two calls to `GetLedgerItems` with identical args invoke `ILedgerClient.GetAsync` exactly once and return the same list instance.
- Calls differing in any key component (dates, debit/credit prefixes, department) result in separate `GetAsync` calls.

### FR-2: GetCosts daily aggregation
**Acceptance criteria:**
- Items with mixed dates/times on the same day and department are summed into one `CostStatistics` per (Date.Date, Department).
- Different departments on the same day yield separate rows; output ordered by Date ascending.
- Empty ledger yields an empty list.
- `GetCosts` passes `debitAccountPrefixes` and department through to `ILedgerClient.GetAsync`; `GetPersonalCosts` uses ["52"], `GetDirectCosts` uses ["51","52"].

### FR-3: Existing cancellation tests retained
No change to the two existing OperationCanceledException tests; optionally assert the exception is rethrown unwrapped and that no Warning is logged in the caller-abort case (and vice versa).

## Non-Functional Requirements
### NFR-1: Test hygiene
Follow existing xUnit + Moq + FluentAssertions style; tests deterministic, no real network, no time dependence beyond cache TTL (not tested).
### NFR-2: Security
N/A.

## Data Model
`LedgerItem` (Domain.Accounting.Ledger: Date, Department, Amount), `CostStatistics` (Date, Department, Cost), `LedgerItemFlexiDto`.

## API / Interface Design
None. Tests target `LedgerService` public methods in `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/LedgerServiceTests.cs` (or a sibling file).

## Dependencies
Moq, FluentAssertions, Microsoft.Extensions.Caching.Memory (already referenced). AutoMapper mock for `Map<List<LedgerItem>>`.

## Out of Scope
Production changes, cache expiry (15 min TTL) tests, mapping profile tests.

## Open Questions
None.

## Status: COMPLETE
