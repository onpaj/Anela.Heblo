# Architecture Review: Unit Test Coverage for SalesCostProvider

## Skip Design: true
Backend-only test addition. No UI, no visual components, no design decisions required.

## Architectural Fit Assessment
The proposed work is **purely additive test code** that mirrors an already-established sibling pattern in the same test folder: `FlatManufactureCostProviderTests.cs`. It introduces no new architectural surface — no new interfaces, no DI registrations, no production-code changes, no new packages. The fit is excellent because:

1. The target class (`SalesCostProvider`) already exposes a clean seam — five mockable collaborators (`ISalesCostCache`, `ICatalogRepository`, `ILedgerService`, `ILogger<>`, `IOptions<DataSourceOptions>`) — so unit-testing requires no refactor.
2. The sibling test class uses the same toolchain the spec mandates (xUnit + Moq, `[Collection]` for static-lock serialization, relative dates anchored on `DateTime.UtcNow`). Following that template keeps the cost-provider test area uniform.
3. The wall-clock dependency in `GetDateRange()` is the only genuine awkwardness, and the spec correctly defers the `TimeProvider` refactor to a future PR that should touch all four providers together rather than carve out one.

Main integration point: the test class lives entirely under `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/` and depends only on `Anela.Heblo.Application` and `Anela.Heblo.Domain` projects — both already referenced by `Anela.Heblo.Tests.csproj` (lines 47–48).

## Proposed Architecture

### Component Overview
```
SalesCostProviderTests   ── arranges ──▶  Mock<ISalesCostCache>
   │                                 │
   │                                 ├──▶ Mock<ICatalogRepository>
   │                                 ├──▶ Mock<ILedgerService>
   │                                 ├──▶ Mock<ILogger<SalesCostProvider>>
   │                                 └──▶ Options.Create(DataSourceOptions{...})
   │
   │   invokes
   ▼
SalesCostProvider (SUT, real instance)
   │
   │   asserts via
   ▼
   • Captured CostCacheData argument to SetCachedDataAsync
   • Captured DateTime args to GetDirectCosts (for FR-5)
   • Mock.Verify call counts/order
   • ILogger.Verify(Log<It.IsAnyType>(...)) for log assertions

CatalogTestBuilders (private static helpers in the test file)
   • BuildProduct(productCode, monthlySales[])
   • BuildCostStatistics(month, cost, department)
```

### Key Design Decisions

#### Decision 1: Mocking framework — Moq, not NSubstitute
**Options considered:** Moq (already used by `FlatManufactureCostProviderTests`), NSubstitute (referenced in csproj line 17 for other suites).
**Chosen approach:** Moq exclusively.
**Rationale:** The cost-provider test area is uniformly Moq. Mixing frameworks within one folder forces every future maintainer to context-switch between APIs. The spec correctly enforces this in NFR-2.

#### Decision 2: Clock handling — relative assertions, no `TimeProvider`
**Options considered:** (a) Inject `TimeProvider`/`IClock` into `SalesCostProvider` so the test pins "now"; (b) Capture arguments passed to `ILedgerService.GetDirectCosts` and assert structural properties (Day == 1, end-of-month, time-of-day).
**Chosen approach:** (b) — argument capture with relative property assertions, plus a `[Theory]` that exercises `DateTime.DaysInMonth` directly for the leap-year edge case.
**Rationale:** Option (a) is the correct long-term solution, but it changes production code in `SalesCostProvider` AND in the three sibling providers (`FlatManufactureCostProvider`, `DirectManufactureCostProvider`, `ManufactureBasedMaterialCostProvider`) that all read `DateTime.UtcNow` directly. Doing it for one provider in isolation creates inconsistency. The package `Microsoft.Extensions.TimeProvider.Testing` 8.1.0 is already referenced (csproj:26), so the refactor is cheap when the time comes — but it belongs in its own PR. The spec is correct on this.

#### Decision 3: Concurrency test — gate via `TaskCompletionSource`, not `Thread.Sleep`
**Options considered:** Sleep-based timing, `TaskCompletionSource` gating, swapping the static `SemaphoreSlim` to an instance member.
**Chosen approach:** `TaskCompletionSource` on the first mocked `GetDirectCosts` call to deterministically hold the lock while the second `RefreshAsync` starts.
**Rationale:** Sleep-based concurrency tests are flaky in CI; modifying the production semaphore for testability is invasive. `TaskCompletionSource` is the standard .NET pattern for deterministic async-state coordination and produces a sub-second test.

