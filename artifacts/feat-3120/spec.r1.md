# Specification: Unit Tests for FlexiStockTakingDomainService

## Summary

`FlexiStockTakingDomainService.SubmitStockTakingAsync` is a write path against the live FlexiBee ERP with four untested decision branches and a silent catch block that swallows exceptions into an `Error` field. This specification defines the unit test suite that brings line coverage above the 60% threshold and validates all behavioral contracts, including the silent-failure risk in the exception path.

## Background

The weekly coverage-gap routine (CI run #27416879267, 2026-06-15) flagged `FlexiStockTakingDomainService` at 16.7% line coverage against a 60% project threshold. The service orchestrates ERP stock-taking documents via `IStockTakingClient` and `IStockTakingItemsClient`. Because it touches a live ERP (no sandbox), correctness of the branching logic must be verified through unit tests with mocked clients rather than integration tests. The most critical risk is the `catch` block (line 106): any ERP exception returns a `StockTakingRecord` whose `AmountOld` equals the submitted amount — structurally identical to a successful soft-stock-taking result — while the failure is recorded only in the nullable `Error` property. Callers that do not inspect `Error` silently treat failed ERP writes as successes.

## Functional Requirements

### FR-1: SoftStockTaking branch — no ERP calls, AmountNew equals AmountOld

When every lot in `order.StockTakingItems` has `SoftStockTaking == true` (which makes `order.SoftStockTaking` return `true`), the method must:
- Not call `IStockTakingClient.CreateHeaderAsync`
- Not call `IStockTakingItemsClient.AddStockTakingsAsync`
- Not call `IStockTakingClient.SubmitAsync`
- Not call `IStockTakingItemsClient.GetStockTakingsAsync`
- Return a `StockTakingRecord` where `AmountNew == AmountOld` (both equal the sum of requested amounts)
- Save the record to the repository (`AddAsync` + `SaveChangesAsync` called once each)
- Set `Type = StockTakingType.Erp`, `User`, and `Date` on the returned record

**Acceptance criteria:**
- Test: `SoftStockTaking_NoErpCalls_AmountNewEqualsAmountOld`
- Given a request with all lots having `SoftStockTaking = true`, when `SubmitStockTakingAsync` is called, then `IStockTakingClient` has zero invocations, `IStockTakingItemsClient` has zero invocations, and the returned record satisfies `AmountNew == AmountOld == (double)sum(lot.Amount)`.
- `_stockTakingRepository.AddAsync` is called exactly once with the returned record.
- `_stockTakingRepository.SaveChangesAsync` is called exactly once.

### FR-2: Real ERP path (SoftStockTaking=false, DryRun=false) — full submit

When `SoftStockTaking` is false and `DryRun` is false, the method must:
- Call `CreateHeaderAsync` exactly once with `WarehouseId = 5`, the current UTC date, the current user name, `Owner = "Heblo"`, and `Type = "Material-{ProductCode}"`
- Call `AddStockTakingsAsync` exactly once with the header ID and the mapped items
- Call `SubmitAsync` exactly once with the header ID and document type 60
- Return a record with `AmountNew` sourced from `itemsAfter.Sum(AmountFound)` and `AmountOld` from `itemsBefore.Sum(AmountErp)`
- Save the record to the repository

**Acceptance criteria:**
- Test: `RealErpPath_DryRunFalse_SubmitAsyncCalledOnce`
- `_stockTakingClient.SubmitAsync` is verified called once with the correct header ID and `60`.
- Returned record has `AmountNew` and `AmountOld` values sourced from the mock responses (not from the request amounts).
- Repository `AddAsync` and `SaveChangesAsync` each called once.

### FR-3: DryRun=true — ERP document created but not submitted

When `SoftStockTaking` is false and `DryRun` is true, the method must:
- Call `CreateHeaderAsync`, `AddStockTakingsAsync`, and the final `GetHeaderAsync` + `GetStockTakingsAsync` pair
- NOT call `SubmitAsync`
- Still save the record to the repository

**Acceptance criteria:**
- Test: `RealErpPath_DryRunTrue_SubmitAsyncNotCalled`
- `_stockTakingClient.SubmitAsync` is verified with `Times.Never`.
- All other ERP calls (`CreateHeaderAsync`, `AddStockTakingsAsync`, `GetHeaderAsync`, `GetStockTakingsAsync`) are verified called at least once.
- Repository `SaveChangesAsync` called once.

### FR-4: RemoveMissingLots=true — missing lots are fetched and added

When `SoftStockTaking` is false and `RemoveMissingLots` is true, after adding items the method must:
- Call `GetStockTakingsAsync` to fetch current items for the header
- Call `AddMissingLotsAsync` with the header ID and the list of product IDs from current items

**Acceptance criteria:**
- Test: `RemoveMissingLots_True_AddMissingLotsAsyncCalled`
- `_stockTakingItemsClient.GetStockTakingsAsync` is verified called at least once before `_stockTakingClient.AddMissingLotsAsync`.
- `_stockTakingClient.AddMissingLotsAsync` is verified called exactly once with the header ID from `CreateHeaderAsync` response.
- Test: `RemoveMissingLots_False_AddMissingLotsAsyncNotCalled`
- `_stockTakingClient.AddMissingLotsAsync` is verified with `Times.Never`.

### FR-5: Exception path — Error field set, repository not called

When any ERP client call throws an exception, the method must:
- NOT re-throw the exception
- Return a `StockTakingRecord` with `Error` set to the exception message (non-null, non-empty)
- NOT call `_stockTakingRepository.AddAsync`
- NOT call `_stockTakingRepository.SaveChangesAsync`
- Set `Code = order.ProductCode` and `Date` on the error record

**Acceptance criteria:**
- Test: `ErpThrows_ReturnedRecordHasErrorSet_RepositoryNotCalled`
- `_stockTakingClient.CreateHeaderAsync` throws `Exception("ERP connection failed")`.
- Returned record: `Error == "ERP connection failed"`, `Code == order.ProductCode`, `Error` is not null or whitespace.
- `_stockTakingRepository.AddAsync` verified `Times.Never`.
- `_stockTakingRepository.SaveChangesAsync` verified `Times.Never`.
- Test variant: `SubmitAsyncThrows_ReturnedRecordHasErrorSet` — `SubmitAsync` throws, same assertions.

### FR-6: Test project placement and naming

All tests live in the existing `Anela.Heblo.Adapters.Flexi.Tests` project (path: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/`) in a new file `Stock/FlexiStockTakingDomainServiceTests.cs`, consistent with the existing `Stock/FlexiStockClientTests.cs` neighbor.

**Acceptance criteria:**
- New file is at `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Stock/FlexiStockTakingDomainServiceTests.cs`.
- Namespace is `Anela.Heblo.Adapters.Flexi.Tests.Stock`.
- Test class is `FlexiStockTakingDomainServiceTests`.
- All test methods follow `MethodName_Condition_ExpectedBehavior` naming already used in the project.

## Non-Functional Requirements

### NFR-1: Performance

Tests must complete in under 2 seconds total. All dependencies are mocked; no network calls or database I/O.

### NFR-2: Security

No real FlexiBee credentials or ERP endpoints are used. `IStockTakingClient` and `IStockTakingItemsClient` are mocked via Moq.

### NFR-3: Coverage target

After the test suite is added, `dotnet test` with coverlet must report line coverage for `FlexiStockTakingDomainService` at or above 60% (project threshold). The five test scenarios described above cover all executable lines in `SubmitStockTakingAsync`, which constitutes the entirety of the class; coverage should reach approximately 90%+.

### NFR-4: Build compliance

Running `dotnet build` and `dotnet format` must succeed with no warnings introduced by the new test file.

## Data Model

No new persistent entities. The tests exercise the existing domain types:

| Type | Role |
|---|---|
| `ErpStockTakingRequest` | Input to `SubmitStockTakingAsync`; has `ProductCode`, `StockTakingItems`, `RemoveMissingLots`, `DryRun`; `SoftStockTaking` is computed from all-lots predicate |
| `ErpStockTakingLot` | One lot line; has `LotCode`, `Expiration`, `Amount`, `SoftStockTaking` |
| `StockTakingRecord` | Return value and persisted entity; has `Code`, `AmountNew`, `AmountOld`, `Date`, `User`, `Type`, `Error` |
| `StockTakingType` | Enum; tests assert `Type == StockTakingType.Erp` on success paths |

## API / Interface Design

No new API surface. The tests interact with the existing interfaces under test:

**`IStockTakingClient` (mocked)**
- `CreateHeaderAsync(StockTakingHeaderRequest) → StockTakingHeader`
- `GetHeaderAsync(int headerId) → StockTakingHeader`
- `AddMissingLotsAsync(int headerId, List<int> productIds) → Task`
- `SubmitAsync(int headerId, int documentTypeId) → Task`

**`IStockTakingItemsClient` (mocked)**
- `AddStockTakingsAsync(int headerId, int warehouseId, List<AddStockTakingItemRequest>) → Task`
- `GetStockTakingsAsync(int headerId) → List<StockTakingItem>`

**`IStockTakingRepository` (mocked)**
- `AddAsync(StockTakingRecord) → Task`
- `SaveChangesAsync() → Task`

**`ICurrentUserService` (mocked)**
- `GetCurrentUser() → User` returning a stub with a `Name` property

**`TimeProvider` (use `TimeProvider.System` or a fixed fake)**

## Dependencies

- `Anela.Heblo.Adapters.Flexi.Tests.csproj` — already references the production `Anela.Heblo.Adapters.Flexi` project and has Moq, FluentAssertions, xUnit
- `Rem.FlexiBeeSDK.Client` — model types (`StockTakingHeaderRequest`, `StockTakingHeader`, `AddStockTakingItemRequest`, `StockTakingItem`) needed for mock setup; already a transitive dependency
- `ICurrentUserService` from `Anela.Heblo.Domain.Features.Users` — mock returns a stub user
- No new NuGet packages required

## Out of Scope

- Integration tests against a live or containerised FlexiBee instance
- Testing the `IStockTakingRepository` persistence implementation
- Testing the mapping profile (`FlexiStockMappingProfile`)
- Testing `FlexiStockClient` (already tested in `FlexiStockClientTests.cs`)
- Refactoring the silent catch block — this spec covers test coverage only; any decision to change the exception-handling contract is a separate work item
- The `// TODO Convert to decimal` comments in the source file — out of scope for this task

## Open Questions

None.

## Status: COMPLETE
