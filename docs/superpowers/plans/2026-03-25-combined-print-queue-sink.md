# Combined Print Queue Sink Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `"Combined"` print sink that uploads expedition list PDFs to Azure Blob Storage and sends them to CUPS in sequence, with fail-fast semantics.

**Architecture:** `CombinedPrintQueueSink` is a thin combinator class in the API project (composition root). It holds two `IPrintQueueSink` dependencies resolved via ASP.NET Core 8 keyed services — `"azure"` and `"cups"` — which keeps the constructor testable with plain `Mock<IPrintQueueSink>` and avoids concrete-type injection issues. Program.cs registers keyed instances and then registers `CombinedPrintQueueSink` as the non-keyed `IPrintQueueSink`.

**Tech Stack:** .NET 8, ASP.NET Core keyed DI, xUnit, Moq

---

## File Map

| Action | Path | Purpose |
|--------|------|---------|
| **Create** | `backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs` | Combinator — delegates to azure then cups |
| **Modify** | `backend/src/Anela.Heblo.API/Program.cs` (lines 60–73) | Add `"Combined"` switch case + update comment |
| **Create** | `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CombinedPrintQueueSinkTests.cs` | 4 test cases for the combinator |

---

## Task 1: Write the failing tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CombinedPrintQueueSinkTests.cs`

- [ ] **Step 1: Create the test file**

```csharp
using Anela.Heblo.API.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Moq;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class CombinedPrintQueueSinkTests
{
    private readonly Mock<IPrintQueueSink> _azureSink = new();
    private readonly Mock<IPrintQueueSink> _cupsSink = new();

    private CombinedPrintQueueSink CreateSink() =>
        new CombinedPrintQueueSink(_azureSink.Object, _cupsSink.Object);

    [Fact]
    public async Task SendAsync_BothSucceed_CallsBothSinksWithSamePaths()
    {
        // Arrange
        var files = new List<string> { "/tmp/a.pdf", "/tmp/b.pdf" };
        _azureSink
            .Setup(x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _cupsSink
            .Setup(x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sink = CreateSink();

        // Act
        await sink.SendAsync(files);

        // Assert
        _azureSink.Verify(
            x => x.SendAsync(
                It.Is<IEnumerable<string>>(p => p.SequenceEqual(files)),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _cupsSink.Verify(
            x => x.SendAsync(
                It.Is<IEnumerable<string>>(p => p.SequenceEqual(files)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendAsync_AzureThrows_CupsNeverCalledAndExceptionPropagates()
    {
        // Arrange
        var files = new List<string> { "/tmp/a.pdf" };
        _azureSink
            .Setup(x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("azure failed"));

        var sink = CreateSink();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => sink.SendAsync(files));
        _cupsSink.Verify(
            x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendAsync_AzureSucceedsCupsThrows_ExceptionPropagates()
    {
        // Arrange
        var files = new List<string> { "/tmp/a.pdf" };
        _azureSink
            .Setup(x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _cupsSink
            .Setup(x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cups failed"));

        var sink = CreateSink();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => sink.SendAsync(files));
    }

    [Fact]
    public async Task SendAsync_SinglePassEnumerable_BothSinksReceiveAllPaths()
    {
        // Arrange: yield-return produces a single-pass IEnumerable
        IEnumerable<string> SinglePass()
        {
            yield return "/tmp/a.pdf";
            yield return "/tmp/b.pdf";
        }

        List<string>? azureCaptured = null;
        List<string>? cupsCaptured = null;

        _azureSink
            .Setup(x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, CancellationToken>((paths, _) => azureCaptured = paths.ToList())
            .Returns(Task.CompletedTask);
        _cupsSink
            .Setup(x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, CancellationToken>((paths, _) => cupsCaptured = paths.ToList())
            .Returns(Task.CompletedTask);

        var sink = CreateSink();

        // Act
        await sink.SendAsync(SinglePass());

        // Assert: both sinks got both paths, not an empty sequence
        Assert.Equal(["/tmp/a.pdf", "/tmp/b.pdf"], azureCaptured);
        Assert.Equal(["/tmp/a.pdf", "/tmp/b.pdf"], cupsCaptured);
    }
}
```

- [ ] **Step 2: Run the tests — expect compilation failure (class not found)**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CombinedPrintQueueSinkTests" 2>&1 | tail -20
```

Expected: Build error — `CombinedPrintQueueSink` not found.

---

## Task 2: Implement CombinedPrintQueueSink

**Files:**
- Create: `backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs`

- [ ] **Step 1: Create the class**

```csharp
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.API.Features.ExpeditionList;

internal sealed class CombinedPrintQueueSink : IPrintQueueSink
{
    private readonly IPrintQueueSink _azureSink;
    private readonly IPrintQueueSink _cupsSink;

