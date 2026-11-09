# Design: Photobank delta 410 recovery

## Component Design
- `PhotobankIndexJob` (modified): new private `GetDeltaWithResyncAsync(PhotobankIndexRoot, CancellationToken)` returning `GraphDeltaResult`. Single retry, only on `HttpRequestException` with `StatusCode == Gone` and a non-empty stored `DeltaLink`. Sets `root.DeltaLink = null` before the retry and logs a warning (root id only).
- `IPhotobankGraphService` / `PhotobankGraphService`: unchanged.
- Tests: extend `PhotobankIndexJobTests` using existing mock setup; construct `new HttpRequestException("410", null, HttpStatusCode.Gone)`.

## Data Schemas
No database, API or payload changes. `PhotobankIndexRoot.DeltaLink` (string?) semantics unchanged.
