# Architecture Review: Surface Shoptet shipment-validation-failed as an actionable error on Packaging/ScanOrder instead of a generic 503

## Skip Design: false

Set to `false` because FR-3 requires a frontend-visible message-text change on the packing screen (the operator-facing Czech error string shown for a failed scan). It is a one-line addition to an existing message map plus wiring one more field through an existing callback — no new screens, components, or visual layout — but it is still user-facing behavior, so the designer should confirm the message text/placement rather than the architect dictating UI copy unilaterally.

## Architectural Fit Assessment

This fits cleanly into two already-established patterns in this codebase, verified by reading the actual source:

1. **Per-adapter typed exceptions.** Every external-API adapter in this repo that needs to distinguish a specific failure mode from a generic one already does so with a small, adapter-local exception class inheriting `Exception` directly (not a shared base): `PaymentGatewayUnavailableException` (Comgate), `LogetoApiException`, `PlaudAuthExpiredException`, `GraphServiceAuthException`, `FlexiManufactureException`, etc. None of these share a common "ApiException" base — each adapter owns its own type(s). `ShoptetShipmentClient` has no existing exception type of its own; it only throws generic `HttpRequestException`. Adding one typed exception for this one case is squarely idiomatic, not a new pattern.
2. **`ErrorCodes` + `[HttpStatusCode]` + `BaseResponse.Params`.** The 29XX (ShipmentLabels/Packaging) range already has four `UnprocessableEntity`-mapped codes (`ShipmentLabelsNotGenerated`, `ShipmentCarrierNotResolved`, `ShipmentLabelNotReady`, `ShipmentOrderWeightUnavailable`) sitting next to the `ServiceUnavailable`-mapped `ShipmentCreationFailed`. Adding one more `UnprocessableEntity` code in the same range, carrying a message via `Params`, is mechanically identical to what already exists — `BaseApiController.HandleResponse` and `ScanPackingOrderHandler` need zero changes.

The main integration point is `ShoptetShipmentClient.CreateShipmentAsync`'s non-2xx branch (`ShoptetShipmentClient.cs:174-179`), which today is the *only* place in this file that discards the structured error body — every other method in the same class (`FetchShipmentsAsync`, `GetShippingOptionsAsync`, and `CreateShipmentAsync`'s own success path at line 183) already deserializes `errors[].errorCode`/`.message` via `ShoptetErrorDto`, just only when the HTTP status is 2xx. The fix is to apply that same deserialization to the non-2xx body too, scoped to detecting one specific `errorCode`.

## Proposed Architecture

### Component Overview

```
PackagingController.ScanOrder
        │  (unchanged)
        ▼
ScanPackingOrderHandler.Handle
        │  (unchanged)
        ▼
ShipmentCreationService.CreateAndPersistAsync
        │  try { await _shipmentClient.CreateShipmentAsync(...) }
        │  catch (ShoptetShipmentValidationException vex)      ◄── NEW catch, more specific,
        │      → ErrorCodes.ShipmentValidationFailed (422)         must be ordered BEFORE the
        │        Params["ShoptetMessage"] = vex.Message             existing catch (Exception)
        │  catch (Exception ex)                                     since it's still an Exception
        │      → ErrorCodes.ShipmentCreationFailed (503)        ◄── UNCHANGED, existing fallback
        ▼
ShoptetShipmentClient.CreateShipmentAsync
        │  response = POST /api/shipments
        │  if (!response.IsSuccessStatusCode)
        │      body = await response.Content.ReadAsStringAsync()
        │      try to parse body as ShoptetCreateShipmentResponse  ◄── NEW: reuse existing DTO
        │      if parsed && errors contains errorCode ==
        │           "shipment-validation-failed"
        │          throw ShoptetShipmentValidationException(...)   ◄── NEW typed exception
        │      else
        │          throw HttpRequestException(...)                 ◄── UNCHANGED fallback (parse
        │                                                                failure, other errorCode,
        │                                                                no errors array, etc.)
```

### Key Design Decisions

#### Decision 1: New adapter-local exception type vs. reusing `HttpRequestException` with a status-code check

**Options considered:**
- (a) Keep throwing `HttpRequestException` for everything, and have `ShipmentCreationService` inspect `ex is HttpRequestException { StatusCode: HttpStatusCode.UnprocessableEntity }` to decide it's a validation failure.
- (b) Introduce a dedicated `ShoptetShipmentValidationException : Exception` thrown only for the confirmed `shipment-validation-failed` case, alongside the existing generic `HttpRequestException` fallback for everything else.

**Chosen approach:** (b).

