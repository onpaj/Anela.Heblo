# Development — Flexi: `ILotsClient` registered twice with conflicting lifetimes

## What was implemented

Followed `plan-01.md` / `design-01.md` / `architecture-01.md` exactly, with no deviation.

1. **Deleted the duplicate Scoped registration.**
   `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs`
   — removed line 86, `services.AddScoped<ILotsClient, FlexiLotsClient>();`. The sole surviving
   registration is the pre-existing Singleton on line 73:
   `services.AddSingleton<Anela.Heblo.Domain.Features.Catalog.Lots.ILotsClient, FlexiLotsClient>();`
   (left fully-qualified, untouched, per the architecture review's explicit instruction not to
   simplify it as a drive-by change). Net diff: one line deleted, nothing else in the file moved.

2. **Added a regression test** that guards against this defect class recurring, mirroring the
   existing `PersistenceModuleTests.AddPersistenceServices_RegistersNoRepositoryBindings` pattern
   (descriptor-only assertions, no `BuildServiceProvider()`/`GetRequiredService()`):
   `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Lots/FlexiAdapterLotsClientRegistrationTests.cs`
   (new file). It builds a bare `ServiceCollection`, calls `AddFlexiAdapter` with an in-memory
   `IConfiguration` carrying only the `FlexiBeeSettings` keys needed for `AddFlexiBee` to register
   without throwing (no live credentials, no network/HTTP calls — registration only), then asserts:
   - exactly one `ServiceDescriptor` has `ServiceType == typeof(ILotsClient)`
   - that descriptor's `Lifetime == ServiceLifetime.Singleton`
   - that descriptor's `ImplementationType == typeof(FlexiLotsClient)`

No consumer code (`CatalogDataRefreshService`, `FlexiLotLoader`) needed changes — both already
resolve `ILotsClient` through standard DI.

## Files changed

- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs` — deleted duplicate `AddScoped<ILotsClient, FlexiLotsClient>()` line.
- `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Lots/FlexiAdapterLotsClientRegistrationTests.cs` (new) — DI single-registration regression test.

## Verification performed

- `dotnet build src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj` → **Build succeeded** (only pre-existing nullable-reference warnings, none introduced by this change).
- `dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj --filter "FullyQualifiedName~Lots"` → **13/13 passed** (12 pre-existing `FlexiLotsClientTests` + the 1 new registration test).
- `dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj --filter "FullyQualifiedName!~Integration&FullyQualifiedName!~DispatchesThreeStockCallsConcurrently"` → **252/252 passed**. Excluded: the `Integration` test class (requires live FlexiBee credentials/user-secrets not available in this sandbox — fails identically on `main`, unrelated to this change) and one known timing-sensitive concurrency test (`FlexiManufactureTemplateServiceTests.GetManufactureTemplateAsync_DispatchesThreeStockCallsConcurrently`) that flaked under CPU contention from unrelated concurrent processes on this machine — also unrelated to the DI change.
- `dotnet format Anela.Heblo.sln --include <the two changed files> --verify-no-changes` → clean, no formatting drift.
- Confirmed via `git diff` that the change is exactly the one-line deletion the design specified, with no incidental edits.

## How to verify

```bash
cd backend
dotnet build src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj --filter "FullyQualifiedName~Lots"
```

The new `FlexiAdapterLotsClientRegistrationTests.AddFlexiAdapter_RegistersLotsClientExactlyOnce_AsSingleton`
test would have failed before this fix (it would have found 2 descriptors instead of 1).
