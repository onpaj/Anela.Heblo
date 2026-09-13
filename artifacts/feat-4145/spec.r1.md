# Specification: GoogleAdsTransactionSource constructor visibility fix

## Summary
`GoogleAdsTransactionSource` (in `Anela.Heblo.Adapters.GoogleAds`) declares its constructor `internal`, which is inconsistent with the sibling `MetaAdsTransactionSource` (`public` constructor) and forces the DI registration to use a manual factory lambda instead of the standard `AddScoped<T>()` resolution. This change makes the constructor `public` and simplifies the DI registration accordingly, with no behavioral change.

## Background
`GoogleAdsAdapterServiceCollectionExtensions.AddGoogleAdsAdapter` currently registers `GoogleAdsTransactionSource` via:

```csharp
services.AddScoped<GoogleAdsTransactionSource>(sp =>
    new GoogleAdsTransactionSource(
        sp.GetRequiredService<IAccountBudgetFetcher>(),
        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GoogleAdsTransactionSource>>()));
```

This factory lambda exists only because the constructor is `internal`, bypassing the DI container's own constructor-resolution. The equivalent `MetaAdsAdapterServiceCollectionExtensions.AddMetaAdsAdapter` instead does:

```csharp
services.AddHttpClient<MetaAdsTransactionSource>();
services.AddScoped<IMarketingTransactionSource>(sp =>
    sp.GetRequiredService<MetaAdsTransactionSource>());
```

because `MetaAdsTransactionSource`'s constructor is `public`.

**Discovered nuance (not in the original issue body):** the adapter project already declares
`<InternalsVisibleTo Include="Anela.Heblo.Tests" />` in
`Anela.Heblo.Adapters.GoogleAds.csproj`, and a test file
(`backend/test/Anela.Heblo.Tests/Adapters/GoogleAds/GoogleAdsTransactionSourceTests.cs`) already
exists and successfully constructs `GoogleAdsTransactionSource` directly (`new(...)`) using that
`InternalsVisibleTo` grant. So the issue's claim that unit tests "cannot directly instantiate"
the type is not literally true today — a workaround (`InternalsVisibleTo`) is already in place.
However, the underlying architectural complaints remain valid and are the actual reason to make
this change:
- The `internal` modifier provides no real encapsulation since the type itself is `public` —
  any code in the assembly, or any assembly granted `InternalsVisibleTo`, can already construct
  it. It only exists to force the DI factory-lambda workaround in `GoogleAdsAdapterServiceCollectionExtensions`.
- The DI registration is inconsistent with `MetaAdsTransactionSource`'s pattern, and could be
  simplified to the standard `AddScoped<>()` overload once the constructor is `public`, without
  relying on `InternalsVisibleTo` for either compilation or DI.
- Consistency between the two adapters (which implement the same `IMarketingTransactionSource`
  interface and follow near-identical structure) reduces cognitive overhead for anyone navigating
  the codebase.

This is a small, self-contained architectural clean-up with **no logic change**.

## Functional Requirements

### FR-1: Make `GoogleAdsTransactionSource` constructor public
Change the constructor of `GoogleAdsTransactionSource`
(`backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsTransactionSource.cs`, line 17)
from `internal` to `public`. No other change to the constructor body, parameters, or the rest of
the class.

**Acceptance criteria:**
- The constructor signature is `public GoogleAdsTransactionSource(IAccountBudgetFetcher fetcher, ILogger<GoogleAdsTransactionSource> logger)`.
- All existing behavior of `GoogleAdsTransactionSource` (mapping logic in `GetTransactionsAsync`) is unchanged.
- The type remains `public` (unchanged).

### FR-2: Simplify DI registration to match the MetaAds pattern
In `GoogleAdsAdapterServiceCollectionExtensions.AddGoogleAdsAdapter`
(`backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsAdapterServiceCollectionExtensions.cs`),
replace the manual factory-lambda registration of `GoogleAdsTransactionSource` with the standard
`AddScoped<GoogleAdsTransactionSource>()` overload (container-resolved constructor), keeping the
subsequent `AddScoped<IMarketingTransactionSource>(sp => sp.GetRequiredService<GoogleAdsTransactionSource>())`
line as-is (this part already matches the MetaAds pattern).

