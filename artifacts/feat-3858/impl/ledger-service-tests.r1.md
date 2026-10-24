# ledger-service-tests r1
- Added Accounting/LedgerServiceSingleFlightTests.cs (Flexi.Tests): 10 parallel calls -> 1 client call and shared result; failed and canceled shared requests not cached (retry); caller cancel leaves other waiter served; closed/open TTL boundaries via FakeTimeProvider + MemoryCache driven by a fake ISystemClock; QueryCompleted log with rows/spanDays.
- Added Accounting/LedgerServiceFinancialAnalysisTests.cs (FR-5): real LedgerService over counting fake ILedgerClient, 3 overlapping FinancialAnalysisService runs; asserts calls <= distinct tuples. Placed in Flexi.Tests (not Anela.Heblo.Tests) because it needs the real LedgerService and that project already references Flexi + Application.
- Added Microsoft.Extensions.TimeProvider.Testing 8.1.0 to Flexi.Tests csproj.
- Existing timeout/caller-cancel log tests remain in Accounting/LedgerServiceTests.cs. `dotnet test --filter "LedgerService|FinancialAnalysis"` on Flexi.Tests: 10/10 pass; dotnet format clean.
