# Architecture Review: ShoptetApiInvoiceSource test coverage

## Skip Design: true
Test-only change; no UI.

## Architectural Fit Assessment
`ShoptetApiInvoiceSource` is an adapter implementing `IIssuedInvoiceSource` over `IShoptetInvoiceClient`. Tests for the adapter belong in `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/`, which is where `ShoptetApiInvoiceSourceTests.cs` already lives and covers the four scenarios in the issue. No architectural change is needed.

## Proposed Architecture

### Component Overview
Mock `IShoptetInvoiceClient` -> real `ShoptetInvoiceMapper` (built from `BillingMethodMapper`, `ShippingMethodMapper`) -> `ShoptetApiInvoiceSource`.

### Key Design Decisions
#### Decision 1: Do not duplicate existing tests
**Chosen approach:** verify, measure coverage, only add tests for lines actually uncovered.
**Rationale:** the issue's four cases are already implemented; duplicating adds noise (CLAUDE.md: surgical changes).

## Implementation Guidance
### Directory / Module Structure
Only `Unit/ShoptetApiInvoiceSourceTests.cs` may be extended.
### Interfaces and Contracts
Follow existing helpers `BuildMapper`, `BuildSource`, `BuildDto`, Arrange/Act/Assert, FluentAssertions.
### Data Flow
Query -> (QueryByInvoice? GetInvoiceAsync : ListInvoicesAsync -> currency filter -> GetInvoiceAsync per code) -> map -> single batch.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Coverage figure in issue stems from CI not running this test project | Low | Check coverage config in `.github/workflows/ci-*.yml`; report finding rather than broad CI changes |
| Docs gate (process docs CI) | Low | No owned production code touched |

## Specification Amendments
None.

## Prerequisites
.NET 8 SDK; restore of test project.
