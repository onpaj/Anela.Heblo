# CUPS Print Sink Design

**Date:** 2026-03-25
**Feature:** CUPS adapter — generic printing service + ExpeditionList sink

## Summary

Add a `CupsPrintQueueSink` that implements the existing `IPrintQueueSink` interface by delegating to a new generic `ICupsPrintingService`. The CUPS adapter is isolated from ExpeditionList concerns: the generic `ICupsPrintingService` / `CupsPrintingService` live at the adapter root; the `CupsPrintQueueSink` bridge lives under `Features/ExpeditionList/`. Communication uses **SharpIppNext** 3.x (NuGet: `SharpIppNext`) over HTTP with Basic authentication to a CUPS server running in Azure, reachable via Tailscale. Note: the original `SharpIpp` package is unmaintained (v0.10.0); `SharpIppNext` is the actively maintained fork.

---

## Project Structure

New adapter project: `backend/src/Adapters/Anela.Heblo.Adapters.Cups/`

```
Anela.Heblo.Adapters.Cups/
├── Anela.Heblo.Adapters.Cups.csproj
├── CupsAdapterServiceCollectionExtensions.cs
├── CupsOptions.cs
├── CupsAuthHandler.cs
├── ICupsPrintingService.cs
├── CupsPrintingService.cs
└── Features/
    └── ExpeditionList/
        └── CupsPrintQueueSink.cs
```

The project must be added to the solution file. The test project `Anela.Heblo.Tests` must add a `ProjectReference` to `Anela.Heblo.Adapters.Cups` (following the same pattern as the existing `AzureBlobPrintQueueSinkTests`).

### .csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Anela.Heblo.Adapters.Cups</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SharpIppNext" Version="3.*" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="8.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Anela.Heblo.Application\Anela.Heblo.Application.csproj" />
  </ItemGroup>
</Project>
```

---

## CupsOptions

**Config section:** `Cups`

```csharp
public class CupsOptions
{
    public const string ConfigurationKey = "Cups";

    public string ServerUrl { get; set; } = string.Empty;    // e.g. "http://cups.internal:631"
    public string PrinterName { get; set; } = string.Empty;  // fallback printer name
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
```

```json
"Cups": {
  "ServerUrl": "http://cups.internal:631",
  "PrinterName": "expedition-printer",
  "Username": "...",
  "Password": "..."
}
```

---

## CupsAuthHandler

A `DelegatingHandler` that reads `IOptions<CupsOptions>` at request time and adds `Authorization: Basic <credentials>` to every outgoing HTTP request. This avoids mutating `DefaultRequestHeaders` on the shared pooled `HttpMessageHandler`, which is not thread-safe.

```csharp
internal class CupsAuthHandler : DelegatingHandler
{
    public CupsAuthHandler(IOptions<CupsOptions> options) { ... }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Encode credentials and set Authorization header on the request
        ...
        return base.SendAsync(request, cancellationToken);
    }
}
```

---

## ICupsPrintingService / CupsPrintingService

Generic service injected by interface for testability. Can be used by any future feature that needs CUPS printing.

```csharp
public interface ICupsPrintingService
{
    Task PrintAsync(string filePath, string? printerName = null, CancellationToken cancellationToken = default);
}
```

**Constructor:**

```csharp
public CupsPrintingService(
    ISharpIppClient sharpIppClient,
    IOptions<CupsOptions> options,
    ILogger<CupsPrintingService> logger)
```

**Behavior of `PrintAsync`:**
- Resolves printer name: use `printerName` if provided, else fall back to `CupsOptions.PrinterName`
- Fail fast with `InvalidOperationException` if both are null/empty (before any I/O)
- Fail fast with `InvalidOperationException` if `CupsOptions.ServerUrl` is null/empty
- Opens the file via `File.OpenRead(filePath)` (streaming, consistent with `AzureBlobPrintQueueSink`; async file I/O is not required given single-PDF payload sizes); `FileNotFoundException` propagates unchanged if the file does not exist
- Creates a `PrintJobRequest` (`SharpIpp.Models.Requests`) with:
  - `Document = fileStream`
  - `OperationAttributes.PrinterUri = new Uri($"{ServerUrl}/printers/{resolvedPrinterName}")`
  - `OperationAttributes.DocumentFormat = "application/pdf"`
- Sends via `ISharpIppClient.PrintJobAsync(request, cancellationToken)`
- Checks `response.StatusCode != IppStatusCode.SuccessfulOk` — throws `InvalidOperationException` including the status code value in the message
- Logs the returned IPP job ID at Debug level on success

---

## CupsPrintQueueSink

Thin bridge between `IPrintQueueSink` (ExpeditionList concern) and `ICupsPrintingService` (generic).

**Constructor:**

```csharp
public CupsPrintQueueSink(
    ICupsPrintingService cupsPrintingService,
    ILogger<CupsPrintQueueSink> logger)
