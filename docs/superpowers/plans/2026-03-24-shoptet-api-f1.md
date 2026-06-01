# ShoptetApi F1 — ShoptetPay Payout Downloads Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Automatically download ShoptetPay payout reports (ABO format) via REST API and feed them into the existing FlexiBee bank import pipeline.

**Architecture:** Add `BankClientProvider` enum + `IBankClientFactory` to support multiple `IBankClient` implementations. Move `AboFile` to Xcc as shared utility. Create new `Anela.Heblo.Adapters.ShoptetApi` class library with `ShoptetPayBankClient`. Wire all into existing `ImportBankStatementHandler` — no business logic change.

**Tech Stack:** .NET 8, xUnit + Moq + FluentAssertions, `System.Net.Http.HttpClient` typed client, `Microsoft.Extensions.Options` with `ValidateOnStart`.

**Spec:** `docs/superpowers/specs/2026-03-24-shoptet-api-adapter-f1-design.md`

---

## File Map

### Created
- `backend/src/Anela.Heblo.Xcc/Abo/AboFile.cs` — moved from Comgate, namespace `Anela.Heblo.Xcc.Abo`
- `backend/src/Anela.Heblo.Domain/Features/Bank/BankClientProvider.cs` — new enum
- `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/IBankClientFactory.cs`
- `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankClientFactory.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetPay/ShoptetPaySettings.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetPay/ShoptetPayBankClient.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetPay/Model/PayoutReportDto.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetPay/Model/PayoutReportListResponse.cs`
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankClientFactoryTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Bank/ShoptetPayBankClientTests.cs`

### Modified
- `backend/src/Adapters/Anela.Heblo.Adapters.Comgate/Anela.Heblo.Adapters.Comgate.csproj` — add direct Xcc ProjectReference
- `backend/src/Adapters/Anela.Heblo.Adapters.Comgate/ComgateBankClient.cs` — remove AboFile classes, add `using`, update interface
- `backend/src/Anela.Heblo.Domain/Features/Bank/IBankClient.cs`
- `backend/src/Anela.Heblo.Domain/Features/Bank/BankAccountConfiguration.cs`
- `backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs`
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — pass `configuration` to `AddBankModule`
- `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankImportRequestDto.cs`
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/ComgateCzkImportJob.cs`
- `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/ComgateEurImportJob.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Comgate/ComgateAdapterServiceCollectionExtensions.cs`
- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs`
- `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
- `backend/src/Anela.Heblo.API/Program.cs`
- `backend/src/Anela.Heblo.API/appsettings.json`
- `backend/test/Anela.Heblo.Tests/Features/Bank/ComgateBankClientTests.cs` — update namespace reference
- `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs` — update for new signatures

---

## Task 1: Move AboFile to Xcc

**Files:**
- Create: `backend/src/Anela.Heblo.Xcc/Abo/AboFile.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Comgate/ComgateBankClient.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Bank/ComgateBankClientTests.cs`

> `AboFile`, `AboHeader`, `AboLine` currently live after line 157 of `ComgateBankClient.cs` with no namespace. The tests reference them directly as `AboFile`, `AboLine`, `AboHeader`. After this task they live in `Anela.Heblo.Xcc.Abo`.

- [ ] **Step 1: Create `AboFile.cs` in Xcc**

```csharp
// backend/src/Anela.Heblo.Xcc/Abo/AboFile.cs
namespace Anela.Heblo.Xcc.Abo;

public class AboFile
{
    public AboHeader Header { get; set; } = new();
    public List<AboLine> Lines { get; set; } = new();

    public static AboFile Parse(string data)
    {
        return new AboFile
        {
            Header = GetHeader(data),
            Lines = GetLines(data)
        };
    }

    private static List<AboLine> GetLines(string data)
    {
        var lines = data.Split(new[] { Environment.NewLine, "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        return lines.Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => new AboLine(line))
            .ToList();
    }

    private static AboHeader GetHeader(string data)
    {
        var lines = data.Split(new[] { Environment.NewLine, "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        var firstLine = lines.FirstOrDefault() ?? string.Empty;
        return new AboHeader(firstLine);
    }
}

public class AboLine
{
    public string Raw { get; }

    public AboLine(string rawLine)
    {
        Raw = rawLine ?? string.Empty;
    }
}

public class AboHeader
{
    public string Raw { get; }

    public AboHeader(string headerLine = "")
    {
        Raw = headerLine;
    }
}
```

- [ ] **Step 2: Add direct Xcc project reference to Comgate adapter**

> **Important:** .NET SDK-style `<ProjectReference>` is NOT transitive for compilation. Even though `Application` references `Xcc`, the `Comgate` adapter cannot use `Anela.Heblo.Xcc.Abo` types without its own direct reference.

In `backend/src/Adapters/Anela.Heblo.Adapters.Comgate/Anela.Heblo.Adapters.Comgate.csproj`, add inside the existing `<ItemGroup>` with project references:

```xml
<ProjectReference Include="..\..\Anela.Heblo.Xcc\Anela.Heblo.Xcc.csproj" />
```

- [ ] **Step 3: Remove AboFile classes from ComgateBankClient.cs**

