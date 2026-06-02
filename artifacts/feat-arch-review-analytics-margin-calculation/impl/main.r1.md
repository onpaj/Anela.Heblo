Implementation complete. Here's a summary:

## What was done

All 4 tasks implemented and reviewed:

**Files modified (production):**
- `MarginCalculator.cs` — added `CalculateForProduct(AnalyticsProduct, IEnumerable<SalesDataPoint>)` to interface and impl with XML `<remarks>` documenting the single-pass and caller-filters contracts
- `GetMarginReportHandler.cs` — injected `IMarginCalculator` (4th ctor param), replaced 16-line inline block with one call
- `GetProductMarginAnalysisHandler.cs` — injected `IMarginCalculator` (3rd ctor param), deleted `CalculateProductMargins` static method, removed redundant `.Where(date)` filter
- `ReportBuilderService.cs` — injected `IMarginCalculator` via ctor, replaced 4-line inline formula

**Files created (tests):**
- `MarginCalculatorTests.cs` — 9 unit tests (empty, B2B-only, B2C-only, mixed, zero price, zero margin, negative margin, large values, single-enumeration)
- `ReportBuilderServiceTests.cs` — zero-sales-month regression + valid-sales test

**Files updated (test wiring):**
- Both handler test files updated to inject `new MarginCalculator()` instead of mocks; all expected values unchanged

**One concern:** `dotnet` is not in the PATH in this agent environment, so build compilation was not verified locally. The code was reviewed by a code quality agent (PASS) and the patterns are standard C#. Run `dotnet build` + `dotnet test --filter "FullyQualifiedName~Analytics"` before merging.