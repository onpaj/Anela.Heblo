## Module
MarketingInvoices

## Finding
`ImportMarketingInvoicesHandler` declares a concrete dependency on `MarketingInvoiceImportService` rather than an interface:

```
backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/ImportMarketingInvoices/ImportMarketingInvoicesHandler.cs
  line 12:  private readonly MarketingInvoiceImportService _importService;
  line 17:      MarketingInvoiceImportService importService,
```

There is no `IMarketingInvoiceImportService` interface in `Services/`. The service is also registered as a concrete type:

```
backend/src/Anela.Heblo.Application/Features/MarketingInvoices/MarketingInvoicesModule.cs
  line 13:  services.AddScoped<MarketingInvoiceImportService>();
```

As a result, the handler test (`ImportMarketingInvoicesHandlerTests.cs`, lines 14–15) instantiates the real `MarketingInvoiceImportService` and a mocked repository rather than isolating the handler under test. Any failure in the service logic surfaces as a handler test failure, obscuring which component is at fault.

## Why it matters
Violates the Dependency Inversion Principle: a high-level policy (the MediatR handler) depends on a concrete low-level detail (the service implementation). The handler cannot be tested in isolation from the service, and the import strategy cannot be swapped without modifying the handler.

## Suggested fix
1. Add `IMarketingInvoiceImportService` to `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/` with the single method:
   ```csharp
   Task<MarketingImportResult> ImportAsync(IMarketingTransactionSource source, DateTime from, DateTime to, CancellationToken ct = default);
   ```
2. Implement it on `MarketingInvoiceImportService`.
3. Change the handler field/parameter to `IMarketingInvoiceImportService`.
4. Update DI registration: `services.AddScoped<IMarketingInvoiceImportService, MarketingInvoiceImportService>()`.

---
_Filed by daily arch-review routine on 2026-05-25._