Delete lines 157–212 (everything after the closing brace of `ComgateBankClient`) from `backend/src/Adapters/Anela.Heblo.Adapters.Comgate/ComgateBankClient.cs`. Add `using Anela.Heblo.Xcc.Abo;` at the top of the file.

- [ ] **Step 4: Update ComgateBankClientTests.cs namespace**

Add `using Anela.Heblo.Xcc.Abo;` at the top of `backend/test/Anela.Heblo.Tests/Features/Bank/ComgateBankClientTests.cs`.

- [ ] **Step 5: Build to verify**

```bash
cd backend && dotnet build
```

Expected: Build succeeds with no errors.

- [ ] **Step 6: Run AboFile tests**

```bash
cd backend && dotnet test --filter "FullyQualifiedName~ComgateBankClientTests" -v normal
```

Expected: All 9 tests pass.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Xcc/Abo/AboFile.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.Comgate/Anela.Heblo.Adapters.Comgate.csproj \
        backend/src/Adapters/Anela.Heblo.Adapters.Comgate/ComgateBankClient.cs \
        backend/test/Anela.Heblo.Tests/Features/Bank/ComgateBankClientTests.cs
git commit -m "refactor: move AboFile to Xcc.Abo shared namespace; add Xcc reference to Comgate adapter"
```

---

## Task 2: Add BankClientProvider Enum and Update Domain Interfaces

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Bank/BankClientProvider.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Bank/IBankClient.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Bank/BankAccountConfiguration.cs`

> `CurrencyCode` already exists in `Anela.Heblo.Domain.Shared`. Check its namespace with: `grep -r "enum CurrencyCode" backend/src/Anela.Heblo.Domain`

- [ ] **Step 1: Create BankClientProvider enum**

```csharp
// backend/src/Anela.Heblo.Domain/Features/Bank/BankClientProvider.cs
namespace Anela.Heblo.Domain.Features.Bank;

public enum BankClientProvider
{
    Comgate,
    ShoptetPay
}
```

- [ ] **Step 2: Update IBankClient**

Replace the full content of `backend/src/Anela.Heblo.Domain/Features/Bank/IBankClient.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Bank;

public interface IBankClient
{
    BankClientProvider Provider { get; }
    Task<BankStatementData> GetStatementAsync(string statementId);
    Task<IList<BankStatementHeader>> GetStatementsAsync(string accountNumber, DateTime dateFrom, DateTime dateTo);
}
```

- [ ] **Step 3: Update BankAccountConfiguration**

Replace the full content of `backend/src/Anela.Heblo.Domain/Features/Bank/BankAccountConfiguration.cs`:

```csharp
using Anela.Heblo.Domain.Shared;

namespace Anela.Heblo.Domain.Features.Bank;

public class BankAccountConfiguration
{
    public string Name { get; set; } = null!;
    public BankClientProvider Provider { get; set; }
    public string AccountNumber { get; set; } = null!;
    public int FlexiBeeId { get; set; }
    public CurrencyCode Currency { get; set; }
}
```

> Verify the `CurrencyCode` namespace by checking where it's imported in the existing handler: `grep -n "CurrencyCode" backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs`

- [ ] **Step 4: Build to see which files break (expected)**

```bash
cd backend && dotnet build 2>&1 | grep -E "error|Error" | head -30
```

Expected: Errors in `ComgateBankClient.cs` (missing Provider, wrong GetStatementsAsync signature) and `ImportBankStatementHandler.cs` (wrong GetStatementsAsync call). These are fixed in subsequent tasks.

- [ ] **Step 5: Commit domain changes**

> **Note:** This commit leaves the codebase in a non-compilable state. `ComgateBankClient` and `ImportBankStatementHandler` still use the old interface and will fail to build until Tasks 3 and 5 respectively. This is intentional — the interface change is committed first, then implementations follow.

```bash
git add backend/src/Anela.Heblo.Domain/Features/Bank/BankClientProvider.cs \
        backend/src/Anela.Heblo.Domain/Features/Bank/IBankClient.cs \
        backend/src/Anela.Heblo.Domain/Features/Bank/BankAccountConfiguration.cs
git commit -m "feat: add BankClientProvider enum, update IBankClient and BankAccountConfiguration (breaks build until Task 3+5)"
```

---