**Rationale:** Option (a) is fragile and wrong here: `HttpRequestException.StatusCode` is only populated by `HttpClient` for *connection-level* failures raised by the client itself, not by the status-code check this code performs manually (this code builds `HttpRequestException` itself via `new HttpRequestException($"... returned {(int)response.StatusCode}: {body}")`, a string-only constructor — `StatusCode` on that instance is `null`). Branching on the 422 status code alone would also be wrong on its own terms: Shoptet uses 422 for `shipment-validation-failed` but the spec explicitly requires only *that* `errorCode` to take the non-retryable path (a 422 with a different `errorCode`, or one whose body fails to parse, must still fall back to the generic/transient path per FR-1). A typed exception carrying the parsed `errorCode`, `message`, and `instance` is the only option that lets `ShipmentCreationService` make that exact decision without re-parsing anything, and matches the one-exception-type-per-distinguishable-failure-mode convention already used throughout the codebase (see Architectural Fit Assessment) — its exact project location is constrained by dependency direction, not by the pattern itself (see Implementation Guidance).

#### Decision 2: Where to parse the non-2xx body — in `CreateShipmentAsync` itself vs. a shared helper

**Options considered:**
- (a) Inline the `try`-parse-and-check logic directly in `CreateShipmentAsync`'s existing `if (!response.IsSuccessStatusCode)` block.
- (b) Extract a small private helper (e.g. `TryParseValidationError(string body, out ...)`) reusable by other `ShoptetShipmentClient` methods later.

**Chosen approach:** (a) for this change; leave extraction for a future PR if/when a second method needs the same detection.

**Rationale:** Per the spec's Out of Scope, only `CreateShipmentAsync`'s non-2xx handling is in scope — the other three methods (`FetchShipmentsAsync`, `GetShippingOptionsAsync`, `CancelShipmentAsync`) are not implicated by this incident and don't currently have a documented case where they need to distinguish `shipment-validation-failed` from other failures. Extracting a shared helper now for a single caller is premature generalization (YAGNI) and would touch more surface than the fix requires — CLAUDE.md's "surgical changes" guidance applies directly. If a second call site needs the same logic later, it can be extracted then.

#### Decision 3: New `ErrorCodes` member number and range

**Chosen approach:** Add `ShipmentValidationFailed = 2910` (next free value; `2909` = `ShipmentOrderWeightUnavailable` is the current highest in the `29XX` ShipmentLabels/Shipment range in `ErrorCodes.cs`) with `[HttpStatusCode(HttpStatusCode.UnprocessableEntity)]`, placed immediately after `ShipmentOrderWeightUnavailable = 2909` in the enum, matching the file's existing "append at the end of the range" convention. Do **not** reuse or renumber `3001`+ (Packaging module range) — this error originates in shipment *creation*, same conceptual bucket as `ShipmentCarrierNotResolved`/`ShipmentCreationFailed`/`ShipmentOrderWeightUnavailable`, which all live in `29XX`.

**Rationale:** Keeps semantically related shipment-creation failure codes contiguous, consistent with how the existing four `29XX` UnprocessableEntity codes are grouped; avoids a gap or an out-of-order insertion that would make the enum harder to scan.

## Implementation Guidance

### Directory / Module Structure

No new files needed for the DTO layer — reuse `ShoptetCreateShipmentResponse` / `ShoptetErrorDto` (`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/Dto/ShoptetCreateShipmentResponse.cs`, `.../ShoptetErrorDto.cs`), which already model exactly this response shape and are currently only exercised on the success path.

One new file:
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShoptetShipmentValidationException.cs` — the new typed exception. **Verified** (see Prerequisites — this is now resolved, not open): `Anela.Heblo.Adapters.ShoptetApi.csproj` has a `ProjectReference` to `Anela.Heblo.Application.csproj` (one-directional, standard Clean Architecture: adapter → application). `Anela.Heblo.Application` cannot reference the adapter project back, so the exception type cannot live next to `ShoptetShipmentClient.cs` the way `PaymentGatewayUnavailableException`/`LogetoApiException` sit next to their clients (those adapters don't have this constraint the same way, or their exception is only consumed within the same adapter). It must live in `Anela.Heblo.Application.Features.ShipmentLabels`, next to `IShipmentClient` — the interface `ShoptetShipmentClient` already implements and that `ShipmentCreationService` already depends on — so both the adapter (which throws it) and the application layer (which catches it) can reference it.

Modified files:
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ShoptetShipmentClient.cs` — `CreateShipmentAsync`'s non-2xx branch (lines 174-179).
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — new `ShipmentValidationFailed = 2910` entry (after line 388/`ShipmentOrderWeightUnavailable`).
- `backend/src/Anela.Heblo.Application/Features/Packaging/Services/ShipmentCreationService.cs` — add a `catch (ShoptetShipmentValidationException vex)` block before the existing `catch (Exception ex)` in `CreateAndPersistAsync` (lines 80-89).
- `frontend/src/api/hooks/useScanPackingOrder.ts` — add an entry to `SCAN_ERROR_MESSAGES` (or extend the `toMessage` callback at line 130) for the new error code, using `params` for the specific message.

