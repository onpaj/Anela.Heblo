### task: ledger-options-and-di
Add `FlexiLedgerOptions` and wire it in DI.
- Create `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Ledger/FlexiLedgerOptions.cs`: class (not record) with `const string SectionName = "FlexiLedger"`, `int ClosedPeriodCacheMinutes = 360`, `int OpenPeriodCacheMinutes = 15`.
- In `FlexiAdapterServiceCollectionExtensions.cs` add `services.Configure<FlexiLedgerOptions>(configuration.GetSection(FlexiLedgerOptions.SectionName))` next to the `ILedgerService` registration (use whatever `IConfiguration` handle that method already has; keep defaults in code, no appsettings change required).
- Verify: `dotnet build backend` succeeds.