**Acceptance criteria:**
- `services.AddScoped<GoogleAdsTransactionSource>();` replaces the factory-lambda registration.
- `services.AddScoped<IMarketingTransactionSource>(sp => sp.GetRequiredService<GoogleAdsTransactionSource>());` is retained unchanged.
- `IAccountBudgetFetcher` and `ILogger<GoogleAdsTransactionSource>` continue to be resolved from the container automatically (no explicit factory needed since both are already registered — `IAccountBudgetFetcher` via `AddSingleton` earlier in the same method, and `ILogger<T>` via the standard logging infrastructure).
- No other line in `AddGoogleAdsAdapter` changes (the `Configure<GoogleAdsSettings>`, `AddSingleton<IAccountBudgetFetcher, ...>`, and `AddScoped<IRecurringJob, GoogleAdsInvoiceImportJob>()` lines remain untouched).

### FR-3: No regressions in existing tests
The existing test suite for `GoogleAdsTransactionSource`
(`backend/test/Anela.Heblo.Tests/Adapters/GoogleAds/GoogleAdsTransactionSourceTests.cs`) must
continue to pass unmodified — the tests already construct the type directly via `new(...)`, which
remains valid (and now requires no `InternalsVisibleTo` grant, though that grant may remain in
place; see Out of Scope).

**Acceptance criteria:**
- All 5 existing tests in `GoogleAdsTransactionSourceTests.cs` pass without modification.
- The DI-registered path (`AddGoogleAdsAdapter` → `IMarketingTransactionSource`) resolves to a
  working `GoogleAdsTransactionSource` instance at runtime, exercised by any existing integration
  coverage for the marketing invoices pipeline (if present) or verified by a build + manual DI
  container validation.

## Non-Functional Requirements

### NFR-1: No behavioral change
This is a pure visibility / DI-wiring refactor. `GetTransactionsAsync`'s logic, the mapping of
`RawAccountBudget` to `MarketingTransaction`, logging, and error handling must be byte-for-byte
identical before and after this change.

### NFR-2: Build and test integrity
`dotnet build` must succeed with no new warnings, and `dotnet format` must report no changes
needed (or the changes must be applied) per the project's validation checklist.

## Data Model
No data model changes. `MarketingTransaction`, `RawAccountBudget`, and `IAccountBudgetFetcher`
are unaffected.

## API / Interface Design
No public API surface changes beyond the constructor's accessibility modifier
(`internal` → `public`), which is a widening change and therefore non-breaking for all existing
callers.

## Dependencies
- None beyond what already exists: `IAccountBudgetFetcher`, `ILogger<GoogleAdsTransactionSource>`,
  and the DI container (`Microsoft.Extensions.DependencyInjection`).

## Out of Scope
- Removing the `<InternalsVisibleTo Include="Anela.Heblo.Tests" />` entry from
  `Anela.Heblo.Adapters.GoogleAds.csproj`. It is not strictly required by the fix (other assembly
  members, or other `internal` types in the same project, may still rely on it), and the issue's
  suggested fix does not ask for its removal. Leaving it in place is harmless. If the developer
  implementing this finds nothing else in the assembly needs it, removing it is a reasonable
  optional cleanup but is not required for acceptance.
- Any change to `MetaAdsTransactionSource` or `MetaAdsAdapterServiceCollectionExtensions` — they
  are already in the desired state and serve only as the reference pattern.
- Any change to `GoogleAdsInvoiceImportJob`, `IAccountBudgetFetcher`/`SdkAccountBudgetFetcher`, or
  any other class in the `Anela.Heblo.Adapters.GoogleAds` project.
- Adding new unit tests. Existing tests already cover `GoogleAdsTransactionSource` in isolation;
  no new test scenarios are introduced by this change since there is no logic change.

## Open Questions
None.

## Status: COMPLETE
