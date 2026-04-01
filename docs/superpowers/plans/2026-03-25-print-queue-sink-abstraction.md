# PrintQueueSink Abstraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the hardcoded filesystem `SendToPrinter` logic in `ExpeditionListService` with an `IPrintQueueSink` abstraction backed by two implementations: `FileSystemPrintQueueSink` (debug) and `AzureBlobPrintQueueSink` (production), selected via config.

**Architecture:** `IPrintQueueSink` lives in the Application layer. `FileSystemPrintQueueSink` lives alongside it. `AzureBlobPrintQueueSink` lives in a new `Anela.Heblo.Adapters.Azure` project. `Program.cs` reads `ExpeditionList:PrintSink` config and registers the appropriate implementation.

**Tech Stack:** .NET 8, `Azure.Storage.Blobs` (BlobContainerClient), IOptions pattern, xUnit + Moq.

**Spec:** `docs/superpowers/specs/2026-03-25-print-queue-sink-abstraction-design.md`

---

## File Map

**Create:**
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/IPrintQueueSink.cs`
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/FileSystemPrintQueueSink.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Anela.Heblo.Adapters.Azure.csproj`
- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueOptions.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs`
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/FileSystemPrintQueueSinkTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServicePrintSinkTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/AzureBlobPrintQueueSinkTests.cs`

**Modify:**
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs` — inject `IPrintQueueSink`, remove `SendToPrinter` method
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs` — add `PrintSink` property
- `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` — add project reference to Azure adapter
- `backend/src/Anela.Heblo.API/Program.cs` — register `IPrintQueueSink` based on config
- `backend/src/Anela.Heblo.API/appsettings.json` — add `PrintSink` value + `ExpeditionListBlobStorage` section
- `Anela.Heblo.sln` — add Azure adapter project

---

## Task 1: Define IPrintQueueSink interface

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/IPrintQueueSink.cs`

- [ ] **Step 1: Create the interface**

```csharp
// backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/IPrintQueueSink.cs
namespace Anela.Heblo.Application.Features.ExpeditionList.Services;