    public CombinedPrintQueueSink(
        [FromKeyedServices("azure")] IPrintQueueSink azureSink,
        [FromKeyedServices("cups")] IPrintQueueSink cupsSink)
    {
        _azureSink = azureSink;
        _cupsSink = cupsSink;
    }

    public async Task SendAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
    {
        var paths = filePaths.ToList();
        await _azureSink.SendAsync(paths, cancellationToken);
        await _cupsSink.SendAsync(paths, cancellationToken);
    }
}
```

- [ ] **Step 2: Run the tests — expect all 4 to pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CombinedPrintQueueSinkTests" 2>&1 | tail -20
```

Expected:
```
Passed!  - Failed: 0, Passed: 4, Skipped: 0
```

- [ ] **Step 3: Commit**

```bash
cd backend && git add \
  src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs \
  test/Anela.Heblo.Tests/Features/ExpeditionList/CombinedPrintQueueSinkTests.cs
git commit -m "feat(expedition-list): add CombinedPrintQueueSink (Azure + CUPS)"
```

---

## Task 3: Wire up in Program.cs

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Program.cs` (lines 60–73)

- [ ] **Step 1: Add the `"Combined"` switch case and update the comment**

Find the print sink block (lines 60–73):

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

Replace with:

```csharp
        // Print queue sink — valid values: "FileSystem" (default), "AzureBlob", "Cups", "Combined"
        var printSink = builder.Configuration["ExpeditionList:PrintSink"];
        switch (printSink)
        {
            case "AzureBlob":
                builder.Services.AddAzurePrintQueueSink(builder.Configuration);
                break;
            case "Cups":
                builder.Services.AddCupsAdapter(builder.Configuration);
                break;
            case "Combined":
                // AddAzurePrintQueueSink and AddCupsAdapter each also register a non-keyed
                // IPrintQueueSink as a side effect; those bindings are unused here — the
                // last non-keyed registration (CombinedPrintQueueSink below) wins.
                builder.Services.AddAzurePrintQueueSink(builder.Configuration);
                builder.Services.AddCupsAdapter(builder.Configuration);
                builder.Services.AddKeyedScoped<IPrintQueueSink, AzureBlobPrintQueueSink>("azure");
                builder.Services.AddKeyedScoped<IPrintQueueSink, CupsPrintQueueSink>("cups");
                builder.Services.AddScoped<IPrintQueueSink, CombinedPrintQueueSink>();
                break;
            default: // "FileSystem" or unset
                builder.Services.AddScoped<IPrintQueueSink, FileSystemPrintQueueSink>();
                break;
        }
```

- [ ] **Step 2: Add missing using directives at the top of Program.cs if needed**

The file already imports:
- `using Anela.Heblo.Adapters.Azure;`  ✓ (for `AddAzurePrintQueueSink`)
- `using Anela.Heblo.Adapters.Cups;`  ✓ (for `AddCupsAdapter`)
- `using Anela.Heblo.Application.Features.ExpeditionList.Services;`  ✓ (for `IPrintQueueSink`, `FileSystemPrintQueueSink`)

Add the missing using for the new class and the concrete sink types:

```csharp
using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;
using Anela.Heblo.Adapters.Cups.Features.ExpeditionList;
using Anela.Heblo.API.Features.ExpeditionList;
```

- [ ] **Step 3: Verify the build**

```bash
cd backend && dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj 2>&1 | tail -20
```

Expected:
```
Build succeeded.
  0 Warning(s)
  0 Error(s)
```

- [ ] **Step 4: Run dotnet format to satisfy CI (both API and test projects)**

```bash
cd backend && dotnet format src/Anela.Heblo.API/Anela.Heblo.API.csproj --verify-no-changes 2>&1 | tail -10
dotnet format test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --verify-no-changes 2>&1 | tail -10
```

If either reports changes needed, apply them:

```bash
cd backend && dotnet format src/Anela.Heblo.API/Anela.Heblo.API.csproj
dotnet format test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

- [ ] **Step 5: Run all expedition list tests to ensure nothing is broken**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ExpeditionList" 2>&1 | tail -20
```

Expected:
```
Passed!  - Failed: 0, ...
```

- [ ] **Step 6: Commit**

```bash
cd backend && git add src/Anela.Heblo.API/Program.cs
git commit -m "feat(expedition-list): wire CombinedPrintQueueSink into Program.cs"
```

---

## Done

The `"Combined"` print sink is fully implemented:
- Set `ExpeditionList:PrintSink = "Combined"` in configuration
- Configure both `ExpeditionListBlobStorage` (Azure) and `Cups` sections
- PDFs are uploaded to Azure Blob then sent to CUPS on every job run
