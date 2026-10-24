# ledger-options-and-di r1
- Added `FlexiLedgerOptions` (class, SectionName "FlexiLedger", ClosedPeriodCacheMinutes=360, OpenPeriodCacheMinutes=15).
- Registered `services.Configure<FlexiLedgerOptions>(configuration.GetSection(...))` after the `ILedgerService` registration in `FlexiAdapterServiceCollectionExtensions.cs`.
- `dotnet build` of Anela.Heblo.Adapters.Flexi: 0 errors. `dotnet format --verify-no-changes` on touched files: clean.
