telemetry-signal: exception:HttpRequestException@ShoptetStockClient.ListAsync

**Window:** P7D (2026-06-05 – 2026-06-12)
**Occurrences:** 8 (~1.1/day)

## Signal

`System.Net.Http.HttpRequestException` thrown at `Anela.Heblo.Adapters.ShoptetApi.Stock.ShoptetStockClient+<ListAsync>d__7.MoveNext` — HTTP-level failures when listing stock data from the Shoptet REST API.

| Metric | Value |
|---|---|
| Exception type | `System.Net.Http.HttpRequestException` |
| Frame | `ShoptetStockClient.ListAsync` |
| Failures in window | 8 |
| Rate | ~1.1 / day |

## Context

The slow-dependency table shows `api.myshoptet.com` with 1367 calls, 0 logged failures, and p95 241ms — the 8 exceptions appear at the application layer and may represent connection-level or parsing-layer faults that the dependency tracker did not capture as HTTP failures (e.g. connection timeout before a response, or an exception thrown during response parsing).

## Correlation hypothesis

No direct merge correlation in window. The Shoptet stock client is used for stock-taking / stock-up workflows. Failures are likely transient Shoptet API timeouts or oversized payloads that trigger a connection reset, rather than a code regression.

## Next step

1. Check exception `message` / `outerMessage` for the 8 occurrences — distinguish timeout vs DNS vs server-error.
2. If timeouts: review `HttpClient` timeout config for `ShoptetStockClient` and confirm a Polly retry policy is in place for transient faults.
3. If consistent server errors: check Shoptet API incident history for the window and document any new endpoint behaviour in `docs/integrations/shoptet-api.md`.