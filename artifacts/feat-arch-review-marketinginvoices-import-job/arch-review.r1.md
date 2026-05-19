# Architecture Review: Refactor MarketingInvoices Import Jobs to Use MediatR and DI

## Skip Design: true

Backend-only refactor. No new visual components, screens, or layouts. No UI/UX work required.

## Architectural Fit Assessment

The feature is a **conformance refactor** — it brings two outlier jobs into line with a pattern the project already enforces everywhere else. Verified against the codebase:

- `DailyConsumptionJob` and `KnowledgeBaseIngestionJob` both inject `IMediator` + `IRecurringJobStatusChecker` + logger, run the enabled-check, dispatch a MediatR request, and catch-log-rethrow. The two ad-platform jobs are the only `IRecurringJob` implementations that `new` an application service.
- MediatR handlers are auto-registered: `ApplicationModule.cs:55` calls `RegisterServicesFromAssembly(typeof(ApplicationModule).Assembly)`. A new handler placed in the Application assembly is picked up with **zero registration code** — consistent with the "MediatR handlers are automatically registered by AddMediatR scan" comment in five existing modules.
- Both adapter `.csproj` files already reference `Anela.Heblo.Application`, so the jobs can dispatch `ImportMarketingInvoicesRequest` without new project references.
- `IMarketingTransactionSource` lives in `Anela.Heblo.Domain` — the Application-layer handler depending on it is layer-clean. Both concrete sources already implement it and expose a `Platform` string.

The proposal fits cleanly. Two integration nuances need explicit handling (covered in Design Decisions): the **adapter source DI lifetimes differ** (MetaAds is a typed `HttpClient`, GoogleAds is an explicit scoped factory with an `internal` constructor), and the **exception/Hangfire-retry contract** must not be silently broken by the new handler.

## Proposed Architecture

### Component Overview

```
  Adapters.MetaAds              Adapters.GoogleAds
  ┌──────────────────────┐     ┌──────────────────────────┐
  │ MetaAdsInvoiceImport  │     │ GoogleAdsInvoiceImport    │
  │ Job : IRecurringJob   │     │ Job : IRecurringJob       │
  │  inject: IMediator,   │     │  inject: IMediator,       │
  │  IRecurringJobStatus  │     │  IRecurringJobStatus      │
  │  Checker, ILogger     │     │  Checker, ILogger         │
  └──────────┬───────────┘     └───────────┬──────────────┘
             │  _mediator.Send(ImportMarketingInvoicesRequest{Platform,From,To})
             └───────────────┬─────────────┘
                             ▼
   Application/Features/MarketingInvoices/UseCases/ImportMarketingInvoices
   ┌─────────────────────────────────────────────────────────────────┐
   │ ImportMarketingInvoicesHandler                                   │
   │  inject: IEnumerable<IMarketingTransactionSource>,               │
   │          MarketingInvoiceImportService (scoped, DI-managed),     │
   │          ILogger                                                 │
   │  1. select source where source.Platform == request.Platform     │
   │     (throw on none / on duplicate)                               │
   │  2. service.ImportAsync(source, From, To)                        │
   │  3. map MarketingImportResult → ImportMarketingInvoicesResponse  │
   └───────────────────────┬─────────────────────────────────────────┘
                            ▼
        MarketingInvoiceImportService  ──►  IImportedMarketingTransactionRepository
              (now DI-registered scoped)         (scoped, already registered)
```

Note one structural change to the service relationship: `MarketingInvoiceImportService` currently takes its `IMarketingTransactionSource` as a **constructor** dependency. Once the handler selects the source at runtime from an enumerable, the source can no longer be a constructor parameter of a DI-registered service. See Decision 2.

### Key Design Decisions

#### Decision 1: Source registration — forward the interface, keep concrete registrations as-is
**Options considered:**
- (a) Register each source only as `IMarketingTransactionSource`.
- (b) Keep the concrete registration and add a forwarding registration for the interface.

**Chosen approach:** (b). In each adapter's `ServiceCollectionExtensions`, keep the existing concrete registration and **add** a forwarding line:
```csharp
// MetaAds — concrete stays a typed HttpClient (transient); forward the interface
services.AddHttpClient<MetaAdsTransactionSource>();
services.AddScoped<IMarketingTransactionSource>(sp => sp.GetRequiredService<MetaAdsTransactionSource>());

// GoogleAds — concrete is an explicit scoped factory; forward the interface
services.AddScoped<GoogleAdsTransactionSource>(sp => new GoogleAdsTransactionSource(
    sp.GetRequiredService<IAccountBudgetFetcher>(),
    sp.GetRequiredService<ILogger<GoogleAdsTransactionSource>>()));
services.AddScoped<IMarketingTransactionSource>(sp => sp.GetRequiredService<GoogleAdsTransactionSource>());
```

