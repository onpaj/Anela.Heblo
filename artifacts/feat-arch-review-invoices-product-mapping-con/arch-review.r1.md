Now I have enough context to write the architecture review.

# Architecture Review: Externalize Product Mapping Codes for Invoice Import Transformation

## Skip Design: true

## Architectural Fit Assessment

This refactor is a textbook fit for the codebase's existing Options pattern. The repo already standardizes on `*Options.cs` classes with a `SectionName` constant, bound via `services.AddOptions<T>().Bind(...).ValidateDataAnnotations().ValidateOnStart()` — see `MeetingTasksOptions`/`MeetingTasksModule` (`backend/src/Anela.Heblo.Application/Features/MeetingTasks/`), and the analogous pattern across `Bank`, `Catalog`, `Photobank`, `Article`, `Leaflet`, `Manufacture`, `OrgChart`, etc. The Invoices module is currently one of a small minority that does not accept `IConfiguration`; this change brings it into line with `ApplicationModule.cs:78-118`.

Integration points are minimal and contained:
1. `InvoicesModule.AddInvoicesModule(...)` — signature change.
2. `ApplicationModule.cs:95` — single call site update.
3. `appsettings.json` — additive config section.
4. New `ProductMappingOptions.cs` — new file, follows `MeetingTasksOptions.cs` style.

No domain-model, persistence, MediatR contract, or public API changes. The transformation class itself (`ProductMappingIssuedInvoiceImportTransformation.cs`) is untouched — its existing two-string constructor is exactly the seam this refactor uses.

## Proposed Architecture

### Component Overview

```
appsettings.json
  └─ "ProductMapping": { "ShoptetCode": "1287", "ErpCode": "SLU000001" }
                │
                ▼   bound via .AddOptions<T>().Bind(...).ValidateDataAnnotations().ValidateOnStart()
ProductMappingOptions   (new, Features/Invoices/ProductMappingOptions.cs)
                │
                ▼   resolved by DI factory (IOptions<ProductMappingOptions>)
InvoicesModule.AddInvoicesModule(IServiceCollection, IConfiguration)
                │
                ▼   constructs Transient instance per resolution
ProductMappingIssuedInvoiceImportTransformation(opts.ShoptetCode, opts.ErpCode)
                │
                ▼   enumerated by import pipeline alongside two siblings
IEnumerable<IIssuedInvoiceImportTransformation> consumers (InvoiceImportService)
```

### Key Design Decisions

#### Decision 1: Options class location
**Options considered:** (a) Place `ProductMappingOptions.cs` at the module root next to `InvoicesModule.cs`, mirroring `MeetingTasksOptions.cs`. (b) Place it under `Infrastructure/Transformations/` next to the consuming class.
**Chosen approach:** (a) — module root.
**Rationale:** Every existing options class in the codebase lives at module root (`MeetingTasksOptions.cs`, `OrgChartOptions.cs`, `CatalogCacheOptions.cs`, `PhotobankOptions.cs`, etc.). Consistency wins over physical co-location with the consumer. The options class is a configuration contract, not infrastructure plumbing.

#### Decision 2: Bind-and-validate vs. eager `.Value` resolution at registration time
**Options considered:** (a) Resolve `IOptions<ProductMappingOptions>.Value` inside the DI factory lambda (lazy, per-instantiation). (b) Resolve `configuration.GetSection(...).Get<ProductMappingOptions>()` once at registration time and capture in a closure.
**Chosen approach:** (a) — resolve `IOptions<T>` lazily inside the factory.
**Rationale:** Lazy resolution honors `ValidateOnStart()` and integrates with the host's options validation pipeline. Eager binding at registration time bypasses validation and short-circuits the options system. `IOptions<T>` resolution is O(1) and cached; the cost is negligible.

#### Decision 3: Pass `IConfiguration` to `AddInvoicesModule` (signature change) vs. resolving from `IServiceProvider` inside the factory
**Options considered:** (a) Change `AddInvoicesModule` signature to accept `IConfiguration`. (b) Keep the no-arg signature and resolve `IConfiguration` via `provider.GetRequiredService<IConfiguration>()` inside the transformation factory.
**Chosen approach:** (a) — signature change, accept `IConfiguration`.
**Rationale:** Every other configuration-bound module in `ApplicationModule.cs` takes `IConfiguration` explicitly. Hiding the configuration dependency inside the DI lambda is non-idiomatic and prevents the bind/validate pipeline from running at startup. The single call site is updated in the same PR; there is no external consumer.

