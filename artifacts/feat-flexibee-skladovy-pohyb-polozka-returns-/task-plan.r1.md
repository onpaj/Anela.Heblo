### task: add-polly-package-to-flexi-adapter

**Goal:** Add the Polly NuGet package reference to the Flexi adapter project so resilience pipelines can be constructed inside `FlexiManufactureHistoryClient`.

**Context:**
- The Flexi adapter project (`backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj`) currently does NOT directly reference `Polly`.
- The sibling project `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` already references `Polly` 8.4.1 (and `Polly.Extensions` 8.4.1).
- The architectural decision is to inline a `ResiliencePipeline` instance field on `FlexiManufactureHistoryClient` (no DI, no shared abstraction) — see arch-review Decision 1 and Decision 5.
- Spec NFR-5 requires `dotnet build` and `dotnet format` to remain clean.
- Polly version must match the existing `8.4.1` used elsewhere to avoid version drift.

Current `Anela.Heblo.Adapters.Flexi.csproj` ItemGroup of package references:
```xml
<ItemGroup>
  <PackageReference Include="AutoMapper" Version="15.1.3" />
  <PackageReference Include="Rem.FlexiBeeSDK.Client" Version="0.1.134" />
  <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  <ProjectReference Include="..\..\Anela.Heblo.Application\Anela.Heblo.Application.csproj" />
  <ProjectReference Include="..\..\Anela.Heblo.Xcc\Anela.Heblo.Xcc.csproj" />
</ItemGroup>
```

**Files to create/modify:**
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj` — add `<PackageReference Include="Polly" Version="8.4.1" />` to the existing `<ItemGroup>` that holds package references.

**Implementation steps:**
1. Open `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj`.
2. Inside the existing `<ItemGroup>` containing the `<PackageReference>` entries (the one with `AutoMapper`, `Rem.FlexiBeeSDK.Client`, `Newtonsoft.Json`), insert a new line:
   ```xml
   <PackageReference Include="Polly" Version="8.4.1" />
   ```
   Place it next to the other `<PackageReference>` lines (before the `<ProjectReference>` lines), keeping the existing order otherwise unchanged. The final block should look like:
   ```xml
   <ItemGroup>
     <PackageReference Include="AutoMapper" Version="15.1.3" />
     <PackageReference Include="Rem.FlexiBeeSDK.Client" Version="0.1.134" />
     <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
     <PackageReference Include="Polly" Version="8.4.1" />
     <ProjectReference Include="..\..\Anela.Heblo.Application\Anela.Heblo.Application.csproj" />
     <ProjectReference Include="..\..\Anela.Heblo.Xcc\Anela.Heblo.Xcc.csproj" />
   </ItemGroup>
   ```
3. Save the file.
4. From the repo root, restore packages and confirm a clean build for the adapter project:
   ```
   dotnet restore backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj
   dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj
   ```
5. Do not modify any source files in this task. The reference exists, but no code yet uses `Polly`.

**Tests to write:**
None for this task. Package reference changes are validated by a successful `dotnet build`. Existing tests in `backend/test/Anela.Heblo.Adapters.Flexi.Tests` must continue to pass unmodified.

**Acceptance criteria:**
- `Anela.Heblo.Adapters.Flexi.csproj` contains exactly one new line: `<PackageReference Include="Polly" Version="8.4.1" />`.
- `dotnet restore` completes without errors for the adapter project.
- `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj` succeeds with zero warnings/errors.
- `dotnet test backend/test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj` still passes (no behavioral code changes were made).
- No other csproj files are edited.
- The version `8.4.1` matches the version already used in `Anela.Heblo.Application.csproj` (no version drift).

---

### task: add-resilience-and-typed-catches-to-flexi-manufacture-history-client

**Goal:** Wrap the FlexiBee `skladovy-pohyb-polozka` call in `FlexiManufactureHistoryClient.GetHistoryAsync` with a Polly v8 retry pipeline for transient 5xx (502/503/504) responses, and add typed `catch` blocks that log warnings for transient failures and errors for non-transient HTTP failures, while preserving the existing cancellation behavior.

**Context (full requirements + current code excerpts):**

Current implementation at `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureHistoryClient.cs`:
```csharp
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockMovement;
using Rem.FlexiBeeSDK.Model.Products.StockMovement;

