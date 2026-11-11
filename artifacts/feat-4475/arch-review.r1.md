# Architecture Review: ListByExternalCodePrefixAsync unit tests

## Skip Design: true
Test-only, backend-only change.

## Architectural Fit Assessment
Fits the existing adapter test pattern in `ShoptetOrderClientTests.cs` (fake `HttpMessageHandler` via `BuildClient`, `Json` helper). No production change; `ShoptetOrderClient` is public and constructible with `HttpClient` + `IOptions<ShoptetOrdersSettings>`.

## Proposed Architecture

### Component Overview
Test -> `ShoptetOrderClient` -> `HttpClient` -> `FakeDelegatingHandler(handler)`. The handler routes on `request.RequestUri.PathAndQuery`: `/api/orders?page=N&itemsPerPage=50` returns list pages; `/api/orders/{code}` returns `{data:{order:{...}}}` detail.

### Key Design Decisions
#### Decision 1: Reuse the existing in-file helpers
**Chosen approach:** add tests to `ShoptetOrderClientTests.cs` with a small private routing helper that records requested paths. **Rationale:** consistency, no new files; `FakeDelegatingHandler` is already internal to the test assembly.

#### Decision 2: Assert on recorded requests
Record `PathAndQuery` in a `List<string>` to assert pagination stop and that filtered-out orders get no detail call.

## Implementation Guidance
### Directory / Module Structure
Only modify `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetOrderClientTests.cs`.
### Interfaces and Contracts
Method under test: `Task<List<EshopOrderSummary>> ListByExternalCodePrefixAsync(string prefix, string? emailFilter = null, CancellationToken ct = default)`. JSON is camelCase (`Json` helper); detail response shape `data.order.{code,externalCode,email,status.id}`.
### Data Flow
List page -> (email filter) -> per-order detail -> ordinal prefix check -> `MapToSummary` -> next page unless `page >= pageCount`.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Fake handler routing breaks on query ordering | Low | Match with `StartsWith`/`Contains` on exact current URLs |
| Tests coupled to itemsPerPage=50 | Low | Assert only on `page=` parameter |

## Specification Amendments
None.

## Prerequisites
None.