## Task 3: Update ComgateBankClient to New Interface

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Comgate/ComgateBankClient.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Bank/ComgateBankClientTests.cs`

- [ ] **Step 1: Write failing test for date-range iteration**

Add to `ComgateBankClientTests.cs`:

```csharp
// This test documents expected behaviour post-refactor.
// ComgateBankClient.Provider should return Comgate.
[Fact]
public void Provider_ReturnsComgate()
{
    var client = new ComgateBankClient(_httpClient, _optionsMock.Object, _loggerMock.Object);
    Assert.Equal(BankClientProvider.Comgate, client.Provider);
}
```

Add `using Anela.Heblo.Domain.Features.Bank;` to the test file.

- [ ] **Step 2: Run test to confirm it fails**

```bash
cd backend && dotnet test --filter "Provider_ReturnsComgate" -v normal
```

Expected: Compile error — `IBankClient` now requires `Provider` which `ComgateBankClient` does not yet implement.

- [ ] **Step 3: Update ComgateBankClient**

In `backend/src/Adapters/Anela.Heblo.Adapters.Comgate/ComgateBankClient.cs`:

Add at the top of the class body:
```csharp
public BankClientProvider Provider => BankClientProvider.Comgate;
```

Replace the `GetStatementsAsync` signature and body:
```csharp
public async Task<IList<BankStatementHeader>> GetStatementsAsync(string accountNumber, DateTime dateFrom, DateTime dateTo)
{
    var results = new List<BankStatementHeader>();

    for (var date = dateFrom.Date; date <= dateTo.Date; date = date.AddDays(1))
    {
        var url = string.Format(GetStatementsUrlTemplate, _settings.MerchantId, _settings.Secret, date.ToString("yyyy-MM-dd"));
        var anonymizedUrl = AnonymizeUrl(url);

        _logger.LogInformation(
            "Comgate API: Fetching statements list - Account: {AccountNumber}, Date: {StatementDate}, URL: {Url}",
            accountNumber, date.ToString("yyyy-MM-dd"), anonymizedUrl);

        var sw = Stopwatch.StartNew();
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            var response = await _httpClient.SendAsync(request);

            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Comgate API: HTTP request failed - StatusCode: {StatusCode}, Account: {AccountNumber}, Date: {Date}, Duration: {Duration}ms",
                    response.StatusCode, accountNumber, date.ToString("yyyy-MM-dd"), sw.ElapsedMilliseconds);
            }

            response.EnsureSuccessStatusCode();

            var dayResults = await response.Content.ReadAsAsync<List<ComgateStatementHeader>>();

            var filtered = dayResults
                .Where(w => w.AccountCounterParty == accountNumber)
                .Select(s => new BankStatementHeader
                {
                    StatementId = s.TransferId,
                    Date = DateTime.ParseExact(s.TransferDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Account = s.AccountCounterParty
                })
                .ToList();

            _logger.LogInformation(
                "Comgate API: Statements fetched for {Date} - Account: {AccountNumber}, Count: {Count}, Duration: {Duration}ms",
                date.ToString("yyyy-MM-dd"), accountNumber, filtered.Count, sw.ElapsedMilliseconds);

            results.AddRange(filtered);
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Comgate API: HTTP request failed - Account: {AccountNumber}, Date: {Date}, URL: {Url}, Duration: {Duration}ms",
                accountNumber, date.ToString("yyyy-MM-dd"), anonymizedUrl, sw.ElapsedMilliseconds);
            throw;
        }
    }

    return results;
}
```

Add `using Anela.Heblo.Domain.Features.Bank;` at the top of the file.

- [ ] **Step 4: Run tests**

```bash
cd backend && dotnet test --filter "FullyQualifiedName~ComgateBankClientTests" -v normal
```

Expected: `Provider_ReturnsComgate` passes. Other existing tests still pass (they don't call `GetStatementsAsync`).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Comgate/ComgateBankClient.cs \
        backend/test/Anela.Heblo.Tests/Features/Bank/ComgateBankClientTests.cs
git commit -m "feat: update ComgateBankClient — add Provider, iterate date range in GetStatementsAsync"
```

---

## Task 4: Add IBankClientFactory + BankClientFactory, Update BankModule

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/IBankClientFactory.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankClientFactory.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Comgate/ComgateAdapterServiceCollectionExtensions.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Bank/BankClientFactoryTests.cs`

- [ ] **Step 1: Write failing factory tests**

```csharp
// backend/test/Anela.Heblo.Tests/Features/Bank/BankClientFactoryTests.cs
using Anela.Heblo.Application.Features.Bank.Infrastructure;
using Anela.Heblo.Domain.Features.Bank;
using Moq;

namespace Anela.Heblo.Tests.Features.Bank;

public class BankClientFactoryTests
{
    private readonly Mock<IBankClient> _comgateClient;
    private readonly Mock<IBankClient> _shoptetPayClient;

    public BankClientFactoryTests()
    {
        _comgateClient = new Mock<IBankClient>();
        _comgateClient.Setup(x => x.Provider).Returns(BankClientProvider.Comgate);

        _shoptetPayClient = new Mock<IBankClient>();
        _shoptetPayClient.Setup(x => x.Provider).Returns(BankClientProvider.ShoptetPay);
    }

    [Fact]
    public void GetClient_WithComgateProvider_ReturnsComgateClient()
    {
        var factory = new BankClientFactory(new[] { _comgateClient.Object, _shoptetPayClient.Object });
        var config = new BankAccountConfiguration { Provider = BankClientProvider.Comgate };

        var result = factory.GetClient(config);

        Assert.Same(_comgateClient.Object, result);
    }

    [Fact]
    public void GetClient_WithShoptetPayProvider_ReturnsShoptetPayClient()
    {
        var factory = new BankClientFactory(new[] { _comgateClient.Object, _shoptetPayClient.Object });
        var config = new BankAccountConfiguration { Provider = BankClientProvider.ShoptetPay };

        var result = factory.GetClient(config);

        Assert.Same(_shoptetPayClient.Object, result);
    }