#### Decision 4: Static-lock isolation — `[Collection]` attribute (not `IDisposable` reset)
**Options considered:** `[Collection("SalesCostProviderTests")]` (xUnit serial collection), reflection-based reset of the private static `SemaphoreSlim`, making `RefreshLock` test-internal.
**Chosen approach:** `[Collection]` — exactly matches `FlatManufactureCostProviderTests.cs:21`.
**Rationale:** xUnit collections give serial execution within the class without touching production internals. Reflection on private statics is brittle. The sibling class already proves this works.

#### Decision 5: Logger verification — `Verify` on the virtual `Log<TState>` method
**Options considered:** Verify on extension methods like `LogWarning` (impossible — they're static), verify on `Log<TState>` with custom `It.Is<>` matchers, capture via Serilog test sink.
**Chosen approach:** `Verify(x => x.Log(LogLevel.Warning, It.IsAny<EventId>(), It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("SalesCostCache not hydrated")), ...))` patterns extracted into a small helper.
**Rationale:** The only Moq-friendly path through `ILogger<T>`. A helper hides the verbose generic signature and prevents copy-paste drift across the seven log assertions the spec requires.

## Implementation Guidance

### Directory / Module Structure
```
backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/
  ├─ FlatManufactureCostProviderTests.cs        # already exists — template
  └─ SalesCostProviderTests.cs                  # NEW — single file, no companion builder file
```

The spec allows extracting builders to a separate `*TestBuilders.cs`. Given seven tests with mostly distinct fixtures, **keep builders as private static helpers inside `SalesCostProviderTests.cs`** until duplication actually appears. Premature extraction would add a file the rest of the area does not have. If the helpers are reused by future provider tests, promote them at that time.

### Interfaces and Contracts

The test class must define and follow this internal shape:

```csharp
[Collection("SalesCostProviderTests")]
public class SalesCostProviderTests
{
    // Helpers
    private static CatalogAggregate BuildProduct(
        string productCode,
        IEnumerable<(DateTime date, double amount)> sales);

    private static SalesCostProvider CreateProvider(
        Mock<ISalesCostCache>? cache = null,
        Mock<ICatalogRepository>? repo = null,
        Mock<ILedgerService>? ledger = null,
        Mock<ILogger<SalesCostProvider>>? logger = null,
        int manufactureCostHistoryDays = 90);

    private static void VerifyLog(
        Mock<ILogger<SalesCostProvider>> logger,
        LogLevel level,
        string messageContains,
        Times? times = null);
}
```

Test names follow the established convention from `FlatManufactureCostProviderTests`:
`Method_DoesExpected_WhenCondition` (internal access modifier, matching the sibling file's style).

### Data Flow
Each test follows the same arrange/act/assert flow:

```
1. Build Mock<ISalesCostCache>
     • For Refresh* tests: SetCachedDataAsync setup with Callback to capture CostCacheData
     • For Get* tests: GetCachedDataAsync returns hydrated/empty as required
2. Build Mock<ICatalogRepository>
     • WaitForCurrentMergeAsync → Task.CompletedTask
     • GetAllAsync → list of test-built CatalogAggregate
3. Build Mock<ILedgerService>
     • GetDirectCosts(...,"SKLAD",...) and GetDirectCosts(...,"MARKETING",...)
       each return a fixed CostStatistics list
     • Use Callback to capture (DateTime from, DateTime to) for FR-5 assertions
4. Instantiate SalesCostProvider via CreateProvider helper
5. Act: await provider.RefreshAsync(ct)  or  await provider.GetCostsAsync(...)
6. Assert:
     • Captured CostCacheData shape (ProductCosts keys, monthly counts, Cost values)
     • Mock.Verify on call counts (e.g. GetDirectCosts called exactly twice)
     • VerifyLog for expected warning/error/info messages
```

Special data flow for **FR-6 (concurrency)**:
```
1. Create TaskCompletionSource<IList<CostStatistics>> gate
2. ledgerService.Setup(GetDirectCosts("SKLAD",...)).Returns(gate.Task)
3. var firstRefresh = provider.RefreshAsync()        // does NOT await
4. await Task.Yield(); await Task.Delay(1)           // let first call enter the lock
5. await provider.RefreshAsync()                     // second call — must return immediately
6. VerifyLog(Information, "refresh already in progress")
7. gate.SetResult(emptyList)                         // release first call
8. await firstRefresh
9. Reset mock counters, call RefreshAsync() a third time, assert SetCachedDataAsync was called
```

Special data flow for **FR-7 (lock release on exception)**:
```
1. Sequence ledgerService.Setup so first call throws, second call returns normally
   (use SetupSequence)
2. await Assert.ThrowsAsync<...>(() => provider.RefreshAsync())
3. await provider.RefreshAsync()   // must NOT log "already in progress" — must proceed
4. Verify SetCachedDataAsync was called once (on the second invocation)
```

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Static `RefreshLock` state leaks across tests, producing flakes | High | `[Collection("SalesCostProviderTests")]` (mirrors sibling pattern at FlatManufactureCostProviderTests.cs:21). Verified in `FlatManufactureCostProvider.cs` — same lock topology, same fix. |
| FR-5 wall-clock dependency: test runs spanning month boundaries could destabilize day/month assertions | Medium | Assert **structural** properties of captured args (`Day == 1`, `Day == DaysInMonth(year,month)`, `Hour == 23`), never absolute equality vs. a recomputed `DateTime.UtcNow`. Theory-driven leap-year coverage hits `DateTime.DaysInMonth` directly without `now`. |
| `Mock<ILogger<T>>.Verify` on extension methods like `LogWarning` fails silently — they're static, not virtual | Medium | Always verify against the virtual `Log<TState>(LogLevel, EventId, TState, Exception?, Func<TState, Exception?, string>)`. Centralize in `VerifyLog` helper to prevent copy-paste error. |
| FR-7 lock-release test ordering — if the SUT's `finally` block ever moves, the "subsequent call proceeds" assertion becomes the only signal | Medium | Assert both (a) `SetCachedDataAsync` called exactly once after the throw-then-succeed sequence AND (b) no "already in progress" log entry on the second call. Two independent signals catch regressions either way. |
| `SetupSequence` on async returning methods can subtly require `ReturnsAsync` rather than `Returns(Task...)` — easy to get wrong | Low | Verify the throw case uses `.ThrowsAsync` (not `.Throws`) and the success case uses `.ReturnsAsync`. Run locally; CI will surface mismatched task semantics fast. |
| `[Collection]` attribute serializes within a class but not across classes sharing the same static lock | Low | Only one test class uses `SalesCostProvider`'s lock. If a future test class is added, it must share the same collection name. |
| Builder helpers grow as more provider tests are added → duplication across files | Low | Defer extraction until duplication appears. Single-file builders are simpler now; the spec explicitly allows promotion when warranted. |

## Specification Amendments

The spec is implementation-ready. Two minor clarifications worth adding to avoid execution ambiguity:

1. **FR-1 call-order verification:** the spec says "verified by ordered mock setup or by asserting call order." Recommend the implementer use `MockSequence` (`new MockSequence()` + `InSequence(sequence).Setup(...)`) for `WaitForCurrentMergeAsync` → `GetAllAsync` ordering, with a comment explaining the contract. Plain `Verify(..., Times.Once)` does not validate order.

2. **FR-3 mutation guard:** "The original cache dictionary is not mutated by the filter." Asserting non-mutation requires inspecting the cache mock's stored data after the filter runs. Recommend capturing the original dictionary by reference in the `GetCachedDataAsync` mock setup, calling `GetCostsAsync`, then asserting `originalDict.Count` and `originalDict.Keys` are unchanged.

3. **NFR-3 file location: the path in the spec is correct (`backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/SalesCostProviderTests.cs`).** Confirmed that `FlatManufactureCostProviderTests.cs` already lives at the same location, so directory creation is not needed.

No structural changes to the spec are required.

## Prerequisites
None. All required infrastructure is already in place:

- `Anela.Heblo.Tests.csproj` already references xUnit 2.9.2, Moq 4.20.72, FluentAssertions 6.12.0, and the `Anela.Heblo.Application` + `Anela.Heblo.Domain` projects.
- The test directory `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/` already exists (contains the sibling test class).
- `SalesCostProvider` and all its collaborators (`ISalesCostCache`, `ICatalogRepository`, `ILedgerService`, `CostStatistics`, `CostCacheData`, `MonthlyCost`, `CatalogAggregate`, `DataSourceOptions`) are present and exportable from the application/domain assemblies.

Implementation can begin immediately.