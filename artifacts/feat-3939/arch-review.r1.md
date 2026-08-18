# Architecture Review: ShoptetApiInvoiceSource Unit Test Coverage

## Skip Design: true

## Architectural Fit Assessment
This is a pure test-addition task against an existing, stable adapter class (`ShoptetApiInvoiceSource`) in the Vertical Slice `Anela.Heblo.Adapters.ShoptetApi` project. No production code, DTO shapes, or module boundaries change. The spec's plan — mock `IShoptetInvoiceClient`, use the real `ShoptetInvoiceMapper`, assert via FluentAssertions/Moq in an xUnit `[Fact]`/`[Theory]` class — matches this repo's documented testing strategy (`docs/architecture/testing-strategy.md`: xUnit + Moq + FluentAssertions, "Business Value Focus", test collaborators' seams not internals) and matches the actual conventions already present in `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/` (e.g. `ShoptetOrderClient_SetAdditionalFieldTests.cs`, `ShoptetPriceClientTests.cs`) verbatim: same test project, same `Unit/` subfolder, same `Anela.Heblo.Adapters.Shoptet.Tests.Unit` namespace, same `BuildClient()`/`BuildX()` private static factory-method idiom, same `Times.Once`/`Times.Never` verification style. No architectural amendment is needed to the class under test. Verdict: proceed as specified, with the concrete guidance below.

## Proposed Architecture

### Component Overview
One new test file is added; nothing else changes:

```
backend/test/Anela.Heblo.Adapters.Shoptet.Tests/
└── Unit/
    └── ShoptetApiInvoiceSourceTests.cs   ← NEW
```

It exercises `ShoptetApiInvoiceSource.GetAllAsync` end-to-end against:
- `Mock<IShoptetInvoiceClient>` — the only faked collaborator (Moq, strict per-call setups).
- A **real** `ShoptetInvoiceMapper` built exactly like `ShoptetInvoiceMapperTests.BuildMapper()` does (`backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetInvoiceMapperTests.cs`): `new ShoptetInvoiceMapper(new BillingMethodMapper(), new ShippingMethodMapper(Options.Create(new ShoptetApiSettings())))`.
- `Mock.Of<ILogger<ShoptetApiInvoiceSource>>()` — no-op, no log assertions required by the brief.

### Key Design Decisions

#### Decision 1: Real mapper vs. mocked mapper
**Options considered:**
- Mock `ShoptetInvoiceMapper.Map` (not directly mockable — it's a concrete class, not an interface, so this would require introducing an abstraction) vs.
- Instantiate the real `ShoptetInvoiceMapper` with its own real dependencies (`BillingMethodMapper`, `ShippingMethodMapper`).

**Chosen approach:** Real mapper, built the same way `ShoptetInvoiceMapperTests` builds it.

**Rationale:** `ShoptetInvoiceMapper` is a concrete `sealed`-in-spirit class with no interface — introducing one purely to mock it in this test would be a production-code change, which is explicitly out of scope. Using the real mapper is also cheap: `BillingMethodMapper` and `ShippingMethodMapper` (with a default `ShoptetApiSettings`) are themselves side-effect-free value mappers. This keeps the new tests focused on `ShoptetApiInvoiceSource`'s own branching (single-fetch vs. list, currency filter, null guard) while still proving per FR-1 that the mapper is actually invoked and its output flows through — exactly what the brief's suggested approach asks for. `ShoptetInvoiceMapper`'s own field-level correctness (VAT rates, rounding, address mapping) is already covered by `ShoptetInvoiceMapperTests` and must not be re-verified here.

#### Decision 2: Test project and file placement
**Options considered:**
- `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/` (where `ShoptetInvoiceMapperTests.cs` lives) vs.
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/` (where the sibling adapter-level unit tests for this same `IssuedInvoices` folder — and the existing `Integration/ShoptetApiInvoiceSourceIntegrationTests.cs` for this exact class — live).

**Chosen approach:** `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs`, namespace `Anela.Heblo.Adapters.Shoptet.Tests.Unit`.

**Rationale:** `Anela.Heblo.Tests` holds mapper/value-object-level unit tests reached via a different test project that only references the mapper's own dependencies; `Anela.Heblo.Adapters.Shoptet.Tests` is where every other test for classes physically inside `Anela.Heblo.Adapters.ShoptetApi/IssuedInvoices/` and `.../Orders/` already lives (`ShoptetOrderClient_SetAdditionalFieldTests.cs`, `ShoptetPriceClientTests.cs`, and critically the existing `ShoptetApiInvoiceSourceIntegrationTests.cs` for this identical class). Placing the new unit tests there keeps all tests for `ShoptetApiInvoiceSource` in one project, avoids adding a `ShoptetInvoiceMapper`-only dependency chain to `Anela.Heblo.Tests`, and matches the spec's own stated dependency section. Confirmed: the target csproj (`Anela.Heblo.Adapters.Shoptet.Tests.csproj`) already references `Anela.Heblo.Adapters.ShoptetApi.csproj` and already has `Moq`, `FluentAssertions`, `xunit`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging` as package references — no csproj edits needed.

#### Decision 3: Mocking granularity for `IShoptetInvoiceClient`
**Options considered:** loose `It.IsAny<string>()` setups returning one canned response vs. per-code exact-match setups.

**Chosen approach:** Exact-argument Moq setups per invoice code (e.g. `mock.Setup(x => x.GetInvoiceAsync("A", It.IsAny<CancellationToken>()))`, a separate setup for `"B"`), combined with `Times.Once`/`Times.Never`/`Times.Exactly` verification.

**Rationale:** FR-3/FR-4/FR-5 specifically require proving *which* codes were (and were not) sent to `GetInvoiceAsync` — this is only observable with per-code setups and per-code `Verify` calls, not a blanket any-args stub. This mirrors the existing `ShoptetApiExpeditionListSource_CoolingMarkerTests.cs` pattern of setting up distinct responses per order code and verifying call counts per code.

## Implementation Guidance

### Directory / Module Structure
Create exactly one file:
```
backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs
```
```csharp
namespace Anela.Heblo.Adapters.Shoptet.Tests.Unit;

public class ShoptetApiInvoiceSourceTests
{
    private static ShoptetInvoiceMapper BuildMapper() =>
        new(new BillingMethodMapper(), new ShippingMethodMapper(Options.Create(new ShoptetApiSettings())));

    private static ShoptetApiInvoiceSource BuildSource(Mock<IShoptetInvoiceClient> client) =>
        new(client.Object, BuildMapper(), Mock.Of<ILogger<ShoptetApiInvoiceSource>>());

    private static ShoptetInvoiceDto BuildDto(string code, string? orderCode = null, string currency = "CZK") =>
        new()
        {
            Code = code,
            OrderCode = orderCode ?? $"ORD-{code}",
            Items = new List<ShoptetInvoiceItemDto>(),
            Price = new ShoptetInvoicePriceDto { CurrencyCode = currency, WithVat = "0", WithoutVat = "0" },
        };

    // FR-1..FR-5 as [Fact]/[Theory] methods below, one Arrange/Act/Assert block each.
}
```
No changes to `Anela.Heblo.Adapters.Shoptet.Tests.csproj` are required — all needed packages are already referenced (verified above).

### Interfaces and Contracts
Mock only `IShoptetInvoiceClient`; use its exact members as declared in `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/IssuedInvoices/IShoptetInvoiceClient.cs`:
```csharp
Task<IReadOnlyList<ShoptetInvoiceDto>> ListInvoicesAsync(DateTime? dateFrom, DateTime? dateTo, CancellationToken ct = default);
Task<ShoptetInvoiceDto?> GetInvoiceAsync(string code, CancellationToken ct = default);
```
`GetInvoiceRawJsonAsync` needs no setup (unused by `GetAllAsync`).

**Critical mapping detail the developer must get right in assertions** — `ShoptetInvoiceMapper.Map` inverts `Code`/`OrderCode` (`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/IssuedInvoices/Mapping/ShoptetInvoiceMapper.cs:88-89`):
```csharp
Code = src.OrderCode ?? string.Empty,   // mapped Code comes from DTO.OrderCode
OrderCode = src.Code,                   // mapped OrderCode comes from DTO.Code
```
FR-1's acceptance criterion ("assert the mapped invoice's identifying field ... reflects the DTO passed in") must assert `result.Invoices[0].OrderCode == dto.Code` (not `result.Invoices[0].Code == dto.Code`) to actually prove the real mapper ran, rather than accidentally passing against a stub. Use distinct, non-equal values for `Code` and `OrderCode` in every test DTO (as the `BuildDto` helper above does) so a Code/OrderCode swap bug in a future refactor would fail the test.

### Data Flow
- **FR-1** (`QueryByInvoice == true`, hit): `query.InvoiceId = "INV-1"` → `GetInvoiceAsync("INV-1", ct)` mocked to return `BuildDto("INV-1", orderCode: "ORD-1")` → assert 1 batch, `BatchId == query.RequestId`, `Invoices.Count == 1`, `Invoices[0].OrderCode == "INV-1"` → `client.Verify(x => x.ListInvoicesAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never)` and `client.Verify(x => x.GetInvoiceAsync("INV-1", It.IsAny<CancellationToken>()), Times.Once)`.
- **FR-2** (`QueryByInvoice == true`, miss): `GetInvoiceAsync("INV-1", ct)` mocked to return `(ShoptetInvoiceDto?)null` → assert no throw, 1 batch, `Invoices` non-null and `Count == 0`.
- **FR-3** (currency filter excludes): `query.InvoiceId = null`, `query.Currency = "CZK"`; `ListInvoicesAsync` returns `[BuildDto("A", currency: "CZK"), BuildDto("B", currency: "EUR")]`; `GetInvoiceAsync("A", ct)` returns a populated DTO. Assert `GetInvoiceAsync("A", …)` called `Times.Once`, `GetInvoiceAsync("B", …)` called `Times.Never`, and `Invoices` contains exactly the mapped "A" result.
- **FR-4** (case-insensitive match, `[Theory]`): `[InlineData("czk", "CZK")]` / `[InlineData("CZK", "czk")]` — summary `CurrencyCode` vs. `query.Currency` in each casing pair; assert the code is still fetched and included.
- **FR-5** (null-detail guard): two matching-currency summaries `"A"`/`"B"`; `GetInvoiceAsync("A", …)` → `null`, `GetInvoiceAsync("B", …)` → populated DTO. Assert no throw, `Invoices.Count == 1` (only "B"'s mapped result), and both `GetInvoiceAsync("A", …)` and `GetInvoiceAsync("B", …)` were each called `Times.Once` (loop did not short-circuit).

For all list-mode tests, mock `ListInvoicesAsync` with `It.IsAny<DateTime?>(), It.IsAny<DateTime?>()` (the query's `DateFrom`/`DateTo` are irrelevant to these branches) and leave `query.InvoiceId` unset (`null`) so `QueryByInvoice` is `false`.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Asserting `Code` instead of `OrderCode` in FR-1 (or vice versa) silently passes even if the mapper call is stubbed out or broken, because both fields default to non-null strings | Medium | Use distinct `Code`/`OrderCode` values per DTO (see `BuildDto` above) and assert on `OrderCode` per the mapping table documented above; this makes a Code/OrderCode regression fail loudly |
| `ShoptetInvoicePriceDto` left `null` on a summary causes `i.Price?.CurrencyCode` to evaluate to `null`, which `OrdinalIgnoreCase` correctly treats as non-matching against `"CZK"`/`"EUR"` — easy to mistake for a bug when it's actually correct null-safe behavior | Low | Not a required test case per the spec (out of scope); no action needed, but do not add a test asserting this is a "bug" — it is a documented `?.` guard in the source |
| Forgetting `CancellationToken` in Moq setups (`It.IsAny<CancellationToken>()`) causes setups to not match because `GetAllAsync` is called with `CancellationToken.None` by default from the test, while the interface signature has a default parameter — mismatched Moq setups return `default`/null and cause confusing false failures | Medium | Always include the `CancellationToken` parameter explicitly in every `Setup`/`Verify` (as shown in the Data Flow section); do not rely on default-parameter matching in Moq |
| New test file accidentally placed in or duplicated into `Anela.Heblo.Tests` (where `ShoptetInvoiceMapperTests` lives) instead of `Anela.Heblo.Adapters.Shoptet.Tests/Unit/` | Low | Follow Decision 2 exactly — single file at `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs` |

## Specification Amendments
None required to the spec's functional requirements or scope — FR-1 through FR-5 are correct and sufficient to close the three flagged coverage gaps. One clarification for the implementing developer (not a spec defect): the spec's data-model note says DTO `Items` "must be initialized ... to avoid a NullReferenceException" — confirmed accurate defensively, but `ShoptetInvoiceDto.Items` already defaults to `new()` in the production type (`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/IssuedInvoices/Model/ShoptetInvoiceDto.cs:43`), so explicit initialization in test DTOs is a good-practice safeguard rather than a strict requirement — no spec change needed, just noting it isn't masking a real null-safety gap.

## Prerequisites
None. All required NuGet packages (`Moq`, `FluentAssertions`, `xunit`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging`) and project references (`Anela.Heblo.Adapters.ShoptetApi.csproj`) are already present in `Anela.Heblo.Adapters.Shoptet.Tests.csproj` — verified directly. No new fixtures, builders, or shared test infrastructure are needed; construct DTOs inline via a small private `BuildDto` helper local to the new test class, per the existing `BuildClient`/`DetailFor` idiom used elsewhere in this test project.
