# CUPS Print Sink Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `CupsPrintQueueSink` that sends expedition list PDFs directly to a CUPS server via IPP/HTTP using SharpIppNext, selectable via config alongside the existing `FileSystem` and `AzureBlob` sinks.

**Architecture:** New adapter project `Anela.Heblo.Adapters.Cups` contains a generic `CupsPrintingService` (SharpIpp-aware) and a thin `CupsPrintQueueSink` bridge. Basic auth is applied per-request via a `DelegatingHandler`. The sink is selected in `Program.cs` based on `ExpeditionList:PrintSink = "Cups"`.

**Tech Stack:** .NET 8, SharpIppNext 3.x (`SharpIppNext` NuGet), Moq, xUnit, `Microsoft.Extensions.Http`

**Spec:** `docs/superpowers/specs/2026-03-25-cups-print-sink-design.md`

---

## File Map

| Action | Path |
|--------|------|
| Create | `backend/src/Adapters/Anela.Heblo.Adapters.Cups/Anela.Heblo.Adapters.Cups.csproj` |
| Create | `backend/src/Adapters/Anela.Heblo.Adapters.Cups/CupsOptions.cs` |
| Create | `backend/src/Adapters/Anela.Heblo.Adapters.Cups/CupsAuthHandler.cs` |
| Create | `backend/src/Adapters/Anela.Heblo.Adapters.Cups/ICupsPrintingService.cs` |
| Create | `backend/src/Adapters/Anela.Heblo.Adapters.Cups/CupsPrintingService.cs` |
| Create | `backend/src/Adapters/Anela.Heblo.Adapters.Cups/Features/ExpeditionList/CupsPrintQueueSink.cs` |
| Create | `backend/src/Adapters/Anela.Heblo.Adapters.Cups/CupsAdapterServiceCollectionExtensions.cs` |
| Create | `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CupsPrintingServiceTests.cs` |
| Create | `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CupsPrintQueueSinkTests.cs` |
| Modify | `Anela.Heblo.sln` — add new project |
| Modify | `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — add ProjectReference |
| Modify | `backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs:15` — update comment |
| Modify | `backend/src/Anela.Heblo.API/Program.cs:61-64` — if/else → switch with Cups case |
| Modify | `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` — add ProjectReference to Cups adapter |

---

## Task 1: Scaffold the adapter project

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Cups/Anela.Heblo.Adapters.Cups.csproj`
- Modify: `Anela.Heblo.sln`

- [ ] **Step 1: Create the .csproj file**

Create `backend/src/Adapters/Anela.Heblo.Adapters.Cups/Anela.Heblo.Adapters.Cups.csproj`:

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

- [ ] **Step 2: Add project to solution**

Run from the repo root (`/Users/pajgrtondrej/Work/GitHub/Anela.Heblo`):

```bash
dotnet sln Anela.Heblo.sln add backend/src/Adapters/Anela.Heblo.Adapters.Cups/Anela.Heblo.Adapters.Cups.csproj
```

Expected output: `Project ... added to the solution.`

- [ ] **Step 3: Verify it builds**

```bash
cd backend && dotnet build src/Adapters/Anela.Heblo.Adapters.Cups/Anela.Heblo.Adapters.Cups.csproj
```

Expected: `Build succeeded.`

---

## Task 2: CupsOptions

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Cups/CupsOptions.cs`

- [ ] **Step 1: Create CupsOptions**

```csharp
namespace Anela.Heblo.Adapters.Cups;

public class CupsOptions
{
    public const string ConfigurationKey = "Cups";

    public string ServerUrl { get; set; } = string.Empty;    // e.g. "http://cups.internal:631"
    public string PrinterName { get; set; } = string.Empty;  // fallback printer name
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Build to verify**

```bash
cd backend && dotnet build src/Adapters/Anela.Heblo.Adapters.Cups/Anela.Heblo.Adapters.Cups.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Cups/CupsOptions.cs
git commit -m "feat(cups): add CupsOptions"
```

---

## Task 3: CupsAuthHandler

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Cups/CupsAuthHandler.cs`

The handler encodes `Username:Password` as Base64 and sets the `Authorization: Basic ...` header on each outgoing HTTP request. Credentials are read from `IOptions<CupsOptions>` at request time (not at startup), which is thread-safe and works correctly with the pooled `HttpMessageHandler`.

- [ ] **Step 1: Create CupsAuthHandler**

```csharp
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Cups;

internal class CupsAuthHandler : DelegatingHandler
{
    private readonly IOptions<CupsOptions> _options;

