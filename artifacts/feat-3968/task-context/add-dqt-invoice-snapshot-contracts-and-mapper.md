### task: add-dqt-invoice-snapshot-contracts-and-mapper

**Goal**

Introduce the three DataQuality-owned invoice snapshot types (`DqtInvoiceSourceQuery`,
`DqtInvoiceSnapshot`, `DqtInvoiceItem`) and the provider-owned mapping helper
(`InvoiceDqtSnapshotMapper`) that converts Invoices-domain `IssuedInvoiceDetail`/
`IssuedInvoiceDetailItem` into them. This task is self-contained and does not yet touch
`IInvoiceShoptetSource`, `IInvoiceErpClient`, the two adapters' `GetAllAsync` bodies, or
`InvoiceDqtComparer` — it only adds new, currently-unused types and a mapper, so the rest of the
codebase keeps compiling and behaving exactly as before until task 2 wires them in.

**Context** (self-contained — the engineer only reads this section)

This repo's DTO rule: **DTOs are classes, never C# records** (OpenAPI client generators mishandle
record parameter order). The three new types are plain classes with `{ get; set; }` properties.

The Invoices-domain types being mapped from already exist, unmodified, at:

`backend/src/Anela.Heblo.Domain/Features/Invoices/IssuedInvoiceDetail.cs`:
```csharp
namespace Anela.Heblo.Domain.Features.Invoices;

public class IssuedInvoiceDetail
{
    public string Code { get; set; } = string.Empty;
    // ... OrderCode, CreationTime, ChangeTime, DueDate, TaxDate, AddressesEqual, VarSymbol,
    // ConstSymbol, SpecSymbol, BillingMethod, ShippingMethod, VatPayer, BillingAddress,
    // DeliveryAddress, Customer — all deliberately NOT mapped, DataQuality never reads them.
    public List<IssuedInvoiceDetailItem> Items { get; set; } = new();
    public InvoicePrice Price { get; set; } = new();
}
```

`backend/src/Anela.Heblo.Domain/Features/Invoices/IssuedInvoiceDetailItem.cs`:
```csharp
namespace Anela.Heblo.Domain.Features.Invoices
{
    public class IssuedInvoiceDetailItem
    {
        public string Code { get; set; }
        public string Name { get; set; }              // not mapped
        public string VariantName { get; set; }        // not mapped
        public decimal Amount { get; set; }
        public Guid? ProductGuid { get; set; }          // not mapped
        public string AmountUnit { get; set; }          // not mapped
        public InvoicePrice ItemPrice { get; set; }
        public InvoicePrice BuyPrice { get; set; }       // not mapped
        public bool IsNonStock { get; set; }             // not mapped
    }
}
```

`backend/src/Anela.Heblo.Domain/Features/Invoices/InvoicePrice.cs`:
```csharp
namespace Anela.Heblo.Domain.Features.Invoices
{
    public class InvoicePrice
    {
        public decimal Vat { get; set; }                 // not mapped
        public string CurrencyCode { get; set; } = "CZK"; // not mapped
        public decimal WithVat { get; set; }
        public decimal WithoutVat { get; set; }
        public decimal? ExchangeRate { get; set; }         // not mapped
        public string? VatRate { get; set; }               // not mapped
        public decimal TotalWithoutVat { get; set; }
        public decimal TotalWithVat { get; set; }
    }
}
```

Field mapping this task implements (verified by inspecting `InvoiceDqtComparer` — it reads exactly
these fields today, nothing else):
```
IssuedInvoiceDetail       →  DqtInvoiceSnapshot
  .Code                   →   .Code
  .Price.TotalWithVat     →   .TotalWithVat
  .Price.TotalWithoutVat  →   .TotalWithoutVat
  .Items[] (via ToDqtItem)→   .Items[]

IssuedInvoiceDetailItem   →  DqtInvoiceItem
  .Code                   →   .Code
  .Amount                 →   .Amount
  .ItemPrice.WithVat      →   .WithVat
  .ItemPrice.WithoutVat   →   .WithoutVat
```

Placement rule (from the design doc, Decision 3): the mapper is provider-owned code — it references
both the Invoices domain namespace (source) and DataQuality's contracts namespace (target), which
only provider-side code may do. It must live beside the two adapters it serves
(`Features/Invoices/Infrastructure/`), not in `Contracts/` and not in DataQuality's `Services/`
folder.

