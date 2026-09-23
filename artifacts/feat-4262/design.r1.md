# Design: Surface Shoptet shipment-validation-failed as an actionable error on Packaging/ScanOrder instead of a generic 503

## UX/UI Design

The packing screen's existing scan-error surface (the same inline/toast error path already used for `ShoptetOrderNotFound`, `ShipmentCarrierNotResolved`, `ShipmentCreationFailed`, etc., rendered from `useScanPackingOrder`'s thrown `Error(message)`) gains exactly one new message variant. No new component, screen, modal, or layout — this is a copy + data-plumbing change only.

**Message copy (Czech, matching the existing tone/length of sibling messages in `SCAN_ERROR_MESSAGES`):**

- Base message (used when the response carries no further detail):
  `"Adresu příjemce nelze použít pro vytvoření zásilky (chybí povinné údaje) — opravte ji v Shoptetu."`
- Detailed message (used when `params` carries the Shoptet-reported detail — see Data Schemas below for the exact key), interpolating the missing-fields text Shoptet returned:
  `"Adresu příjemce nelze použít pro vytvoření zásilky — opravte v Shoptetu: {ShoptetDetail}."`
  where `{ShoptetDetail}` is the raw string forwarded from Shoptet's `message` field, e.g. `"Invalid recipient of order, missing fields: city, zip."` (Shoptet's messages are already in English; this spec does not translate Shoptet's own text — only the surrounding sentence is Czech, consistent with how other backend exception text is not translated elsewhere in this error map).

**Placement and interaction:** identical to every other scan-error case already handled by this hook — no new interaction pattern. The operator sees the message where they already see "Objednávka nebyla nalezena." etc. today. Critically, the wording explicitly says "opravte" (fix) rather than "zkuste znovu" (try again), which is the entire point of this fix — telling the operator that rescanning will not help.

**Explicitly not in scope for this design:** a deep link to the Shoptet order-edit page, a dedicated "address invalid" banner/icon distinct from the existing error surface, or disabling the scan button. All out of scope per the spec.

## Component Design

No new components. Three existing units change behavior, each with a single, narrow responsibility boundary:

### `ShoptetShipmentClient.CreateShipmentAsync` (adapter)
**Responsibility (extended):** in addition to its existing job of POSTing to Shoptet and throwing on failure, it now also classifies a non-2xx response by attempting to parse it as a structured Shoptet error envelope and checking for the specific `shipment-validation-failed` code. This is the *only* place that knows how to recognize this Shoptet error shape — no downstream code parses response bodies.
**Contract:** throws `ShoptetShipmentValidationException` (new, in `Anela.Heblo.Application.Features.ShipmentLabels` per the architecture review) when it can positively identify the validation failure; throws the existing generic `HttpRequestException` for every other non-2xx case (network failure, unparseable body, different `errorCode`, no `errors` array). Callers must not need to inspect the raw exception text to distinguish the two cases — the type itself is the signal.

### `ShipmentCreationService.CreateAndPersistAsync` (application service)
**Responsibility (extended):** catches the new exception type specifically and maps it to a distinguishable, non-retryable `ShipmentCreationResult`. Owns the decision of *which* `ErrorCodes` value and message text (at the result-object level, not yet localized) represents "Shoptet rejected this permanently" vs. "something else went wrong."
**Contract:** `ShipmentCreationResult` gains one additional piece of information — the Shoptet validation message — alongside its existing `IsSuccess`/`ErrorCode`/`ShipmentGuid`/`Labels`. Existing consumers reading only the fields they already read are unaffected (additive change).

### `useScanPackingOrder` (frontend hook)
**Responsibility (extended):** its existing job of turning an error envelope into operator-facing Czech text gains one more case, and — new — actually reads `params` for this one case (today it discards `params` entirely for scan errors). No other error code in this map starts reading `params`; this is scoped to the new code only, so the change to the `toMessage` callback's signature/body stays local to this one case rather than a rewrite of the whole map.

## Data Schemas

### New exception (in-memory only, not persisted)

```csharp
namespace Anela.Heblo.Application.Features.ShipmentLabels;

public class ShoptetShipmentValidationException : Exception
{
    public string OrderCode { get; }
    public string ShoptetErrorCode { get; }   // "shipment-validation-failed"
    public string? Instance { get; }           // Shoptet's "instance" field, e.g. "data.orderCode"

    public ShoptetShipmentValidationException(string orderCode, string shoptetErrorCode, string message, string? instance)
        : base(message)
    {
        OrderCode = orderCode;
        ShoptetErrorCode = shoptetErrorCode;
        Instance = instance;
    }
}
```

### `ErrorCodes` addition (`backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`)

```csharp
[HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
ShipmentValidationFailed = 2910,
```

### `ShipmentCreationResult` — additive field

Whatever the current shape is (verify at implementation time; the architecture review notes it currently has no message-carrying field), add one nullable field to carry the Shoptet message through to the controller response, e.g.:

```csharp
public class ShipmentCreationResult
{
    public bool IsSuccess { get; init; }
    public ErrorCodes? ErrorCode { get; init; }
    public Dictionary<string, string>? Params { get; init; }   // NEW — null except for ShipmentValidationFailed
    // ...existing fields (ShipmentGuid, CarrierCode, CarrierName, Labels) unchanged
}
```

### Wire shape — `POST /api/packaging/orders/{orderCode}/scan` failure response (new case only)

No change to the response *envelope* type (`ScanPackingOrderResponse : BaseResponse`, already has `Success`, `ErrorCode`, `Params`) — only a new value/shape *within* the existing `Params` dictionary for this one error code:

```json
// HTTP 422 (was: HTTP 503 with ErrorCode "ShipmentCreationFailed" and no Params)
{
  "success": false,
  "errorCode": "ShipmentValidationFailed",
  "params": {
    "ShoptetMessage": "Invalid recipient of order, missing fields: city, zip."
  }
}
```

The exact `Params` key name (`ShoptetMessage` above is illustrative) must match between the backend write site (`ShipmentCreationService`) and the frontend read site (`useScanPackingOrder.ts`'s `toMessage` callback) — this is the one piece of "schema" that must be kept in lockstep across the stack, and should be defined once (e.g. as a shared constant or simply documented consistently) rather than hardcoded as a magic string in two places without cross-reference. The planner should make this key name an explicit, named decision in one task so the frontend task references the exact same string rather than guessing.

### Frontend — `SCAN_ERROR_MESSAGES` / `toMessage` extension

No new TypeScript interface needed — `ApiErrorEnvelope.params` (`frontend/src/api/apiErrorEnvelope.ts`) already types `params?: Record<string, string>`, which already fits the shape above. Only the `toMessage` callback passed to `callApi` in `useScanPackingOrder.ts` needs to branch on `errorCode === 'ShipmentValidationFailed'` and read `params?.ShoptetMessage` (or whatever key name the backend task settles on) to build the detailed message; falls back to the base message (see UX/UI Design) when `params` is absent.