```

**Behavior of `SendAsync`:**
- Iterates `filePaths` sequentially
- Calls `ICupsPrintingService.PrintAsync(filePath, cancellationToken: cancellationToken)` for each (no explicit printer name — uses configured default)
- Does not catch exceptions — propagates to `ExpeditionListService`

---

## Call Chain

```
ExpeditionListService
  → IPrintQueueSink (injected)
    → CupsPrintQueueSink (implements IPrintQueueSink)
      → ICupsPrintingService (injected)
        → CupsPrintingService → ISharpIppClient (SharpIppNext 3.x)
```

---

## DI Registration

### CupsAdapterServiceCollectionExtensions

```csharp
public static class CupsAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddCupsAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<CupsOptions>(configuration.GetSection(CupsOptions.ConfigurationKey));

        // CupsAuthHandler adds the Basic Authorization header per-request at resolve-time,
        // avoiding mutation of shared DefaultRequestHeaders on the pooled HttpMessageHandler.
        services.AddTransient<CupsAuthHandler>();
        services.AddHttpClient("Cups")
            .AddHttpMessageHandler<CupsAuthHandler>();

        // Transient: each resolve gets a fresh HttpClient from the factory (disposal is managed by the factory)
        services.AddTransient<ISharpIppClient>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("Cups");
            return new SharpIppClient(httpClient);
        });

        services.AddScoped<ICupsPrintingService, CupsPrintingService>();
        services.AddScoped<IPrintQueueSink, CupsPrintQueueSink>();

        return services;
    }
}
```

`IPrintQueueSink` is registered here because the Cups adapter project already references `Anela.Heblo.Application` (required for `ICupsPrintingService` to remain in `Application`). This mirrors the Azure adapter pattern where `AzureBlobPrintQueueSink` also implements `IPrintQueueSink` directly. Future features that need CUPS printing without the `IPrintQueueSink` binding can call a separate `AddCupsPrintingService` extension method if that need arises; for now YAGNI applies.

### Wiring in Program.cs

The `PrintSink` switch is added to `Program.cs` alongside the other adapter registrations. This refactors the existing `if/else` to a `switch` to accommodate the third case cleanly:

```csharp
// Print sink selection — valid values: "FileSystem" (default), "AzureBlob", "Cups"
var printSink = builder.Configuration["ExpeditionList:PrintSink"];
switch (printSink)
{
    case "AzureBlob":
        builder.Services.AddAzurePrintQueueSink(builder.Configuration);
        break;
    case "Cups":
        builder.Services.AddCupsAdapter(builder.Configuration);
        break;
    default: // "FileSystem" or unset
        builder.Services.AddScoped<IPrintQueueSink, FileSystemPrintQueueSink>();
        break;
}
```

`FileSystemPrintQueueSink` is intentionally registered inline (no adapter extension method) to stay consistent with the existing code. The inline comment on `PrintPickingListOptions.PrintSink` must be updated to: `// "FileSystem" | "AzureBlob" | "Cups"`.

---

## Testing

**`CupsPrintingServiceTests`** (unit, in `Anela.Heblo.Tests`):
- Mocks `ISharpIppClient` (interface from `SharpIppNext`, namespace `SharpIpp`)
- Verifies `PrintJobRequest.OperationAttributes.PrinterUri` is `{ServerUrl}/printers/{resolvedPrinterName}`
- Verifies `PrintJobRequest.OperationAttributes.DocumentFormat` is `"application/pdf"`
- Verifies fallback to `CupsOptions.PrinterName` when `printerName` parameter is `null`
- Verifies `InvalidOperationException` when `ServerUrl` is empty
- Verifies `InvalidOperationException` when both `printerName` parameter and `CupsOptions.PrinterName` are empty (covers both `null` and `""` cases)
- Verifies `InvalidOperationException` when `printerName` is an empty string `""` with no fallback configured
- Verifies `InvalidOperationException` when `ISharpIppClient` returns a response with `StatusCode != IppStatusCode.SuccessfulOk`; status code value must appear in exception message

**`CupsPrintQueueSinkTests`** (unit, in `Anela.Heblo.Tests`):
- Mocks `ICupsPrintingService`
- Verifies each file path is forwarded to `PrintAsync`
- Verifies `printerName` is not passed (null — uses configured default)

No integration tests against a real CUPS server.

---

## Out of Scope

- Multiple printers per sink invocation
- Retry logic (handled by the caller if needed)
- Job status polling / print completion confirmation
- CUPS server provisioning or Tailscale configuration
- `AzureAdapterServiceCollectionExtensions` wiring (covered by the existing abstraction spec)
- Separate `AddCupsPrintingService` extension without `IPrintQueueSink` binding (YAGNI)
