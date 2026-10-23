# Design: Unit tests for ComgateBankClient.GetStatementsAsync

## Component Design
Tests only, in `ComgateBankClientTests.cs`, reusing `CreateClient(handler)`. A small recording handler (private nested class) captures `HttpRequestMessage` method/URI and returns a response from a per-call factory. Tests:
1. `GetStatementsAsync_TwoDayRange_ReturnsOnlyMatchingAccount` - 2 POSTs, filtered results, mapped fields.
2. `GetStatementsAsync_SingleDay_IssuesExactlyOnePost` - from == to (with differing time-of-day).
3. `GetStatementsAsync_NoMatchingAccount_ReturnsEmpty`.
4. `GetStatementsAsync_WhenCircuitBreakerOpen_ThrowsPaymentGatewayUnavailable` - StatusCode null, inner BrokenCircuitException.
5. `GetStatementsAsync_WhenServerError503_ThrowsPaymentGatewayUnavailable` - StatusCode 503.
6. `GetStatementsAsync_WhenClientError400_RethrowsHttpRequestException`.

## Data Schemas
Response body: `[{"TransferId":"T1","TransferDate":"2026-10-01","AccountCounterParty":"123/0100","AccountOutgoing":"x","VariableSymbol":"1"}]`.