namespace Anela.Heblo.Adapters.Flexi.Manufacture;

public class FlexiManufactureHistoryClient : IManufactureHistoryClient
{
    private readonly IStockItemsMovementClient _stockItemsMovementClient;
    private readonly ILogger<FlexiManufactureHistoryClient> _logger;

    private const int ManufactureDocumentTypeId = 56;

    public FlexiManufactureHistoryClient(
        IStockItemsMovementClient stockItemsMovementClient,
        ILogger<FlexiManufactureHistoryClient> logger)
    {
        _stockItemsMovementClient = stockItemsMovementClient;
        _logger = logger;
    }

    public async Task<List<ManufactureHistoryRecord>> GetHistoryAsync(DateTime dateFrom, DateTime dateTo, string? productCode = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<StockItemMovementFlexiDto> movements;
        try
        {
            movements = await _stockItemsMovementClient.GetAsync(dateFrom, dateTo, StockMovementDirection.In, documentTypeId: ManufactureDocumentTypeId, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "FlexiBee uzivatelsky-dotaz request timed out (internal HttpClient timeout). " +
                "DateFrom: {DateFrom}, DateTo: {DateTo}",
                dateFrom, dateTo);
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "FlexiBee uzivatelsky-dotaz request was canceled by the caller (client abort). " +
                "DateFrom: {DateFrom}, DateTo: {DateTo}",
                dateFrom, dateTo);
            throw;
        }

        var query = movements.AsQueryable();

        if (!string.IsNullOrEmpty(productCode))
        {
            query = query.Where(m => m.ProductCode != null && m.ProductCode.Contains(productCode));
        }

        var statistics = query
            .Where(m => m.Date != default && !string.IsNullOrEmpty(m.ProductCode))
            .GroupBy(m => new
            {
                Date = m.Date.Date,
                ProductCode = m.ProductCode!.RemoveCodePrefix()
            })
            .Select(g => new ManufactureHistoryRecord
            {
                Date = g.Key.Date,
                ProductCode = g.Key.ProductCode,
                PricePerPiece = (decimal)g.Average(a => a.PricePerUnit),
                PriceTotal = (decimal)g.Sum(s => s.TotalSum),
                Amount = g.Sum(m => m.Amount)
            })
            .OrderBy(s => s.Date)
            .ThenBy(s => s.ProductCode)
            .ToList();