public interface IPrintQueueSink
{
    Task SendAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Build to verify no errors**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/IPrintQueueSink.cs
git commit -m "feat(expedition-list): add IPrintQueueSink interface"
```

---

## Task 2: Implement FileSystemPrintQueueSink (TDD)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/FileSystemPrintQueueSink.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/FileSystemPrintQueueSinkTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// backend/test/Anela.Heblo.Tests/Features/ExpeditionList/FileSystemPrintQueueSinkTests.cs
using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class FileSystemPrintQueueSinkTests : IDisposable
{
    private readonly string _sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly string _outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public FileSystemPrintQueueSinkTests()
    {
        Directory.CreateDirectory(_sourceDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_sourceDir)) Directory.Delete(_sourceDir, recursive: true);
        if (Directory.Exists(_outputDir)) Directory.Delete(_outputDir, recursive: true);
    }

    private FileSystemPrintQueueSink CreateSink(string folder) =>
        new FileSystemPrintQueueSink(
            Options.Create(new PrintPickingListOptions { PrintQueueFolder = folder }),
            NullLogger<FileSystemPrintQueueSink>.Instance);

    [Fact]
    public async Task SendAsync_ValidFiles_CopiesFilesToOutputFolder()
    {
        // Arrange
        var file1 = Path.Combine(_sourceDir, "order1.pdf");
        var file2 = Path.Combine(_sourceDir, "order2.pdf");
        await File.WriteAllTextAsync(file1, "pdf1");
        await File.WriteAllTextAsync(file2, "pdf2");

        var sink = CreateSink(_outputDir);

        // Act
        await sink.SendAsync([file1, file2]);

        // Assert
        Assert.True(File.Exists(Path.Combine(_outputDir, "order1.pdf")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "order2.pdf")));
    }

    [Fact]
    public async Task SendAsync_OutputFolderDoesNotExist_CreatesItAndCopiesFiles()
    {
        // Arrange
        var file = Path.Combine(_sourceDir, "order.pdf");
        await File.WriteAllTextAsync(file, "pdf");
        var nonExistentFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        var sink = CreateSink(nonExistentFolder);

        try
        {
            // Act
            await sink.SendAsync([file]);

            // Assert
            Assert.True(File.Exists(Path.Combine(nonExistentFolder, "order.pdf")));
        }
        finally
        {
            if (Directory.Exists(nonExistentFolder)) Directory.Delete(nonExistentFolder, recursive: true);
        }
    }

    [Fact]
    public async Task SendAsync_PrintQueueFolderNotConfigured_DoesNotThrow()
    {
        // Arrange
        var file = Path.Combine(_sourceDir, "order.pdf");
        await File.WriteAllTextAsync(file, "pdf");
        var sink = CreateSink(string.Empty);

        // Act & Assert
        var exception = await Record.ExceptionAsync(() => sink.SendAsync([file]));
        Assert.Null(exception);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FileSystemPrintQueueSinkTests"
```
Expected: Compilation error — `FileSystemPrintQueueSink` does not exist yet.

- [ ] **Step 3: Implement FileSystemPrintQueueSink (extract logic from ExpeditionListService)**

```csharp
// backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/FileSystemPrintQueueSink.cs
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionList.Services;

public class FileSystemPrintQueueSink : IPrintQueueSink
{
    private readonly IOptions<PrintPickingListOptions> _options;
    private readonly ILogger<FileSystemPrintQueueSink> _logger;

    public FileSystemPrintQueueSink(
        IOptions<PrintPickingListOptions> options,
        ILogger<FileSystemPrintQueueSink> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task SendAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
    {
        var folder = _options.Value.PrintQueueFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            _logger.LogWarning("PrintQueueFolder is not configured. Skipping printer queue copy.");
            return Task.CompletedTask;
        }

        Directory.CreateDirectory(folder);

        foreach (var f in filePaths)
        {
            var fileName = Path.GetFileName(f);
            if (string.IsNullOrEmpty(fileName))
            {
                _logger.LogWarning("Skipping file with invalid path: {FilePath}", f);
                continue;
            }

            File.Copy(f, Path.Combine(folder, fileName));
        }

        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FileSystemPrintQueueSinkTests"
```
Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/FileSystemPrintQueueSink.cs \
        backend/test/Anela.Heblo.Tests/Features/ExpeditionList/FileSystemPrintQueueSinkTests.cs
git commit -m "feat(expedition-list): implement FileSystemPrintQueueSink with tests"
```

---

## Task 3: Update ExpeditionListService to use IPrintQueueSink (TDD)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServicePrintSinkTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServicePrintSinkTests.cs
using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Domain.Features.Logistics.Picking;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class ExpeditionListServicePrintSinkTests
{
    private readonly Mock<IPickingListSource> _pickingListSource = new();
    private readonly Mock<ISendGridClient> _emailSender = new();
    private readonly Mock<IPrintQueueSink> _printQueueSink = new();

    private ExpeditionListService CreateService() => new ExpeditionListService(
        _pickingListSource.Object,
        _emailSender.Object,
        TimeProvider.System,
        Options.Create(new PrintPickingListOptions { EmailSender = "test@test.com" }),
        _printQueueSink.Object,
        NullLogger<ExpeditionListService>.Instance);

    [Fact]
    public async Task PrintPickingListAsync_SendToPrinterTrue_CallsSink()
    {
        // Arrange
        var files = new List<string> { "/tmp/order1.pdf" };
        _pickingListSource
            .Setup(x => x.CreatePickingList(It.IsAny<PrintPickingListRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrintPickingListResult { ExportedFiles = files, TotalCount = 1 });

        var request = new PrintPickingListRequest { SendToPrinter = true };
        var svc = CreateService();

        // Act
        await svc.PrintPickingListAsync(request);

        // Assert
        _printQueueSink.Verify(x => x.SendAsync(files, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PrintPickingListAsync_SendToPrinterFalse_DoesNotCallSink()
    {
        // Arrange
        var files = new List<string> { "/tmp/order1.pdf" };
        _pickingListSource
            .Setup(x => x.CreatePickingList(It.IsAny<PrintPickingListRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrintPickingListResult { ExportedFiles = files, TotalCount = 1 });

        var request = new PrintPickingListRequest { SendToPrinter = false };
        var svc = CreateService();

        // Act
        await svc.PrintPickingListAsync(request);

        // Assert
        _printQueueSink.Verify(x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

Note: `IPickingListSource` and `ISendGridClient` are referenced via their actual namespaces. Check existing usings in the project for the exact namespace of `IPickingListSource`. It likely lives in `Anela.Heblo.Domain.Features.Logistics.Picking` — confirm by searching the codebase.

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExpeditionListServicePrintSinkTests"
```
Expected: Compilation error — constructor mismatch (IPrintQueueSink not yet in ExpeditionListService).

- [ ] **Step 3: Update ExpeditionListService — inject IPrintQueueSink, remove SendToPrinter**

Replace the full file content:

```csharp
// backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs
using Anela.Heblo.Domain.Features.Logistics.Picking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Anela.Heblo.Application.Features.ExpeditionList.Services;

public class ExpeditionListService : IExpeditionListService
{
    private readonly IPickingListSource _pickingListSource;
    private readonly ISendGridClient _emailSender;
    private readonly TimeProvider _clock;
    private readonly IOptions<PrintPickingListOptions> _options;
    private readonly IPrintQueueSink _printQueueSink;
    private readonly ILogger<ExpeditionListService> _logger;

    public ExpeditionListService(
        IPickingListSource pickingListSource,
        ISendGridClient emailSender,
        TimeProvider clock,
        IOptions<PrintPickingListOptions> options,
        IPrintQueueSink printQueueSink,
        ILogger<ExpeditionListService> logger)
    {
        _pickingListSource = pickingListSource;
        _emailSender = emailSender;
        _clock = clock;
        _options = options;
        _printQueueSink = printQueueSink;
        _logger = logger;
    }

    public async Task<PrintPickingListResult> PrintPickingListAsync(
        PrintPickingListRequest request,
        IList<string>? emailList = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Generating new expedition list");
        var result = await _pickingListSource.CreatePickingList(request, cancellationToken);
        _logger.LogDebug("Expedition list generated");

        if (emailList != null && emailList.Any())
        {
            await SendEmailCopy(result, emailList);
            _logger.LogDebug("Copy sent by email");
        }

        if (request.SendToPrinter)
        {
            await _printQueueSink.SendAsync(result.ExportedFiles, cancellationToken);
            _logger.LogDebug("Sent to print queue");
        }

        await Cleanup(result);

        return result;
    }

    private Task Cleanup(PrintPickingListResult result)
    {
        foreach (var f in result.ExportedFiles)
        {
            if (File.Exists(f))
                File.Delete(f);
        }

        return Task.CompletedTask;
    }

    private async Task SendEmailCopy(PrintPickingListResult result, IEnumerable<string> emailRecipients)
    {
        var now = _clock.GetLocalNow();
        var msg = new SendGridMessage()
        {
            From = new EmailAddress(_options.Value.EmailSender),
            Subject = $"Expedice {now:yyyy-MM-dd}",
            HtmlContent = $@"
<strong>Expedice vygenerovana {now:yyyy-MM-dd HH:mm:ss}</strong></br>
</br>
</br>
<strong>Celkem {result.TotalCount} zakazek</strong></br>
</br>
</br>
",
        };

        msg.AddTos(emailRecipients.Select(s => new EmailAddress(s)).ToList());

        foreach (var a in result.ExportedFiles)
        {
            var bytes = await File.ReadAllBytesAsync(a);
            var b64 = Convert.ToBase64String(bytes);
            msg.AddAttachment(Path.GetFileName(a), b64, "pdf");
        }

        var response = await _emailSender.SendEmailAsync(msg);
        _logger.LogDebug("Sent email with result {SendGridStatusCode}: {SendGridMessage}",
            response.StatusCode,
            await response.Body.ReadAsStringAsync());
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExpeditionListServicePrintSinkTests"
```
Expected: 2 passed.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs \
        backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServicePrintSinkTests.cs
git commit -m "feat(expedition-list): inject IPrintQueueSink into ExpeditionListService"
```

---

## Task 4: Add PrintSink config property to PrintPickingListOptions

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs`

- [ ] **Step 1: Add PrintSink property**

Add one line to the class — after `SendToPrinterByDefault`:

```csharp
public string PrintSink { get; set; } = "FileSystem"; // "FileSystem" | "AzureBlob"
```

Full file after change:

```csharp
namespace Anela.Heblo.Application.Features.ExpeditionList;

public class PrintPickingListOptions
{
    public const string ConfigurationKey = "ExpeditionList";

    public string EmailSender { get; set; } = string.Empty;
    public string PrintQueueFolder { get; set; } = string.Empty;
    public string SendGridApiKey { get; set; } = string.Empty;
    public List<string> DefaultEmailRecipients { get; set; } = new();
    public int SourceStateId { get; set; } = -2;
    public int DesiredStateId { get; set; } = 26;
    public bool SendToPrinterByDefault { get; set; } = false;
    public bool ChangeOrderStateByDefault { get; set; } = true;
    public string PrintSink { get; set; } = "FileSystem"; // "FileSystem" | "AzureBlob"
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs
git commit -m "feat(expedition-list): add PrintSink config property"
```

---

## Task 5: Create Anela.Heblo.Adapters.Azure project

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Anela.Heblo.Adapters.Azure.csproj`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueOptions.cs`
- Modify: `Anela.Heblo.sln`

- [ ] **Step 1: Create the project file**

```xml
<!-- backend/src/Adapters/Anela.Heblo.Adapters.Azure/Anela.Heblo.Adapters.Azure.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net8.0</TargetFramework>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <RootNamespace>Anela.Heblo.Adapters.Azure</RootNamespace>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Azure.Storage.Blobs" Version="12.25.0" />
        <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="8.0.0" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\..\Anela.Heblo.Application\Anela.Heblo.Application.csproj" />
    </ItemGroup>

</Project>
```

- [ ] **Step 2: Create AzureBlobPrintQueueOptions**

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueOptions.cs
namespace Anela.Heblo.Adapters.Azure.Features.ExpeditionList;

public class AzureBlobPrintQueueOptions
{
    public const string ConfigurationKey = "ExpeditionListBlobStorage";