**Rationale:** `MetaAdsTransactionSource` is registered via `AddHttpClient<T>()`, which makes the *typed client transient* — it cannot be registered directly as a scoped interface. `GoogleAdsTransactionSource` has an **`internal` constructor**, so `AddScoped<IMarketingTransactionSource, GoogleAdsTransactionSource>()` (reflection activation) would fail — a factory delegate is mandatory. Forwarding via `GetRequiredService<TConcrete>()` works for both and is the only uniform approach. **Correction to spec FR-3:** the phrase "sharing the same scoped instance as its concrete registration" does not hold for MetaAds — the concrete typed client is transient. This is harmless: after this refactor, nothing injects the concrete `MetaAdsTransactionSource`/`GoogleAdsTransactionSource` directly anymore (the jobs stop doing so), so only the forwarded `IMarketingTransactionSource` registration is actually consumed.

#### Decision 2: `MarketingInvoiceImportService` takes the source per-call, not per-construction
**Options considered:**
- (a) Keep `IMarketingTransactionSource` as a constructor dependency of the service; let DI pick "the" source.
- (b) Remove the source from the constructor; pass it as a parameter to `ImportAsync`.

**Chosen approach:** (b). Constructor becomes `(IImportedMarketingTransactionRepository, ILogger)`; signature becomes `ImportAsync(IMarketingTransactionSource source, DateTime from, DateTime to, CancellationToken)`.

**Rationale:** Two sources are registered against `IMarketingTransactionSource`. A constructor-injected service would get an ambiguous/last-wins resolution — wrong. The handler is the component that knows *which* platform was requested, so it must select the source and hand it to the service. This keeps `MarketingInvoiceImportService` a stateless, DI-managed scoped service (satisfying FR-2) while the per-import variable (the source) flows as a method argument. **This is a spec amendment** — the spec implies the service is registered and used unchanged, but registering it as scoped is incompatible with a constructor-injected `IMarketingTransactionSource` when two implementations exist.

#### Decision 3: The handler must NOT swallow import exceptions
**Options considered:**
- (a) Follow `ProcessDailyConsumptionHandler` exactly — catch all exceptions, return `Success = false`.
- (b) Let infrastructure exceptions (source fetch failures, DB failures) propagate; only fail-fast validation is distinct.

**Chosen approach:** (b). The handler does **not** wrap `service.ImportAsync(...)` in a catch-all. Exceptions propagate to the job, whose existing `catch → log → throw` re-surfaces them to Hangfire for retry.

**Rationale:** The spec explicitly requires "Hangfire retry semantics unchanged" (FR-5) and "catch-log-rethrow" preserved. Today, a failure inside `MarketingInvoiceImportService.ImportAsync` (e.g. `GetTransactionsAsync` throwing) propagates out of the job and triggers a Hangfire retry. If the new handler copies `ProcessDailyConsumptionHandler`'s catch-all, the exception becomes a `Success = false` response, the job sees no exception, never rethrows, and **retries silently stop happening** — a behavior regression. This is the single most important correctness constraint of the refactor. Per-transaction failures remain counted in `result.Failed` as today (the service already catches those internally).

#### Decision 4: Unknown / duplicate platform → throw `ArgumentException`/`InvalidOperationException`
**Chosen approach:** The handler resolves the source with `sources.Where(s => s.Platform == request.Platform)`. Zero matches or more than one match throws immediately, before any import work.

**Rationale:** An unknown or duplicate platform is a configuration/wiring error, not transient — retrying won't fix it, but it must be loud. Throwing (rather than returning a `BaseResponse` error) is consistent with Decision 3: the job's catch-log-rethrow surfaces it, and Hangfire's dashboard records the failure. `ImportMarketingInvoicesResponse` still inherits `BaseResponse` (project convention, FR-1) but in practice always carries `Success = true` on the returned path — failures are exceptions.

## Implementation Guidance

### Directory / Module Structure

New slice (Application assembly — auto-scanned by MediatR, no registration needed):
```
backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/ImportMarketingInvoices/
  ImportMarketingInvoicesRequest.cs
  ImportMarketingInvoicesResponse.cs
  ImportMarketingInvoicesHandler.cs
```

Modified files:
- `Application/Features/MarketingInvoices/MarketingInvoicesModule.cs` — add `services.AddScoped<MarketingInvoiceImportService>();`
- `Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` — move source from ctor to `ImportAsync` parameter (Decision 2).
- `Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsAdapterServiceCollectionExtensions.cs` — add `IMarketingTransactionSource` forwarding registration.
- `Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsAdapterServiceCollectionExtensions.cs` — add `IMarketingTransactionSource` forwarding registration.
- `Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsInvoiceImportJob.cs` — reduce to MediatR dispatcher.
- `Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsInvoiceImportJob.cs` — reduce to MediatR dispatcher.

### Interfaces and Contracts

DTOs are classes (project rule — never records for request/response types):

```csharp
// ImportMarketingInvoicesRequest.cs
public class ImportMarketingInvoicesRequest : IRequest<ImportMarketingInvoicesResponse>
{
    public string Platform { get; set; } = string.Empty;
    public DateTime From { get; set; }
    public DateTime To { get; set; }
}

// ImportMarketingInvoicesResponse.cs  (Anela.Heblo.Application.Shared.BaseResponse)
public class ImportMarketingInvoicesResponse : BaseResponse
{
    public string Platform { get; set; } = string.Empty;
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
}
```

