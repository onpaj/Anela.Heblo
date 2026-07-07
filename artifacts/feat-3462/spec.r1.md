# Specification: Fix phantom non-keyed `IPrintQueueSink` singleton in `AddAzurePrintQueueSink`

## Summary
`AzureAdapterModule.AddAzurePrintQueueSink()` conflates two responsibilities: provisioning Azure Blob print-queue infrastructure (`BlobContainerClient`, `AzureBlobPrintQueueSink`) and binding a non-keyed `IPrintQueueSink` singleton. Because the `"Combined"` print-sink mode in `ServiceCollectionExtensions.AddPrintQueueSink` needs only the infrastructure but calls the combined method anyway, the DI container ends up with two non-keyed `IPrintQueueSink` registrations (a `Singleton` `AzureBlobPrintQueueSink` and a `Scoped` `CombinedPrintQueueSink`). This is a latent double-dispatch bug for any future `IEnumerable<IPrintQueueSink>` / `GetServices<IPrintQueueSink>()` consumer, and a design smell today (a workaround comment excusing an unwanted side effect). This spec defines a targeted refactor that splits infrastructure registration from service binding, eliminating the phantom registration with no behavior change for existing consumers.

## Background
`ServiceCollectionExtensions.AddPrintQueueSink` (`backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`, lines 406-441) configures the `IPrintQueueSink` implementation used by the ExpeditionList module based on `ExpeditionList:PrintSink` configuration (`FileSystem` default, `AzureBlob`, `Cups`, `Combined`).

For `"AzureBlob"` mode, it calls `AzureAdapterModule.AddAzurePrintQueueSink(configuration)` (`backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs`, lines 14-27), which today does three things:
1. Registers a singleton `BlobContainerClient` factory.
2. Registers a singleton, non-keyed `IPrintQueueSink -> AzureBlobPrintQueueSink`.

This is correct and desired for `"AzureBlob"` mode — the non-keyed singleton is exactly what should be resolved.

For `"Combined"` mode, the same method is called to obtain the `BlobContainerClient`/`AzureBlobPrintQueueSink` infrastructure, then the code additionally registers:
- `AddKeyedScoped<IPrintQueueSink, AzureBlobPrintQueueSink>("azure")`
- `AddKeyedScoped<IPrintQueueSink, CupsPrintQueueSink>("cups")`
- A non-keyed `AddScoped<IPrintQueueSink>` factory that builds `CombinedPrintQueueSink` from the two keyed services.

Because ASP.NET Core DI resolves `GetService<T>()`/constructor injection to the *last* registration in registration order, the scoped `CombinedPrintQueueSink` factory (registered after the singleton) wins for single-instance resolution — this is what `CombinedPrintQueueSinkRegistrationTests.Combined_ResolvesCombinedPrintQueueSink` verifies. However, the earlier non-keyed singleton `IPrintQueueSink -> AzureBlobPrintQueueSink` registration is never removed from the `IServiceCollection`. Any consumer that resolves *all* registrations of `IPrintQueueSink` — via `IEnumerable<IPrintQueueSink>` constructor injection or `provider.GetServices<IPrintQueueSink>()` — receives **both** the phantom singleton `AzureBlobPrintQueueSink` and the scoped `CombinedPrintQueueSink` (which itself wraps the same Azure sink). No such consumer exists in the codebase today (confirmed by search), so the bug is currently latent/dormant rather than actively firing — but the container state is incorrect regardless of whether anything currently observes it, and the existing source comment in `ServiceCollectionExtensions.cs` (lines 423-424) explicitly documents the team's awareness of and discomfort with this state.

The root cause is a single-responsibility violation: `AddAzurePrintQueueSink` bundles "set up Azure infrastructure" with "bind the non-keyed `IPrintQueueSink` service," so a caller that wants only the infrastructure (the `"Combined"` case) cannot opt out of the unwanted binding.

## Functional Requirements

### FR-1: Split infrastructure provisioning from service binding in `AzureAdapterModule`
`AzureAdapterModule` must expose two distinct extension methods instead of one:

1. `AddAzurePrintQueueSinkInfrastructure(this IServiceCollection services, IConfiguration configuration)` — registers only the `BlobContainerClient` factory and the concrete `AzureBlobPrintQueueSink` type (as itself or in a form resolvable for keyed/direct construction by callers), with **no** non-keyed `IPrintQueueSink` binding.
2. `AddAzurePrintQueueSink(this IServiceCollection services, IConfiguration configuration)` — calls `AddAzurePrintQueueSinkInfrastructure(configuration)` and additionally registers the non-keyed singleton `IPrintQueueSink -> AzureBlobPrintQueueSink` binding. This preserves the exact current public contract/behavior for any caller that wants the full "AzureBlob mode" registration.

**Acceptance criteria:**
- `AddAzurePrintQueueSinkInfrastructure` registers the `BlobContainerClient` factory and makes `AzureBlobPrintQueueSink` constructible via DI, but after calling it alone, `services.BuildServiceProvider().GetService<IPrintQueueSink>()` (non-keyed) returns `null` (or resolves to whatever else is registered, but never to a registration contributed by this method).
- `AddAzurePrintQueueSink` continues to register exactly one non-keyed `IPrintQueueSink` singleton bound to `AzureBlobPrintQueueSink`, matching pre-change behavior for the `"AzureBlob"` case.
- Both methods return `IServiceCollection` to preserve fluent chaining, consistent with existing extension method conventions in this codebase.

### FR-2: Update `"Combined"` case in `ServiceCollectionExtensions.AddPrintQueueSink` to use infrastructure-only registration
The `"Combined"` case (`backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`, lines 422-434) must call `AddAzurePrintQueueSinkInfrastructure(configuration)` instead of `AddAzurePrintQueueSink(configuration)`, then proceed with its existing keyed registrations (`"azure"`, `"cups"`) and the non-keyed `CombinedPrintQueueSink` scoped factory exactly as today. The workaround comment (lines 423-424: `// AddAzurePrintQueueSink registers a non-keyed IPrintQueueSink as a side effect; ...`) must be removed since the side effect it describes no longer exists.

**Acceptance criteria:**
- After this change, building a service provider with `ExpeditionList:PrintSink = "Combined"` results in `services` containing exactly one non-keyed `IPrintQueueSink` registration (the `CombinedPrintQueueSink` scoped factory) — no non-keyed singleton `AzureBlobPrintQueueSink` registration is present.
- `provider.GetServices<IPrintQueueSink>()` in `"Combined"` mode returns exactly one item, and it is a `CombinedPrintQueueSink` instance.
- The `"AzureBlob"` case is unchanged: it still calls `AddAzurePrintQueueSink(configuration)` (the full method), and continues to resolve a single non-keyed singleton `AzureBlobPrintQueueSink`.
- The comment describing the workaround is deleted; no new comment is needed to explain a side effect that no longer occurs.

### FR-3: No behavior change for `"AzureBlob"`, `"Cups"`, and `"FileSystem"` modes
This is a structural/internal refactor. The `"AzureBlob"`, `"Cups"`, and `"FileSystem"` (default) branches of `AddPrintQueueSink` must resolve to the same concrete types, same lifetimes (Singleton/Scoped), and same keyed/non-keyed bindings as before this change.

**Acceptance criteria:**
- Existing tests `CombinedPrintQueueSinkRegistrationTests.Combined_KeyedAzureSlot_ResolvesAzureBlobPrintQueueSink`, `Combined_KeyedCupsSlot_ResolvesCupsPrintQueueSink`, and `FileSystem_ResolvesFileSystemPrintQueueSink` continue to pass unmodified.
- `Combined_ResolvesCombinedPrintQueueSink` continues to pass unmodified.
- No change to the `"Cups"` or default `"FileSystem"` branches' code.