No new project references or DI registrations are needed beyond placing the new exception type correctly (see above) — `ShoptetShipmentClient` already has a `using Anela.Heblo.Application.Features.ShipmentLabels;` (for `IShipmentClient`, `ShipmentLabel`, etc. — `ShoptetShipmentClient.cs:4`), so throwing a type from that same namespace requires no new `using` beyond what's already there.

### Interfaces and Contracts

```csharp
// New: ShoptetShipmentValidationException.cs
public class ShoptetShipmentValidationException : Exception
{
    public string OrderCode { get; }
    public string ShoptetErrorCode { get; }   // e.g. "shipment-validation-failed"
    public string? Instance { get; }           // e.g. "data.orderCode", as sent by Shoptet

    public ShoptetShipmentValidationException(string orderCode, string shoptetErrorCode, string message, string? instance)
        : base(message)
    {
        OrderCode = orderCode;
        ShoptetErrorCode = shoptetErrorCode;
        Instance = instance;
    }
}
```

`CreateShipmentAsync`'s non-2xx branch becomes (illustrative, not final code — developer fills in exact parsing per the planner's task breakdown):

```csharp
if (!response.IsSuccessStatusCode)
{
    var body = await response.Content.ReadAsStringAsync(ct);

    var parsed = TryDeserialize<ShoptetCreateShipmentResponse>(body); // swallow JSON errors, return null
    var validationError = parsed?.Errors?
        .FirstOrDefault(e => e.ErrorCode == "shipment-validation-failed");

    if (validationError is not null)
    {
        throw new ShoptetShipmentValidationException(
            command.OrderCode, validationError.ErrorCode!, validationError.Message ?? body, validationError.Instance);
    }

    throw new HttpRequestException(
        $"POST /api/shipments for order {command.OrderCode} returned {(int)response.StatusCode}: {body}");
}
```

`ShipmentCreationService.CreateAndPersistAsync`'s catch block:

```csharp
catch (ShoptetShipmentValidationException vex)
{
    _logger.LogWarning(vex,
        "Shoptet rejected shipment for order {OrderCode}: {ShoptetMessage}", order.Code, vex.Message);
    return new ShipmentCreationResult
    {
        IsSuccess = false,
        ErrorCode = ErrorCodes.ShipmentValidationFailed,
        // Exact Params shape (key names) is the planner/developer's call — reuse the
        // BaseResponse(Exception) convention ("ErrorMessage") for consistency, or a
        // more specific key; either satisfies FR-2/FR-3 as long as the frontend key matches.
    };
}
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to create shipment for order {OrderCode}", order.Code);
    return new ShipmentCreationResult { IsSuccess = false, ErrorCode = ErrorCodes.ShipmentCreationFailed };
}
```

Note `ShipmentCreationResult` currently has no `Params`/message field of its own (only `ErrorCode`) — check its definition; if it doesn't carry a message today, it needs one added (a `Dictionary<string, string>? Params` or a simple `string? ErrorMessage`) so `ScanPackingOrderHandler`/`ScanPackingOrderResponse` can plumb it into `BaseResponse.Params`. This is a small, additive change to `ShipmentCreationResult`, not a breaking one (existing callers that only read `IsSuccess`/`ErrorCode`/`ShipmentGuid`/`Labels` are unaffected).

### Data Flow

