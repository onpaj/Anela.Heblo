# Specification: Unit tests for ComgateBankClient.GetStatementsAsync

## Summary
Add unit tests covering `ComgateBankClient.GetStatementsAsync` (date-range iteration, account filter, exception wrapping). Test-only change; no production code changes.

## Background
Issue #4474: coverage of `ComgateBankClient.cs` is 40%. `GetStatementsAsync` is the import entry point for Comgate statements; defects in the loop boundary or account filter cause silent financial data errors.

## Functional Requirements

### FR-1: Multi-day range with account filter
**Acceptance criteria:**
- Two-day range, each day's response has one matching and one non-matching `AccountCounterParty`; only matching entries are returned, with correct StatementId, Date, Account.
- Exactly two POST requests are issued, URLs contain `date=` for each day (first and last day both included).

### FR-2: Single-day boundary
**Acceptance criteria:**
- `dateFrom == dateTo` issues exactly one POST; time-of-day components are ignored.

### FR-3: Circuit-breaker wrapping
**Acceptance criteria:**
- A pipeline/handler that throws `BrokenCircuitException` yields `PaymentGatewayUnavailableException` with null StatusCode and the original as InnerException.

### FR-4: 5xx wrapping
**Acceptance criteria:**
- HTTP 503 yields `PaymentGatewayUnavailableException` with StatusCode 503.
- Non-5xx failure (e.g. 400) rethrows `HttpRequestException` (optional extra).

### FR-5: Filter is exact
**Acceptance criteria:**
- Non-matching account (including case/whitespace variants) is excluded; empty response yields empty list.

## Non-Functional Requirements
### NFR-1: Performance
Tests use `ResiliencePipeline.Empty` (no retry delays); run in well under a second each.
### NFR-2: Security
No real secrets; use fake settings. Requests never leave the process.

## Data Model
Response JSON: array of `{TransferId, TransferDate (yyyy-MM-dd), AccountCounterParty, AccountOutgoing, VariableSymbol}`. `ComgateStatementHeader` is internal, so tests build JSON directly.

## API / Interface Design
Target: `Task<IList<BankStatementHeader>> GetStatementsAsync(string accountNumber, DateTime dateFrom, DateTime dateTo)`. Tests added to `backend/test/Anela.Heblo.Tests/Features/Bank/ComgateBankClientTests.cs` using the existing `FakeHttpHandler` / `CreateClient` helpers.

## Dependencies
xUnit, Moq, Polly (already referenced). `ReadAsAsync<T>` (JSON) must be able to deserialize the test JSON (verify property-name casing handling).

## Out of Scope
Production code changes, `GetStatementAsync` tests, retry-timing tests, process-doc changes (behaviour unchanged).

## Open Questions
None.

## Status: COMPLETE
