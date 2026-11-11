# Specification: Unit tests for ShoptetOrderClient.ListByExternalCodePrefixAsync

## Summary
Add deterministic unit tests (mocked HttpClient) for `ShoptetOrderClient.ListByExternalCodePrefixAsync`, covering pagination termination, the optional email filter and the ordinal ExternalCode prefix match. Test-only change; no production code changes.

## Background
`ShoptetOrderClient.cs` has 46% line coverage (threshold 60%). The method is used for test-environment seeding/reconciliation and is only exercised by a live-store integration test. Behaviour: GET `/api/orders?page={n}&itemsPerPage=50`; optional case-insensitive email filter on list entries; per candidate GET `/api/orders/{code}` for detail; keep orders whose detail `ExternalCode` starts with prefix (ordinal); stop when `page >= Paginator.PageCount`.

## Functional Requirements

### FR-1: Single page, no email filter
**Acceptance criteria:**
- Page with order A (ExternalCode `TEST-1`) and B (ExternalCode `OTHER-1`), prefix `TEST-`: only A returned, mapped with Code, ExternalCode, Email, StatusId.
- Exactly one list request issued.

### FR-2: Multi-page pagination
**Acceptance criteria:**
- Two pages (`pageCount=2`), matching order only on page 2: it is returned.
- Requests for page=1 and page=2 occur, no page=3.

### FR-3: Email filter
**Acceptance criteria:**
- One page, two orders both with matching prefix, different emails; filter applied case-insensitively (e.g. filter `Seed@Heblo.test` vs `seed@heblo.test`): only the matching-email order returned.
- Detail endpoint is NOT called for the filtered-out order.

### FR-4: Empty results / termination edge cases
**Acceptance criteria:**
- `pageCount=1` with no prefix match: empty list, no exception.
- `pageCount=0` with empty orders: empty list, single request, no exception.

### FR-5: Prefix comparison is ordinal (case-sensitive)
**Acceptance criteria:**
- Order with ExternalCode `test-1` is not matched by prefix `TEST-`.
- Order with null ExternalCode is not matched.

## Non-Functional Requirements
### NFR-1: Performance
Tests run in-memory, no network, well under 1s each.
### NFR-2: Security
No real credentials or store calls.

## Data Model
Uses existing `OrderListResponse`, `OrderSummary`, `Paginator`, `CreateOrderResponse`, `EshopOrderSummary`.

## API / Interface Design
No change. Tests added to `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetOrderClientTests.cs`, reusing `BuildClient`, `Json` and `FakeDelegatingHandler`.

## Dependencies
xUnit, FluentAssertions (existing).

## Out of Scope
Production code changes; other untested methods; integration tests.

## Open Questions
None.

## Status: COMPLETE