    [Fact]
    public void GetClient_WithUnknownProvider_ThrowsInvalidOperationException()
    {
        var factory = new BankClientFactory(new[] { _comgateClient.Object });
        var config = new BankAccountConfiguration { Provider = BankClientProvider.ShoptetPay };

        Assert.Throws<InvalidOperationException>(() => factory.GetClient(config));
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
cd backend && dotnet test --filter "FullyQualifiedName~BankClientFactoryTests" -v normal
```

Expected: Compile error — `IBankClientFactory` and `BankClientFactory` not found.

- [ ] **Step 3: Create IBankClientFactory**

```csharp
// backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/IBankClientFactory.cs
using Anela.Heblo.Domain.Features.Bank;

namespace Anela.Heblo.Application.Features.Bank.Infrastructure;

public interface IBankClientFactory
{
    IBankClient GetClient(BankAccountConfiguration accountSettings);
}
```

- [ ] **Step 4: Create BankClientFactory**

```csharp
// backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankClientFactory.cs
using Anela.Heblo.Domain.Features.Bank;

namespace Anela.Heblo.Application.Features.Bank.Infrastructure;

public class BankClientFactory : IBankClientFactory
{
    private readonly IEnumerable<IBankClient> _clients;

    public BankClientFactory(IEnumerable<IBankClient> clients)
        => _clients = clients;

    public IBankClient GetClient(BankAccountConfiguration accountSettings)
    {
        return _clients.SingleOrDefault(c => c.Provider == accountSettings.Provider)
            ?? throw new InvalidOperationException(
                $"No bank client registered for provider '{accountSettings.Provider}'");
    }
}
```

- [ ] **Step 5: Run factory tests**

```bash
cd backend && dotnet test --filter "FullyQualifiedName~BankClientFactoryTests" -v normal
```

Expected: All 3 tests pass.

- [ ] **Step 6: Update BankModule**

Replace the full content of `backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs`:

```csharp
using Anela.Heblo.Application.Features.Bank.Infrastructure;
using Anela.Heblo.Domain.Features.Bank;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Bank;

public static class BankModule
{
    public static IServiceCollection AddBankModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAutoMapper(typeof(BankMappingProfile));
        services.AddTransient<IBankClientFactory, BankClientFactory>();
        services.Configure<BankAccountSettings>(configuration.GetSection(BankAccountSettings.ConfigurationKey));

        return services;
    }
}
```

- [ ] **Step 7: Update ApplicationModule.cs call site**

Find the line `services.AddBankModule();` in `backend/src/Anela.Heblo.Application/ApplicationModule.cs` and change it to:

```csharp
services.AddBankModule(configuration);
```

> `AddApplicationServices` already receives `IConfiguration configuration` as a parameter — just pass it through.

- [ ] **Step 8: Remove BankAccountSettings registration from ComgateAdapterServiceCollectionExtensions**

In `backend/src/Adapters/Anela.Heblo.Adapters.Comgate/ComgateAdapterServiceCollectionExtensions.cs`, remove these two lines:

```csharp
var bankAccountSection = configuration.GetSection(BankAccountSettings.ConfigurationKey);
services.Configure<BankAccountSettings>(bankAccountSection);
```

- [ ] **Step 9: Build to verify (partial)**

```bash
cd backend && dotnet build 2>&1 | grep -E "error" | head -20
```

Expected: `ImportBankStatementHandler.cs` still produces compile errors (it still injects `IBankClient` directly and calls the old `GetStatementsAsync` signature). This is expected — it is fixed in Task 5. All other files should compile cleanly.

- [ ] **Step 10: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/IBankClientFactory.cs \
        backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankClientFactory.cs \
        backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs \
        backend/src/Anela.Heblo.Application/ApplicationModule.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.Comgate/ComgateAdapterServiceCollectionExtensions.cs \
        backend/test/Anela.Heblo.Tests/Features/Bank/BankClientFactoryTests.cs
git commit -m "feat: add IBankClientFactory, BankClientFactory; move BankAccountSettings ownership to BankModule"
```

---

## Task 5: Update Request/Handler/Controller/Jobs

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankImportRequestDto.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/ComgateCzkImportJob.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/ComgateEurImportJob.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs`

- [ ] **Step 1: Update ImportBankStatementRequest**

Replace the full content of `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementRequest.cs`:

```csharp
using Anela.Heblo.Application.Features.Bank.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;

public class ImportBankStatementRequest : IRequest<ImportBankStatementResponse>
{
    public string AccountName { get; set; } = null!;
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }

    public ImportBankStatementRequest(string accountName, DateTime dateFrom, DateTime dateTo)
    {
        AccountName = accountName;
        DateFrom = dateFrom;
        DateTo = dateTo;
    }
}
```

- [ ] **Step 2: Update BankImportRequestDto**

Replace the full content of `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankImportRequestDto.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Bank.Contracts;

public class BankImportRequestDto
{
    [Required]
    public string AccountName { get; set; } = null!;

    [Required]
    public DateTime DateFrom { get; set; }

    [Required]
    public DateTime DateTo { get; set; }
}
```

- [ ] **Step 3: Update BankStatementsController**

In `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs`, replace line 37:

```csharp
// Before:
var importRequest = new ImportBankStatementRequest(request.AccountName, request.StatementDate);

