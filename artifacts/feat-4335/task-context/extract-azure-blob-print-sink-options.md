### task: extract-azure-blob-print-sink-options

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintSinkOptions.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs:24-37`
- Test: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/AzureBlobPrintSinkOptionsBindingTests.cs` (new file)

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/AzureBlobPrintSinkOptionsBindingTests.cs`:

```csharp
using Anela.Heblo.Adapters.Azure;
using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

/// <summary>
/// Regression guard for the ExpeditionList/PrintPickingListOptions layering fix (issue #4335):
/// AzureBlobPrintSinkOptions must bind BlobConnectionString/BlobContainerName from the SAME
/// "ExpeditionList" configuration section that PrintPickingListOptions binds from, and
/// AddAzurePrintQueueSinkInfrastructure must construct BlobContainerClient from it.
/// </summary>
public class AzureBlobPrintSinkOptionsBindingTests
{
    // Azurite development storage connection string — never actually connects, just parses.
    private const string DevelopmentBlobConnectionString =
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;";

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExpeditionList:BlobConnectionString"] = DevelopmentBlobConnectionString,
                ["ExpeditionList:BlobContainerName"] = "expedition-lists-test",
            })
            .Build();

    [Fact]
    public void AddAzurePrintQueueSinkInfrastructure_BindsAzureBlobPrintSinkOptions_FromExpeditionListSection()
    {
        // Arrange
        var configuration = BuildConfiguration();
        var services = new ServiceCollection();

        // Act
        services.AddAzurePrintQueueSinkInfrastructure(configuration);
        var provider = services.BuildServiceProvider();

        // Assert — the new options type picks up both keys from the shared "ExpeditionList" section
        var options = provider.GetRequiredService<IOptions<AzureBlobPrintSinkOptions>>().Value;
        Assert.Equal(DevelopmentBlobConnectionString, options.BlobConnectionString);
        Assert.Equal("expedition-lists-test", options.BlobContainerName);
    }

    [Fact]
    public void AddAzurePrintQueueSinkInfrastructure_ConstructsBlobContainerClient_FromAzureBlobPrintSinkOptions()
    {
        // Arrange
        var configuration = BuildConfiguration();
        var services = new ServiceCollection();

        // Act
        services.AddAzurePrintQueueSinkInfrastructure(configuration);
        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<BlobContainerClient>();

        // Assert — connection-string account name and configured container name both flow through
        Assert.Equal("devstoreaccount1", client.AccountName);
        Assert.Equal("expedition-lists-test", client.Name);
    }

    [Fact]
    public void AzureBlobPrintSinkOptions_BlobContainerName_DefaultsToExpeditionLists()
    {
        // Assert — default must match the pre-refactor PrintPickingListOptions.BlobContainerName
        // default exactly, since Development leaves this key unset and relies on the class default.
        Assert.Equal("expedition-lists", new AzureBlobPrintSinkOptions().BlobContainerName);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails to compile**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: FAIL — compilation error `The type or namespace name 'AzureBlobPrintSinkOptions' could not be found (are you missing a using directive or an assembly reference?)`. This is intentional; the next steps create the type.

- [ ] **Step 3: Create `AzureBlobPrintSinkOptions`**

Write `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintSinkOptions.cs`:

```csharp
namespace Anela.Heblo.Adapters.Azure.Features.ExpeditionList;

/// <summary>
/// Configuration for the Azure Blob print-queue sink adapter (<see cref="AzureBlobPrintQueueSink"/>).
/// </summary>
/// <remarks>
/// Bound from the same <c>"ExpeditionList"</c> configuration section that
/// <see cref="Anela.Heblo.Application.Features.ExpeditionList.PrintPickingListOptions"/> binds from
/// (see <c>AzureAdapterModule.AddAzurePrintQueueSinkInfrastructure</c>) — not a separate section.
/// This is a deliberate choice (arch-review for issue #4335, Decision 2): it keeps this adapter's
/// configuration keys and the production Key Vault secret (<c>ExpeditionList--BlobConnectionString</c>)
/// unchanged, while still moving the C# property ownership out of the Application layer.
/// </remarks>
public class AzureBlobPrintSinkOptions
{
    public string BlobConnectionString { get; set; } = string.Empty;
    public string BlobContainerName { get; set; } = "expedition-lists";
}
```

- [ ] **Step 4: Run the test again to confirm the new compile error**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: PASS to compile now (the type exists), but running the tests will FAIL at runtime — `AddAzurePrintQueueSinkInfrastructure` does not yet bind `AzureBlobPrintSinkOptions`, so `GetRequiredService<IOptions<AzureBlobPrintSinkOptions>>()` throws `InvalidOperationException` (no service for type `IOptions<AzureBlobPrintSinkOptions>`), and `GetRequiredService<BlobContainerClient>()` still resolves the OLD factory built from `IOptions<PrintPickingListOptions>`, which is not registered by this test's minimal `ServiceCollection` — that call also throws. Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~AzureBlobPrintSinkOptionsBindingTests"` and confirm the first two facts fail (the third, the default-value test, already passes since it only constructs the POCO directly).

- [ ] **Step 5: Update `AzureAdapterModule.AddAzurePrintQueueSinkInfrastructure`**

In `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs`, replace lines 24-37:

```csharp
    public static IServiceCollection AddAzurePrintQueueSinkInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<PrintPickingListOptions>>().Value;
            return new BlobContainerClient(options.BlobConnectionString, options.BlobContainerName);
        });

        services.AddSingleton<AzureBlobPrintQueueSink>();

        return services;
    }
```

with:

```csharp
    public static IServiceCollection AddAzurePrintQueueSinkInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AzureBlobPrintSinkOptions>(
            configuration.GetSection(PrintPickingListOptions.ConfigurationKey));

        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<AzureBlobPrintSinkOptions>>().Value;
            return new BlobContainerClient(options.BlobConnectionString, options.BlobContainerName);
        });

        services.AddSingleton<AzureBlobPrintQueueSink>();

        return services;
    }
```

`PrintPickingListOptions.ConfigurationKey` is still referenced (it stays on the class after Step 7), and `Anela.Heblo.Adapters.Azure.Features.ExpeditionList` / `Anela.Heblo.Application.Features.ExpeditionList` are already `using`'d at the top of this file (lines 2 and 4) — no new `using` needed.

- [ ] **Step 6: Run the new test file to confirm it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~AzureBlobPrintSinkOptionsBindingTests"`
Expected: PASS — all three facts green.

- [ ] **Step 7: Trim `PrintPickingListOptions`**

Replace the full contents of `backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs` with:

```csharp
namespace Anela.Heblo.Application.Features.ExpeditionList;

/// <summary>
/// Application-layer configuration for expedition-list picking, printing, and order-state
/// transitions. This class intentionally does NOT hold Azure Blob print-sink connection details
/// or print-sink selection — see
/// <see cref="Anela.Heblo.Adapters.Azure.Features.ExpeditionList.AzureBlobPrintSinkOptions"/>
/// (Blob connection/container) and <c>ServiceCollectionExtensions.AddPrintQueueSink</c>, which reads
/// <c>configuration["ExpeditionList:PrintSink"]</c> directly to select the print-sink adapter.
/// Both still read from this same <c>"ExpeditionList"</c> configuration section (see
/// <see cref="ConfigurationKey"/>) — only the C# property ownership is split by layer.
/// </summary>
public class PrintPickingListOptions
{
    public const string ConfigurationKey = "ExpeditionList";

    public string EmailSender { get; set; } = string.Empty;
    public string PrintQueueFolder { get; set; } = string.Empty;
    public List<string> DefaultEmailRecipients { get; set; } = new();
    public int SourceStateId { get; set; } = -2;
    public int FixSourceStateId { get; set; } = 73;
    public int DesiredStateId { get; set; } = 26;
    public string DesiredStateName { get; set; } = "Balí se";
    public int NoteStateId { get; set; } = 35;
    public bool SendToPrinterByDefault { get; set; } = false;
    public bool ChangeOrderStateByDefault { get; set; } = true;
}
```

- [ ] **Step 8: Build the full solution**

Run: `dotnet build`
Expected: PASS, zero errors. This confirms no remaining code anywhere references `PrintPickingListOptions.PrintSink`, `.BlobConnectionString`, or `.BlobContainerName` (arch-review confirmed by repo-wide inspection that `AzureAdapterModule` was the only such consumer, and it was fixed in Step 5).

- [ ] **Step 9: Run the full ExpeditionList and print-sink test suites**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExpeditionList|FullyQualifiedName~PrintQueueSink"`
Expected: PASS for all of: `AzureBlobPrintSinkOptionsBindingTests` (new), `ExpeditionListServicePrintSinkTests`, `ExpeditionListServiceOrderStateTests`, `PrintExpeditionOrderHandlerTests`, `FileSystemPrintQueueSinkTests`, `CombinedPrintQueueSinkRegistrationTests` — none of these were modified by this task, and none construct `PrintPickingListOptions` with the removed properties, so they must all still pass unchanged. `CombinedPrintQueueSinkRegistrationTests` in particular already seeds `ExpeditionList:BlobConnectionString` / `ExpeditionList:BlobContainerName` in its in-memory configuration and exercises the full `AddPrintQueueSink` → `AddAzurePrintQueueSinkInfrastructure` path for both `"AzureBlob"` and `"Combined"` modes — this is the strongest existing regression guard that the refactor is behavior-preserving.

- [ ] **Step 10: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintSinkOptions.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs \
        backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs \
        backend/test/Anela.Heblo.Tests/Features/ExpeditionList/AzureBlobPrintSinkOptionsBindingTests.cs
git commit -m "refactor(expeditionlist): move Azure Blob print-sink config out of PrintPickingListOptions"
```

---

