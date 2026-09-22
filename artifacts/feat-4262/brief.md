telemetry-signal: req-5xx:Packaging/ScanOrder:503

## Signal

**Window:** P7D ending 2026-09-21T23:04 UTC
**Endpoint:** `POST Packaging/ScanOrder [orderCode]`
**Occurrences:** 5 × 503, 100% of this endpoint's failures in the window (395 total calls, 1.3% fail rate) — all on **2026-09-21 between 05:43:48Z and 06:15:02Z** (31 minutes), all for the **same order code: 126020133**.

Each 503 maps 1:1 (same `operation_Id`) to the same exception:

```
System.Net.Http.HttpRequestException at Anela.Heblo.Adapters.ShoptetApi.Shipments.ShoptetShipmentClient+<CreateShipmentAsync>d__11.MoveNext
```

with the same underlying Shoptet response every time:

```
POST /api/shipments for order 126020133 returned 422: {"data":null,"errors":[{"errorCode":"shipment-validation-failed","message":"Invalid recipient of order, missing fields: city, zip.","instance":"data.orderCode"}]}
```

Timestamps of the 5 identical attempts: 05:43:48Z, 05:47:44Z, 05:47:52Z, 06:01:45Z, 06:15:01Z.

## Correlation hypothesis

Order 126020133's recipient address in Shoptet is missing `city`/`zip`, so shipment creation permanently fails validation (a genuine 4xx client error, not a transient fault). `ShoptetShipmentClient`/the `Packaging/ScanOrder` handler doesn't distinguish this permanent validation failure from a transient one — it surfaces as an opaque `503 Service Unavailable` to the packing operator instead of the actual "missing city/zip" message. That's consistent with the 5 retries over 31 minutes: either an operator repeatedly re-scanning against a generic "service unavailable" with no actionable feedback, or an automatic retry with no backing-off on a non-retryable error. No commit or PR in the last 7 days touches `ShoptetShipmentClient` or shipment validation, so this is unaddressed.

## Next step

- Map Shoptet's `422 shipment-validation-failed` to a clear 4xx response on `Packaging/ScanOrder` that surfaces the actual message ("recipient missing city/zip") to the operator instead of a 503, so they know to fix the address rather than retry.
- Separately, fix order 126020133's recipient address in Shoptet (missing city/zip) to unblock its stuck shipment.