Modified service contract:
```csharp
// MarketingInvoiceImportService
public MarketingInvoiceImportService(IImportedMarketingTransactionRepository repository,
                                     ILogger<MarketingInvoiceImportService> logger)
public Task<MarketingImportResult> ImportAsync(IMarketingTransactionSource source,
                                               DateTime from, DateTime to, CancellationToken ct = default)
```

**Platform-name constant.** The jobs must pass a platform literal but no longer construct the source. Add a `public const string PlatformName = "MetaAds";` (resp. `"GoogleAds"`) to each `*TransactionSource` class and have `Platform => PlatformName`. The job references `MetaAdsTransactionSource.PlatformName` — a compile-time constant, no instance, no coupling to construction. This keeps the literal single-sourced and avoids a magic string drifting between job and source. **Spec amendment:** the spec does not state where the job's platform literal comes from; this resolves it.

### Data Flow

Meta Ads import (Google Ads identical, different cron/platform):
1. Hangfire fires `MetaAdsInvoiceImportJob.ExecuteAsync` on cron `0 6,18 * * *`.
2. Job calls `_statusChecker.IsJobEnabledAsync("meta-ads-invoice-import")`; if disabled, log + return.
3. Job computes `to = DateTime.UtcNow`, `from = to.AddDays(-7)`.
4. Job dispatches `_mediator.Send(new ImportMarketingInvoicesRequest { Platform = MetaAdsTransactionSource.PlatformName, From = from, To = to })`.
5. Handler resolves the single `IMarketingTransactionSource` with matching `Platform` (throws on 0 or >1).
6. Handler calls `MarketingInvoiceImportService.ImportAsync(source, From, To, ct)` — fetch transactions, dedupe via repository `ExistsAsync`, persist new ones, per-transaction failures counted.
7. Handler maps `MarketingImportResult` → `ImportMarketingInvoicesResponse` (`Success = true`).
8. Job logs `Imported/Skipped/Failed`. Any exception from step 5–7 propagates → job catch logs → `throw` → Hangfire retry.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| New handler copies `ProcessDailyConsumptionHandler`'s catch-all and swallows exceptions, silently disabling Hangfire retries | HIGH | Decision 3 — handler does not catch import exceptions; add an explicit job test asserting an exception thrown by the source is rethrown by the job |
| `GoogleAdsTransactionSource` `internal` constructor breaks a reflection-based `AddScoped<IMarketingTransactionSource, GoogleAdsTransactionSource>()` | MEDIUM | Decision 1 — use a factory delegate for both forwarding registrations |
| A future third source registered with a duplicate `Platform` string causes the handler's `Single`-style selection to throw at runtime | LOW | Handler fails fast with a clear message naming the platform; covered by the duplicate-platform handler test (FR-6) |
| `MetaAdsTransactionSource` typed client is transient; if any code still injects the concrete type it gets a different instance than the forwarded interface | LOW | After refactor nothing injects the concrete sources; grep to confirm no remaining concrete usages before merge |
| Removing source from the service constructor breaks existing `MarketingInvoiceImportServiceTests` | MEDIUM | FR-6 already requires those tests stay green — update test arrangement to pass the source into `ImportAsync` (test-only signature change, behavior identical) |

## Specification Amendments

1. **FR-2 / Service signature.** `MarketingInvoiceImportService` cannot keep `IMarketingTransactionSource` as a constructor dependency once registered as scoped with two implementations present. Move the source to an `ImportAsync` parameter (Decision 2). The existing `MarketingInvoiceImportServiceTests` arrangement changes accordingly (still behavior-neutral).
2. **FR-3 wording.** "Sharing the same scoped instance as its concrete registration" is inaccurate for MetaAds (typed `HttpClient` is transient). Restate as: register a scoped `IMarketingTransactionSource` that forwards to the concrete registration via `GetRequiredService` (Decision 1).
3. **FR-1 / Handler exception behavior.** The handler must let import-time exceptions propagate (not convert them to `Success = false`), and must throw on unknown/duplicate platform. `ImportMarketingInvoicesResponse` inherits `BaseResponse` for convention but the returned path is always `Success = true` (Decision 3 & 4). Add this explicitly so an implementer does not default to the `ProcessDailyConsumptionHandler` catch-all idiom.
4. **FR-4/FR-5 / Platform literal.** Specify that each job passes its platform via a `const string PlatformName` declared on the corresponding `*TransactionSource` class, referenced as a compile-time constant — the job does not construct or inject the source.

## Prerequisites

None. No DB migration, no config keys, no infrastructure. The MediatR handler is auto-registered by the existing `ApplicationModule` assembly scan; both adapter projects already reference `Anela.Heblo.Application`. Implementation can start immediately. Validate with `dotnet build` + `dotnet format` and run `MarketingInvoiceImportServiceTests` plus the new handler/job tests before completion.