// After:
var importRequest = new ImportBankStatementRequest(request.AccountName, request.DateFrom, request.DateTo);
```

Also update the log message on line 34 to log `DateFrom`/`DateTo` instead of `StatementDate`.

- [ ] **Step 4: Update ImportBankStatementHandler**

Replace the full content of `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs`:

```csharp
using System.Diagnostics;
using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Application.Features.Bank.Infrastructure;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Domain.Shared;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;

public class ImportBankStatementHandler : IRequestHandler<ImportBankStatementRequest, ImportBankStatementResponse>
{
    private readonly IBankClientFactory _factory;
    private readonly IBankStatementImportService _bankStatementImportService;
    private readonly IBankStatementImportRepository _repository;
    private readonly BankAccountSettings _bankSettings;
    private readonly IMapper _mapper;
    private readonly ILogger<ImportBankStatementHandler> _logger;

    public ImportBankStatementHandler(
        IBankClientFactory factory,
        IBankStatementImportService bankStatementImportService,
        IBankStatementImportRepository repository,
        IOptions<BankAccountSettings> bankSettings,
        IMapper mapper,
        ILogger<ImportBankStatementHandler> logger)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _bankStatementImportService = bankStatementImportService ?? throw new ArgumentNullException(nameof(bankStatementImportService));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _bankSettings = bankSettings.Value ?? throw new ArgumentNullException(nameof(bankSettings));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ImportBankStatementResponse> Handle(ImportBankStatementRequest request, CancellationToken cancellationToken)
    {
        var totalSw = Stopwatch.StartNew();

        _logger.LogInformation(
            "Bank import START - Account: {AccountName}, DateFrom: {DateFrom}, DateTo: {DateTo}",
            request.AccountName, request.DateFrom, request.DateTo);

        var accountSetting = _bankSettings.Accounts?.SingleOrDefault(a => a.Name == request.AccountName);
        if (accountSetting == null)
        {
            var availableAccounts = _bankSettings.Accounts != null
                ? string.Join(", ", _bankSettings.Accounts.Select(a => a.Name))
                : "None";

            _logger.LogError(
                "Bank import FAILED - Account not found: {AccountName}. Available accounts: {AvailableAccounts}",
                request.AccountName, availableAccounts);

            throw new ArgumentException(
                $"Account name {request.AccountName} not found in {BankAccountSettings.ConfigurationKey} configuration. Available accounts: {availableAccounts}");
        }

        var client = _factory.GetClient(accountSetting);

        _logger.LogInformation(
            "Account config resolved - Account: {AccountName}, Provider: {Provider}, FlexiBeeId: {FlexiBeeId}",
            request.AccountName, accountSetting.Provider, accountSetting.FlexiBeeId);

        var statements = await client.GetStatementsAsync(accountSetting.AccountNumber, request.DateFrom, request.DateTo);

        _logger.LogInformation(
            "Bank client returned {StatementCount} statements - Account: {AccountName}",
            statements.Count, request.AccountName);

        var imports = new List<BankStatementImportDto>();

        foreach (var statement in statements)
        {
            try
            {
                _logger.LogInformation("Processing statement {StatementId}", statement.StatementId);

                var aboData = await client.GetStatementAsync(statement.StatementId);
                var import = new BankStatementImport(statement.StatementId, statement.Date);

                var importResult = await _bankStatementImportService.ImportStatementAsync(accountSetting.FlexiBeeId, aboData.Data);

                import.Account = accountSetting.AccountNumber;
                import.Currency = accountSetting.Currency;
                import.ItemCount = aboData.ItemCount;
                import.ImportResult = importResult.IsSuccess ? ImportStatus.Success : importResult.ErrorMessage ?? ImportStatus.UnknownError;

                var savedImport = await _repository.AddAsync(import);
                imports.Add(_mapper.Map<BankStatementImportDto>(savedImport));

                _logger.LogInformation("Successfully processed statement {StatementId} with result: {Result}",
                    statement.StatementId, import.ImportResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing statement {StatementId}", statement.StatementId);

                var failedImport = new BankStatementImport(statement.StatementId, statement.Date);
                failedImport.Account = accountSetting.AccountNumber;
                failedImport.Currency = accountSetting.Currency;
                failedImport.ImportResult = $"{ImportStatus.ProcessingError}: {ex.Message}";

                var savedFailedImport = await _repository.AddAsync(failedImport);
                imports.Add(_mapper.Map<BankStatementImportDto>(savedFailedImport));
            }
        }

        totalSw.Stop();

        var successCount = imports.Count(i => i.ImportResult == ImportStatus.Success);
        var errorCount = imports.Count - successCount;

        _logger.LogInformation(
            "Bank import COMPLETED - Account: {AccountName}, Total: {TotalCount}, Success: {SuccessCount}, Errors: {ErrorCount}, Duration: {Duration}ms",
            request.AccountName, imports.Count, successCount, errorCount, totalSw.ElapsedMilliseconds);

        return new ImportBankStatementResponse { Statements = imports };
    }
}
```

- [ ] **Step 5: Update jobs**

In `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/ComgateCzkImportJob.cs`, replace:
```csharp
var yesterdayDate = DateTime.Today.AddDays(-1);
var request = new ImportBankStatementRequest("ComgateCZK", yesterdayDate);
```
With:
```csharp
var yesterday = DateTime.Today.AddDays(-1);
var request = new ImportBankStatementRequest("ComgateCZK", yesterday, yesterday);
```

Apply the same change in `ComgateEurImportJob.cs` using `"ComgateEUR"`.

- [ ] **Step 6: Update ImportBankStatementHandlerTests**

> **Deliberate rename:** The existing tests use account names `"CZK"` and `"EUR"`. This step renames them to `"ComgateCZK"` and `"ComgateEUR"` to match the job strings and config entries. Any test passing the old names will now correctly throw `ArgumentException` — this is intentional.

Replace the full content of `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Bank.Infrastructure;
using Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Domain.Shared;
using AutoMapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank;

public class ImportBankStatementHandlerTests
{
    private readonly Mock<IBankClientFactory> _mockFactory;
    private readonly Mock<IBankClient> _mockBankClient;
    private readonly Mock<IBankStatementImportService> _mockImportService;
    private readonly Mock<IBankStatementImportRepository> _mockRepository;
    private readonly Mock<IMapper> _mockMapper;
    private readonly Mock<ILogger<ImportBankStatementHandler>> _mockLogger;
    private readonly BankAccountSettings _bankSettings;
    private readonly ImportBankStatementHandler _handler;

