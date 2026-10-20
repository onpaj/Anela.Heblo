# Architecture Review: LedgerService coverage gap tests

## Skip Design: true
Backend test-only change; no UI.

## Architectural Fit Assessment
Fits existing pattern: adapter tests live in `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/`. No production or contract change; no process doc impact (no behaviour change).

## Proposed Architecture

### Component Overview
Test class -> `LedgerService` (real) with Mock<ILedgerClient>, real `MemoryCache`, Mock<IMapper>, Mock<ILogger>.

### Key Design Decisions

#### Decision 1: Real MemoryCache for cache tests
**Options considered:** mock IMemoryCache (existing fixture) vs real `MemoryCache(new MemoryCacheOptions())`.
**Chosen approach:** real MemoryCache in a separate helper/factory within the same test file; keep the existing constructor-fixture (always-miss mock) for the cancellation tests untouched.
**Rationale:** TryGetValue/Set on mocks with extension methods is brittle; a real cache verifies the true hit behaviour.

#### Decision 2: Mapper stubbing
Mock `IMapper.Map<List<LedgerItem>>(It.IsAny<object>())` to return a prepared list so GetCosts aggregation is driven by controlled `LedgerItem`s.

## Implementation Guidance

### Directory / Module Structure
Extend `LedgerServiceTests.cs` (or add `LedgerServiceCacheAndCostsTests.cs` in same folder). `ILedgerClient.GetAsync` signature (as used by existing tests): (DateTime, DateTime, IEnumerable<string>, IEnumerable<string>, string?, int, int, CancellationToken).

### Interfaces and Contracts
Verify GetAsync call counts with `Times.Once`; assert CostStatistics via FluentAssertions `BeEquivalentTo(..., o => o.WithStrictOrdering())`.

### Data Flow
GetLedgerItems: key -> cache miss -> GetAsync -> Map -> Set -> return; hit -> return cached.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Issue items 2/3 already covered, duplicated tests | Low | Do not duplicate; optionally strengthen assertions |
| Dispose of MemoryCache | Low | Use `using`/IDisposable in tests |

## Specification Amendments
None.

## Prerequisites
None.