#### Decision 4: Single-mapping options shape vs. list-of-mappings shape
**Options considered:** (a) `ProductMappingOptions { string ShoptetCode; string ErpCode; }` — exactly mirrors current behavior. (b) `ProductMappingOptions { List<Mapping> Mappings; }` — more general.
**Chosen approach:** (a) — single mapping.
**Rationale:** YAGNI. The spec explicitly puts list-of-mappings out of scope. The transformation class today maps one pair; expanding to a list would require also changing the transformation class and the registration cardinality (one transformation per mapping). Defer until business needs it.

#### Decision 5: Validation strategy
**Options considered:** (a) `[Required]` data annotations on both properties + `.ValidateDataAnnotations().ValidateOnStart()`. (b) Add a custom `.Validate(...)` lambda that also rejects whitespace-only values.
**Chosen approach:** (a) — data annotations only.
**Rationale:** `[Required]` on `string` rejects null and empty string, which is sufficient for the stated failure mode ("silently corrupt data via empty codes"). Whitespace-only values are an extremely unlikely operator typo and not called out in the spec. Match the simpler `MeetingTasksOptions` approach.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/Invoices/
├── InvoicesModule.cs                   ← modified (signature + registration)
├── ProductMappingOptions.cs            ← NEW
├── Contracts/
├── Infrastructure/
│   └── Transformations/
│       └── ProductMappingIssuedInvoiceImportTransformation.cs   ← UNCHANGED
└── ...

backend/src/Anela.Heblo.Application/
└── ApplicationModule.cs                ← modified (line 95: pass `configuration`)

backend/src/Anela.Heblo.API/
└── appsettings.json                    ← modified (add "ProductMapping" section)

backend/test/Anela.Heblo.Tests/Features/Invoices/
└── InvoicesModuleTests.cs              ← NEW (wiring tests; FR-6)
```

### Interfaces and Contracts

**New file** — `ProductMappingOptions.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Invoices;

public class ProductMappingOptions
{
    public const string SectionName = "ProductMapping";

    [Required]
    public string ShoptetCode { get; set; } = string.Empty;

