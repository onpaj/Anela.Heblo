### task: backend-dto-extraction

**Goal:** Remove `IsBelowThreshold` from the Domain `DailyInvoiceCount`, add `DailyInvoiceCountDto` to the Analytics `Contracts/` folder, move the threshold computation into `GetInvoiceImportStatisticsHandler`, fix the now-dead `IsBelowThreshold = false` initializers in `InvoiceImportStatisticsSourceAdapter`, and update all affected backend tests.

**Files to change:**
- `backend/src/Anela.Heblo.Domain/Features/Analytics/DailyInvoiceCount.cs` — remove the `IsBelowThreshold` property/setter; keep only `Date` (DateTime) and `Count` (int).
- `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/DailyInvoiceCountDto.cs` — new file. Plain C# class (not a record), namespace `Anela.Heblo.Application.Features.Analytics.Contracts`, properties `Date` (DateTime), `Count` (int), `IsBelowThreshold` (bool), public getters/setters, matching the style of the sibling `TopProductDto.cs` in the same folder.
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetInvoiceImportStatistics/GetInvoiceImportStatisticsHandler.cs` — replace the in-place mutation loop over `DailyInvoiceCount` with a `Select` projection into `DailyInvoiceCountDto`, computing `IsBelowThreshold = c.Count < minimumThreshold` (same `<` semantics) at projection time. Add `using Anela.Heblo.Application.Features.Analytics.Contracts;`; drop the `using` for the Domain namespace if it becomes unused in this file.
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetInvoiceImportStatistics/GetInvoiceImportStatisticsResponse.cs` — change `Data` from `List<DailyInvoiceCount>` to `List<DailyInvoiceCountDto>`; update `using` accordingly.
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapter.cs` — remove the `IsBelowThreshold = false` initializer from all three `new DailyInvoiceCount { ... }` construction sites (approx. lines 47–52, 71–76, 92–97).
- `backend/src/Anela.Heblo.Domain/Features/Analytics/IInvoiceImportStatisticsSource.cs` — edit the XML doc comment on `GetDailyCountsAsync` to remove the trailing clause about `IsBelowThreshold` always being `false`.
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetInvoiceImportStatisticsHandlerTests.cs` — drop `IsBelowThreshold = false` from the mocked `DailyInvoiceCount` initializers (repository mock still returns Domain-typed objects, now without the property). Keep the assertions on `result.Data[0].IsBelowThreshold` / `result.Data[1].IsBelowThreshold`, now read off `DailyInvoiceCountDto` instances in `result.Data`.
- `backend/test/Anela.Heblo.Tests/Features/Analytics/DashboardTiles/InvoiceImportStatisticsTileTests.cs` — drop `IsBelowThreshold = false` from the two `DailyInvoiceCount` literals (lines ~41, ~79); no assertions reference the flag in this file.
- `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapterTests.cs` — delete the single assertion line `result[0].IsBelowThreshold.Should().BeFalse();` (line ~73, inside `GetDailyCountsAsync_InvoiceDateBranch_ReturnsCountsGroupedByDay`); no replacement assertion, no other changes.

**Steps:**
1. Remove `IsBelowThreshold` from `DailyInvoiceCount.cs` (Domain).
2. Create `DailyInvoiceCountDto.cs` in `Application/Features/Analytics/Contracts/`, matching `TopProductDto.cs` style.
3. Update `GetInvoiceImportStatisticsResponse.cs` so `Data` is `List<DailyInvoiceCountDto>`.
4. Update `GetInvoiceImportStatisticsHandler.cs` to project `DailyInvoiceCount` → `DailyInvoiceCountDto` via `Select`, computing `IsBelowThreshold` there instead of mutating.
5. Remove the three dead `IsBelowThreshold = false` initializers in `InvoiceImportStatisticsSourceAdapter.cs`.
6. Update the XML doc on `IInvoiceImportStatisticsSource.GetDailyCountsAsync` to drop the now-inapplicable sentence about `IsBelowThreshold`.
7. Update the three backend test files as described above so they compile and continue to assert equivalent behavior.
8. Run `dotnet build` from `backend/` and confirm no remaining references to `DailyInvoiceCount.IsBelowThreshold` anywhere in the solution.
9. Run `dotnet format` from `backend/` to apply formatting conventions.
10. Run the affected test suites and confirm they pass.

**Acceptance criteria:**
- `dotnet build` succeeds with zero errors/warnings related to `DailyInvoiceCount.IsBelowThreshold`.
- `dotnet format` reports no outstanding changes (or has been applied) for touched files.
- `DailyInvoiceCount` (Domain) has only `Date` and `Count`; no `IsBelowThreshold` property or setter anywhere in `backend/src/Anela.Heblo.Domain/Features/Analytics/DailyInvoiceCount.cs`.
- `DailyInvoiceCountDto` exists at `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/DailyInvoiceCountDto.cs`, is a `class` (not a `record`), with `Date` (DateTime), `Count` (int), `IsBelowThreshold` (bool).
- `GetInvoiceImportStatisticsResponse.Data` is `List<DailyInvoiceCountDto>`.
- Run `dotnet test --filter "FullyQualifiedName~GetInvoiceImportStatisticsHandlerTests"` (in `backend/test/Anela.Heblo.Tests`) — all pass, including `Handle_ShouldReturnStatisticsWithMinimumThreshold` asserting `Count = 15`/threshold `10` → `IsBelowThreshold == false`, and `Count = 5` → `IsBelowThreshold == true`, read from `DailyInvoiceCountDto` objects.
- Run `dotnet test --filter "FullyQualifiedName~InvoiceImportStatisticsTileTests"` — all pass.
- Run `dotnet test --filter "FullyQualifiedName~InvoiceImportStatisticsSourceAdapterTests"` — all pass, with no assertion remaining on `IsBelowThreshold`.
- No production or test code outside this task's listed files references `DailyInvoiceCount.IsBelowThreshold`; verified by `dotnet build` failing loudly on any miss (compile error, not silent).
- Serialized JSON shape of the `GET` invoice import statistics response is unchanged (`date`, `count`, `isBelowThreshold`, `minimumThreshold` fields) — no controller or route changes were made.
