# Design: ShoptetApiInvoiceSource Unit Test Coverage

## Component Design

**File:** `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs`
**Namespace:** `Anela.Heblo.Adapters.Shoptet.Tests.Unit`
**Test class:** `ShoptetApiInvoiceSourceTests`

This is the only file added. It sits alongside the existing `Integration/ShoptetApiInvoiceSourceIntegrationTests.cs` for the same class, and follows the sibling unit-test conventions already present in `Anela.Heblo.Adapters.Shoptet.Tests/Unit/` (e.g. `ShoptetPriceClientTests.cs`, `ShoptetApiExpeditionListSource_CoolingMarkerTests.cs`): xUnit `[Fact]`/`[Theory]`, Moq, FluentAssertions, private `static` `Build*` factory helpers instead of a shared fixture/builder class.

### Collaborators — fake vs. real

- **`IShoptetInvoiceClient` — mocked.** The only faked collaborator, via `Mock<IShoptetInvoiceClient>` (Moq). Only the two members `GetAllAsync` actually calls are set up:
  - `Task<IReadOnlyList<ShoptetInvoiceDto>> ListInvoicesAsync(DateTime?, DateTime?, CancellationToken)`
  - `Task<ShoptetInvoiceDto?> GetInvoiceAsync(string, CancellationToken)`
  `GetInvoiceRawJsonAsync` is left unconfigured (unused by the class under test). Setups are **exact-argument, per-code** (e.g. `.Setup(x => x.GetInvoiceAsync("A", It.IsAny<CancellationToken>()))` with a distinct setup for `"B"`), not a blanket `It.IsAny<string>()` stub — this is required because FR-3/FR-4/FR-5 assert *which* codes were and were not sent to `GetInvoiceAsync`, verified with `Times.Once`/`Times.Never`/`Times.Exactly`. The `CancellationToken` parameter is always matched explicitly with `It.IsAny<CancellationToken>()` in both `Setup` and `Verify` calls to avoid default-parameter mismatch.

- **`ShoptetInvoiceMapper` — real instance, not mocked.** It is a concrete class with no interface, so mocking it would require a production-code change (out of scope). Constructed identically to `ShoptetInvoiceMapperTests.BuildMapper()`: `new ShoptetInvoiceMapper(new BillingMethodMapper(), new ShippingMethodMapper(Options.Create(new ShoptetApiSettings())))`. Using the real mapper proves (per FR-1) that `GetAllAsync` actually invokes mapping and the DTO's data flows through, without re-verifying the mapper's own field-level correctness (already covered by `ShoptetInvoiceMapperTests`).

- **`ILogger<ShoptetApiInvoiceSource>` — no-op.** `Mock.Of<ILogger<ShoptetApiInvoiceSource>>()`. No log-output assertions are required by the spec.

### Helper methods (private `static`, local to the test class)

```csharp
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
```

`Code` and `OrderCode` are always given distinct values in `BuildDto` so that assertions against the mapped result's `OrderCode` field (see FR-1 below) would fail loudly if a future refactor swapped the two — `ShoptetInvoiceMapper.Map` inverts them (`mapped.Code = src.OrderCode`, `mapped.OrderCode = src.Code`). `Items` is always initialized to an empty (non-null) list, as the real mapper dereferences it. No shared builder/fixture class is introduced; this keeps the change self-contained to one file, consistent with how `ShoptetApiExpeditionListSourceTests` builds its own DTOs inline.

Each test constructs its own `IssuedInvoiceSourceQuery` inline (no shared query-builder needed) — single-invoice-mode tests set `InvoiceId`; list-mode tests leave it `null` so `QueryByInvoice` evaluates `false`, and set `Currency` explicitly where the filter is under test.

### Test scenarios (one method per FR)

#### FR-1: Single-invoice fetch returns the mapped invoice when the client finds it
Arrange `query.InvoiceId = "INV-1"` and mock `GetInvoiceAsync("INV-1", ct)` to return `BuildDto("INV-1", orderCode: "ORD-1")`. Act via `source.GetAllAsync(query)`. Assert: exactly one batch is returned; `BatchId == query.RequestId`; `Invoices.Count == 1`; `Invoices[0].OrderCode == "INV-1"` (the field that proves the real mapper ran, per the `Code`/`OrderCode` inversion noted above — not `Invoices[0].Code`). Verify `ListInvoicesAsync` was called `Times.Never` and `GetInvoiceAsync("INV-1", ...)` was called `Times.Once`.

#### FR-2: Single-invoice fetch returns an empty invoice list when the client returns null
Arrange `query.InvoiceId = "INV-1"` and mock `GetInvoiceAsync("INV-1", ct)` to return `(ShoptetInvoiceDto?)null`. Act. Assert: the call completes without throwing; exactly one batch is returned with `BatchId == query.RequestId`; `Invoices` is non-null with `Count == 0`.

#### FR-3: List-mode currency filter excludes non-matching currencies
Arrange `query.InvoiceId = null`, `query.Currency = "CZK"`; mock `ListInvoicesAsync` to return `[BuildDto("A", currency: "CZK"), BuildDto("B", currency: "EUR")]`; mock `GetInvoiceAsync("A", ct)` to return a populated DTO. Act. Assert: `GetInvoiceAsync("A", ...)` called `Times.Once`; `GetInvoiceAsync("B", ...)` called `Times.Never`; `Invoices` contains exactly the mapped result for `"A"` and nothing derived from `"B"`.

#### FR-4: List-mode currency filter comparison is case-insensitive
`[Theory]` with `[InlineData("czk", "CZK")]` and `[InlineData("CZK", "czk")]` (summary `CurrencyCode` vs. `query.Currency`, in each casing direction, to remove ambiguity about which side is normalized). Mock `ListInvoicesAsync` to return one summary with the theory's `CurrencyCode`; set `query.Currency` to the theory's other value; mock `GetInvoiceAsync` for that code to return a populated DTO. Assert: the code is fetched (`Times.Once`) and its mapped invoice appears in `Invoices`.

#### FR-5: Null individual-detail guard excludes the affected code without aborting the batch
Arrange two currency-matching summaries `"A"`/`"B"` via `ListInvoicesAsync`; mock `GetInvoiceAsync("A", ct)` to return `null` and `GetInvoiceAsync("B", ct)` to return a populated DTO. Act. Assert: the call completes without throwing; exactly one batch is returned; `Invoices.Count == 1` containing only the mapped result for `"B"`. Verify both `GetInvoiceAsync("A", ...)` and `GetInvoiceAsync("B", ...)` were each called `Times.Once`, confirming the loop did not short-circuit on the null result.

## Data Schemas

No new schemas are introduced. This is a test-only change; all types below already exist in production code and are used by the tests exactly as they are defined today:

- `Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceSourceQuery` — `RequestId`, `InvoiceId` (drives `QueryByInvoice`), `DateFrom`, `DateTo`, `Currency`.
- `Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceDetailBatch` — `BatchId`, `Invoices` (`List<IssuedInvoiceDetail>`).
- `Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model.ShoptetInvoiceDto` — `Code`, `OrderCode`, `Items` (`List<ShoptetInvoiceItemDto>`), `Price` (`ShoptetInvoicePriceDto?`).
- `Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model.ShoptetInvoicePriceDto` — `CurrencyCode`, `WithVat`, `WithoutVat`.
- `Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.IShoptetInvoiceClient` — the interface being mocked; its `ListInvoicesAsync`/`GetInvoiceAsync` signatures are consumed as-is.

No modifications to any of these types are required or in scope.