    [Required]
    public string ErpCode { get; set; } = string.Empty;
}
```

**Modified** — `InvoicesModule.cs` (relevant section only):

```csharp
public static IServiceCollection AddInvoicesModule(
    this IServiceCollection services,
    IConfiguration configuration)
{
    services.AddOptions<ProductMappingOptions>()
        .Bind(configuration.GetSection(ProductMappingOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    // ... existing registrations unchanged ...

    services.AddTransient<IIssuedInvoiceImportTransformation, GiftWithoutVATIssuedInvoiceImportTransformation>();
    services.AddTransient<IIssuedInvoiceImportTransformation, RemoveDAtTheEndOfProductCodeIssuedInvoiceImportTransformation>();
    services.AddTransient<IIssuedInvoiceImportTransformation>(provider =>
    {
        var opts = provider.GetRequiredService<IOptions<ProductMappingOptions>>().Value;
        return new ProductMappingIssuedInvoiceImportTransformation(opts.ShoptetCode, opts.ErpCode);
    });

    return services;
}
```

Required new `using`s: `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Options`.

**Modified** — `ApplicationModule.cs:95`:

```csharp
services.AddInvoicesModule(configuration);
```

### Data Flow

Startup:
1. Host builds `IConfiguration` from `appsettings.json` + environment-specific overrides.
2. `ApplicationModule.AddApplicationServices` calls `AddInvoicesModule(configuration)`.
3. `AddOptions<ProductMappingOptions>().Bind(...).ValidateDataAnnotations().ValidateOnStart()` registers the options binding + a startup validation hook.
4. Host runs startup validation. If `ProductMapping` is missing or fields are empty, `OptionsValidationException` is thrown and the app fails to start.

Runtime (per invoice import):
1. `InvoiceImportService` resolves `IEnumerable<IIssuedInvoiceImportTransformation>`.
2. For each Transient registration, DI invokes the factory. The product-mapping factory resolves `IOptions<ProductMappingOptions>`, reads `.Value` (cached singleton), and constructs the transformation.
3. Transformations run in registration order: `GiftWithoutVAT` → `RemoveDAtTheEnd` → `ProductMapping`. Order is preserved.
4. Each invoice item with `Code == opts.ShoptetCode` has its code rewritten to `opts.ErpCode`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Startup validation triggers in test environments that don't supply `ProductMapping` (causing unrelated test failures across the API project's `WebApplicationFactory`-based suites). | Medium | Add `"ProductMapping"` section to `appsettings.Test.json` (and `appsettings.Conductor.json` if it is used as a base for `WebApplicationFactory`). Audit all `appsettings.*.json` files used at test startup before declaring done. **This is the most likely landmine** — the spec says "no other appsettings.*.json is changed" but that assumption may not survive contact with the test bootstrap. |
| Transformation registration order changes silently between PR branches due to file-level edits, altering import behavior. | Low | Preserve the literal order: `GiftWithoutVAT` (line 53), `RemoveDAtTheEnd` (line 54), `ProductMapping` (line 57-58). Add an explicit code comment if order is significant for correctness (the chained transformations may not be order-dependent, but preserving today's order is the safe default). |
| Removing the no-arg `AddInvoicesModule()` overload silently breaks an unknown caller (e.g., test fixture). | Low | Grep the entire repo for `AddInvoicesModule()` before merge. The spec asserts the only known caller is `ApplicationModule.cs:95`; verify. |
| `IOptions<T>` injected via `provider.GetRequiredService<IOptions<...>>()` is captured in the factory closure but resolved per-Transient — fine for correctness but could mask subsequent options-binding bugs (e.g., a future switch to `IOptionsMonitor`). | Low | Use `IOptions<T>` (not `IOptionsSnapshot` or `IOptionsMonitor`) — values are static for the process lifetime; no hot-reload requested. Document the choice with a one-line comment in the factory if it aids future maintainers, but only if non-obvious. |

## Specification Amendments

1. **FR-3 amendment (test settings).** The spec asserts "No other `appsettings.*.json` file is changed." Verify against test bootstrap behavior before locking this in. If `Anela.Heblo.API`'s `WebApplicationFactory` is used by any integration test and `appsettings.Test.json` overrides the base file completely (or is loaded in a context where `appsettings.json` isn't), the `ProductMapping` section must also be present there to avoid `OptionsValidationException` at test startup. **Action for implementer:** add the same `ProductMapping` block to `appsettings.Test.json` if any of `InvoiceImportIntegrationTests` or other `WebApplicationFactory`-based tests in `backend/test/Anela.Heblo.Tests/` boot the full host. Otherwise leave it out.

2. **FR-6 clarification (second test).** The spec asks for a test that "asserts that omitting the `ProductMapping` section causes the host build / options validation to fail." Note that `.ValidateOnStart()` only triggers on `IHost.StartAsync()`, not on `ServiceCollection.BuildServiceProvider()`. The test must therefore:
   - Use `Host.CreateDefaultBuilder()` + `host.StartAsync()` (or a minimal equivalent), **or**
   - Manually invoke the validation by resolving `IOptions<ProductMappingOptions>.Value` after building the provider and asserting `OptionsValidationException` is thrown. The second approach is simpler and sufficient for proving the wiring.

3. **No other amendments.** The remaining FRs and NFRs are accurate, executable, and consistent with codebase conventions.

## Prerequisites

None. The codebase already has:
- `Microsoft.Extensions.Options` and `Microsoft.Extensions.Configuration` transitively referenced in `Anela.Heblo.Application` (used by all other options-pattern modules).
- `System.ComponentModel.DataAnnotations` available (used by `MeetingTasksOptions`).
- An established Options pattern with `SectionName` constants, `AddOptions<T>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`, and identically-shaped sibling modules to copy from.
- The transformation class's constructor already accepts both codes — the necessary seam exists and is unchanged by this work.

Implementation can start immediately.