    public ImportBankStatementHandlerTests()
    {
        _mockFactory = new Mock<IBankClientFactory>();
        _mockBankClient = new Mock<IBankClient>();
        _mockImportService = new Mock<IBankStatementImportService>();
        _mockRepository = new Mock<IBankStatementImportRepository>();
        _mockMapper = new Mock<IMapper>();
        _mockLogger = new Mock<ILogger<ImportBankStatementHandler>>();

        _bankSettings = new BankAccountSettings
        {
            Accounts = new List<BankAccountConfiguration>
            {
                new BankAccountConfiguration
                {
                    Name = "ComgateCZK",
                    Provider = BankClientProvider.Comgate,
                    AccountNumber = "123456789",
                    FlexiBeeId = 1,
                    Currency = CurrencyCode.CZK
                },
                new BankAccountConfiguration
                {
                    Name = "ComgateEUR",
                    Provider = BankClientProvider.Comgate,
                    AccountNumber = "987654321",
                    FlexiBeeId = 2,
                    Currency = CurrencyCode.EUR
                }
            }
        };

        _mockFactory.Setup(x => x.GetClient(It.IsAny<BankAccountConfiguration>()))
            .Returns(_mockBankClient.Object);

        _handler = new ImportBankStatementHandler(
            _mockFactory.Object,
            _mockImportService.Object,
            _mockRepository.Object,
            Options.Create(_bankSettings),
            _mockMapper.Object,
            _mockLogger.Object);
    }

    [Fact]
    public void Constructor_WithNullFactory_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ImportBankStatementHandler(
            null!,
            _mockImportService.Object,
            _mockRepository.Object,
            Options.Create(_bankSettings),
            _mockMapper.Object,
            _mockLogger.Object));
    }

    [Fact]
    public async Task Handle_WithUnknownAccount_ThrowsArgumentException()
    {
        var request = new ImportBankStatementRequest("UNKNOWN", DateTime.Today, DateTime.Today);

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _handler.Handle(request, CancellationToken.None));

        Assert.Contains("Account name UNKNOWN not found", exception.Message);
    }

    [Fact]
    public async Task Handle_WithValidAccount_ResolvesClientViaFactory()
    {
        var dateFrom = DateTime.Today.AddDays(-1);
        var dateTo = DateTime.Today;
        var request = new ImportBankStatementRequest("ComgateCZK", dateFrom, dateTo);

        _mockBankClient.Setup(x => x.GetStatementsAsync("123456789", dateFrom, dateTo))
            .ReturnsAsync(new List<BankStatementHeader>());

        await _handler.Handle(request, CancellationToken.None);

        _mockFactory.Verify(x => x.GetClient(It.Is<BankAccountConfiguration>(c => c.Name == "ComgateCZK")), Times.Once);
        _mockBankClient.Verify(x => x.GetStatementsAsync("123456789", dateFrom, dateTo), Times.Once);
    }
}
```

- [ ] **Step 7: Build and run all bank tests**

```bash
cd backend && dotnet build && dotnet test --filter "FullyQualifiedName~Features.Bank" -v normal
```

Expected: All tests pass.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementRequest.cs \
        backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankImportRequestDto.cs \
        backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs \
        backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs \
        backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/ComgateCzkImportJob.cs \
        backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/ComgateEurImportJob.cs \
        backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs
git commit -m "feat: update handler and request to use IBankClientFactory with dateFrom/dateTo range"
```

---

