# GoogleAdsTransactionSource Constructor Visibility Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `GoogleAdsTransactionSource`'s constructor `public` (matching `MetaAdsTransactionSource`) and simplify its DI registration to the standard `AddScoped<T>()` overload, with no logic change.

**Architecture:** Two existing files in `Anela.Heblo.Adapters.GoogleAds` are edited in place: the constructor's access modifier changes from `internal` to `public`, and the manual factory-lambda DI registration in `GoogleAdsAdapterServiceCollectionExtensions` is replaced by the parameterless `AddScoped<GoogleAdsTransactionSource>()` overload, letting the container resolve the constructor itself — exactly mirroring the existing `MetaAdsAdapterServiceCollectionExtensions` pattern.

**Tech Stack:** .NET 8, Microsoft.Extensions.DependencyInjection, xUnit, FluentAssertions.

---

### task: widen-google-ads-transaction-source-constructor

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsTransactionSource.cs:17`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsAdapterServiceCollectionExtensions.cs:16-19`
- Test (existing, unmodified — used to verify no regression): `backend/test/Anela.Heblo.Tests/Adapters/GoogleAds/GoogleAdsTransactionSourceTests.cs`

- [ ] **Step 1: Run the existing test suite to confirm the baseline passes before any change**

Run:
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GoogleAdsTransactionSourceTests"
```
Expected: All 5 existing tests in `GoogleAdsTransactionSourceTests` PASS (they already construct
`GoogleAdsTransactionSource` directly via the `InternalsVisibleTo` grant, so this should be green
before touching anything).

- [ ] **Step 2: Change the constructor from `internal` to `public`**

In `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsTransactionSource.cs`, change
line 17 from:

```csharp
    internal GoogleAdsTransactionSource(
        IAccountBudgetFetcher fetcher,
        ILogger<GoogleAdsTransactionSource> logger)
```

to:

```csharp
    public GoogleAdsTransactionSource(
        IAccountBudgetFetcher fetcher,
        ILogger<GoogleAdsTransactionSource> logger)
```

No other line in this file changes. The full constructor (unchanged body) remains:

```csharp
    public GoogleAdsTransactionSource(
        IAccountBudgetFetcher fetcher,
        ILogger<GoogleAdsTransactionSource> logger)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
```

- [ ] **Step 3: Simplify the DI registration in `GoogleAdsAdapterServiceCollectionExtensions`**

In `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsAdapterServiceCollectionExtensions.cs`,
replace:

```csharp
        services.AddScoped<GoogleAdsTransactionSource>(sp =>
            new GoogleAdsTransactionSource(
                sp.GetRequiredService<IAccountBudgetFetcher>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GoogleAdsTransactionSource>>()));
        services.AddScoped<IMarketingTransactionSource>(sp =>
            sp.GetRequiredService<GoogleAdsTransactionSource>());
```

with:

```csharp
        services.AddScoped<GoogleAdsTransactionSource>();
        services.AddScoped<IMarketingTransactionSource>(sp =>
            sp.GetRequiredService<GoogleAdsTransactionSource>());
```

The rest of `AddGoogleAdsAdapter` (the `Configure<GoogleAdsSettings>(...)`,
`AddSingleton<IAccountBudgetFetcher, SdkAccountBudgetFetcher>()`, and
`AddScoped<IRecurringJob, GoogleAdsInvoiceImportJob>()` lines) is unchanged. The resulting method
should read:

```csharp
    public static IServiceCollection AddGoogleAdsAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<GoogleAdsSettings>(configuration.GetSection(GoogleAdsSettings.ConfigurationKey));
        services.AddSingleton<IAccountBudgetFetcher, SdkAccountBudgetFetcher>();
        services.AddScoped<GoogleAdsTransactionSource>();
        services.AddScoped<IMarketingTransactionSource>(sp =>
            sp.GetRequiredService<GoogleAdsTransactionSource>());
        services.AddScoped<IRecurringJob, GoogleAdsInvoiceImportJob>();
        return services;
    }
