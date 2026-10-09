# Design: Remove stale IMarginCalculationService comment from AnalyticsModule

## Component Design
No new or restructured components. The only touched unit is `AnalyticsModule.AddAnalyticsModule(IServiceCollection, IConfiguration)` in
`backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs`, whose responsibility (registering Analytics' DI services, validators, and MediatR pipeline behaviors) is unchanged. The single edit is the removal of this line, currently sitting between the `IProductFilterService` and `IReportBuilderService` registrations:

```csharp
// Note: IMarginCalculationService is registered by CatalogModule and injected here
```

No interface, class, or method signature changes. `IMarginCalculator` (Analytics' actual margin-calculation abstraction, registered at line 47 of the same file) and `IMarginCalculationService` (Catalog's own abstraction, registered in `CatalogModule.cs`) are both left exactly as they are today.

## Data Schemas
Not applicable. No database schema, API request/response shape, or event payload is created, changed, or removed by this change.
