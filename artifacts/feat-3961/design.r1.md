# Design: ShippingMethodMapper Unit Test Coverage

## Component Design

No production components are added or changed. This design covers one new test file exercising the existing `ShippingMethodMapper` class.

### `ShippingMethodMapperTests`

- **Location:** `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShippingMethodMapperTests.cs`
- **Namespace:** `Anela.Heblo.Tests.Adapters.ShoptetApi`
- **Responsibility:** Exercise `ShippingMethodMapper.Map(ShoptetInvoiceShippingDto?)` across all three logical branches of its GUID-to-`ShippingMethod` resolution, plus both public constructors, bringing the file's line coverage to ≥60% (target: at or near 100%).
- **Style:** Mirrors `BillingMethodMapperTests.cs` (same directory) — plain xUnit class, `[Theory]`/`[InlineData]` for enumerated mappings, `[Fact]` for edge cases, `FluentAssertions` for value assertions. Adds `Mock<ILogger<ShippingMethodMapper>>` verification, following the idiom already used in `InvoiceImportServiceTests.cs`.
- **No shared fixture / `IClassFixture`:** each test builds its own `IOptions<ShoptetApiSettings>` and logger mock, consistent with NFR-3 (isolation, no I/O).

#### Test helper

A private static helper reduces duplication across FR-2/FR-3 setup:

```csharp
private static ShippingMethodMapper CreateMapper(
    Dictionary<string, ShippingMethod>? guidMap,
    out Mock<ILogger<ShippingMethodMapper>> loggerMock)
{
    loggerMock = new Mock<ILogger<ShippingMethodMapper>>();
    var settings = Options.Create(new ShoptetApiSettings
    {
        InvoiceShippingGuidMap = guidMap ?? new()
    });
    return new ShippingMethodMapper(settings, loggerMock.Object);
}
```

- Touches only `InvoiceShippingGuidMap` (never the unrelated `ShippingGuidMap` string-map property), structurally avoiding the naming trap flagged in the spec.
- Always uses the two-argument constructor (`IOptions<ShoptetApiSettings>, ILogger<ShippingMethodMapper>`), since log verification requires a real mock instance rather than the `NullLogger` used by the single-argument constructor.

#### Test groups

1. **FR-1 — "no shipping GUID" path** (`[Fact]` each, or one `[Theory]`/`[InlineData]` covering the three null/empty variants):
   - `Map(null)` → `ShippingMethod.PickUp`.
   - `Map(new ShoptetInvoiceShippingDto { Guid = null })` → `ShippingMethod.PickUp`.
   - `Map(new ShoptetInvoiceShippingDto { Guid = "" })` → `ShippingMethod.PickUp`.
   - Each asserts `loggerMock.Verify(x => x.Log(LogLevel.Warning, ...), Times.Never)`.

2. **FR-2 — "known GUID" path** (`[Theory]`/`[InlineData]`, mirroring `BillingMethodMapperTests.Map_ResolvesByDocumentedNumericId`):
   - `InvoiceShippingGuidMap` populated with ≥2 distinct GUID→`ShippingMethod` entries (e.g. one → `PPL`, one → `Zasilkovna`).
   - For each configured entry, `Map` returns the exact configured value; no `LogWarning` call occurs.

3. **FR-3 — "unknown GUID" path** (`[Fact]`, at least two variants):
   - Non-empty `InvoiceShippingGuidMap` containing unrelated entries, called with an unmapped GUID → returns `ShippingMethod.PickUp` **and** `loggerMock.Verify(..., Times.Once)` at `LogLevel.Warning`, with the logged state/message containing the passed-in GUID (via the `It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(unknownGuid))` matcher, copied from `InvoiceImportServiceTests.cs`).
   - A second variant using an *empty* `InvoiceShippingGuidMap` — same assertions — to prove the miss isn't an artifact of an empty dictionary.

4. **FR-4 — constructor coverage** (`[Fact]`):
   - One test instantiates `ShippingMethodMapper` via the single-argument constructor (`IOptions<ShoptetApiSettings>` only, delegating internally to `NullLogger<ShippingMethodMapper>.Instance`) and calls `Map` with the FR-1 null-GUID case, asserting `ShippingMethod.PickUp` (no logger verification needed/possible here).
   - All other tests (FR-1 no-log checks, FR-2, FR-3) use the two-argument constructor via `CreateMapper`.

No new interfaces, seams, or abstractions are introduced; the test targets `ShippingMethodMapper`'s existing public surface only (`ShippingMethodMapperTests` → `ShippingMethodMapper.Map`).

## Data Schemas

No new schemas. The suite exercises these existing, unmodified types:

- **`ShoptetInvoiceShippingDto`** (`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/IssuedInvoices/Model/ShoptetInvoiceShippingDto.cs`)
  ```csharp
  { string? Guid; string? Name; }
  ```
  JSON-deserialized from Shoptet's `guid`/`name` fields; constructed directly in-test (no JSON deserialization involved).

- **`ShoptetApiSettings`** (`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiSettings.cs`) — relevant member:
  ```csharp
  Dictionary<string, ShippingMethod> InvoiceShippingGuidMap
  ```
  Populated in-test via object initializer, wrapped in `IOptions<ShoptetApiSettings>` via `Options.Create(...)`. Note: the class also carries an unrelated `Dictionary<string, string> ShippingGuidMap` property, which the test helper never touches.

- **`ShippingMethod` enum** (`Anela.Heblo.Domain.Features.Invoices.ShippingMethod`):
  ```csharp
  PickUp = 0, PPL, PPLParcelShop, ZasilkovnaDoRuky, Zasilkovna, GLS
  ```
  Used as both the `[InlineData]` expected-value parameter (FR-2) and the fixed expected default (FR-1, FR-3).

- **Method under test:**
  ```csharp
  public class ShippingMethodMapper
  {
      public ShippingMethodMapper(IOptions<ShoptetApiSettings> settings);
      public ShippingMethodMapper(IOptions<ShoptetApiSettings> settings, ILogger<ShippingMethodMapper> logger);
      public ShippingMethod Map(ShoptetInvoiceShippingDto? shipping);
  }
  ```

No API request/response shapes, database schemas, or event payloads are involved — this is a pure in-memory unit test with no I/O (NFR-1, NFR-3).
