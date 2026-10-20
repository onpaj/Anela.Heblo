# Design: LedgerService coverage gap tests

## Component Design
Tests (xUnit, Arrange/Act/Assert) in `LedgerServiceTests` namespace `Anela.Heblo.Adapters.Flexi.Tests.Accounting`:
- `GetLedgerItems_SecondCallWithSameArgs_UsesCacheAndCallsClientOnce`
- `GetLedgerItems_DifferentKeyComponents_CallClientSeparately` (Theory: department, debit prefix, credit prefix, dates)
- `GetCosts_MixedDatesAndDepartments_AggregatesPerDayAndDepartmentOrderedByDate`
- `GetCosts_EmptyLedger_ReturnsEmptyList`
- `GetCosts_PassesPrefixesAndDepartmentToClient`
- `GetPersonalCosts_UsesPrefix52` / `GetDirectCosts_UsesPrefixes51And52`
Helper `CreateService(IMemoryCache cache, IList<LedgerItem> mapped)` builds a LedgerService with a real MemoryCache and mapper mock returning `mapped`.

## Data Schemas
No schemas. Test data: `LedgerItem { Date, Department, Amount }` items spanning same-day different times (e.g. 10:00 and 23:59) and next-day, two departments.
