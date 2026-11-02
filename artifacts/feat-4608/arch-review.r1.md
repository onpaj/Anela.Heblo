# Architecture Review: ShoptetPaging unit tests

## Skip Design: true
Backend test-only change; no UI.

## Architectural Fit Assessment
Fits existing convention: sibling tests live in `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Analytics/`, use xUnit + FluentAssertions with Arrange/Act/Assert comments. `ShoptetPaging` is internal; access is already granted via `InternalsVisibleTo("Anela.Heblo.Adapters.Shoptet.Tests")` (declared in `ShoptetApiExpeditionListSource.cs`). No production change needed.

## Proposed Architecture

### Component Overview
One new static-method test class `ShoptetPagingTests` calling `ShoptetPaging` directly with hand-built `ShoptetPaginatorDto` instances.

### Key Design Decisions
#### Decision 1: Direct unit tests, no fakes
**Options considered:** drive through `FakeShoptetOrderAnalyticsClient` and sync services; test the static class directly.
**Chosen approach:** direct tests.
**Rationale:** the logic is pure; indirect tests already exist and cannot isolate each guard branch.

#### Decision 2: Theories for boundaries
Use `[Theory]/[InlineData]` for page >= PageCount and collected >= TotalCount boundaries.

## Implementation Guidance

### Directory / Module Structure
Create `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Analytics/ShoptetPagingTests.cs`.

### Interfaces and Contracts
`ShoptetPaging.IsLastPage(ShoptetPaginatorDto?, int itemsOnPage, int page, string description) : bool`
`ShoptetPaging.EnsureComplete(ShoptetPaginatorDto?, int collected, string description) : void`
Throws `ShoptetOrderSyncException` (public sealed, namespace `Anela.Heblo.Adapters.ShoptetApi.Analytics`).

### Data Flow
Test builds paginator -> calls static method -> asserts return value or exception type and message fragments.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Over-asserting exact messages makes tests brittle | Low | Assert with `WithMessage("*fragment*")` on stable facts (description, page, counts) only |
| Internal access fails to compile | Low | InternalsVisibleTo verified present |

## Specification Amendments
None.

## Prerequisites
None.