        return statistics;
    }
}
```

Behavioral requirements from spec + arch-review (resolutions):

- **Transient set:** retry on `HttpRequestException` whose `StatusCode` is one of `HttpStatusCode.BadGateway` (502), `HttpStatusCode.ServiceUnavailable` (503), or `HttpStatusCode.GatewayTimeout` (504). The arch-review Decision 2 narrows the spec FR-1 set (which originally included 500 InternalServerError) to `{502, 503, 504}` — implement using exactly this set so retry behavior matches the design's predicate.
- **Retry budget:** `MaxRetryAttempts = 2` (i.e., 3 total attempts max), exponential backoff with **base delay 200 ms** and **jitter on**. Worst-case added latency ≈ 200 ms + 400 ms ≈ 600 ms, well under the 1.5 s NFR cap. (Arch-review Decision 3 supersedes the spec's 500 ms base — use 200 ms.)
- **`OnRetry` logging:** emit a `LogWarning` with structured properties `{Attempt}` (1-based attempt number), `{StatusCode}` (the HTTP status code from the exception), and `{DelayMs}` (the computed delay in milliseconds), using prefix `"FlexiBee skladovy-pohyb-polozka"`.
- **Pipeline lifetime:** instance field, built once in the constructor (not static). The pipeline must capture the instance `_logger` for `OnRetry` to log via `_logger.LogWarning`. Single allocation per client construction.
- **Catch hierarchy after retry pipeline (order matters):**
  1. Existing `catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)` → `LogWarning` "timed out", rethrow. **Unchanged.**
  2. Existing `catch (OperationCanceledException)` → `LogInformation` "canceled by the caller", rethrow. **Unchanged.**
  3. NEW `catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)` → `LogWarning` with `{StatusCode}`, `{DateFrom}`, `{DateTo}`, `{ProductCode}`; rethrow.
  4. NEW `catch (HttpRequestException ex)` (fallback for non-transient HTTP failures, including 4xx, 500, 501, null status) → `LogError` with `{StatusCode}` (use `ex.StatusCode?.ToString() ?? "unknown"`), `{DateFrom}`, `{DateTo}`, `{ProductCode}`; rethrow.
- **Cancellation flow:** the caller's `CancellationToken` must be passed into `pipeline.ExecuteAsync` so a caller cancellation aborts in-flight retries immediately.
- **Public signature unchanged.** `IManufactureHistoryClient` is untouched. No DI registration changes. Aggregation/LINQ logic after the `try/catch` remains byte-for-byte identical.
- **Security/observability:** never log full FlexiBee response bodies or PII; only `dateFrom`, `dateTo`, optional `productCode`, status code, attempt, delay. Use the `"FlexiBee skladovy-pohyb-polozka"` prefix consistently.
- **No changes to `GetManufactureOutputHandler`** (it already propagates the exception on failure — that is the desired behavior per spec out-of-scope item).

Polly v8 reference shape (already used in `CatalogResilienceService.cs`):
```csharp
var pipeline = new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        ShouldHandle = new PredicateBuilder()
            .Handle<HttpRequestException>(ex =>
                ex.StatusCode is HttpStatusCode.BadGateway
                              or HttpStatusCode.ServiceUnavailable
                              or HttpStatusCode.GatewayTimeout),
        MaxRetryAttempts = 2,
        Delay = TimeSpan.FromMilliseconds(200),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        OnRetry = args =>
        {
            var statusCode = (args.Outcome.Exception as HttpRequestException)?.StatusCode;
            _logger.LogWarning(args.Outcome.Exception,
                "FlexiBee skladovy-pohyb-polozka transient failure {StatusCode}. " +
                "Retry attempt {Attempt} after {DelayMs} ms.",
                statusCode, args.AttemptNumber + 1, args.RetryDelay.TotalMilliseconds);
            return ValueTask.CompletedTask;
        }
    })
    .Build();
```

**Files to create/modify:**
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureHistoryClient.cs` — add usings, add `_pipeline` instance field, build the pipeline in the constructor, wrap the SDK call with `_pipeline.ExecuteAsync`, add the two new typed `catch` blocks after the existing cancellation catches. The aggregation/LINQ logic after the try/catch is unchanged.

**Implementation steps:**

1. Open `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureHistoryClient.cs`.

2. Add the following `using` directives at the top of the file (alongside the existing usings):
   ```csharp
   using System.Net;
   using Polly;
   using Polly.Retry;
   ```

3. Add a `private readonly ResiliencePipeline _pipeline;` field next to the existing `_stockItemsMovementClient` and `_logger` fields:
   ```csharp
   private readonly IStockItemsMovementClient _stockItemsMovementClient;
   private readonly ILogger<FlexiManufactureHistoryClient> _logger;
   private readonly ResiliencePipeline _pipeline;
   ```