### FR-4: Regression test proving the phantom registration is gone
Add a test (in `backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs` or a new test in the same test class/file) that explicitly asserts, for `"Combined"` mode, that `GetServices<IPrintQueueSink>()` (or `IServiceCollection` inspection) yields exactly one non-keyed registration/resolved instance, and that the resolved instance is `CombinedPrintQueueSink` — not `AzureBlobPrintQueueSink`.

**Acceptance criteria:**
- New test fails against the pre-fix code (i.e., it would have caught this bug) and passes after the fix.
- Test lives alongside the existing `CombinedPrintQueueSinkRegistrationTests` suite and follows its existing `BuildProvider(string printSink)` helper pattern.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a DI container configuration change with no runtime performance impact. No new allocations, I/O, or network calls are introduced.

### NFR-2: Security
Not applicable — no change to authentication, authorization, secrets, or data handling. `BlobContainerClient` connection string handling is unchanged (still sourced from `PrintPickingListOptions` via configuration, unaffected by this refactor).

### NFR-3: Backward compatibility
The public method signature `AddAzurePrintQueueSink(this IServiceCollection services, IConfiguration configuration)` must be preserved (name, parameters, return type) so that any other caller in the codebase (if one exists beyond `ServiceCollectionExtensions`) continues to compile and behave identically. A search of the codebase should be performed as part of implementation to confirm `AddAzurePrintQueueSink` has no other call sites beyond the `"AzureBlob"` case in `ServiceCollectionExtensions.cs`.

## Data Model
Not applicable — this change touches only DI service registration code (`AzureAdapterModule.cs`, `ServiceCollectionExtensions.cs`). No persisted entities, database schema, or data contracts are affected.

## API / Interface Design

**Before:**
```csharp
// AzureAdapterModule.cs
public static IServiceCollection AddAzurePrintQueueSink(
    this IServiceCollection services, IConfiguration configuration)
{
    services.AddSingleton(provider => { /* BlobContainerClient */ });
    services.AddSingleton<IPrintQueueSink, AzureBlobPrintQueueSink>();
    return services;
}
```

**After:**
```csharp
// AzureAdapterModule.cs

/// <summary>
/// Registers Azure Blob print-queue infrastructure (BlobContainerClient, AzureBlobPrintQueueSink)
/// without binding a non-keyed IPrintQueueSink. Use this when the caller will register its own
/// (e.g. keyed) IPrintQueueSink binding, such as the "Combined" print-sink mode.
/// </summary>
public static IServiceCollection AddAzurePrintQueueSinkInfrastructure(
    this IServiceCollection services, IConfiguration configuration)
{
    services.AddSingleton(provider =>
    {
        var options = provider.GetRequiredService<IOptions<PrintPickingListOptions>>().Value;
        return new BlobContainerClient(options.BlobConnectionString, options.BlobContainerName);
    });

    services.AddSingleton<AzureBlobPrintQueueSink>();

    return services;
}

/// <summary>
/// Registers Azure Blob print-queue infrastructure and binds the non-keyed IPrintQueueSink
/// singleton to AzureBlobPrintQueueSink. Use this for the "AzureBlob" print-sink mode, where
/// AzureBlobPrintQueueSink is the sole, directly-resolvable IPrintQueueSink implementation.
/// </summary>
public static IServiceCollection AddAzurePrintQueueSink(
    this IServiceCollection services, IConfiguration configuration)
{
    services.AddAzurePrintQueueSinkInfrastructure(configuration);
    services.AddSingleton<IPrintQueueSink>(provider => provider.GetRequiredService<AzureBlobPrintQueueSink>());
    return services;
}
```

```csharp
// ServiceCollectionExtensions.cs — "Combined" case
case "Combined":
    services.AddAzurePrintQueueSinkInfrastructure(configuration);
    services.AddKeyedSingleton<IPrintQueueSink, AzureBlobPrintQueueSink>("azure");
    services.AddKeyedScoped<IPrintQueueSink, CupsPrintQueueSink>("cups");
    services.AddScoped<IPrintQueueSink>(provider =>
    {
        var azure = provider.GetRequiredKeyedService<IPrintQueueSink>("azure");
        var cups = provider.GetRequiredKeyedService<IPrintQueueSink>("cups");
        return new Anela.Heblo.API.Features.ExpeditionList.CombinedPrintQueueSink(azure, cups);
    });
    break;
```