    public string ConnectionString { get; set; } = string.Empty;
    public string ContainerName { get; set; } = "expedition-lists";
}
```

- [ ] **Step 3: Add project to solution**

```bash
dotnet sln Anela.Heblo.sln add backend/src/Adapters/Anela.Heblo.Adapters.Azure/Anela.Heblo.Adapters.Azure.csproj --solution-folder Adapters
```

- [ ] **Step 4: Build to verify**

```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Azure/Anela.Heblo.Adapters.Azure.csproj
```
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Azure/ Anela.Heblo.sln
git commit -m "feat(expedition-list): add Anela.Heblo.Adapters.Azure project skeleton"
```

---

## Task 6: Implement AzureBlobPrintQueueSink (TDD)

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/AzureBlobPrintQueueSinkTests.cs`

Note: Tests live in `Anela.Heblo.Tests` (not a new test project). The test project already references `Azure.Storage.Blobs` via its transitive dependencies.

- [ ] **Step 1: Write failing tests**

```csharp
// backend/test/Anela.Heblo.Tests/Features/ExpeditionList/AzureBlobPrintQueueSinkTests.cs
using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class AzureBlobPrintQueueSinkTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly Mock<BlobContainerClient> _containerClient = new();
    private readonly Mock<BlobClient> _blobClient = new();