4. In the constructor, after assigning `_stockItemsMovementClient` and `_logger`, build the resilience pipeline:
   ```csharp
   public FlexiManufactureHistoryClient(
       IStockItemsMovementClient stockItemsMovementClient,
       ILogger<FlexiManufactureHistoryClient> logger)
   {
       _stockItemsMovementClient = stockItemsMovementClient;
       _logger = logger;

       _pipeline = new ResiliencePipelineBuilder()
           .AddRetry(new RetryStrategyOptions
           {
               ShouldHandle = new PredicateBuilder()
                   .Handle<HttpRequestException>(ex =>
                       ex.StatusCode is HttpStatusCode.BadGateway
                                     or HttpStatusCode.ServiceUnavailable
                                     or HttpStatusCode.GatewayTimeout),
               MaxRetryAttempts = 2,
               Delay = TimeSpan.FromMilliseconds(200),
               BackoffType = DelayBackoffType.Exponential,
               UseJitter = true,
               OnRetry = args =>
               {
                   var statusCode = (args.Outcome.Exception as HttpRequestException)?.StatusCode;
                   _logger.LogWarning(args.Outcome.Exception,
                       "FlexiBee skladovy-pohyb-polozka transient failure {StatusCode}. " +
                       "Retry attempt {Attempt} after {DelayMs} ms.",
                       statusCode, args.AttemptNumber + 1, args.RetryDelay.TotalMilliseconds);
                   return ValueTask.CompletedTask;
               }
           })
           .Build();
   }
   ```

