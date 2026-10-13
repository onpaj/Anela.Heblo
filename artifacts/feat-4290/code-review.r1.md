## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

## Notes

Reviewed the full feature diff (`main`...`HEAD`, merge-base `22bb3b8f`) covering both completed tasks:

1. **`relocate-adapter-and-di-registration`** — `IssuedInvoiceMonthlyRevenueSource` moved via `git mv` from `Anela.Heblo.Persistence.Marketing` to `Anela.Heblo.Persistence.Invoices` (class body byte-for-byte unchanged except namespace + an explanatory adapter comment); DI registration moved from `MarketingPerformanceModule.AddMarketingPerformanceModule` to `InvoicesModule.AddInvoicesModule`, matching the existing `IInvoiceConsumptionSource`/`IInvoiceImportStatisticsSource` provider-owns-adapter pattern (comment style included). `MarketingPerformanceModule.cs` deliberately keeps `using Anela.Heblo.Persistence.Marketing;` because `MarketingPerformanceRepository` — an unrelated, same-module type — also lives in that namespace; removing it would have broken the build. This is a documented, correct deviation from the literal task-context text, not a re-introduction of the violation being fixed. `IssuedInvoiceMonthlyRevenueSourceTests.cs`'s `using` was updated to the new namespace; test body unchanged.

2. **`add-marketingperformance-invoices-boundary-tests`** — Added `MarketingPerformanceInvoicesAllowlist` (empty) and two `ModuleBoundaryRule` entries (`"MarketingPerformance (Application) -> Invoices"`, `"MarketingPerformance (Domain) -> Invoices"`) to `ModuleBoundariesTests.cs`, mirroring the existing `Analytics (Application/Domain) -> Invoices` pair exactly — correctly split into two rules since `ModuleBoundaryRule` only supports one `InspectedNamespacePrefix`/`InspectedAssembly` per entry, consistent with `design.r1.md` Decision 2.

Both `IMonthlyRevenueSource` (contract, `Anela.Heblo.Domain.Features.MarketingPerformance`) and `MonthlyRevenueSnapshot`/`YearMonth` are unchanged, as required. Both `AddMarketingPerformanceModule` and `AddInvoicesModule` are still invoked from `ApplicationModule.cs`, order-independent (only `InvoicesModule` now registers `IMonthlyRevenueSource`, confirmed no duplicate registration remains).

### Verification performed
- `dotnet build Anela.Heblo.sln`: `0 Error(s)`, `248 Warning(s)` — all pre-existing, none in the five touched files (`IssuedInvoiceMonthlyRevenueSource.cs`, `MarketingPerformanceModule.cs`, `InvoicesModule.cs`, `ModuleBoundariesTests.cs`, `IssuedInvoiceMonthlyRevenueSourceTests.cs`).
- `dotnet test --filter "FullyQualifiedName~ModuleBoundariesTests|FullyQualifiedName~IssuedInvoiceMonthlyRevenueSourceTests|FullyQualifiedName~MarketingPerformanceRefreshServiceTests"`: `Passed! - Failed: 0, Passed: 52, Skipped: 0, Total: 52`, including both new boundary-rule theory cases (`MarketingPerformance (Application) -> Invoices`, `MarketingPerformance (Domain) -> Invoices`) individually confirmed passing.

### Process note (non-blocking)
`state.json` marks task `add-marketingperformance-invoices-boundary-tests` as `completed`, and its code (the `ModuleBoundariesTests.cs` changes) is present and correct on the branch, matching its task-context plan and commit message exactly. However, no `impl/add-marketingperformance-invoices-boundary-tests.r1.md` or `review/add-marketingperformance-invoices-boundary-tests.r1.md` artifact exists for that task (unlike task 1, which has both). This looks like an artifact-writing gap from whatever ran that task, not a code defect — flagging for visibility, not blocking this review.
