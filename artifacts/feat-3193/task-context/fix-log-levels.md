### task: fix-log-levels

Demote the duplicate `LogError` in `CatalogResilienceService` to `LogDebug` (the caller's `LogWarning` in `ProductPairingDqtComparer` will be the canonical signal), and add that caller-side `LogWarning`.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogResilienceService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs`

#### Part A — CatalogResilienceService.cs

- [ ] **Step 1:** Find the generic catch block in `ExecuteWithResilienceAsync` (around line 47 of the file):

  ```csharp
  catch (Exception ex)
  {
      _logger.LogError(ex, "Failed to execute {OperationName} after all retry attempts", operationName);
      throw;
  }
  ```

  Change `LogError` to `LogDebug`:

  ```csharp
  catch (Exception ex)
  {
      _logger.LogDebug(ex, "Failed to execute {OperationName} after all retry attempts", operationName);
      throw;
  }
  ```

  Leave the `BrokenCircuitException` catch block with `LogWarning` unchanged.

#### Part B — ProductPairingDqtComparer.cs

- [ ] **Step 2:** `CompareAsync` currently calls `_resilienceService.ExecuteWithResilienceAsync` for both eshop and ERP fetches without any catch block. If resilience exhausts retries, the exception propagates silently from the comparer's perspective. Add a `try/catch` around the eshop call to log a structured `LogWarning` with context before rethrowing. Do the same for the ERP call.

  The current eshop call (lines 25–28 of the file):
  ```csharp
  var eshopProducts = await _resilienceService.ExecuteWithResilienceAsync(
      async cancellationToken => await _eshopStockClient.ListAsync(cancellationToken),
      "ProductPairingDqtComparer.EshopList",
      ct);
  ```

  The current ERP call (lines 30–33):
  ```csharp
  var erpProducts = await _resilienceService.ExecuteWithResilienceAsync(
      async cancellationToken => await _erpStockClient.ListAsync(cancellationToken),
      "ProductPairingDqtComparer.ErpList",
      ct);
  ```

  The class has no `ILogger` injected. Add it to the constructor. The updated constructor and field declarations:

  ```csharp
  using Anela.Heblo.Application.Features.Catalog.Infrastructure;
  using Anela.Heblo.Domain.Features.Catalog;
  using Anela.Heblo.Domain.Features.Catalog.Stock;
  using Anela.Heblo.Domain.Features.DataQuality;
  using Microsoft.Extensions.Logging;

  namespace Anela.Heblo.Application.Features.DataQuality.Services;

  public class ProductPairingDqtComparer : IDriftDqtComparer
  {
      private readonly IEshopStockClient _eshopStockClient;
      private readonly IErpStockClient _erpStockClient;
      private readonly ICatalogResilienceService _resilienceService;
      private readonly ILogger<ProductPairingDqtComparer> _logger;

      public DqtTestType TestType => DqtTestType.ProductPairing;

      public ProductPairingDqtComparer(
          IEshopStockClient eshopStockClient,
          IErpStockClient erpStockClient,
          ICatalogResilienceService resilienceService,
          ILogger<ProductPairingDqtComparer> logger)
      {
          _eshopStockClient = eshopStockClient;
          _erpStockClient = erpStockClient;
          _resilienceService = resilienceService;
          _logger = logger;
      }
  ```

  Update the two resilience calls in `CompareAsync` to catch and re-log:

  ```csharp
  List<EshopStock> eshopProducts;
  try
  {
      eshopProducts = await _resilienceService.ExecuteWithResilienceAsync(
          async cancellationToken => await _eshopStockClient.ListAsync(cancellationToken),
          "ProductPairingDqtComparer.EshopList",
          ct);
  }
  catch (Exception ex)
  {
      _logger.LogWarning(ex,
          "ProductPairingDqtComparer failed to fetch eshop products after resilience exhaustion. Operation={Operation} ExceptionType={ExceptionType}",
          "ProductPairingDqtComparer.EshopList",
          ex.GetType().Name);
      throw;
  }

  List<ErpStock> erpProducts;
  try
  {
      erpProducts = await _resilienceService.ExecuteWithResilienceAsync(
          async cancellationToken => await _erpStockClient.ListAsync(cancellationToken),
          "ProductPairingDqtComparer.ErpList",
          ct);
  }
  catch (Exception ex)
  {
      _logger.LogWarning(ex,
          "ProductPairingDqtComparer failed to fetch ERP products after resilience exhaustion. Operation={Operation} ExceptionType={ExceptionType}",
          "ProductPairingDqtComparer.ErpList",
          ex.GetType().Name);
      throw;
  }
  ```

  > **Note on `List<ErpStock>` type:** In the original file, `erpProducts` is `var erpProducts = await ...`. After wrapping in a try/catch, you must declare the variable before the try block. The return type of `_erpStockClient.ListAsync` is `List<ErpStock>` — verify by checking `IErpStockClient`. If it differs, use the correct type.

- [ ] **Step 3:** Build to confirm compilation.

  ```bash
  cd /home/user/worktrees/feature-3193-socket-exception-polly/backend
  dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-restore -v q
  ```

  Expected: `Build succeeded.` 0 errors.

- [ ] **Step 4:** Commit.

  ```bash
  git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogResilienceService.cs \
          backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs
  git commit -m "fix: demote CatalogResilienceService generic catch to LogDebug; add LogWarning in ProductPairingDqtComparer"
  ```

---