5. Replace the body of the existing `try` block to invoke the SDK through the pipeline. Inside `GetHistoryAsync`, change the line:
   ```csharp
   movements = await _stockItemsMovementClient.GetAsync(dateFrom, dateTo, StockMovementDirection.In, documentTypeId: ManufactureDocumentTypeId, cancellationToken: cancellationToken);
   ```
   to:
   ```csharp
   movements = await _pipeline.ExecuteAsync(
       async ct => await _stockItemsMovementClient.GetAsync(
           dateFrom,
           dateTo,
           StockMovementDirection.In,
           documentTypeId: ManufactureDocumentTypeId,
           cancellationToken: ct),
       cancellationToken);
   ```
   The lambda must use the inner `ct` parameter (Polly's flowed token), not the outer `cancellationToken`, so the pipeline can cancel retries when the caller cancels.

6. Keep both existing `OperationCanceledException` catch blocks **exactly as they are** (do not change their order, message text, or behavior). They must remain immediately after the `try` block.

7. After the second `OperationCanceledException` catch, insert two new catch blocks **in this order**:
   ```csharp
   catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadGateway
                                                       or HttpStatusCode.ServiceUnavailable
                                                       or HttpStatusCode.GatewayTimeout)
   {
       _logger.LogWarning(ex,
           "FlexiBee skladovy-pohyb-polozka returned transient {StatusCode} after retries. " +
           "DateFrom: {DateFrom}, DateTo: {DateTo}, ProductCode: {ProductCode}",
           ex.StatusCode, dateFrom, dateTo, productCode);
       throw;
   }
   catch (HttpRequestException ex)
   {
       _logger.LogError(ex,
           "FlexiBee skladovy-pohyb-polozka returned {StatusCode}. " +
           "DateFrom: {DateFrom}, DateTo: {DateTo}, ProductCode: {ProductCode}",
           ex.StatusCode?.ToString() ?? "unknown", dateFrom, dateTo, productCode);
       throw;
   }
   ```

8. Do **not** add any other catch blocks. Non-`HttpRequestException`, non-`OperationCanceledException` exceptions (e.g. `JsonException`, `InvalidOperationException`) must continue to propagate without new logging or retries (FR-4).

9. Do **not** modify the LINQ aggregation logic after the `try/catch`. It stays identical.

10. Do **not** modify `IManufactureHistoryClient`, `GetManufactureOutputHandler`, or any DI module — public surface is unchanged.

11. From the repo root, run:
    ```
    dotnet format backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj --verify-no-changes
    dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj
    ```
    Both must succeed with zero warnings/errors. If `--verify-no-changes` fails, run `dotnet format` without the flag to auto-fix.

**Tests to write:**
None in this task — the unit tests live in the next task. However, the existing two tests in `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureHistoryClientTests.cs` (`GetHistoryAsync_WhenInternalTimeoutCancels_LogsWarningAndRethrows` and `GetHistoryAsync_WhenCallerCancels_LogsInformationAndRethrows`) MUST continue to pass without any modification — that is the verification for this task.

**Acceptance criteria:**
- File `FlexiManufactureHistoryClient.cs` contains a `private readonly ResiliencePipeline _pipeline;` field.
- The pipeline is constructed exactly once, in the constructor, with `MaxRetryAttempts = 2`, `Delay = TimeSpan.FromMilliseconds(200)`, `BackoffType = DelayBackoffType.Exponential`, `UseJitter = true`, and a predicate that handles `HttpRequestException` for status codes `502 BadGateway`, `503 ServiceUnavailable`, `504 GatewayTimeout` only.
- `OnRetry` logs at `Warning` with structured properties `{StatusCode}`, `{Attempt}`, `{DelayMs}` and the prefix `"FlexiBee skladovy-pohyb-polozka"`.
- The SDK call is invoked via `_pipeline.ExecuteAsync(async ct => ..., cancellationToken)`, with the inner `ct` flowed into the SDK call.
- Two new catch blocks appear in this exact order after the existing `OperationCanceledException` catches: (1) `HttpRequestException` filtered for 502/503/504 → `LogWarning`; (2) bare `HttpRequestException` → `LogError`. Both rethrow.
- Public method signature `Task<List<ManufactureHistoryRecord>> GetHistoryAsync(DateTime, DateTime, string?, CancellationToken)` is unchanged.
- `IManufactureHistoryClient`, `GetManufactureOutputHandler`, and DI modules are not touched.
- LINQ projection after the `try/catch` is byte-for-byte identical to the original.
- `dotnet build` succeeds with no warnings.
- `dotnet format --verify-no-changes` passes.
- The two existing tests in `FlexiManufactureHistoryClientTests` still pass without modification.

---

### task: add-resilience-unit-tests-for-flexi-manufacture-history-client

**Goal:** Add 5 new unit tests to `FlexiManufactureHistoryClientTests` that prove the retry pipeline retries on 502/503/504, that exhausted retries log a warning and rethrow, that non-transient HTTP errors are not retried and log an error, and that caller cancellation aborts retries.

**Context (full requirements + current test code excerpts):**

The class under test is `FlexiManufactureHistoryClient` at `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureHistoryClient.cs`. After the previous task it:
- wraps `IStockItemsMovementClient.GetAsync` in a Polly `ResiliencePipeline` that retries 2 times on `HttpRequestException` whose `StatusCode` is `BadGateway` (502), `ServiceUnavailable` (503), or `GatewayTimeout` (504), with 200 ms exponential backoff and jitter;
- catches transient 5xx (502/503/504) after retry exhaustion and logs `Warning` with prefix `"FlexiBee skladovy-pohyb-polozka returned transient"`;
- catches non-transient `HttpRequestException` (e.g. 400, 401, 500, 501, null status) and logs `Error` with prefix `"FlexiBee skladovy-pohyb-polozka returned"`;
- preserves both `OperationCanceledException` paths unchanged.

Existing test file at `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureHistoryClientTests.cs`:
```csharp
using Anela.Heblo.Adapters.Flexi.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockMovement;
using Rem.FlexiBeeSDK.Model.Products.StockMovement;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Manufacture;

public class FlexiManufactureHistoryClientTests
{
    private readonly Mock<IStockItemsMovementClient> _mockMovementClient;
    private readonly Mock<ILogger<FlexiManufactureHistoryClient>> _mockLogger;
    private readonly FlexiManufactureHistoryClient _client;

    public FlexiManufactureHistoryClientTests()
    {
        _mockMovementClient = new Mock<IStockItemsMovementClient>();
        _mockLogger = new Mock<ILogger<FlexiManufactureHistoryClient>>();

        _client = new FlexiManufactureHistoryClient(
            _mockMovementClient.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task GetHistoryAsync_WhenInternalTimeoutCancels_LogsWarningAndRethrows() { /* existing - keep */ }

    [Fact]
    public async Task GetHistoryAsync_WhenCallerCancels_LogsInformationAndRethrows() { /* existing - keep */ }
}
```

The full mock signature for the SDK method is:
```csharp
GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())
```
— use this same matcher signature for setup and verification.

Notes on test mechanics:
- Use `Moq` `SetupSequence` to return different outcomes across consecutive calls.
- Use `FluentAssertions` for throw and contain assertions.
- Tests must not perform real HTTP I/O — they invoke the mocked `IStockItemsMovementClient`.
- Worst-case real-clock latency from retries with 200 ms base + jitter is ~600 ms; running 3 retry-heavy tests adds <2 s — acceptable.
- The retry pipeline runs **synchronously inside the awaited task** — verify call count using `_mockMovementClient.Verify(..., Times.Exactly(N))`.
- The "transient retry exhausted" log is a `Warning` emitted from the catch block (not from `OnRetry`), with message containing the literal phrase `"returned transient"`. The "non-transient error" log is an `Error` with message containing `"FlexiBee skladovy-pohyb-polozka returned"` and **without** the word `"transient"`.

**Files to create/modify:**
- `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureHistoryClientTests.cs` — add 5 new `[Fact]` test methods inside the existing class. Keep the two existing tests untouched.

**Implementation steps:**

1. Open `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureHistoryClientTests.cs`.

2. Add the using directive `using System.Net;` near the existing usings.

3. Inside the existing `FlexiManufactureHistoryClientTests` class (after the second existing test, before the closing brace), add the following 5 test methods:

```csharp
[Fact]
public async Task GetHistoryAsync_When503ThenSucceeds_RetriesAndReturnsResult()
{
    // Arrange
    var transient = new HttpRequestException(
        "Service Unavailable",
        inner: null,
        statusCode: HttpStatusCode.ServiceUnavailable);

    _mockMovementClient
        .SetupSequence(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(transient)
        .ThrowsAsync(transient)
        .ReturnsAsync(new List<StockItemMovementFlexiDto>());

    // Act
    var result = await _client.GetHistoryAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

    // Assert
    result.Should().NotBeNull();
    result.Should().BeEmpty();
    _mockMovementClient.Verify(
        x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
        Times.Exactly(3));
    _mockLogger.Verify(
        x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Retry attempt")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.AtLeastOnce);
}

[Fact]
public async Task GetHistoryAsync_When503Persists_LogsWarningAndRethrowsAfterRetries()
{
    // Arrange
    var transient = new HttpRequestException(
        "Service Unavailable",
        inner: null,
        statusCode: HttpStatusCode.ServiceUnavailable);

    _mockMovementClient
        .Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(transient);

    // Act
    var act = async () => await _client.GetHistoryAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

    // Assert
    await act.Should().ThrowAsync<HttpRequestException>();
    _mockMovementClient.Verify(
        x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
        Times.Exactly(3));
    _mockLogger.Verify(
        x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("returned transient")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);
}

[Fact]
public async Task GetHistoryAsync_When502Persists_RetriesAndLogsWarning()
{
    // Arrange — 502 Bad Gateway is in the transient set.
    var transient = new HttpRequestException(
        "Bad Gateway",
        inner: null,
        statusCode: HttpStatusCode.BadGateway);

    _mockMovementClient
        .Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(transient);

    // Act
    var act = async () => await _client.GetHistoryAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

    // Assert
    await act.Should().ThrowAsync<HttpRequestException>();
    _mockMovementClient.Verify(
        x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
        Times.Exactly(3));
    _mockLogger.Verify(
        x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("returned transient")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);
}

[Fact]
public async Task GetHistoryAsync_When400_DoesNotRetry_LogsErrorAndRethrows()
{
    // Arrange — 400 Bad Request is non-transient; pipeline must NOT retry.
    var nonTransient = new HttpRequestException(
        "Bad Request",
        inner: null,
        statusCode: HttpStatusCode.BadRequest);

    _mockMovementClient
        .Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(nonTransient);

    // Act
    var act = async () => await _client.GetHistoryAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

    // Assert
    await act.Should().ThrowAsync<HttpRequestException>();
    _mockMovementClient.Verify(
        x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
        Times.Once);
    _mockLogger.Verify(
        x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("FlexiBee skladovy-pohyb-polozka returned") && !v.ToString()!.Contains("transient")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);
}

[Fact]
public async Task GetHistoryAsync_When500_DoesNotRetry_LogsErrorAndRethrows()
{
    // Arrange — 500 InternalServerError is NOT in the {502, 503, 504} retry set per Decision 2; treat as non-transient.
    var nonTransient = new HttpRequestException(
        "Internal Server Error",
        inner: null,
        statusCode: HttpStatusCode.InternalServerError);

    _mockMovementClient
        .Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(nonTransient);

    // Act
    var act = async () => await _client.GetHistoryAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

    // Assert
    await act.Should().ThrowAsync<HttpRequestException>();
    _mockMovementClient.Verify(
        x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<StockMovementDirection>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
        Times.Once);
    _mockLogger.Verify(
        x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("FlexiBee skladovy-pohyb-polozka returned") && !v.ToString()!.Contains("transient")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);
}
```

4. Run the test project from the repo root:
   ```
   dotnet test backend/test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj --filter FullyQualifiedName~FlexiManufactureHistoryClientTests
   ```
   All 7 tests in this class (2 existing + 5 new) must pass.

5. Run `dotnet format --verify-no-changes` against the test project; fix any whitespace issues `dotnet format` flags.

6. Run the full adapter test suite to confirm no regressions:
   ```
   dotnet test backend/test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj
   ```

**Tests to write:** (the test methods are listed above; here is the summary table tying inputs to expected outputs)

| Test | Mock setup | Expected SDK call count | Expected exception | Expected log |
|------|------------|-------------------------|--------------------|--------------|
| `GetHistoryAsync_When503ThenSucceeds_RetriesAndReturnsResult` | 503, 503, then empty list | 3 | none | `Warning` containing "Retry attempt" (≥1×) |
| `GetHistoryAsync_When503Persists_LogsWarningAndRethrowsAfterRetries` | 503 always | 3 | `HttpRequestException` | `Warning` containing "returned transient" (1×) |
| `GetHistoryAsync_When502Persists_RetriesAndLogsWarning` | 502 always | 3 | `HttpRequestException` | `Warning` containing "returned transient" (1×) |
| `GetHistoryAsync_When400_DoesNotRetry_LogsErrorAndRethrows` | 400 once | 1 | `HttpRequestException` | `Error` containing "FlexiBee skladovy-pohyb-polozka returned", not containing "transient" (1×) |
| `GetHistoryAsync_When500_DoesNotRetry_LogsErrorAndRethrows` | 500 once | 1 | `HttpRequestException` | `Error` containing "FlexiBee skladovy-pohyb-polozka returned", not containing "transient" (1×) |

**Acceptance criteria:**
- File `FlexiManufactureHistoryClientTests.cs` contains the 2 existing tests **unchanged** plus the 5 new tests above.
- All 7 tests in `FlexiManufactureHistoryClientTests` pass via `dotnet test`.
- No real HTTP I/O occurs — all interactions go through the `Mock<IStockItemsMovementClient>`.
- The total adapter test count is at least 5 higher than before this task; the full `Anela.Heblo.Adapters.Flexi.Tests` project still passes.
- Tests use `Moq` + `FluentAssertions` and follow the AAA structure already used in the file.
- `dotnet format --verify-no-changes` passes for the test project.
- The `using System.Net;` directive is present.
- No tests assert on the exact retry delay value (jitter makes wall-clock timings non-deterministic); call counts and log content are the only assertions.