The `Anela.Heblo.Application` assembly already exposes its `internal` types to the test assembly via
`backend/src/Anela.Heblo.Application/AssemblyInfo.cs`:
```csharp
[assembly: InternalsVisibleTo("Anela.Heblo.Tests")]
```
so an `internal static class InvoiceDqtSnapshotMapper` is directly testable from
`Anela.Heblo.Tests` without any accessibility changes — this is the same pattern already used by
`InvoiceConsumptionSourceAdapter` (`internal sealed`, tested directly in
`backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceConsumptionSourceAdapterTests.cs`).

The test project (`backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`) has `xunit` as a global
`Using` (so `[Fact]` needs no explicit `using Xunit;`), but `FluentAssertions` and `Moq` are **not**
global — both need an explicit `using` in every test file that uses them.

**Files to create/modify/delete**

- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/DqtInvoiceSnapshot.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceDqtSnapshotMapper.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceDqtSnapshotMapperTests.cs`

**Implementation steps**

1. **Write the failing test first.** Create
   `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceDqtSnapshotMapperTests.cs`
   with this exact content (it references `DqtInvoiceSnapshot`, `DqtInvoiceItem`, and
   `InvoiceDqtSnapshotMapper`, none of which exist yet, so the project will fail to compile):

   ```csharp
   using Anela.Heblo.Application.Features.DataQuality.Contracts;
   using Anela.Heblo.Application.Features.Invoices.Infrastructure;
   using Anela.Heblo.Domain.Features.Invoices;
   using FluentAssertions;
   using Xunit;

   namespace Anela.Heblo.Tests.Features.Invoices.Infrastructure;

   public class InvoiceDqtSnapshotMapperTests
   {
       [Fact]
       public void ToDqtSnapshot_MapsInvoiceLevelFields()
       {
           var invoice = new IssuedInvoiceDetail
           {
               Code = "INV-100",
               Price = new InvoicePrice { TotalWithVat = 1210m, TotalWithoutVat = 1000m },
               Items = new List<IssuedInvoiceDetailItem>()
           };

           var snapshot = invoice.ToDqtSnapshot();

           snapshot.Code.Should().Be("INV-100");
           snapshot.TotalWithVat.Should().Be(1210m);
           snapshot.TotalWithoutVat.Should().Be(1000m);
           snapshot.Items.Should().BeEmpty();
       }

       [Fact]
       public void ToDqtSnapshot_MapsMultipleItems_WithoutSwappingWithVatAndWithoutVat()
       {
           var invoice = new IssuedInvoiceDetail
           {
               Code = "INV-101",
               Price = new InvoicePrice { TotalWithVat = 2420m, TotalWithoutVat = 2000m },
               Items = new List<IssuedInvoiceDetailItem>
               {
                   new IssuedInvoiceDetailItem
                   {
                       Code = "PROD-A",
                       Amount = 2m,
                       ItemPrice = new InvoicePrice { WithVat = 121m, WithoutVat = 100m },
                       BuyPrice = new InvoicePrice()
                   },
                   new IssuedInvoiceDetailItem
                   {
                       Code = "PROD-B",
                       Amount = 5m,
                       ItemPrice = new InvoicePrice { WithVat = 363m, WithoutVat = 300m },
                       BuyPrice = new InvoicePrice()
                   }
               }
           };

           var snapshot = invoice.ToDqtSnapshot();

           snapshot.Items.Should().HaveCount(2);

           snapshot.Items[0].Code.Should().Be("PROD-A");
           snapshot.Items[0].Amount.Should().Be(2m);
           snapshot.Items[0].WithVat.Should().Be(121m);
           snapshot.Items[0].WithoutVat.Should().Be(100m);

           snapshot.Items[1].Code.Should().Be("PROD-B");
           snapshot.Items[1].Amount.Should().Be(5m);
           snapshot.Items[1].WithVat.Should().Be(363m);
           snapshot.Items[1].WithoutVat.Should().Be(300m);
       }

       [Fact]
       public void ToDqtItem_MapsFieldsFromNestedItemPrice()
       {
           var item = new IssuedInvoiceDetailItem
           {
               Code = "PROD-C",
               Amount = 3m,
               ItemPrice = new InvoicePrice { WithVat = 121m, WithoutVat = 100m },
               BuyPrice = new InvoicePrice()
           };

           var dqtItem = item.ToDqtItem();

           dqtItem.Code.Should().Be("PROD-C");
           dqtItem.Amount.Should().Be(3m);
           dqtItem.WithVat.Should().Be(121m);
           dqtItem.WithoutVat.Should().Be(100m);
       }
   }
   ```

   Note the deliberately asymmetric sample values (`WithVat = 121m` vs. `WithoutVat = 100m`, never
   equal) — this guards against a `WithVat`↔`WithoutVat` field swap in the mapper, which two equal
   values would let slip through silently.

2. **Confirm it fails to compile.** From the repo root:

   ```bash
   dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
   ```

   Expected: build fails with `CS0246`/`CS1061`-style errors — `DqtInvoiceSnapshot`, `DqtInvoiceItem`,
   and the extension methods `ToDqtSnapshot`/`ToDqtItem` do not exist yet.

3. **Create the three DataQuality-owned types.** Create
   `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/DqtInvoiceSnapshot.cs`:

   ```csharp
   namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

   public class DqtInvoiceSourceQuery
   {
       public string RequestId { get; set; } = string.Empty;
       public DateOnly DateFrom { get; set; }
       public DateOnly DateTo { get; set; }
   }

   public class DqtInvoiceSnapshot
   {
       public string Code { get; set; } = string.Empty;
       public decimal TotalWithVat { get; set; }
       public decimal TotalWithoutVat { get; set; }
       public List<DqtInvoiceItem> Items { get; set; } = new();
   }

   public class DqtInvoiceItem
   {
       public string Code { get; set; } = string.Empty;
       public decimal Amount { get; set; }
       public decimal WithVat { get; set; }
       public decimal WithoutVat { get; set; }
   }
   ```

   This file must have zero `using` directives referencing `Anela.Heblo.Domain.Features.Invoices` —
   there are none needed, since the file only uses BCL types and its own classes.

4. **Create the mapper.** Create
   `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceDqtSnapshotMapper.cs`:

   ```csharp
   using Anela.Heblo.Application.Features.DataQuality.Contracts;
   using Anela.Heblo.Domain.Features.Invoices;

   namespace Anela.Heblo.Application.Features.Invoices.Infrastructure;

   /// <summary>
   /// Provider-owned mapping from Invoices domain types to DataQuality's consumer-owned
   /// snapshot contracts (DqtInvoiceSnapshot/DqtInvoiceItem). Shared by InvoiceShoptetSourceAdapter
   /// and InvoiceErpClientAdapter so the mapping is written once, not duplicated per adapter.
   /// </summary>
   internal static class InvoiceDqtSnapshotMapper
   {
       public static DqtInvoiceSnapshot ToDqtSnapshot(this IssuedInvoiceDetail invoice)
       {
           return new DqtInvoiceSnapshot
           {
               Code = invoice.Code,
               TotalWithVat = invoice.Price.TotalWithVat,
               TotalWithoutVat = invoice.Price.TotalWithoutVat,
               Items = invoice.Items.Select(ToDqtItem).ToList()
           };
       }

       public static DqtInvoiceItem ToDqtItem(this IssuedInvoiceDetailItem item)
       {
           return new DqtInvoiceItem
           {
               Code = item.Code,
               Amount = item.Amount,
               WithVat = item.ItemPrice.WithVat,
               WithoutVat = item.ItemPrice.WithoutVat
           };
       }
   }
   ```

5. **Run the test again and confirm it passes.**

   ```bash
   dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceDqtSnapshotMapperTests"
   ```

   Expected: build succeeds, `3` tests run, `3` passed, `0` failed.

6. **Commit.**

   ```bash
   git add backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/DqtInvoiceSnapshot.cs \
           backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceDqtSnapshotMapper.cs \
           backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceDqtSnapshotMapperTests.cs
   git commit -m "Add DataQuality-owned invoice snapshot types and Invoices-side mapper"
   ```

---