## Task 6: Create Anela.Heblo.Adapters.ShoptetApi Project

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetPay/ShoptetPaySettings.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetPay/Model/PayoutReportDto.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetPay/Model/PayoutReportListResponse.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetPay/ShoptetPayBankClient.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Bank/ShoptetPayBankClientTests.cs`

- [ ] **Step 1: Write failing ShoptetPayBankClient tests**

> **HttpClient note:** Tests instantiate `ShoptetPayBankClient` with `new HttpClient()` (no base address). This is sufficient for `Provider_ReturnsShoptetPay`. Tests that call `GetStatementsAsync` or `GetStatementAsync` would need a `MockHttpMessageHandler` or a test server — not covered here as the happy-path behaviour is validated by integration against real API.

```csharp
// backend/test/Anela.Heblo.Tests/Features/Bank/ShoptetPayBankClientTests.cs
using Anela.Heblo.Adapters.ShoptetApi.ShoptetPay;
using Anela.Heblo.Domain.Features.Bank;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.Bank;

public class ShoptetPayBankClientTests
{
    private readonly Mock<ILogger<ShoptetPayBankClient>> _loggerMock;
    private readonly ShoptetPaySettings _settings;

    public ShoptetPayBankClientTests()
    {
        _loggerMock = new Mock<ILogger<ShoptetPayBankClient>>();
        _settings = new ShoptetPaySettings
        {
            ApiToken = "test-token",
            BaseUrl = "https://api.shoptetpay.com"
        };
    }

    [Fact]
    public void Provider_ReturnsShoptetPay()
    {
        var client = new ShoptetPayBankClient(
            new HttpClient(),
            Options.Create(_settings),
            _loggerMock.Object);

        Assert.Equal(BankClientProvider.ShoptetPay, client.Provider);
    }
}
```

- [ ] **Step 2: Confirm test fails (project not yet created)**

```bash
cd backend && dotnet build 2>&1 | grep -E "error" | head -10
```

Expected: Compile error — `Anela.Heblo.Adapters.ShoptetApi` not found.

- [ ] **Step 3: Create the project file**

```xml
<!-- backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Anela.Heblo.Adapters.ShoptetApi</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="8.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Anela.Heblo.Application\Anela.Heblo.Application.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Create ShoptetPaySettings**

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetPay/ShoptetPaySettings.cs
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Adapters.ShoptetApi.ShoptetPay;

public class ShoptetPaySettings
{
    public static string ConfigurationKey => "ShoptetPay";

    [Required]
    public string ApiToken { get; set; } = null!;

    [Required]
    public string BaseUrl { get; set; } = "https://api.shoptetpay.com";
}
```

- [ ] **Step 5: Create PayoutReportDto and PayoutReportListResponse**

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetPay/Model/PayoutReportDto.cs
namespace Anela.Heblo.Adapters.ShoptetApi.ShoptetPay.Model;

public class PayoutReportDto
{
    public string Id { get; set; } = null!;
    public string Currency { get; set; } = null!;
    public string Type { get; set; } = null!;
    public int SerialNumber { get; set; }
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetPay/Model/PayoutReportListResponse.cs
namespace Anela.Heblo.Adapters.ShoptetApi.ShoptetPay.Model;

public class PayoutReportListResponse
{
    public List<PayoutReportDto> Data { get; set; } = new();
    public int Total { get; set; }
}
```

- [ ] **Step 6: Create ShoptetPayBankClient**

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetPay/ShoptetPayBankClient.cs
using System.Net.Http.Json;
using Anela.Heblo.Adapters.ShoptetApi.ShoptetPay.Model;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Xcc.Abo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi.ShoptetPay;

public class ShoptetPayBankClient : IBankClient
{
    private readonly HttpClient _httpClient;
    private readonly ShoptetPaySettings _settings;
    private readonly ILogger<ShoptetPayBankClient> _logger;

    public BankClientProvider Provider => BankClientProvider.ShoptetPay;