```

- [ ] **Step 4: Build the solution to confirm it compiles**

Run:
```bash
cd backend
dotnet build
```
Expected: Build succeeds with no new errors or warnings. In particular, confirm there is no
"unused `using`" warning introduced — the fully-qualified `Microsoft.Extensions.Logging.ILogger<GoogleAdsTransactionSource>`
reference that was inline in the removed factory lambda is gone, but `GoogleAdsAdapterServiceCollectionExtensions.cs`
does not otherwise reference `Microsoft.Extensions.Logging`, so no `using` directive needs
removing (it was never added as a top-level `using` — it was fully qualified inline).

- [ ] **Step 5: Run the existing test suite again to confirm no regression**

Run:
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GoogleAdsTransactionSourceTests"
```
Expected: All 5 tests in `GoogleAdsTransactionSourceTests` still PASS, unmodified. The tests
construct `GoogleAdsTransactionSource` directly via `new(...)`; this continues to compile and
behave identically now that the constructor is `public` (no `InternalsVisibleTo` grant is
required for this call anymore, though the grant remains present in the csproj — see Step 6 note).

- [ ] **Step 6 (verification only, no code change): Confirm DI resolves the type correctly**

There is no dedicated DI-container integration test for `AddGoogleAdsAdapter` in this repo. Verify
by inspection: `AddScoped<GoogleAdsTransactionSource>()` lets the container resolve
`IAccountBudgetFetcher` (registered as `AddSingleton` immediately above in the same method) and
`ILogger<GoogleAdsTransactionSource>` (resolvable app-wide via the standard logging
infrastructure already wired at host startup) automatically via constructor injection — this is
the same mechanism `MetaAdsAdapterServiceCollectionExtensions.AddMetaAdsAdapter` already relies on
for `MetaAdsTransactionSource`, so no new host wiring is needed. If the wider test suite includes
any host-startup / DI-validation test (e.g., a test that calls `IServiceCollection.BuildServiceProvider().ValidateOnBuild = true`
or an integration test that spins up the full app host), run it as part of Step 7 below and
confirm it stays green — but do not add a new one, since none is required by the spec (see
`spec.r1.md`, Out of Scope).

- [ ] **Step 7: Run the full backend test suite and formatter to confirm project-wide validation passes**

Run:
```bash
cd backend
dotnet build
dotnet format --verify-no-changes
dotnet test
```
Expected: `dotnet build` succeeds; `dotnet format --verify-no-changes` reports no formatting
violations (if it does, run `dotnet format` without `--verify-no-changes` to apply the fix, then
re-run `--verify-no-changes` to confirm); the full test suite passes with no failures introduced
by this change.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsTransactionSource.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsAdapterServiceCollectionExtensions.cs
git commit -m "fix(google-ads): make GoogleAdsTransactionSource constructor public

Widen the constructor from internal to public so it matches
MetaAdsTransactionSource's public constructor, and simplify the DI
registration in GoogleAdsAdapterServiceCollectionExtensions to the
standard AddScoped<T>() overload instead of a manual factory lambda
that existed only to work around the internal constructor. No logic
change."
```

---

## Self-Review

**1. Spec coverage:**
- FR-1 (public constructor) → Step 2.
- FR-2 (simplified DI registration) → Step 3.
- FR-3 (no regressions in existing tests) → Steps 1 (baseline), 5 (post-change), 7 (full suite).
- NFR-1 (no behavioral change) → Steps 2-3 change only the constructor's access modifier and the
  DI registration call shape; `GetTransactionsAsync` and all mapping logic are untouched by every
  step in this plan.
- NFR-2 (build/format integrity) → Steps 4 and 7.
- Out of Scope items (removing `InternalsVisibleTo`, touching MetaAds files, adding new tests) →
  correctly excluded from every step above; Step 6 explicitly notes no new test is added.

No gaps found.

**2. Placeholder scan:** No "TBD"/"TODO"/"implement later" placeholders. Every step shows the
exact before/after code or the exact command with expected output. No step says "similar to
Task N" — this plan has a single task, so there is nothing to cross-reference.

**3. Type consistency:** `GoogleAdsTransactionSource`, `IAccountBudgetFetcher`,
`ILogger<GoogleAdsTransactionSource>`, `IMarketingTransactionSource`, `IRecurringJob`,
`GoogleAdsInvoiceImportJob`, and `GoogleAdsSettings` are used consistently with their existing
signatures throughout every step — no new or renamed symbols are introduced anywhere in this
plan.
