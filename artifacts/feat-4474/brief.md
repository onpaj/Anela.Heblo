## Module / File
`backend/src/Adapters/Anela.Heblo.Adapters.Comgate/ComgateBankClient.cs`

## Coverage
Line coverage: 40% (filter threshold: 60%). The existing tests cover `GetStatementAsync` (circuit-breaker open, server-error 500, 4xx client error) and `AboFile.Parse`, but `GetStatementsAsync` — the method that fetches the list of bank statements over a date range — has no test at all.

## What's not tested
`GetStatementsAsync` contains three distinct behaviors that are not exercised:

1. **Date-range iteration**: the method walks from `dateFrom` to `dateTo` one calendar day at a time and issues one POST per day. If the loop boundary (`date <= dateTo.Date`) is off by one, the first or last day's statements are silently dropped.
2. **Account filter**: each day's response is filtered by `w.AccountCounterParty == accountNumber`. A wrong comparison (e.g. case sensitivity or field mapping) would silently return statements for other accounts — or return nothing.
3. **Exception wrapping**: `BrokenCircuitException` is wrapped into `PaymentGatewayUnavailableException`; server-error `HttpRequestException` gets the same wrapper with the status code. Neither path is asserted in the multi-day scenario.

## Why it matters
This method is the entry point for importing Comgate bank statements. A bug in the account filter could silently accept transactions belonging to a different counter-party; a date-loop defect would produce an incomplete import with no error raised. Both are financial data integrity issues.

## Suggested approach
Unit test with a mocked `HttpClient` (via `MockHttp` or a custom `DelegatingHandler`):
- **Happy path**: two-day range, response contains one matching and one non-matching `AccountCounterParty` — assert only the matching entry is returned.
- **Single-day boundary**: `dateFrom == dateTo` — assert exactly one POST is issued.
- **Circuit-breaker path**: mock `HttpClient` to throw `BrokenCircuitException` — assert `PaymentGatewayUnavailableException` is thrown.
- **5xx after retries**: mock an HTTP 503 — assert `PaymentGatewayUnavailableException` with status code 503.

Effort: ~1–2 hours.

---
_Filed by weekly coverage-gap routine on 2026-10-12. Based on CI run #37593051210 (01ae6b757d0ee6facb6b3170eaba26b420c59288)._