1. Operator scans order → `PackagingController.ScanOrder` → `ScanPackingOrderHandler.Handle` → `ShipmentCreationService.CreateAndPersistAsync` → `ShoptetShipmentClient.CreateShipmentAsync` → Shoptet `POST /api/shipments` → `422 shipment-validation-failed`.
2. `ShoptetShipmentClient` parses the body, matches `errorCode`, throws `ShoptetShipmentValidationException` carrying the Shoptet message.
3. `ShipmentCreationService` catches it specifically, logs at Warning (expected/actionable, not an anomaly once this ships — satisfies NFR-2), returns `ShipmentCreationResult { IsSuccess = false, ErrorCode = ShipmentValidationFailed, <message> }`.
4. `ScanPackingOrderHandler.Handle` (line 117-118, unchanged) sees `!result.IsSuccess` and returns `new ScanPackingOrderResponse(result.ErrorCode!.Value)` — **must** be extended (or `ScanPackingOrderResponse`'s constructor must be) to also carry the message through as `Params`, mirroring how `BaseResponse(ErrorCodes, Dictionary<string,string>?)` already supports this.
5. `BaseApiController.HandleResponse` reads `ErrorCodes.ShipmentValidationFailed`'s `[HttpStatusCode(UnprocessableEntity)]` → returns HTTP 422 with the `BaseResponse` body (`errorCode`, `params`).
6. `useScanPackingOrder.ts`'s `callApi` throws `Error(toMessage({errorCode, params}))`; the hook's message map/callback resolves the new error code (using `params` for specifics) into an actionable Czech message, which the existing scan-error UI surface displays unchanged.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| A developer instinctively colocates the new exception next to `ShoptetShipmentClient.cs` (matching the `PaymentGatewayUnavailableException`/`LogetoApiException` precedent) and it fails to compile once `ShipmentCreationService` tries to catch it | Medium — caught immediately at build time, but wastes a cycle | Already resolved by this review (see Implementation Guidance / Directory Structure): place `ShoptetShipmentValidationException` in `Anela.Heblo.Application.Features.ShipmentLabels`, not in the adapter project. Confirmed via `Anela.Heblo.Adapters.ShoptetApi.csproj`'s `ProjectReference` to `Anela.Heblo.Application.csproj` (one-directional). |
| Shoptet changes the exact `errorCode` string or wraps `shipment-validation-failed` under a different HTTP status in the future | Low | Matching is done on the parsed `errorCode` string only (not the status code), so it's resilient to Shoptet using a different status for the same code; if Shoptet renames the code entirely, this degrades gracefully to the existing generic 503 fallback (FR-1's explicit "unrecognized errorCode falls back" requirement), not a crash. |
| A malformed/non-JSON 422 body (e.g. an HTML error page from an upstream proxy) breaks the new parsing path | Low | FR-1 requires an unparseable body to fall back to the existing generic `HttpRequestException` — implementation must wrap the `JsonSerializer.Deserialize` call in try/catch (or use `JsonSerializer.TryDeserialize`-equivalent pattern) exactly as `FetchShipmentsAsync`/`GetShippingOptionsAsync` already rely on `ReadFromJsonAsync` succeeding only for well-formed bodies. |
| Widening `ShipmentCreationResult` with a new message field changes a shared contract | Low | Purely additive (nullable/optional field); existing call sites that don't read it are unaffected. Covered by existing unit tests for `ShipmentCreationService` plus new ones added by this change. |
| New 422 status surprises an existing frontend/monitoring consumer that treats "non-503" as success-ish or doesn't expect 422 from this endpoint | Low | `ScanOrder` already returns 422-mapped codes today for other cases? — check: currently no 29XX `UnprocessableEntity` code is reachable from `ScanPackingOrderHandler`'s direct returns (`InvalidPackageCount`, `ShoptetOrderNotFound`, plus whatever `CreateAndPersistAsync` returns). Confirm in the planner/dev stage whether the frontend's generic HTTP-error handling already tolerates arbitrary 4xx (it does, per `callApi`/`readApiErrorEnvelope`'s design — it does not hardcode 503) before assuming zero risk. |

## Specification Amendments

- **FR-2 clarification:** The spec says "the new error code carrying the Shoptet message as a response `Param`" — architecture confirms `ShipmentCreationResult` (the internal service-layer result type) currently has no field to carry that message and must be extended with one (e.g. `Params`/`ErrorMessage`) as part of this work; this is an implementation detail of FR-2, not a new requirement, but the planner should include it as an explicit task since it's easy to miss (the message would otherwise silently get lost between `ShipmentCreationService` and `ScanPackingOrderResponse`).
- **FR-1 addendum:** Confirmed the parsing must be defensive (try/catch around JSON deserialization of the error body) since a 422 in the wild is not guaranteed to be valid JSON matching `ShoptetCreateShipmentResponse` — added explicitly to the Risks table so the planner writes a test for a malformed-body case, not just the happy path shown in the incident's telemetry.

## Prerequisites

None. The one open question this review would otherwise have flagged — project reference direction between `Anela.Heblo.Application` and `Anela.Heblo.Adapters.ShoptetApi` — has already been verified during this review: `Anela.Heblo.Adapters.ShoptetApi.csproj` has a one-directional `ProjectReference` to `Anela.Heblo.Application.csproj`. The new exception type's location (`Anela.Heblo.Application.Features.ShipmentLabels`) is fixed by this and stated in Implementation Guidance above. No migrations, config, or infrastructure changes are needed before implementation can start.
