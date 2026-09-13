# Architecture Review: GoogleAdsTransactionSource constructor visibility fix

## Skip Design: true

## Architectural Fit Assessment
This is a pure DI-wiring / access-modifier clean-up inside a single Adapter project
(`Anela.Heblo.Adapters.GoogleAds`). It touches no domain logic, no contracts, no public API
surface beyond widening one constructor's visibility, and introduces no new component. It aligns
exactly with the existing pattern already used by the sibling `Anela.Heblo.Adapters.MetaAds`
project, which implements the same `IMarketingTransactionSource` interface with a `public`
constructor and a plain `AddScoped<T>()` DI registration. There are no architectural risks: this
converges a divergent implementation back onto the project's own established convention.

Confirmed against the actual code (not just the issue body):
- `GoogleAdsTransactionSource.cs` line 17: `internal GoogleAdsTransactionSource(...)`.
- `GoogleAdsAdapterServiceCollectionExtensions.cs`: registers via a factory lambda that calls
  `new GoogleAdsTransactionSource(...)` directly, bypassing constructor injection.
- `MetaAdsTransactionSource.cs` line 28: `public MetaAdsTransactionSource(...)`.
- `MetaAdsAdapterServiceCollectionExtensions.cs`: registers via `services.AddHttpClient<MetaAdsTransactionSource>()`
  (framework-resolved) then exposes it as `IMarketingTransactionSource` via
  `sp.GetRequiredService<MetaAdsTransactionSource>()` — no manual `new`.
- `Anela.Heblo.Adapters.GoogleAds.csproj` already declares
  `<InternalsVisibleTo Include="Anela.Heblo.Tests" />`, and
  `GoogleAdsTransactionSourceTests.cs` already exists and already constructs the type directly
  via `new(...)`, relying on that grant. So today's tests already work; the fix is about removing
  an unnecessary access restriction and a compensating DI workaround, not about unblocking tests
  that are currently broken.

## Proposed Architecture

### Component Overview
No new components. Existing structure is retained as-is:

```
GoogleAdsAdapterServiceCollectionExtensions.AddGoogleAdsAdapter(services, configuration)
        │
        ├─ services.Configure<GoogleAdsSettings>(...)              (unchanged)
        ├─ services.AddSingleton<IAccountBudgetFetcher, SdkAccountBudgetFetcher>()  (unchanged)
        ├─ services.AddScoped<GoogleAdsTransactionSource>()         (CHANGED: standard overload,
        │                                                            container resolves ctor)
        ├─ services.AddScoped<IMarketingTransactionSource>(sp =>
        │       sp.GetRequiredService<GoogleAdsTransactionSource>()) (unchanged)
        └─ services.AddScoped<IRecurringJob, GoogleAdsInvoiceImportJob>()  (unchanged)

GoogleAdsTransactionSource : IMarketingTransactionSource
        public GoogleAdsTransactionSource(IAccountBudgetFetcher, ILogger<GoogleAdsTransactionSource>)  (CHANGED: internal → public)
        GetTransactionsAsync(...)   (UNCHANGED — no logic touched)
```

### Key Design Decisions

#### Decision 1: Constructor accessibility
**Options considered:**
1. Leave `internal`, keep the factory-lambda DI workaround (status quo).
2. Change to `public`, simplify DI to `AddScoped<GoogleAdsTransactionSource>()` (issue's suggested fix).
3. Change to `public` and additionally remove the now-possibly-unnecessary `InternalsVisibleTo`
   entry from the csproj.

**Chosen approach:** Option 2 — make the constructor `public`, simplify the DI registration.
Do not touch `InternalsVisibleTo` (see Decision 2).

**Rationale:** The type is already `public`; `internal` on the constructor of a `public` class
provides no real encapsulation boundary (anything in-assembly, or any assembly granted
`InternalsVisibleTo`, can already instantiate it) — it exists purely as an artifact that forces
the DI factory-lambda workaround. Making it `public` removes that artifact and aligns the class
1:1 with `MetaAdsTransactionSource`, which implements the same interface. This is the smallest
change that fully addresses both symptoms named in the issue (test-instantiation friction and
DI-registration inconsistency).

#### Decision 2: Leave `InternalsVisibleTo` in the csproj as-is
**Options considered:**
1. Remove `<InternalsVisibleTo Include="Anela.Heblo.Tests" />` from
   `Anela.Heblo.Adapters.GoogleAds.csproj` since the one internal-constructor consumer inside the
   test project going away removes the only known reason for it.
2. Leave it in place.

**Chosen approach:** Option 2 — leave it in place.

**Rationale:** This is out of scope per the issue's suggested fix (which only asks to change the
constructor and DI registration). Removing an `InternalsVisibleTo` grant is a broader,
higher-blast-radius change that requires verifying no other `internal` member in the
`Anela.Heblo.Adapters.GoogleAds` assembly is exercised by the test project — that verification is
unnecessary work for this fix and is explicitly called out as optional/out-of-scope in the spec.
Leaving the grant in place is harmless (an unused `InternalsVisibleTo` entry has no runtime cost
and does not violate any project convention observed elsewhere in the repo — several other
adapter projects carry the same entry).

## Implementation Guidance

### Directory / Module Structure
No new files, no new directories. Two existing files are edited in place:
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsTransactionSource.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsAdapterServiceCollectionExtensions.cs`

### Interfaces and Contracts
- `IMarketingTransactionSource` — unchanged, still implemented identically.
- `IAccountBudgetFetcher` — unchanged, still injected via constructor.
- `GoogleAdsTransactionSource`'s public contract widens (constructor becomes callable from
  outside the assembly too), which is a non-breaking, backward-compatible change.

### Data Flow
Unchanged. `AddGoogleAdsAdapter` → DI container resolves `GoogleAdsTransactionSource` (now via
its own public constructor, resolving `IAccountBudgetFetcher` and `ILogger<GoogleAdsTransactionSource>`
from the container automatically) → exposed as `IMarketingTransactionSource` → consumed by
`GoogleAdsInvoiceImportJob` and by whatever aggregates `IMarketingTransactionSource` implementations
for the marketing invoices import pipeline. `GetTransactionsAsync`'s internal data flow
(fetch → map `RawAccountBudget` → `MarketingTransaction` → log) is untouched.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Widening a constructor's visibility could theoretically be seen as an API-surface change | Very Low | Widening (`internal`→`public`) is additive/backward-compatible by definition — no existing caller breaks. |
| DI resolution of `GoogleAdsTransactionSource` might fail if `ILogger<GoogleAdsTransactionSource>` isn't available in the container | Very Low | Standard `ILogger<T>` is always resolvable once logging is configured (as it already is app-wide); the previous factory lambda already required and successfully obtained the same `ILogger<T>` from the container, so this is proven to resolve. |
| Existing tests could accidentally be broken by touching the constructor | Low | No parameters, order, or types change — only the access modifier. Existing tests construct the type directly and will continue to compile and pass unchanged. |

## Specification Amendments
None. The specification (`spec.r1.md`) as written is architecturally sound and requires no
changes.

## Prerequisites
None. No migrations, configuration, or infrastructure changes are required before implementation
can start.