Notes on the sketch above:
- Registering `AzureBlobPrintQueueSink` as itself (a concrete singleton) inside the infrastructure method, then having `AddAzurePrintQueueSink` bind `IPrintQueueSink` to that same instance via `GetRequiredService<AzureBlobPrintQueueSink>()`, guarantees the `"AzureBlob"` mode and the `"Combined"` mode's `"azure"` keyed slot resolve to the *same singleton instance* rather than constructing `AzureBlobPrintQueueSink` twice with two separate lifetimes. The implementer should verify this is consistent with `AzureBlobPrintQueueSink`'s constructor dependencies (e.g., whether it depends on scoped services, which would make a singleton registration invalid) before finalizing — see Open Questions.
- The keyed `"azure"` registration in `"Combined"` mode is shown promoted from `AddKeyedScoped` to `AddKeyedSingleton` to match `AddAzurePrintQueueSinkInfrastructure`'s singleton lifetime; the implementer must confirm `AzureBlobPrintQueueSink` has no scoped dependencies before doing so, or otherwise keep the keyed registration scoped and have it resolve via a factory delegating to the singleton instance.
- No HTTP endpoints, events, or UI are involved; this is a code-internal interface change (extension method signatures) within the backend DI composition root.

## Dependencies
- `Azure.Storage.Blobs` (`BlobContainerClient`) — no version or usage change.
- `Microsoft.Extensions.DependencyInjection` keyed service APIs (`AddKeyedScoped`, `AddKeyedSingleton`, `GetRequiredKeyedService`) — already in use in this codebase (.NET 8 keyed DI), no new dependency introduced.
- Existing types: `AzureBlobPrintQueueSink` (`backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs`), `CupsPrintQueueSink`, `CombinedPrintQueueSink` (`backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs`), `PrintPickingListOptions`.
- Existing test infrastructure: `CombinedPrintQueueSinkRegistrationTests` (`backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs`) provides the `BuildProvider(string printSink)` harness to extend for FR-4.

## Out of Scope
- Any change to `AzureBlobPrintQueueSink`, `CupsPrintQueueSink`, or `CombinedPrintQueueSink`'s internal `SendAsync` logic/behavior.
- Any change to the `"FileSystem"` or `"Cups"` print-sink modes' registration logic.
- Introducing a new print-sink mode or configuration option.
- Adding an actual `IEnumerable<IPrintQueueSink>`/`GetServices<IPrintQueueSink>()` consumer — none exists today; this fix prevents a latent bug for if/when one is added, it does not add one.
- Broader DI registration audits of other modules/adapters beyond `AzureAdapterModule` and the `AddPrintQueueSink` method.
- Renaming `CombinedPrintQueueSink`, changing its namespace, or altering `IPrintQueueSink`'s interface contract.

## Open Questions
- `AzureBlobPrintQueueSink`'s constructor dependencies need to be checked to confirm it is safe to register as a `Singleton` (as it already is today) and to determine whether the `"Combined"` case's keyed `"azure"` registration should be promoted to `AddKeyedSingleton` (sharing the one infrastructure-registered instance) or kept as `AddKeyedScoped` with a factory that resolves the singleton per-scope. Recommend the implementer inspect `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs` before finalizing the exact registration calls in FR-1/FR-2; either choice satisfies the acceptance criteria (single non-keyed registration, no phantom singleton) as long as it is applied consistently.
- Confirm via codebase search whether `AddAzurePrintQueueSink` (the two-argument extension method) has any call sites other than the `"AzureBlob"` branch in `ServiceCollectionExtensions.AddPrintQueueSink` — assumed "no" per the search performed while drafting this spec, but the implementer should re-verify at implementation time in case of drift.
