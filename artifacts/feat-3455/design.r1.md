# Design: Open/Closed DQT Runner Dispatch (RunDqtHandler / GetDqtRunDetailHandler)

## Component Design

### `IDqtJobRunner` (new interface)
- **Location:** `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IDqtJobRunner.cs`
- **Namespace:** `Anela.Heblo.Application.Features.DataQuality.Services` (same folder/namespace as `IDriftDqtComparer`, `IInvoiceDqtJobRunner`, `IDriftDqtJobRunner`; add `using Anela.Heblo.Domain.Features.DataQuality;` for `DqtTestType`).
- **Responsibility:** Uniform, predicate-based dispatch contract shared by every top-level DQT runner, so `RunDqtHandler` can resolve "which runner handles this `DqtTestType`" without a hardcoded binary switch.
- **Contract:**
  ```csharp
  public interface IDqtJobRunner
  {
      bool CanHandle(DqtTestType testType);
      Task RunAsync(Guid runId, CancellationToken ct = default);
  }
  ```
- **Design rule:** `CanHandle` (predicate), not a scalar `TestType` property — chosen because `DriftDqtJobRunner` legitimately handles more than one `DqtTestType` (delegates to its own `IDriftDqtComparer` collection), so a single-value property would either be wrong or duplicate a set the comparer collection already owns. This is a strategy-resolution pattern, not a discriminator pattern.

### `InvoiceDqtJobRunner` (modified)
- **Location:** `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtJobRunner.cs`
- **Change:** Additionally implements `IDqtJobRunner` (retains existing `IInvoiceDqtJobRunner`). No change to existing `RunAsync` logic; its existing signature already satisfies `IDqtJobRunner.RunAsync`.
- **New member:**
  ```csharp
  public bool CanHandle(DqtTestType testType) => testType == DqtTestType.IssuedInvoiceComparison;
  ```

### `DriftDqtJobRunner` (modified)
- **Location:** `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/DriftDqtJobRunner.cs`
- **Change:** Additionally implements `IDqtJobRunner` (retains existing `IDriftDqtJobRunner`). No change to existing `RunAsync`/comparer-resolution logic.
- **New member:**
  ```csharp
  public bool CanHandle(DqtTestType testType) => _comparers.Any(c => c.TestType == testType);
  ```
  Delegates to the already-injected `IEnumerable<IDriftDqtComparer>` — single source of truth for which drift `DqtTestType` values are supported; no separate type list to keep in sync.

### `DataQualityModule` (modified DI registration)
- **Location:** `backend/src/Anela.Heblo.Application/Features/DataQuality/DataQualityModule.cs`
- **Change:** Additive only — existing `IInvoiceDqtJobRunner`/`IDriftDqtJobRunner` registrations are retained unchanged; two new `IDqtJobRunner` registrations are added directly beneath them so both bindings for each class are visually adjacent:
  ```csharp
  services.AddScoped<IInvoiceDqtJobRunner, InvoiceDqtJobRunner>();
  services.AddScoped<IDriftDqtJobRunner, DriftDqtJobRunner>();
  services.AddScoped<IDqtJobRunner, InvoiceDqtJobRunner>();
  services.AddScoped<IDqtJobRunner, DriftDqtJobRunner>();
  ```
- **Contract:** Resolving `IEnumerable<IDqtJobRunner>` from the container yields exactly two instances — one `InvoiceDqtJobRunner`, one `DriftDqtJobRunner`.

### `RunDqtHandler` (modified dispatch)
- **Location:** `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/RunDqt/RunDqtHandler.cs`
- **Responsibility change:** Inside the existing fire-and-forget `Task.Run` (which already creates its own DI scope via `_scopeFactory.CreateScope()`), replace the binary `if (TestType == IssuedInvoiceComparison) {...} else {...}` with predicate-based resolution over all registered `IDqtJobRunner`s:
  ```csharp
  using var scope = _scopeFactory.CreateScope();
  var runner = scope.ServiceProvider
      .GetServices<IDqtJobRunner>()
      .SingleOrDefault(r => r.CanHandle(request.TestType))
      ?? throw new InvalidOperationException($"No IDqtJobRunner registered for {request.TestType}");
  await runner.RunAsync(run.Id);
  ```
- **`SingleOrDefault` (not `FirstOrDefault`):** deliberate — if two registered runners both claim `CanHandle == true` for the same `DqtTestType` (a future misconfiguration), this must surface as a loud `InvalidOperationException`, not a silent, registration-order-dependent pick.
- **No change** to anything before/after this block: the outer synchronous `try/catch`, run creation, and `Success = true` response construction are untouched. As today, an exception thrown here (including the new `InvalidOperationException` for an unmatched `TestType`) is not observed by the HTTP caller and does not persist a `run.Fail(...)` state — this is a pre-existing fire-and-forget characteristic, not something this change alters.