    public CupsAuthHandler(IOptions<CupsOptions> options)
    {
        _options = options;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var opts = _options.Value;
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{opts.Username}:{opts.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        return base.SendAsync(request, cancellationToken);
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
cd backend && dotnet build src/Adapters/Anela.Heblo.Adapters.Cups/Anela.Heblo.Adapters.Cups.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Cups/CupsAuthHandler.cs
git commit -m "feat(cups): add CupsAuthHandler"
```

---

## Task 4: ICupsPrintingService + CupsPrintingService (TDD)

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Cups/ICupsPrintingService.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Cups/CupsPrintingService.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CupsPrintingServiceTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`

**SharpIppNext API (verified against v3.0.0 DLL):**
- Interface: `SharpIpp.ISharpIppClient`
- Method: `Task<PrintJobResponse> PrintJobAsync(PrintJobRequest request, CancellationToken cancellationToken)`
- Request type: `SharpIpp.Models.Requests.PrintJobRequest`
  - `Stream Document`
  - `PrintJobOperationAttributes OperationAttributes` (`SharpIpp.Models.Requests.PrintJobOperationAttributes`)
    - `Uri PrinterUri`
    - `string DocumentFormat`
- Response type: `SharpIpp.Models.Responses.PrintJobResponse`
  - `IppStatusCode StatusCode` (`SharpIpp.Protocol.Models.IppStatusCode`)
  - `IppStatusCode.SuccessfulOk` is the success value

- [ ] **Step 1: Add ProjectReference to test project**

In `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`, add inside the existing `<ItemGroup>` that contains the other ProjectReferences:

```xml
<ProjectReference Include="..\..\src\Adapters\Anela.Heblo.Adapters.Cups\Anela.Heblo.Adapters.Cups.csproj" />
```

- [ ] **Step 2: Create the interface**

Create `backend/src/Adapters/Anela.Heblo.Adapters.Cups/ICupsPrintingService.cs`:

```csharp
namespace Anela.Heblo.Adapters.Cups;

public interface ICupsPrintingService
{
    Task PrintAsync(string filePath, string? printerName = null, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Write failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CupsPrintingServiceTests.cs`:

```csharp
using Anela.Heblo.Adapters.Cups;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SharpIpp;
using SharpIpp.Models.Requests;
using SharpIpp.Models.Responses;
using SharpIpp.Protocol.Models;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class CupsPrintingServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly Mock<ISharpIppClient> _sharpIppClient = new();

    public CupsPrintingServiceTests()
    {
        Directory.CreateDirectory(_tempDir);

        _sharpIppClient
            .Setup(x => x.PrintJobAsync(It.IsAny<PrintJobRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrintJobResponse { StatusCode = IppStatusCode.SuccessfulOk });
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private string CreateTempPdf(string name = "test.pdf")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, [0x25, 0x50, 0x44, 0x46]); // %PDF header
        return path;
    }

    private CupsPrintingService CreateService(string serverUrl = "http://cups.internal:631", string printerName = "default-printer") =>
        new CupsPrintingService(
            _sharpIppClient.Object,
            Options.Create(new CupsOptions
            {
                ServerUrl = serverUrl,
                PrinterName = printerName,
                Username = "admin",
                Password = "secret"
            }),
            NullLogger<CupsPrintingService>.Instance);

    [Fact]
    public async Task PrintAsync_ValidFile_SendsPrintJobWithCorrectPrinterUri()
    {
        // Arrange
        var file = CreateTempPdf();
        PrintJobRequest? captured = null;
        _sharpIppClient
            .Setup(x => x.PrintJobAsync(It.IsAny<PrintJobRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PrintJobRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new PrintJobResponse { StatusCode = IppStatusCode.SuccessfulOk });

        var svc = CreateService(serverUrl: "http://cups.internal:631", printerName: "default-printer");

        // Act
        await svc.PrintAsync(file, printerName: "my-printer");

        // Assert
        Assert.NotNull(captured);
        Assert.Equal("http://cups.internal:631/printers/my-printer", captured.OperationAttributes.PrinterUri.ToString());
    }

    [Fact]
    public async Task PrintAsync_ValidFile_SendsDocumentFormatAsPdf()
    {
        // Arrange
        var file = CreateTempPdf();
        PrintJobRequest? captured = null;
        _sharpIppClient
            .Setup(x => x.PrintJobAsync(It.IsAny<PrintJobRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PrintJobRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new PrintJobResponse { StatusCode = IppStatusCode.SuccessfulOk });

        var svc = CreateService();

        // Act
        await svc.PrintAsync(file, printerName: "my-printer");

        // Assert
        Assert.NotNull(captured);
        Assert.Equal("application/pdf", captured.OperationAttributes.DocumentFormat);
    }

    [Fact]
    public async Task PrintAsync_NullPrinterName_FallsBackToConfiguredDefault()
    {
        // Arrange
        var file = CreateTempPdf();
        PrintJobRequest? captured = null;
        _sharpIppClient
            .Setup(x => x.PrintJobAsync(It.IsAny<PrintJobRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PrintJobRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new PrintJobResponse { StatusCode = IppStatusCode.SuccessfulOk });

        var svc = CreateService(serverUrl: "http://cups.internal:631", printerName: "fallback-printer");

        // Act
        await svc.PrintAsync(file, printerName: null);

        // Assert
        Assert.NotNull(captured);
        Assert.Equal("http://cups.internal:631/printers/fallback-printer", captured.OperationAttributes.PrinterUri.ToString());
    }

    [Fact]
    public async Task PrintAsync_EmptyServerUrl_ThrowsInvalidOperationException()
    {
        // Arrange
        var file = CreateTempPdf();
        var svc = CreateService(serverUrl: "");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.PrintAsync(file));
    }

    [Fact]
    public async Task PrintAsync_NullPrinterNameAndNoFallback_ThrowsInvalidOperationException()
    {
        // Arrange
        var file = CreateTempPdf();
        var svc = CreateService(printerName: ""); // no fallback

        // Act & Assert: explicit null
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.PrintAsync(file, printerName: null));
    }

    [Fact]
    public async Task PrintAsync_EmptyStringPrinterNameAndNoFallback_ThrowsInvalidOperationException()
    {
        // Arrange
        var file = CreateTempPdf();
        var svc = CreateService(printerName: ""); // no fallback

        // Act & Assert: empty string
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.PrintAsync(file, printerName: ""));
    }

    [Fact]
    public async Task PrintAsync_IppErrorStatus_ThrowsInvalidOperationExceptionWithStatusCode()
    {
        // Arrange
        var file = CreateTempPdf();
        _sharpIppClient
            .Setup(x => x.PrintJobAsync(It.IsAny<PrintJobRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrintJobResponse { StatusCode = IppStatusCode.ClientErrorNotFound });

        var svc = CreateService();

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.PrintAsync(file));

        // Assert: status code value appears in message
        Assert.Contains("ClientErrorNotFound", ex.Message);
    }
}
```

- [ ] **Step 4: Run tests — verify they fail to build**

```bash
cd backend && dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | grep "error CS"
```

Expected: Compiler error mentioning `CupsPrintingService` — e.g. `error CS0246: The type or namespace name 'CupsPrintingService' could not be found`.

- [ ] **Step 5: Create CupsPrintingService**

Create `backend/src/Adapters/Anela.Heblo.Adapters.Cups/CupsPrintingService.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpIpp;
using SharpIpp.Models.Requests;
using SharpIpp.Protocol.Models;

namespace Anela.Heblo.Adapters.Cups;

public class CupsPrintingService : ICupsPrintingService
{
    private readonly ISharpIppClient _sharpIppClient;
    private readonly IOptions<CupsOptions> _options;
    private readonly ILogger<CupsPrintingService> _logger;

    public CupsPrintingService(
        ISharpIppClient sharpIppClient,
        IOptions<CupsOptions> options,
        ILogger<CupsPrintingService> logger)
    {
        _sharpIppClient = sharpIppClient;
        _options = options;
        _logger = logger;
    }

    public async Task PrintAsync(string filePath, string? printerName = null, CancellationToken cancellationToken = default)
    {
        var opts = _options.Value;

        if (string.IsNullOrWhiteSpace(opts.ServerUrl))
            throw new InvalidOperationException("CupsOptions.ServerUrl is not configured.");

        var resolvedPrinter = string.IsNullOrWhiteSpace(printerName)
            ? opts.PrinterName
            : printerName;

        if (string.IsNullOrWhiteSpace(resolvedPrinter))
            throw new InvalidOperationException(
                "No printer name provided and CupsOptions.PrinterName is not configured.");

        using var fileStream = File.OpenRead(filePath);

        var request = new PrintJobRequest
        {
            Document = fileStream,
            OperationAttributes = new PrintJobOperationAttributes
            {
                PrinterUri = new Uri($"{opts.ServerUrl}/printers/{resolvedPrinter}"),
                DocumentFormat = "application/pdf"
            }
        };

        var response = await _sharpIppClient.PrintJobAsync(request, cancellationToken);

        if (response.StatusCode != IppStatusCode.SuccessfulOk)
            throw new InvalidOperationException(
                $"CUPS print job failed with status: {response.StatusCode}");

        _logger.LogDebug("CUPS print job submitted. JobId: {JobId}, Printer: {Printer}",
            response.JobAttributes?.JobId, resolvedPrinter);
    }
}
```

- [ ] **Step 6: Run tests — verify they pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CupsPrintingServiceTests" 2>&1 | tail -10
```

Expected: `7 passed, 0 failed`

- [ ] **Step 7: Run dotnet format**

```bash
cd backend && dotnet format src/Adapters/Anela.Heblo.Adapters.Cups/Anela.Heblo.Adapters.Cups.csproj
```

- [ ] **Step 8: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Cups/ICupsPrintingService.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.Cups/CupsPrintingService.cs \
        backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CupsPrintingServiceTests.cs \
        backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
        Anela.Heblo.sln
git commit -m "feat(cups): add CupsPrintingService with tests"
```

---

## Task 5: CupsPrintQueueSink (TDD)

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Cups/Features/ExpeditionList/CupsPrintQueueSink.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CupsPrintQueueSinkTests.cs`

- [ ] **Step 1: Write failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CupsPrintQueueSinkTests.cs`:

```csharp
using Anela.Heblo.Adapters.Cups;
using Anela.Heblo.Adapters.Cups.Features.ExpeditionList;
using Moq;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class CupsPrintQueueSinkTests
{
    private readonly Mock<ICupsPrintingService> _printingService = new();

    private CupsPrintQueueSink CreateSink() =>
        new CupsPrintQueueSink(_printingService.Object);

    [Fact]
    public async Task SendAsync_MultipleFiles_CallsPrintAsyncForEachFile()
    {
        // Arrange
        var files = new List<string> { "/tmp/a.pdf", "/tmp/b.pdf" };
        _printingService
            .Setup(x => x.PrintAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sink = CreateSink();

        // Act
        await sink.SendAsync(files);

        // Assert
        _printingService.Verify(
            x => x.PrintAsync("/tmp/a.pdf", null, It.IsAny<CancellationToken>()),
            Times.Once);
        _printingService.Verify(
            x => x.PrintAsync("/tmp/b.pdf", null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendAsync_PassesNullPrinterName_UsesConfiguredDefault()
    {
        // Arrange: verify printerName is never explicitly set (always null)
        var files = new List<string> { "/tmp/order.pdf" };
        string? capturedPrinterName = "sentinel"; // non-null sentinel

        _printingService
            .Setup(x => x.PrintAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string?, CancellationToken>((_, pn, _) => capturedPrinterName = pn)
            .Returns(Task.CompletedTask);

        var sink = CreateSink();

        // Act
        await sink.SendAsync(files);

        // Assert
        Assert.Null(capturedPrinterName);
    }

    [Fact]
    public async Task SendAsync_EmptyList_DoesNotCallPrintAsync()
    {
        // Arrange
        var sink = CreateSink();

        // Act
        await sink.SendAsync([]);

        // Assert
        _printingService.Verify(
            x => x.PrintAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
```

- [ ] **Step 2: Run tests — verify they fail to build**

```bash
cd backend && dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | grep "error CS"
```

Expected: Compiler error mentioning `CupsPrintQueueSink` — e.g. `error CS0246: The type or namespace name 'CupsPrintQueueSink' could not be found`.

- [ ] **Step 3: Create CupsPrintQueueSink**

Create `backend/src/Adapters/Anela.Heblo.Adapters.Cups/Features/ExpeditionList/CupsPrintQueueSink.cs`:

```csharp
using Anela.Heblo.Application.Features.ExpeditionList.Services;

namespace Anela.Heblo.Adapters.Cups.Features.ExpeditionList;

public class CupsPrintQueueSink : IPrintQueueSink
{
    private readonly ICupsPrintingService _cupsPrintingService;

    public CupsPrintQueueSink(ICupsPrintingService cupsPrintingService)
    {
        _cupsPrintingService = cupsPrintingService;
    }

    public async Task SendAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
    {
        foreach (var filePath in filePaths)
        {
            await _cupsPrintingService.PrintAsync(filePath, cancellationToken: cancellationToken);
        }
    }
}
```

- [ ] **Step 4: Run tests — verify they pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CupsPrintQueueSinkTests" 2>&1 | tail -10
```

Expected: `3 passed, 0 failed`

- [ ] **Step 5: Run dotnet format**

```bash
cd backend && dotnet format src/Adapters/Anela.Heblo.Adapters.Cups/Anela.Heblo.Adapters.Cups.csproj
```

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Cups/Features/ \
        backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CupsPrintQueueSinkTests.cs
git commit -m "feat(cups): add CupsPrintQueueSink with tests"
```

---

## Task 6: DI Registration

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Cups/CupsAdapterServiceCollectionExtensions.cs`

- [ ] **Step 1: Create the extension method**

Create `backend/src/Adapters/Anela.Heblo.Adapters.Cups/CupsAdapterServiceCollectionExtensions.cs`:

```csharp
using Anela.Heblo.Adapters.Cups.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharpIpp;

namespace Anela.Heblo.Adapters.Cups;

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

        // Transient: each resolve gets a fresh HttpClient from the factory (disposal managed by factory)
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

- [ ] **Step 2: Build to verify**

```bash
cd backend && dotnet build src/Adapters/Anela.Heblo.Adapters.Cups/Anela.Heblo.Adapters.Cups.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Cups/CupsAdapterServiceCollectionExtensions.cs
git commit -m "feat(cups): add CupsAdapterServiceCollectionExtensions"
```

---

## Task 7: Wire into Program.cs

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` — add ProjectReference
- Modify: `backend/src/Anela.Heblo.API/Program.cs:59-64` — if/else → switch with Cups case
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs:15` — update comment

- [ ] **Step 1: Add ProjectReference to API project**

In `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`, add inside the `<ItemGroup>` that contains the other adapter `ProjectReference` entries:

```xml
<ProjectReference Include="..\Adapters\Anela.Heblo.Adapters.Cups\Anela.Heblo.Adapters.Cups.csproj" />
```

- [ ] **Step 2: Update PrintPickingListOptions comment**

In `backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs`, line 15, change:

```csharp
public string PrintSink { get; set; } = "FileSystem"; // "FileSystem" | "AzureBlob"
```

to:

```csharp
public string PrintSink { get; set; } = "FileSystem"; // "FileSystem" | "AzureBlob" | "Cups"
```

- [ ] **Step 3: Update Program.cs**

In `backend/src/Anela.Heblo.API/Program.cs`, find the existing print sink block (currently lines 59–64):

```csharp
// Print queue sink — config-driven
var printSink = builder.Configuration["ExpeditionList:PrintSink"];
if (printSink == "AzureBlob")
    builder.Services.AddAzurePrintQueueSink(builder.Configuration);
else
    builder.Services.AddScoped<IPrintQueueSink, FileSystemPrintQueueSink>();
```

Replace with:

```csharp
// Print queue sink — valid values: "FileSystem" (default), "AzureBlob", "Cups"
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

Also add the using at the top of `Program.cs` (with the other adapter usings):

```csharp
using Anela.Heblo.Adapters.Cups;
```

- [ ] **Step 4: Build the API project**

```bash
cd backend && dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 5: Run dotnet format**

```bash
cd backend && dotnet format src/Anela.Heblo.API/Anela.Heblo.API.csproj && dotnet format src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj \
        backend/src/Anela.Heblo.API/Program.cs \
        backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs
git commit -m "feat(cups): wire CupsPrintQueueSink into Program.cs print sink selection"
```

---

## Task 8: Final verification

- [ ] **Step 1: Run all ExpeditionList tests**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExpeditionList" 2>&1 | tail -10
```

Expected: All pass (includes existing `FileSystemPrintQueueSinkTests`, `AzureBlobPrintQueueSinkTests`, `ExpeditionListServicePrintSinkTests`, and new `CupsPrintingServiceTests`, `CupsPrintQueueSinkTests`).

- [ ] **Step 2: Run full test suite**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | tail -5
```

Expected: All pass, 0 failed.

- [ ] **Step 3: Full solution build**

```bash
cd backend && dotnet build Anela.Heblo.sln 2>&1 | tail -5
```

Expected: `Build succeeded.`