    public AzureBlobPrintQueueSinkTests()
    {
        Directory.CreateDirectory(_tempDir);
        _containerClient
            .Setup(x => x.GetBlobClient(It.IsAny<string>()))
            .Returns(_blobClient.Object);
        _containerClient
            .Setup(x => x.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Azure.Response<BlobContainerInfo>>());
        _blobClient
            .Setup(x => x.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Azure.Response<BlobContentInfo>>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private AzureBlobPrintQueueSink CreateSink() =>
        new AzureBlobPrintQueueSink(
            _containerClient.Object,
            TimeProvider.System,
            NullLogger<AzureBlobPrintQueueSink>.Instance);

    [Fact]
    public async Task SendAsync_ValidFiles_UploadsEachFileToBlob()
    {
        // Arrange
        var file1 = Path.Combine(_tempDir, "order1.pdf");
        var file2 = Path.Combine(_tempDir, "order2.pdf");
        await File.WriteAllTextAsync(file1, "pdf1");
        await File.WriteAllTextAsync(file2, "pdf2");

        var sink = CreateSink();

        // Act
        await sink.SendAsync([file1, file2]);

        // Assert
        _blobClient.Verify(x => x.UploadAsync(
            It.IsAny<Stream>(),
            true,
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SendAsync_ValidFile_UsesBlobNameWithDatePrefix()
    {
        // Arrange
        var file = Path.Combine(_tempDir, "order1.pdf");
        await File.WriteAllTextAsync(file, "pdf");

        string? capturedBlobName = null;
        _containerClient
            .Setup(x => x.GetBlobClient(It.IsAny<string>()))
            .Callback<string>(name => capturedBlobName = name)
            .Returns(_blobClient.Object);

        var sink = CreateSink();

        // Act
        await sink.SendAsync([file]);

        // Assert
        Assert.NotNull(capturedBlobName);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}/order1\.pdf$", capturedBlobName);
    }

    [Fact]
    public async Task SendAsync_EmptyFilePaths_DoesNotUpload()
    {
        // Arrange
        var sink = CreateSink();

        // Act
        await sink.SendAsync([]);

        // Assert
        _blobClient.Verify(x => x.UploadAsync(
            It.IsAny<Stream>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~AzureBlobPrintQueueSinkTests"
```
Expected: Compilation error — `AzureBlobPrintQueueSink` does not exist yet.

Note: If the test project doesn't reference the Azure adapter project yet, add the reference first:
```bash
dotnet add backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj reference backend/src/Adapters/Anela.Heblo.Adapters.Azure/Anela.Heblo.Adapters.Azure.csproj
```

- [ ] **Step 3: Implement AzureBlobPrintQueueSink**

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.Azure.Features.ExpeditionList;

public class AzureBlobPrintQueueSink : IPrintQueueSink
{
    private readonly BlobContainerClient _containerClient;
    private readonly TimeProvider _clock;
    private readonly ILogger<AzureBlobPrintQueueSink> _logger;

    public AzureBlobPrintQueueSink(
        BlobContainerClient containerClient,
        TimeProvider clock,
        ILogger<AzureBlobPrintQueueSink> logger)
    {
        _containerClient = containerClient;
        _clock = clock;
        _logger = logger;
    }

    public async Task SendAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
    {
        await _containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        var datePrefix = _clock.GetLocalNow().ToString("yyyy-MM-dd");

        foreach (var filePath in filePaths)
        {
            var fileName = Path.GetFileName(filePath);
            if (string.IsNullOrEmpty(fileName))
            {
                _logger.LogWarning("Skipping file with invalid path: {FilePath}", filePath);
                continue;
            }

            var blobName = $"{datePrefix}/{fileName}";
            await using var fileStream = File.OpenRead(filePath);
            var blobClient = _containerClient.GetBlobClient(blobName);
            await blobClient.UploadAsync(fileStream, overwrite: true, cancellationToken: cancellationToken);
            _logger.LogDebug("Uploaded {FileName} to blob {BlobName}", fileName, blobName);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~AzureBlobPrintQueueSinkTests"
```
Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs \
        backend/test/Anela.Heblo.Tests/Features/ExpeditionList/AzureBlobPrintQueueSinkTests.cs \
        backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
git commit -m "feat(expedition-list): implement AzureBlobPrintQueueSink with tests"
```

---

## Task 7: Create AzureAdapterModule

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs`

- [ ] **Step 1: Create the module**

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs
using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Azure;

public static class AzureAdapterModule
{
    public static IServiceCollection AddAzurePrintQueueSink(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AzureBlobPrintQueueOptions>(
            configuration.GetSection(AzureBlobPrintQueueOptions.ConfigurationKey));

        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<AzureBlobPrintQueueOptions>>().Value;
            return new BlobContainerClient(options.ConnectionString, options.ContainerName);
        });

        services.AddScoped<IPrintQueueSink, AzureBlobPrintQueueSink>();

        return services;
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Azure/Anela.Heblo.Adapters.Azure.csproj
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs
git commit -m "feat(expedition-list): add AzureAdapterModule for IPrintQueueSink registration"
```

---

## Task 8: Wire DI in API, update config

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
- Modify: `backend/src/Anela.Heblo.API/Program.cs`
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`

- [ ] **Step 1: Add Azure adapter project reference to API csproj**

In `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`, add inside the existing `<ItemGroup>` with other project references:

```xml
<ProjectReference Include="..\Adapters\Anela.Heblo.Adapters.Azure\Anela.Heblo.Adapters.Azure.csproj" />
```

- [ ] **Step 2: Add using and conditional registration in Program.cs**

Add the using at the top of Program.cs:
```csharp
using Anela.Heblo.Adapters.Azure;
```

In the "Adapters" section of `Program.cs` (after the existing adapter registrations, before `AddMcpServices()`), add:

```csharp
// Print queue sink — config-driven
var printSink = builder.Configuration["ExpeditionList:PrintSink"];
if (printSink == "AzureBlob")
    builder.Services.AddAzurePrintQueueSink(builder.Configuration);
else
    builder.Services.AddScoped<IPrintQueueSink, FileSystemPrintQueueSink>();
```

Also add the using for the Application types at the top:
```csharp
using Anela.Heblo.Application.Features.ExpeditionList.Services;
```

- [ ] **Step 3: Update appsettings.json**

Add `PrintSink` to the `ExpeditionList` section and add the new `ExpeditionListBlobStorage` section at the end of the file (before the closing `}`):

In the `ExpeditionList` section, add:
```json
"PrintSink": "FileSystem"
```

Add new section after the `ExpeditionList` block:
```json
"ExpeditionListBlobStorage": {
  "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=youraccountname;AccountKey=youraccountkey;EndpointSuffix=core.windows.net",
  "ContainerName": "expedition-lists"
}
```

- [ ] **Step 4: Build the full solution**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```
Expected: Build succeeded.

- [ ] **Step 5: Run all backend tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: All tests pass. Look specifically for `ExpeditionList` and `FileStorage` tests.

- [ ] **Step 6: Run dotnet format**

```bash
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
dotnet format backend/src/Adapters/Anela.Heblo.Adapters.Azure/Anela.Heblo.Adapters.Azure.csproj
dotnet format backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```
Expected: No formatting changes (or apply them if there are).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj \
        backend/src/Anela.Heblo.API/Program.cs \
        backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat(expedition-list): wire IPrintQueueSink DI and update config"
```

---

## Verification Checklist

After all tasks complete:

- [ ] `dotnet build` passes on full solution
- [ ] `dotnet test` passes for `Anela.Heblo.Tests`
- [ ] `ExpeditionList:PrintSink = "FileSystem"` → `FileSystemPrintQueueSink` is used
- [ ] `ExpeditionList:PrintSink = "AzureBlob"` → `AzureBlobPrintQueueSink` is used
- [ ] `ExpeditionListService` no longer contains any filesystem copy logic
- [ ] `dotnet format` reports no violations