### `GetDqtRunDetailHandler` (modified dispatch)
- **Location:** `backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/GetDqtRunDetail/GetDqtRunDetailHandler.cs`
- **Responsibility change:** Replace the implicit-else result-shaping logic with an explicit three-branch dispatch that fails fast on any `DqtTestType` not in the known set:
  ```csharp
  if (run.TestType == DqtTestType.IssuedInvoiceComparison)
  {
      return new GetDqtRunDetailResponse
      {
          Success = true,
          Run = _mapper.Map<DqtRunDto>(run),
          Results = _mapper.Map<List<InvoiceDqtResultDto>>(run.Results)
      };
  }

  if (run.TestType is DqtTestType.ProductPairing or DqtTestType.StockWriteBackReconciliation)
  {
      var (driftItems, driftTotal) = await _repository.GetDriftResultsAsync(
          run.Id, request.ResultPage, request.ResultPageSize, cancellationToken);

      return new GetDqtRunDetailResponse
      {
          Success = true,
          Run = _mapper.Map<DqtRunDto>(run),
          DriftResults = _mapper.Map<List<DqtDriftResultDto>>(driftItems),
          TotalDriftResults = driftTotal
      };
  }

  throw new NotSupportedException($"No result-shaping logic registered for DqtTestType {run.TestType}");
  ```
- **Explicit list, not metadata-driven:** the drift-type set (`ProductPairing`, `StockWriteBackReconciliation`) is listed directly rather than derived from `IDqtJobRunner`/`IDriftDqtComparer` metadata. There are exactly two result *shapes* in this domain (invoice-shaped vs. drift-shaped), driven by different data sources (`run.Results` navigation vs. `_repository.GetDriftResultsAsync`) — a metadata abstraction to serve two cases is not warranted. Revisit only if a third distinct result shape is introduced.
- **Exception-to-error-code mapping:** the `NotSupportedException` propagates to the handler's existing outer `catch (Exception ex)` block (no new nested try/catch). That block gains one conditional to distinguish this failure mode:
  ```csharp
  catch (Exception ex)
  {
      _logger.LogError(ex, "Error getting DQT run detail for {Id}", request.Id);
      return new GetDqtRunDetailResponse
      {
          Success = false,
          ErrorCode = ex is NotSupportedException ? ErrorCodes.DqtUnsupportedTestType : ErrorCodes.Exception
      };
  }
  ```

### `ErrorCodes` (new entry)
- **Location:** `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`, inside the DataQuality `22XX` block, immediately after `DqtExternalServiceError = 2203`:
  ```csharp
  [HttpStatusCode(HttpStatusCode.InternalServerError)]
  DqtUnsupportedTestType = 2204,
  ```
- **Status code rationale:** `InternalServerError`, not `BadRequest`/`NotFound` — the caller supplied a valid, persisted `DqtRun.Id`; the defect is a server-side gap (a `DqtTestType` enum value with no registered result-shaping logic), mirroring `ConfigurationError`'s "server missing its own wiring" semantics.

### Retained interfaces
`IInvoiceDqtJobRunner` and `IDriftDqtJobRunner` remain unchanged and fully retained (additive-only change). They continue to be consumed by their own implementation classes and by the existing narrow unit tests (`InvoiceDqtJobTests`, `ProductPairingDqtJobTests`, `StockWriteBackDqtJobTests`), which only need `RunAsync` and should not be forced to depend on `CanHandle`.

## Data Schemas

No database schema, migration, or persisted-entity changes. `DqtTestType` enum values are unchanged; this is a pure application-layer dispatch refactor.

### `IDqtJobRunner` contract (new, in-process interface — not a wire format)

| Member | Type | Notes |
|---|---|---|
| `CanHandle(DqtTestType testType)` | `bool` | Predicate; `true` iff this runner instance is responsible for the given test type. |
| `RunAsync(Guid runId, CancellationToken ct = default)` | `Task` | Executes the DQT run identified by `runId`. Signature-compatible with existing `IInvoiceDqtJobRunner.RunAsync` / `IDriftDqtJobRunner.RunAsync` — no behavior change. |

### `RunDqtRequest` / `RunDqtResponse`
Unchanged shape. No new fields.

### `GetDqtRunDetailRequest` / `GetDqtRunDetailResponse`
Unchanged shape (`Run`, `Results`, `DriftResults`, `TotalDriftResults`, inherited `BaseResponse.Success`/`ErrorCode`). Behavioral change only: on an unrecognized `run.TestType`, the response is now `{ Success: false, ErrorCode: ErrorCodes.DqtUnsupportedTestType }` instead of either an unhandled exception or a wrongly-shaped `Success = true` response.

### `ErrorCodes` enum (new member)

| Name | Value | HTTP status | Meaning |
|---|---|---|---|
| `DqtUnsupportedTestType` | `2204` | `500 InternalServerError` | `GetDqtRunDetailHandler` encountered a `DqtRun.TestType` value with no registered result-shaping branch (invoice or known-drift-type). Distinguishes this diagnosable dispatch gap from the generic `ErrorCodes.Exception` (`0099`) catch-all. |

### DI registration surface (container-level "schema")

`IEnumerable<IDqtJobRunner>` resolves to exactly:

| Concrete type | Also registered as |
|---|---|
| `InvoiceDqtJobRunner` | `IInvoiceDqtJobRunner`, `IDqtJobRunner` |
| `DriftDqtJobRunner` | `IDriftDqtJobRunner`, `IDqtJobRunner` |

No event payloads, external API contracts, or frontend-facing shapes are introduced or altered by this feature.
