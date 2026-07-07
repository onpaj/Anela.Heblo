## Module
ExpeditionList

## Finding
`AzureAdapterModule.AddAzurePrintQueueSink()` (`backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs` line 24) always registers a non-keyed singleton:

```csharp
services.AddSingleton();
```

This is the correct and only registration for the `"AzureBlob"` sink case. However, in `ServiceCollectionExtensions.AddPrintQueueSink` (`backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` lines 423–432), the `"Combined"` case also calls `AddAzurePrintQueueSink()` to set up the `BlobContainerClient` and related infrastructure — with a comment acknowledging the problem:

```csharp
// AddAzurePrintQueueSink registers a non-keyed IPrintQueueSink as a side effect;
// it is unused here — the last non-keyed registration (the factory below) wins.
services.AddAzurePrintQueueSink(configuration);
...
services.AddScoped(provider => new CombinedPrintQueueSink(azure, cups));
```

Because ASP.NET Core DI returns the *last* registration for `GetService()`, the scoped factory overrides the singleton for direct resolution. But the singleton registration is never removed — it becomes a phantom entry.

The concrete risk: `GetServices<IPrintQueueSink>()` returns **both** the singleton `AzureBlobPrintQueueSink` and the scoped `CombinedPrintQueueSink`. Any code that resolves `IEnumerable<IPrintQueueSink>` (or iterates all registered implementations — including some health-check or diagnostic patterns) would dispatch to the Azure sink twice: once directly, once through `CombinedPrintQueueSink`.

Root cause is an SRP violation in `AddAzurePrintQueueSink`: it bundles two concerns — registering Azure infrastructure (`BlobContainerClient`, `AzureBlobPrintQueueSink`) **and** binding the non-keyed `IPrintQueueSink` — into one method. The caller cannot request infrastructure setup without also getting the unwanted service binding.

## Why it matters
- Creates a DI container state that is misleading: two non-keyed registrations for `IPrintQueueSink` with different lifetimes (Singleton and Scoped).
- Is a latent double-dispatch bug for any `IEnumerable<IPrintQueueSink>` consumer.
- The workaround comment (`"it is unused here — the factory below wins"`) is a code smell that signals the method contract is wrong — callers shouldn't need to know about and work around a method's internal side effects.

## Suggested fix
Split `AddAzurePrintQueueSink` into two methods in `AzureAdapterModule`:

```csharp
// Sets up BlobContainerClient + AzureBlobPrintQueueSink infra — no non-keyed IPrintQueueSink binding
public static IServiceCollection AddAzurePrintQueueSinkInfrastructure(
    this IServiceCollection services, IConfiguration configuration) { ... }

// Registers the non-keyed IPrintQueueSink binding (only for "AzureBlob" mode)
public static IServiceCollection AddAzurePrintQueueSink(
    this IServiceCollection services, IConfiguration configuration)
{
    services.AddAzurePrintQueueSinkInfrastructure(configuration);
    services.AddSingleton<IPrintQueueSink, AzureBlobPrintQueueSink>();
    return services;
}
```

The `"Combined"` case then calls `AddAzurePrintQueueSinkInfrastructure` (no side-effect binding) and adds only the keyed registrations it actually needs. The phantom singleton disappears, the comment is removed, and the method's contract is honest.

---
_Filed by daily arch-review routine on 2026-07-02._
