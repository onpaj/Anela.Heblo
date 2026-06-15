telemetry-signal: req-403:GET /api/StockUpOperations/summary

## Signal

In the last 7 days (P7D, ending 2026-06-12T15:12Z), `GET /api/StockUpOperations/summary` received **210 requests** with virtually all failing:

| resultCode | count |
|---|---|
| 403 Forbidden | 209 |
| 500 Internal Server Error | 1 |
| 200 OK | 0 |

**99.5% of requests to this endpoint returned 403.** Alongside this, `GET StockUpOperations/GetSummary` (the MVC controller route, different path) handled 8,464 requests successfully (p95 82 ms) in the same window — showing the underlying feature is healthy, but this specific `/api/` URL is inaccessible to the caller(s).

The endpoint is absent from the last 2 days of telemetry (P2D ending now), which may mean:
- A frontend change stopped polling this path, OR
- PR #2962 ("open dashboard to all users with per-tile permission enforcement", merged 2026-06-12T12:53Z) incidentally changed permission requirements for this route.

## Correlation

No PR explicitly targets `/api/StockUpOperations/summary`. The 209 × 403 over 5 days (~42/day) indicates a real user or automated client was regularly hitting a misconfigured route. The co-occurring 500 suggests the route occasionally reaches the handler despite the authorization filter.

## Next step

Confirm what `/api/StockUpOperations/summary` maps to in the routing table vs. `StockUpOperations/GetSummary`. Check whether these two paths apply different `[Authorize]` / `[RequiredPermission]` attributes. If the `/api/` path is a duplicate or legacy route with stricter permissions, either align its authorization with the working route or remove it. Monitor for recurrence — if 403s have stopped after the June 12 deployment, the issue may have self-resolved.