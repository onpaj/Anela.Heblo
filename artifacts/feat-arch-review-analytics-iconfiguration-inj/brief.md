## Module
Analytics

## Finding
`GetInvoiceImportStatisticsHandler` (Application layer) directly injects `Microsoft.Extensions.Configuration.IConfiguration` and reads raw key paths from it:

```csharp
// GetInvoiceImportStatisticsHandler.cs, lines 12, 16, 28-29
using Microsoft.Extensions.Configuration;
...
private readonly IConfiguration _configuration;
...
var minimumThreshold = _configuration.GetValue<int>("InvoiceImport:MinimumDailyThreshold", 10);
var defaultDaysBack = _configuration.GetValue<int>("InvoiceImport:DefaultDaysBack", 14);
```

File: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetInvoiceImportStatistics/GetInvoiceImportStatisticsHandler.cs`, lines 12–29.

## Why it matters
`IConfiguration` is a Microsoft infrastructure type — it belongs in the composition root, not in Application-layer handlers. Injecting it here violates the Dependency Inversion Principle: the handler depends on a concrete infrastructure abstraction rather than a domain-owned options type. In practice it also makes the handler harder to unit-test (callers must set up a full `IConfiguration` mock with exact key paths instead of just supplying a typed options object).

## Suggested fix
1. Add a typed options class in the module:
   ```csharp
   // Application/Features/Analytics/AnalyticsOptions.cs
   public class InvoiceImportOptions
   {
       public int MinimumDailyThreshold { get; set; } = 10;
       public int DefaultDaysBack { get; set; } = 14;
   }
   ```
2. Register it in `AnalyticsModule.cs`:
   ```csharp
   services.Configure<InvoiceImportOptions>(configuration.GetSection("InvoiceImport"));
   ```
3. Replace `IConfiguration` with `IOptions<InvoiceImportOptions>` in the handler constructor.

---
_Filed by daily arch-review routine on 2026-06-07._