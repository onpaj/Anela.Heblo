# Design: Suppress label-poll 404 noise in browser telemetry

## Component Design

### `frontend/src/telemetry/labelPollFilter.ts` (new, pure)
- `LABEL_PDF_URL_PATTERN = /\/api\/packaging\/orders\/[^/]+\/packages\/\d+\/label\.pdf/`
- `isExpectedLabelPollNotFound(item: ITelemetryItem): boolean`
  - Returns true only when `item.baseType === 'RemoteDependencyData'`, `baseData.resultCode` (string or number) equals 404, `baseData.target`/`name`/`data` contains a URL matching the pattern, and the request headers in `baseData.properties.requestHeaders` (object, case-insensitive key lookup) contain `X-Label-Poll`.
  - Returns false for anything else, including missing fields (never throws).
- Registered in `initAppInsights` as `instance.addTelemetryInitializer(item => !isExpectedLabelPollNotFound(item))` (initializer returning `false` drops the item). Registered before the identity initializer; identity initializer unchanged.
- Fallback (only if header not surfaced): `printLabelPdf.ts` exports `markLabelPollInFlight(url)` / registry consulted by the filter. Not built unless needed.

### `printLabelPdf.ts`
- Export `LABEL_POLL_HEADER`.
- `printLabelPdf(orderCode, label, onAfterPrint?, onError?, signal?)`: forward `signal` to `printLabelWithReadiness`; in `.then`, `if (signal?.aborted) return;` before the `!result.printed` error branch.

### `PackingLabelPrinter.tsx`
- `abortRef = useRef<AbortController | null>(null)`. `printPackage` aborts the previous controller, creates a new one, passes its signal. Cleanup effect aborts on unmount. The auto-print effect keyed by `order.code|reprintGeneration` naturally aborts via `printPackage`.

### `ZasilkyPage.tsx`
- Same pattern in `handleReprint` with one controller ref; abort on unmount.

## Data Schemas
No database, API, or event payload changes. Telemetry item shape consumed (subset):
```
ITelemetryItem {
  baseType: 'RemoteDependencyData',
  baseData: {
    type: 'Fetch', target: 'heblo.anela.cz', name: 'GET /api/packaging/.../label.pdf',
    data: 'https://heblo.anela.cz/api/packaging/orders/126019126/packages/1/label.pdf',
    resultCode: 404 | '404',
    properties: { requestHeaders: { 'X-Label-Poll': '1' } }
  }
}
```