    public ShoptetPayBankClient(
        HttpClient httpClient,
        IOptions<ShoptetPaySettings> options,
        ILogger<ShoptetPayBankClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settings = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IList<BankStatementHeader>> GetStatementsAsync(string accountNumber, DateTime dateFrom, DateTime dateTo)
    {
        var url = $"/v1/reports/payout?dateFrom={dateFrom:yyyy-MM-dd}&dateTo={dateTo:yyyy-MM-dd}&types=PAYOUT&limit=1000";

        _logger.LogInformation(
            "ShoptetPay API: Fetching payout reports - DateFrom: {DateFrom}, DateTo: {DateTo}",
            dateFrom.ToString("yyyy-MM-dd"), dateTo.ToString("yyyy-MM-dd"));

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<PayoutReportListResponse>()
            ?? new PayoutReportListResponse();

        _logger.LogInformation(
            "ShoptetPay API: Received {Count} payout reports (total: {Total})",
            result.Data.Count, result.Total);

        return result.Data.Select(r => new BankStatementHeader
        {
            StatementId = r.Id,
            Date = r.DateTo,
            Account = r.Currency
        }).ToList();
    }

    public async Task<BankStatementData> GetStatementAsync(string statementId)
    {
        var url = $"/v1/reports/payout/{statementId}/abo";

        _logger.LogInformation("ShoptetPay API: Downloading ABO report - StatementId: {StatementId}", statementId);

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadAsStringAsync();
        var abo = AboFile.Parse(data);

        _logger.LogInformation(
            "ShoptetPay API: ABO report downloaded - StatementId: {StatementId}, Lines: {LineCount}",
            statementId, abo.Lines.Count);

        return new BankStatementData
        {
            StatementId = statementId,
            Data = data,
            ItemCount = abo.Lines.Count
        };
    }
}
```

- [ ] **Step 7: Create ShoptetApiAdapterServiceCollectionExtensions**

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs
using System.Net.Http.Headers;
using Anela.Heblo.Adapters.ShoptetApi.ShoptetPay;
using Anela.Heblo.Domain.Features.Bank;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi;

public static class ShoptetApiAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddShoptetApiAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ShoptetPaySettings>()
            .Bind(configuration.GetSection(ShoptetPaySettings.ConfigurationKey))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<ShoptetPayBankClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<ShoptetPaySettings>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", settings.ApiToken);
        });

        services.AddTransient<IBankClient>(sp => sp.GetRequiredService<ShoptetPayBankClient>());

        return services;
    }
}
```

- [ ] **Step 8: Add project to solution and test project reference**

```bash
cd backend && dotnet sln add src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
```

Add to `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` inside the existing `<ItemGroup>` with project references:
```xml
<ProjectReference Include="..\..\src\Adapters\Anela.Heblo.Adapters.ShoptetApi\Anela.Heblo.Adapters.ShoptetApi.csproj" />
```

- [ ] **Step 9: Run the ShoptetPay tests**

```bash
cd backend && dotnet test --filter "FullyQualifiedName~ShoptetPayBankClientTests" -v normal
```

Expected: `Provider_ReturnsShoptetPay` passes.

- [ ] **Step 10: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ \
        backend/test/Anela.Heblo.Tests/Features/Bank/ShoptetPayBankClientTests.cs \
        backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
        backend/*.sln
git commit -m "feat: add Anela.Heblo.Adapters.ShoptetApi with ShoptetPayBankClient"
```

---

## Task 7: Wire Up in API Project and Update Configuration

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
- Modify: `backend/src/Anela.Heblo.API/Program.cs`
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`

- [ ] **Step 1: Add project reference to API**

In `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`, add inside an `<ItemGroup>`:

```xml
<ProjectReference Include="..\Adapters\Anela.Heblo.Adapters.ShoptetApi\Anela.Heblo.Adapters.ShoptetApi.csproj" />
```

- [ ] **Step 2: Add using + registration in Program.cs**

Add `using Anela.Heblo.Adapters.ShoptetApi;` at the top of `backend/src/Anela.Heblo.API/Program.cs`.

After the existing `builder.Services.AddComgateAdapter(builder.Configuration);` line (line ~53), add:

```csharp
builder.Services.AddShoptetApiAdapter(builder.Configuration);
```

- [ ] **Step 3: Update appsettings.json**

In `backend/src/Anela.Heblo.API/appsettings.json`, update the `BankAccounts` section to add `Provider` and `Currency` to each existing account, and add the ShoptetPay account and config section.

Find the existing Comgate account entries and add `"Provider": "Comgate"` and `"Currency": "CZK"` / `"Currency": "EUR"` to each.

Add a new account entry:
```json
{
  "Name": "ShoptetPay-CZK",
  "Provider": "ShoptetPay",
  "AccountNumber": "",
  "FlexiBeeId": 0,
  "Currency": "CZK"
}
```

Add top-level `ShoptetPay` section:
```json
"ShoptetPay": {
  "ApiToken": "CONFIGURE_IN_USER_SECRETS",
  "BaseUrl": "https://api.shoptetpay.com"
}
```

> **Security:** `ApiToken` must NOT be a real value in `appsettings.json`. Store the real token in user secrets locally (`dotnet user-secrets set "ShoptetPay:ApiToken" "<token>"`) or Azure Key Vault for production.

- [ ] **Step 4: Build the full solution**

```bash
cd backend && dotnet build
```

Expected: Build succeeds with no errors.

- [ ] **Step 5: Run all bank tests**

```bash
cd backend && dotnet test --filter "FullyQualifiedName~Features.Bank" -v normal
```

Expected: All tests pass.

- [ ] **Step 6: Run all tests**

```bash
cd backend && dotnet test
```

Expected: Full test suite passes.

- [ ] **Step 7: Run dotnet format**

```bash
cd backend && dotnet format
```

Expected: No formatting changes (or apply any that are reported).

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj \
        backend/src/Anela.Heblo.API/Program.cs \
        backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat: wire ShoptetApiAdapter into API project and update configuration"
```

---

## Done

All tasks complete. The `ImportBankStatementHandler` now routes to the correct `IBankClient` implementation via `IBankClientFactory`, and `ShoptetPayBankClient` is available to handle `ShoptetPay` accounts by downloading ABO payout reports from `api.shoptetpay.com`.

**To configure a real ShoptetPay account:**
1. Generate an API key in Shoptet Pay admin → API keys Settings → Generate new key
2. Set the token locally: `dotnet user-secrets set "ShoptetPay:ApiToken" "<token>" --project backend/src/Anela.Heblo.API`
3. Update `appsettings.json` with real `FlexiBeeId` for the ShoptetPay account
4. Trigger via the existing bank import endpoint or add a new recurring job following the pattern of `ComgateCzkImportJob`
