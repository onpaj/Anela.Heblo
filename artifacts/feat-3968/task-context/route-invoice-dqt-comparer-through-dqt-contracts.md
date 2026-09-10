### task: route-invoice-dqt-comparer-through-dqt-contracts

**Goal**

Change `IInvoiceShoptetSource` and `IInvoiceErpClient` to return `DqtInvoiceSnapshot` instead of
Invoices-domain types, rewire `InvoiceShoptetSourceAdapter` and `InvoiceErpClientAdapter` to perform
the query/response mapping via `InvoiceDqtSnapshotMapper` (built in the previous task), rewrite
`InvoiceDqtComparer` to consume only `DqtInvoiceSnapshot`/`DqtInvoiceItem`/`DqtInvoiceSourceQuery`,
update the 14 existing `InvoiceDqtComparerTests.cs` cases to the new fixture types, and add new
adapter-level mapping tests. After this task, `Anela.Heblo.Application.Features.DataQuality.*` has
zero actual references to `Anela.Heblo.Domain.Features.Invoices` — only the architecture-test
allowlist (task 3) still needs to be closed to make that a hard gate.

**Context** (self-contained — the engineer only reads this section; assumes the previous task's
`DqtInvoiceSnapshot.cs` and `InvoiceDqtSnapshotMapper.cs` already exist as written above)

**Current state of the two interfaces**, both under
`backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/`:

`IInvoiceShoptetSource.cs`:
```csharp
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

/// <summary>
/// DataQuality-owned read contract over the Shoptet issued-invoice source.
/// Provider (Invoices) supplies an adapter — see InvoiceShoptetSourceAdapter.
/// </summary>
public interface IInvoiceShoptetSource
{
    Task<List<IssuedInvoiceDetailBatch>> GetAllAsync(
        IssuedInvoiceSourceQuery query,
        CancellationToken ct = default);
}
```

`IInvoiceErpClient.cs`:
```csharp
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

/// <summary>
/// DataQuality-owned read contract over the ERP issued-invoice client.
/// Provider (Invoices) supplies an adapter — see InvoiceErpClientAdapter.
/// </summary>
public interface IInvoiceErpClient
{
    Task<List<IssuedInvoiceDetail>> GetAllAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct);
}
```

**Current adapters**, both under
`backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/`, both `internal sealed`,
pure delegation today:

`InvoiceShoptetSourceAdapter.cs`:
```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure;

internal sealed class InvoiceShoptetSourceAdapter : IInvoiceShoptetSource
{
    private readonly IIssuedInvoiceSource _inner;

    public InvoiceShoptetSourceAdapter(IIssuedInvoiceSource inner)
    {
        _inner = inner;
    }

    public Task<List<IssuedInvoiceDetailBatch>> GetAllAsync(
        IssuedInvoiceSourceQuery query,
        CancellationToken ct = default)
        => _inner.GetAllAsync(query, ct);
}
```

`InvoiceErpClientAdapter.cs`:
```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure;

internal sealed class InvoiceErpClientAdapter : IInvoiceErpClient
{
    private readonly IIssuedInvoiceClient _inner;

    public InvoiceErpClientAdapter(IIssuedInvoiceClient inner)
    {
        _inner = inner;
    }

    public Task<List<IssuedInvoiceDetail>> GetAllAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
        => _inner.GetAllAsync(from, to, ct);
}
```

The `IIssuedInvoiceSource`/`IIssuedInvoiceClient` interfaces these wrap (unmodified by this task,
`backend/src/Anela.Heblo.Domain/Features/Invoices/`):
```csharp
public interface IIssuedInvoiceSource
{
    Task<List<IssuedInvoiceDetailBatch>> GetAllAsync(IssuedInvoiceSourceQuery query, CancellationToken cancellationToken = default);
    Task CommitAsync(IssuedInvoiceDetailBatch batch, string? commitMessage = default);
    Task FailAsync(IssuedInvoiceDetailBatch batch, string? errorMessage = default);
}

public interface IIssuedInvoiceClient
{
    Task<string?> SaveAsync(IssuedInvoiceDetail invoiceDetail, CancellationToken cancellationToken = default);
    Task<IssuedInvoiceDetail> GetAsync(string invoiceId, CancellationToken cancellationToken = default);
    Task<List<IssuedInvoiceDetail>> GetAllAsync(DateOnly from, DateOnly to, CancellationToken ct);
}
```

`IssuedInvoiceSourceQuery` (unmodified, `Anela.Heblo.Domain.Features.Invoices`):
```csharp
public class IssuedInvoiceSourceQuery
{
    public string RequestId { get; set; } = "undefined";
    public string? InvoiceId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string Currency { get; set; } = "CZK";
    // ... QueryByInvoice, QueryByDate, DateFromString, DateToString derived properties
}
```
Note its defaults: `InvoiceId` defaults to `null`, `Currency` defaults to `"CZK"` — so the adapter
does not need to set either explicitly to get the spec's required defaulting behavior.

`IssuedInvoiceDetailBatch` (unmodified):
```csharp
public class IssuedInvoiceDetailBatch
{
    public List<IssuedInvoiceDetail> Invoices { get; set; } = new();
    public string BatchId { get; set; } = string.Empty;
}
```

**Current `InvoiceDqtComparer.cs`** (full current content —
`backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtComparer.cs`):
```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

public class InvoiceDqtComparer : IInvoiceDqtComparer
{
    private const decimal Tolerance = 0.02m;

    private readonly IInvoiceShoptetSource _shoptetSource;
    private readonly IInvoiceErpClient _flexiClient;

    public InvoiceDqtComparer(IInvoiceShoptetSource shoptetSource, IInvoiceErpClient flexiClient)
    {
        _shoptetSource = shoptetSource;
        _flexiClient = flexiClient;
    }

    public async Task<InvoiceDqtComparisonResult> CompareAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var shoptetQuery = new IssuedInvoiceSourceQuery
        {
            RequestId = $"dqt-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}",
            DateFrom = from.ToDateTime(TimeOnly.MinValue),
            DateTo = to.ToDateTime(TimeOnly.MinValue)
        };

        var shoptetBatches = await _shoptetSource.GetAllAsync(shoptetQuery, ct);
        var shoptetInvoices = shoptetBatches.SelectMany(b => b.Invoices).ToList();

        var flexiInvoices = await _flexiClient.GetAllAsync(from, to, ct);

        var shoptetGroups = shoptetInvoices.GroupBy(i => i.Code).ToList();
        var flexiGroups = flexiInvoices.GroupBy(i => i.Code).ToList();
        var shoptetByCode = shoptetGroups.ToDictionary(g => g.Key, g => g.First());
        var flexiByCode = flexiGroups.ToDictionary(g => g.Key, g => g.First());
        var shoptetDupCounts = shoptetGroups.Where(g => g.Count() > 1).ToDictionary(g => g.Key, g => g.Count());
        var flexiDupCounts = flexiGroups.Where(g => g.Count() > 1).ToDictionary(g => g.Key, g => g.Count());

        var allCodes = shoptetByCode.Keys.Union(flexiByCode.Keys).ToHashSet();
        var mismatches = new List<InvoiceDqtMismatch>();

        foreach (var code in allCodes)
        {
            var inShoptet = shoptetByCode.TryGetValue(code, out var shoptetInvoice);
            var inFlexi = flexiByCode.TryGetValue(code, out var flexiInvoice);

            var duplicateDetail = BuildDuplicateDetail(code, shoptetDupCounts, flexiDupCounts);
            var duplicateFlag = duplicateDetail is null ? InvoiceMismatchType.None : InvoiceMismatchType.DuplicateInvoiceCode;

            if (inShoptet && !inFlexi)
            {
                mismatches.Add(new InvoiceDqtMismatch
                {
                    InvoiceCode = code,
                    MismatchType = InvoiceMismatchType.MissingInFlexi | duplicateFlag,
                    Details = duplicateDetail
                });
                continue;
            }

            if (!inShoptet && inFlexi)
            {
                mismatches.Add(new InvoiceDqtMismatch
                {
                    InvoiceCode = code,
                    MismatchType = InvoiceMismatchType.MissingInShoptet | duplicateFlag,
                    Details = duplicateDetail
                });
                continue;
            }

            var flags = duplicateFlag;
            string? shoptetVal = null;
            string? flexiVal = null;
            string? details = duplicateDetail;

            if (Math.Abs(shoptetInvoice!.Price.TotalWithVat - flexiInvoice!.Price.TotalWithVat) > Tolerance)
            {
                flags |= InvoiceMismatchType.TotalWithVatDiffers;
                shoptetVal = shoptetInvoice.Price.TotalWithVat.ToString("F2");
                flexiVal = flexiInvoice.Price.TotalWithVat.ToString("F2");
            }

            if (Math.Abs(shoptetInvoice.Price.TotalWithoutVat - flexiInvoice.Price.TotalWithoutVat) > Tolerance)
            {
                flags |= InvoiceMismatchType.TotalWithoutVatDiffers;
                shoptetVal ??= shoptetInvoice.Price.TotalWithoutVat.ToString("F2");
                flexiVal ??= flexiInvoice.Price.TotalWithoutVat.ToString("F2");
            }

            var itemDiff = CompareItems(shoptetInvoice.Items, flexiInvoice.Items);
            if (itemDiff != null)
            {
                flags |= InvoiceMismatchType.ItemsDiffer;
                details = details is null ? itemDiff : $"{details}; {itemDiff}";
            }

            if (flags != InvoiceMismatchType.None)
            {
                mismatches.Add(new InvoiceDqtMismatch
                {
                    InvoiceCode = code,
                    MismatchType = flags,
                    ShoptetValue = shoptetVal,
                    FlexiValue = flexiVal,
                    Details = details
                });
            }
        }

        return new InvoiceDqtComparisonResult
        {
            Mismatches = mismatches,
            TotalChecked = allCodes.Count
        };
    }

    private static string? CompareItems(List<IssuedInvoiceDetailItem> shoptetItems, List<IssuedInvoiceDetailItem> flexiItems)
    {
        var shoptetGroups = shoptetItems
            .Where(i => !string.IsNullOrEmpty(i.Code))
            .GroupBy(i => i.Code)
            .ToList();
        var flexiGroups = flexiItems
            .Where(i => !string.IsNullOrEmpty(i.Code))
            .GroupBy(i => i.Code)
            .ToList();
        var shoptetByCode = shoptetGroups.ToDictionary(g => g.Key, g => g.First());
        var flexiByCode = flexiGroups.ToDictionary(g => g.Key, g => g.First());
        var allCodes = shoptetByCode.Keys.Union(flexiByCode.Keys);

        var diffs = new List<string>();

        foreach (var g in shoptetGroups.Where(g => g.Count() > 1))
            diffs.Add($"Item {g.Key}: duplicated in shoptet (x{g.Count()})");
        foreach (var g in flexiGroups.Where(g => g.Count() > 1))
            diffs.Add($"Item {g.Key}: duplicated in flexi (x{g.Count()})");

        foreach (var code in allCodes)
        {
            var inShoptet = shoptetByCode.TryGetValue(code, out var sItem);
            var inFlexi = flexiByCode.TryGetValue(code, out var fItem);

            if (inShoptet && !inFlexi)
            {
                diffs.Add($"Item {code}: missing in Flexi");
                continue;
            }

            if (!inShoptet && inFlexi)
            {
                diffs.Add($"Item {code}: missing in Shoptet");
                continue;
            }

            if (sItem!.Amount != fItem!.Amount)
                diffs.Add($"Item {code}: Amount shoptet={sItem.Amount} flexi={fItem.Amount}");

            if (Math.Abs(sItem.ItemPrice.WithVat - fItem.ItemPrice.WithVat) > Tolerance)
                diffs.Add($"Item {code}: WithVat shoptet={sItem.ItemPrice.WithVat:F2} flexi={fItem.ItemPrice.WithVat:F2}");

            if (Math.Abs(sItem.ItemPrice.WithoutVat - fItem.ItemPrice.WithoutVat) > Tolerance)
                diffs.Add($"Item {code}: WithoutVat shoptet={sItem.ItemPrice.WithoutVat:F2} flexi={fItem.ItemPrice.WithoutVat:F2}");
        }

        return diffs.Count > 0 ? string.Join("; ", diffs) : null;
    }

    private static string? BuildDuplicateDetail(
        string code,
        IReadOnlyDictionary<string, int> shoptetDupCounts,
        IReadOnlyDictionary<string, int> flexiDupCounts)
    {
        var parts = new List<string>();
        if (shoptetDupCounts.TryGetValue(code, out var shoptetCount))
            parts.Add($"shoptet (x{shoptetCount})");
        if (flexiDupCounts.TryGetValue(code, out var flexiCount))
            parts.Add($"flexi (x{flexiCount})");

        return parts.Count > 0 ? $"Duplicate invoice code in {string.Join(", ", parts)}" : null;
    }
}
```
The tolerance-based total/item diffing, duplicate-code detection/grouping, and message formats are
all unchanged by this task — **only the types flowing through it change** (`IssuedInvoiceDetail` →
`DqtInvoiceSnapshot`, `IssuedInvoiceDetailItem` → `DqtInvoiceItem`, `IssuedInvoiceSourceQuery` →
`DqtInvoiceSourceQuery`), plus the `SelectMany(b => b.Invoices)` flatten and the
`from.ToDateTime(TimeOnly.MinValue)` conversion both disappear from this file (they move into
`InvoiceShoptetSourceAdapter`).

**Current `InvoiceDqtComparerTests.cs`** (full current content —
`backend/test/Anela.Heblo.Tests/Features/DataQuality/InvoiceDqtComparerTests.cs`) has 14 `[Fact]`
methods, all unchanged in this task except the two helper methods (`MakeInvoice`, `MakeItem`) and
the two setup methods (`SetupShoptet`, `SetupFlexi`), which build/mock the new types instead:
`BothEmpty_ReturnsZeroCheckedZeroMismatches`, `InvoiceInShoptetOnly_FlagsMissingInFlexi`,
`InvoiceInFlexiOnly_FlagsMissingInShoptet`, `MatchingInvoices_ReturnsZeroMismatches`,
`WithinTolerance_NoMismatch`, `RoundingDifferenceUnderHalfCrown_NoMismatch`,
`WithVatDiffers_FlagsTotalWithVatDiffers`, `WithoutVatDiffers_FlagsTotalWithoutVatDiffers`,
`ItemsDiffer_ByProductCode`, `ItemsDiffer_ByAmount`, `ItemPriceDiffers`, `MultipleIssues_CombinesFlags`,
`DuplicateShoptetInvoiceCode_DoesNotThrow_AndFlagsDuplicate`,
`DuplicateFlexiInvoiceCode_DoesNotThrow_AndFlagsDuplicate`,
`DuplicateItemCodeWithinInvoice_DoesNotThrow_AndReportsDuplicate`.

**DI registration** for both adapters lives in
`backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs`:
```csharp
services.AddSingleton<IInvoiceShoptetSource, InvoiceShoptetSourceAdapter>();
services.AddScoped<IInvoiceErpClient, InvoiceErpClientAdapter>();
```
This task does **not** touch `InvoicesModule.cs` — lifetimes and registration are unaffected by a
signature-only interface change on an already-registered pair.

**Files to create/modify/delete**

- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IInvoiceShoptetSource.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IInvoiceErpClient.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtComparer.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceShoptetSourceAdapter.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceErpClientAdapter.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/DataQuality/InvoiceDqtComparerTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceShoptetSourceAdapterTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceErpClientAdapterTests.cs`

**Implementation steps**

1. **Update the existing comparer test fixtures to the new types first (red step).** Replace
   `backend/test/Anela.Heblo.Tests/Features/DataQuality/InvoiceDqtComparerTests.cs` in full:

   ```csharp
   using Anela.Heblo.Application.Features.DataQuality.Contracts;
   using Anela.Heblo.Application.Features.DataQuality.Services;
   using Anela.Heblo.Domain.Features.DataQuality;
   using Moq;

   namespace Anela.Heblo.Tests.Features.DataQuality;

   public class InvoiceDqtComparerTests
   {
       private readonly Mock<IInvoiceShoptetSource> _sourceMock = new();
       private readonly Mock<IInvoiceErpClient> _clientMock = new();
       private readonly InvoiceDqtComparer _sut;

       private static readonly DateOnly From = new(2026, 1, 1);
       private static readonly DateOnly To = new(2026, 1, 31);

       public InvoiceDqtComparerTests()
       {
           _sut = new InvoiceDqtComparer(_sourceMock.Object, _clientMock.Object);
       }

       private void SetupShoptet(params DqtInvoiceSnapshot[] invoices)
       {
           _sourceMock.Setup(s => s.GetAllAsync(It.IsAny<DqtInvoiceSourceQuery>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(invoices.ToList());
       }

       private void SetupFlexi(params DqtInvoiceSnapshot[] invoices)
       {
           _clientMock.Setup(c => c.GetAllAsync(From, To, It.IsAny<CancellationToken>()))
               .ReturnsAsync(invoices.ToList());
       }

       private static DqtInvoiceSnapshot MakeInvoice(string code, decimal totalWithVat = 100m, decimal totalWithoutVat = 80m, List<DqtInvoiceItem>? items = null)
       {
           return new DqtInvoiceSnapshot
           {
               Code = code,
               TotalWithVat = totalWithVat,
               TotalWithoutVat = totalWithoutVat,
               Items = items ?? new List<DqtInvoiceItem>()
           };
       }

       private static DqtInvoiceItem MakeItem(string code, decimal amount = 1m, decimal withVat = 100m, decimal withoutVat = 80m)
       {
           return new DqtInvoiceItem
           {
               Code = code,
               Amount = amount,
               WithVat = withVat,
               WithoutVat = withoutVat
           };
       }

       [Fact]
       public async Task BothEmpty_ReturnsZeroCheckedZeroMismatches()
       {
           SetupShoptet();
           SetupFlexi();

           var result = await _sut.CompareAsync(From, To);

           Assert.Equal(0, result.TotalChecked);
           Assert.Empty(result.Mismatches);
       }

       [Fact]
       public async Task InvoiceInShoptetOnly_FlagsMissingInFlexi()
       {
           SetupShoptet(MakeInvoice("INV-001"));
           SetupFlexi();

           var result = await _sut.CompareAsync(From, To);

           Assert.Single(result.Mismatches);
           Assert.Equal("INV-001", result.Mismatches[0].InvoiceCode);
           Assert.Equal(InvoiceMismatchType.MissingInFlexi, result.Mismatches[0].MismatchType);
       }

       [Fact]
       public async Task InvoiceInFlexiOnly_FlagsMissingInShoptet()
       {
           SetupShoptet();
           SetupFlexi(MakeInvoice("INV-002"));

           var result = await _sut.CompareAsync(From, To);

           Assert.Single(result.Mismatches);
           Assert.Equal("INV-002", result.Mismatches[0].InvoiceCode);
           Assert.Equal(InvoiceMismatchType.MissingInShoptet, result.Mismatches[0].MismatchType);
       }

       [Fact]
       public async Task MatchingInvoices_ReturnsZeroMismatches()
       {
           var inv = MakeInvoice("INV-003");
           SetupShoptet(inv);
           SetupFlexi(MakeInvoice("INV-003"));

           var result = await _sut.CompareAsync(From, To);

           Assert.Equal(1, result.TotalChecked);
           Assert.Empty(result.Mismatches);
       }

       [Fact]
       public async Task WithinTolerance_NoMismatch()
       {
           SetupShoptet(MakeInvoice("INV-004", totalWithVat: 100.00m, totalWithoutVat: 80.00m));
           SetupFlexi(MakeInvoice("INV-004", totalWithVat: 100.01m, totalWithoutVat: 80.02m));

           var result = await _sut.CompareAsync(From, To);

           Assert.Empty(result.Mismatches);
       }

       [Fact]
       public async Task RoundingDifferenceUnderHalfCrown_NoMismatch()
       {
           // After the toPay fix: Shoptet mapper uses toPay (14322.00) so both sides agree.
           // This was previously a false "Celkem s DPH" mismatch (Shoptet=14321.5, Flexi=14322).
           SetupShoptet(MakeInvoice("INV-126010118", totalWithVat: 14322.00m, totalWithoutVat: 11835.93m));
           SetupFlexi(MakeInvoice("INV-126010118", totalWithVat: 14322.00m, totalWithoutVat: 11835.93m));

           var result = await _sut.CompareAsync(From, To);

           Assert.Empty(result.Mismatches);
       }

       [Fact]
       public async Task WithVatDiffers_FlagsTotalWithVatDiffers()
       {
           SetupShoptet(MakeInvoice("INV-005", totalWithVat: 100.00m));
           SetupFlexi(MakeInvoice("INV-005", totalWithVat: 110.00m));

           var result = await _sut.CompareAsync(From, To);

           Assert.Single(result.Mismatches);
           Assert.True(result.Mismatches[0].MismatchType.HasFlag(InvoiceMismatchType.TotalWithVatDiffers));
       }

       [Fact]
       public async Task WithoutVatDiffers_FlagsTotalWithoutVatDiffers()
       {
           SetupShoptet(MakeInvoice("INV-006", totalWithoutVat: 80.00m));
           SetupFlexi(MakeInvoice("INV-006", totalWithoutVat: 90.00m));

           var result = await _sut.CompareAsync(From, To);

           Assert.Single(result.Mismatches);
           Assert.True(result.Mismatches[0].MismatchType.HasFlag(InvoiceMismatchType.TotalWithoutVatDiffers));
       }

       [Fact]
       public async Task ItemsDiffer_ByProductCode()
       {
           var shoptetItems = new List<DqtInvoiceItem> { MakeItem("PROD-A"), MakeItem("PROD-B") };
           var flexiItems = new List<DqtInvoiceItem> { MakeItem("PROD-A") };

           SetupShoptet(MakeInvoice("INV-007", items: shoptetItems));
           SetupFlexi(MakeInvoice("INV-007", items: flexiItems));

           var result = await _sut.CompareAsync(From, To);

           Assert.Single(result.Mismatches);
           Assert.True(result.Mismatches[0].MismatchType.HasFlag(InvoiceMismatchType.ItemsDiffer));
           Assert.Contains("PROD-B", result.Mismatches[0].Details);
       }

       [Fact]
       public async Task ItemsDiffer_ByAmount()
       {
           var shoptetItems = new List<DqtInvoiceItem> { MakeItem("PROD-C", amount: 2m) };
           var flexiItems = new List<DqtInvoiceItem> { MakeItem("PROD-C", amount: 3m) };

           SetupShoptet(MakeInvoice("INV-008", items: shoptetItems));
           SetupFlexi(MakeInvoice("INV-008", items: flexiItems));

           var result = await _sut.CompareAsync(From, To);

           Assert.Single(result.Mismatches);
           Assert.True(result.Mismatches[0].MismatchType.HasFlag(InvoiceMismatchType.ItemsDiffer));
           Assert.Contains("Amount", result.Mismatches[0].Details);
       }

       [Fact]
       public async Task ItemPriceDiffers()
       {
           var shoptetItems = new List<DqtInvoiceItem> { MakeItem("PROD-D", withVat: 50m) };
           var flexiItems = new List<DqtInvoiceItem> { MakeItem("PROD-D", withVat: 60m) };

           SetupShoptet(MakeInvoice("INV-009", items: shoptetItems));
           SetupFlexi(MakeInvoice("INV-009", items: flexiItems));

           var result = await _sut.CompareAsync(From, To);

           Assert.Single(result.Mismatches);
           Assert.True(result.Mismatches[0].MismatchType.HasFlag(InvoiceMismatchType.ItemsDiffer));
           Assert.Contains("WithVat", result.Mismatches[0].Details);
       }

       [Fact]
       public async Task MultipleIssues_CombinesFlags()
       {
           // INV-010: missing in Flexi
           // INV-011: total mismatch + item diff
           var shoptetItems = new List<DqtInvoiceItem> { MakeItem("PROD-X", withVat: 50m) };
           var flexiItems = new List<DqtInvoiceItem> { MakeItem("PROD-X", withVat: 60m) };

           SetupShoptet(
               MakeInvoice("INV-010"),
               MakeInvoice("INV-011", totalWithVat: 100m, items: shoptetItems));
           SetupFlexi(
               MakeInvoice("INV-011", totalWithVat: 200m, items: flexiItems));

           var result = await _sut.CompareAsync(From, To);

           Assert.Equal(2, result.TotalChecked);
           Assert.Equal(2, result.Mismatches.Count);

           var missing = result.Mismatches.Single(m => m.InvoiceCode == "INV-010");
           Assert.Equal(InvoiceMismatchType.MissingInFlexi, missing.MismatchType);

           var combined = result.Mismatches.Single(m => m.InvoiceCode == "INV-011");
           Assert.True(combined.MismatchType.HasFlag(InvoiceMismatchType.TotalWithVatDiffers));
           Assert.True(combined.MismatchType.HasFlag(InvoiceMismatchType.ItemsDiffer));
       }

       [Fact]
       public async Task DuplicateShoptetInvoiceCode_DoesNotThrow_AndFlagsDuplicate()
       {
           // Production crash: Shoptet returned invoice code 126013089 twice → ToDictionary threw.
           SetupShoptet(MakeInvoice("126013089"), MakeInvoice("126013089"));
           SetupFlexi(MakeInvoice("126013089"));

           var result = await _sut.CompareAsync(From, To);

           Assert.Equal(1, result.TotalChecked);
           var mismatch = Assert.Single(result.Mismatches);
           Assert.Equal("126013089", mismatch.InvoiceCode);
           Assert.True(mismatch.MismatchType.HasFlag(InvoiceMismatchType.DuplicateInvoiceCode));
           Assert.Contains("shoptet", mismatch.Details);
       }

       [Fact]
       public async Task DuplicateFlexiInvoiceCode_DoesNotThrow_AndFlagsDuplicate()
       {
           SetupShoptet(MakeInvoice("INV-DUP"));
           SetupFlexi(MakeInvoice("INV-DUP"), MakeInvoice("INV-DUP"));

           var result = await _sut.CompareAsync(From, To);

           var mismatch = Assert.Single(result.Mismatches);
           Assert.Equal("INV-DUP", mismatch.InvoiceCode);
           Assert.True(mismatch.MismatchType.HasFlag(InvoiceMismatchType.DuplicateInvoiceCode));
           Assert.Contains("flexi", mismatch.Details);
       }

       [Fact]
       public async Task DuplicateItemCodeWithinInvoice_DoesNotThrow_AndReportsDuplicate()
       {
           // Production crash: duplicate product codes (e.g. BAL0005M) within an invoice → ToDictionary threw.
           var shoptetItems = new List<DqtInvoiceItem> { MakeItem("BAL0005M"), MakeItem("BAL0005M") };
           var flexiItems = new List<DqtInvoiceItem> { MakeItem("BAL0005M") };

           SetupShoptet(MakeInvoice("INV-ITEMDUP", items: shoptetItems));
           SetupFlexi(MakeInvoice("INV-ITEMDUP", items: flexiItems));

           var result = await _sut.CompareAsync(From, To);

           var mismatch = Assert.Single(result.Mismatches);
           Assert.True(mismatch.MismatchType.HasFlag(InvoiceMismatchType.ItemsDiffer));
           Assert.Contains("BAL0005M", mismatch.Details);
       }
   }
   ```

2. **Confirm the whole solution now fails to build** (the interfaces/adapters/comparer still use the
   old types, so the new test file's mock setups and constructor calls no longer match):

   ```bash
   dotnet build
   ```

   Expected: build fails — `IInvoiceShoptetSource`/`IInvoiceErpClient` still declare the old
   signatures, so `Mock<IInvoiceShoptetSource>.Setup(s => s.GetAllAsync(It.IsAny<DqtInvoiceSourceQuery>(), ...))`
   does not match any member on the interface (`CS1929`/`CS0411`-style errors).

3. **Update `IInvoiceShoptetSource.cs`** — full replacement:

   ```csharp
   namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

   /// <summary>
   /// DataQuality-owned read contract over the Shoptet issued-invoice source.
   /// Provider (Invoices) supplies an adapter — see InvoiceShoptetSourceAdapter.
   /// </summary>
   public interface IInvoiceShoptetSource
   {
       Task<List<DqtInvoiceSnapshot>> GetAllAsync(
           DqtInvoiceSourceQuery query,
           CancellationToken ct = default);
   }
   ```

   (No `using` needed — `DqtInvoiceSnapshot`/`DqtInvoiceSourceQuery` are in the same
   `Anela.Heblo.Application.Features.DataQuality.Contracts` namespace as this file. The
   `using Anela.Heblo.Domain.Features.Invoices;` line is simply removed.)

4. **Update `IInvoiceErpClient.cs`** — full replacement:

   ```csharp
   namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

   /// <summary>
   /// DataQuality-owned read contract over the ERP issued-invoice client.
   /// Provider (Invoices) supplies an adapter — see InvoiceErpClientAdapter.
   /// </summary>
   public interface IInvoiceErpClient
   {
       Task<List<DqtInvoiceSnapshot>> GetAllAsync(
           DateOnly from,
           DateOnly to,
           CancellationToken ct);
   }
   ```

5. **Update `InvoiceDqtComparer.cs`** — full replacement:

   ```csharp
   using Anela.Heblo.Application.Features.DataQuality.Contracts;
   using Anela.Heblo.Domain.Features.DataQuality;

   namespace Anela.Heblo.Application.Features.DataQuality.Services;

   public class InvoiceDqtComparer : IInvoiceDqtComparer
   {
       private const decimal Tolerance = 0.02m;

       private readonly IInvoiceShoptetSource _shoptetSource;
       private readonly IInvoiceErpClient _flexiClient;

       public InvoiceDqtComparer(IInvoiceShoptetSource shoptetSource, IInvoiceErpClient flexiClient)
       {
           _shoptetSource = shoptetSource;
           _flexiClient = flexiClient;
       }

       public async Task<InvoiceDqtComparisonResult> CompareAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
       {
           var shoptetQuery = new DqtInvoiceSourceQuery
           {
               RequestId = $"dqt-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}",
               DateFrom = from,
               DateTo = to
           };

           var shoptetInvoices = await _shoptetSource.GetAllAsync(shoptetQuery, ct);

           var flexiInvoices = await _flexiClient.GetAllAsync(from, to, ct);

           // Invoice codes are expected to be unique, but a source occasionally returns the same
           // code twice (e.g. paginated batch overlap). That is itself a data-quality finding —
           // group instead of ToDictionary so a duplicate is reported, not a fatal crash.
           var shoptetGroups = shoptetInvoices.GroupBy(i => i.Code).ToList();
           var flexiGroups = flexiInvoices.GroupBy(i => i.Code).ToList();
           var shoptetByCode = shoptetGroups.ToDictionary(g => g.Key, g => g.First());
           var flexiByCode = flexiGroups.ToDictionary(g => g.Key, g => g.First());
           var shoptetDupCounts = shoptetGroups.Where(g => g.Count() > 1).ToDictionary(g => g.Key, g => g.Count());
           var flexiDupCounts = flexiGroups.Where(g => g.Count() > 1).ToDictionary(g => g.Key, g => g.Count());

           var allCodes = shoptetByCode.Keys.Union(flexiByCode.Keys).ToHashSet();
           var mismatches = new List<InvoiceDqtMismatch>();

           foreach (var code in allCodes)
           {
               var inShoptet = shoptetByCode.TryGetValue(code, out var shoptetInvoice);
               var inFlexi = flexiByCode.TryGetValue(code, out var flexiInvoice);

               var duplicateDetail = BuildDuplicateDetail(code, shoptetDupCounts, flexiDupCounts);
               var duplicateFlag = duplicateDetail is null ? InvoiceMismatchType.None : InvoiceMismatchType.DuplicateInvoiceCode;

               if (inShoptet && !inFlexi)
               {
                   mismatches.Add(new InvoiceDqtMismatch
                   {
                       InvoiceCode = code,
                       MismatchType = InvoiceMismatchType.MissingInFlexi | duplicateFlag,
                       Details = duplicateDetail
                   });
                   continue;
               }

               if (!inShoptet && inFlexi)
               {
                   mismatches.Add(new InvoiceDqtMismatch
                   {
                       InvoiceCode = code,
                       MismatchType = InvoiceMismatchType.MissingInShoptet | duplicateFlag,
                       Details = duplicateDetail
                   });
                   continue;
               }

               // Both exist — compare
               var flags = duplicateFlag;
               string? shoptetVal = null;
               string? flexiVal = null;
               string? details = duplicateDetail;

               if (Math.Abs(shoptetInvoice!.TotalWithVat - flexiInvoice!.TotalWithVat) > Tolerance)
               {
                   flags |= InvoiceMismatchType.TotalWithVatDiffers;
                   shoptetVal = shoptetInvoice.TotalWithVat.ToString("F2");
                   flexiVal = flexiInvoice.TotalWithVat.ToString("F2");
               }

               if (Math.Abs(shoptetInvoice.TotalWithoutVat - flexiInvoice.TotalWithoutVat) > Tolerance)
               {
                   flags |= InvoiceMismatchType.TotalWithoutVatDiffers;
                   shoptetVal ??= shoptetInvoice.TotalWithoutVat.ToString("F2");
                   flexiVal ??= flexiInvoice.TotalWithoutVat.ToString("F2");
               }

               var itemDiff = CompareItems(shoptetInvoice.Items, flexiInvoice.Items);
               if (itemDiff != null)
               {
                   flags |= InvoiceMismatchType.ItemsDiffer;
                   details = details is null ? itemDiff : $"{details}; {itemDiff}";
               }

               if (flags != InvoiceMismatchType.None)
               {
                   mismatches.Add(new InvoiceDqtMismatch
                   {
                       InvoiceCode = code,
                       MismatchType = flags,
                       ShoptetValue = shoptetVal,
                       FlexiValue = flexiVal,
                       Details = details
                   });
               }
           }

           return new InvoiceDqtComparisonResult
           {
               Mismatches = mismatches,
               TotalChecked = allCodes.Count
           };
       }

       private static string? CompareItems(List<DqtInvoiceItem> shoptetItems, List<DqtInvoiceItem> flexiItems)
       {
           // Items without a product code (unidentifiable shipping/billing/discount lines) cannot
           // be matched cross-system — skip them to avoid duplicate-key crashes.
           var shoptetGroups = shoptetItems
               .Where(i => !string.IsNullOrEmpty(i.Code))
               .GroupBy(i => i.Code)
               .ToList();
           var flexiGroups = flexiItems
               .Where(i => !string.IsNullOrEmpty(i.Code))
               .GroupBy(i => i.Code)
               .ToList();
           var shoptetByCode = shoptetGroups.ToDictionary(g => g.Key, g => g.First());
           var flexiByCode = flexiGroups.ToDictionary(g => g.Key, g => g.First());
           var allCodes = shoptetByCode.Keys.Union(flexiByCode.Keys);

           var diffs = new List<string>();

           // A product code appearing more than once within one invoice is itself a finding —
           // report it rather than letting ToDictionary throw.
           foreach (var g in shoptetGroups.Where(g => g.Count() > 1))
               diffs.Add($"Item {g.Key}: duplicated in shoptet (x{g.Count()})");
           foreach (var g in flexiGroups.Where(g => g.Count() > 1))
               diffs.Add($"Item {g.Key}: duplicated in flexi (x{g.Count()})");

           foreach (var code in allCodes)
           {
               var inShoptet = shoptetByCode.TryGetValue(code, out var sItem);
               var inFlexi = flexiByCode.TryGetValue(code, out var fItem);

               if (inShoptet && !inFlexi)
               {
                   diffs.Add($"Item {code}: missing in Flexi");
                   continue;
               }

               if (!inShoptet && inFlexi)
               {
                   diffs.Add($"Item {code}: missing in Shoptet");
                   continue;
               }

               if (sItem!.Amount != fItem!.Amount)
                   diffs.Add($"Item {code}: Amount shoptet={sItem.Amount} flexi={fItem.Amount}");

               if (Math.Abs(sItem.WithVat - fItem.WithVat) > Tolerance)
                   diffs.Add($"Item {code}: WithVat shoptet={sItem.WithVat:F2} flexi={fItem.WithVat:F2}");

               if (Math.Abs(sItem.WithoutVat - fItem.WithoutVat) > Tolerance)
                   diffs.Add($"Item {code}: WithoutVat shoptet={sItem.WithoutVat:F2} flexi={fItem.WithoutVat:F2}");
           }

           return diffs.Count > 0 ? string.Join("; ", diffs) : null;
       }

       private static string? BuildDuplicateDetail(
           string code,
           IReadOnlyDictionary<string, int> shoptetDupCounts,
           IReadOnlyDictionary<string, int> flexiDupCounts)
       {
           var parts = new List<string>();
           if (shoptetDupCounts.TryGetValue(code, out var shoptetCount))
               parts.Add($"shoptet (x{shoptetCount})");
           if (flexiDupCounts.TryGetValue(code, out var flexiCount))
               parts.Add($"flexi (x{flexiCount})");

           return parts.Count > 0 ? $"Duplicate invoice code in {string.Join(", ", parts)}" : null;
       }
   }
   ```

   Note the two behavior-preserving detail changes required by the type swap: `shoptetInvoice.Price.TotalWithVat`
   becomes `shoptetInvoice.TotalWithVat` (no more `.Price` nesting — `DqtInvoiceSnapshot` has the
   total fields directly), and `sItem.ItemPrice.WithVat` becomes `sItem.WithVat` (same reason for
   `DqtInvoiceItem`). No other logic changes.

6. **Update `InvoiceShoptetSourceAdapter.cs`** — full replacement:

   ```csharp
   using Anela.Heblo.Application.Features.DataQuality.Contracts;
   using Anela.Heblo.Domain.Features.Invoices;

   namespace Anela.Heblo.Application.Features.Invoices.Infrastructure;

   /// <summary>
   /// Provider-side adapter binding the DataQuality contract IInvoiceShoptetSource
   /// to the Invoices-module IIssuedInvoiceSource. Maps DataQuality's DqtInvoiceSourceQuery
   /// to IssuedInvoiceSourceQuery, flattens the batch response, and maps each invoice to
   /// DqtInvoiceSnapshot via InvoiceDqtSnapshotMapper.
   /// </summary>
   internal sealed class InvoiceShoptetSourceAdapter : IInvoiceShoptetSource
   {
       private readonly IIssuedInvoiceSource _inner;

       public InvoiceShoptetSourceAdapter(IIssuedInvoiceSource inner)
       {
           _inner = inner;
       }

       public async Task<List<DqtInvoiceSnapshot>> GetAllAsync(
           DqtInvoiceSourceQuery query,
           CancellationToken ct = default)
       {
           var innerQuery = new IssuedInvoiceSourceQuery
           {
               RequestId = query.RequestId,
               DateFrom = query.DateFrom.ToDateTime(TimeOnly.MinValue),
               DateTo = query.DateTo.ToDateTime(TimeOnly.MinValue)
           };

           var batches = await _inner.GetAllAsync(innerQuery, ct);

           return batches
               .SelectMany(b => b.Invoices)
               .Select(InvoiceDqtSnapshotMapper.ToDqtSnapshot)
               .ToList();
       }
   }
   ```

   `innerQuery.InvoiceId` and `innerQuery.Currency` are deliberately left unset — `IssuedInvoiceSourceQuery`
   defaults them to `null` and `"CZK"` respectively, which is exactly the spec's required defaulting.

7. **Update `InvoiceErpClientAdapter.cs`** — full replacement:

   ```csharp
   using Anela.Heblo.Application.Features.DataQuality.Contracts;
   using Anela.Heblo.Domain.Features.Invoices;

   namespace Anela.Heblo.Application.Features.Invoices.Infrastructure;

   /// <summary>
   /// Provider-side adapter binding the DataQuality contract IInvoiceErpClient
   /// to the Invoices-module IIssuedInvoiceClient. Maps each returned IssuedInvoiceDetail
   /// to DqtInvoiceSnapshot via InvoiceDqtSnapshotMapper.
   /// </summary>
   internal sealed class InvoiceErpClientAdapter : IInvoiceErpClient
   {
       private readonly IIssuedInvoiceClient _inner;

       public InvoiceErpClientAdapter(IIssuedInvoiceClient inner)
       {
           _inner = inner;
       }

       public async Task<List<DqtInvoiceSnapshot>> GetAllAsync(
           DateOnly from,
           DateOnly to,
           CancellationToken ct)
       {
           var invoices = await _inner.GetAllAsync(from, to, ct);

           return invoices
               .Select(InvoiceDqtSnapshotMapper.ToDqtSnapshot)
               .ToList();
       }
   }
   ```

8. **Build and confirm it now compiles.**

   ```bash
   dotnet build
   ```

   Expected: `Build succeeded.`

9. **Run the comparer tests and confirm all 14 pass.**

   ```bash
   dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceDqtComparerTests"
   ```

   Expected: `14` tests run, `14` passed, `0` failed.

10. **Write the new adapter-level mapping tests.** Create
    `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceShoptetSourceAdapterTests.cs`:

    ```csharp
    using Anela.Heblo.Application.Features.DataQuality.Contracts;
    using Anela.Heblo.Application.Features.Invoices.Infrastructure;
    using Anela.Heblo.Domain.Features.Invoices;
    using FluentAssertions;
    using Moq;
    using Xunit;

    namespace Anela.Heblo.Tests.Features.Invoices.Infrastructure;

    public class InvoiceShoptetSourceAdapterTests
    {
        private readonly Mock<IIssuedInvoiceSource> _inner = new();

        private InvoiceShoptetSourceAdapter CreateAdapter() => new(_inner.Object);

        [Fact]
        public async Task GetAllAsync_MapsQuery_DateOnlyToDateTime_AndPassesRequestId()
        {
            IssuedInvoiceSourceQuery? captured = null;
            _inner
                .Setup(s => s.GetAllAsync(It.IsAny<IssuedInvoiceSourceQuery>(), It.IsAny<CancellationToken>()))
                .Callback<IssuedInvoiceSourceQuery, CancellationToken>((q, _) => captured = q)
                .ReturnsAsync(new List<IssuedInvoiceDetailBatch>());

            var query = new DqtInvoiceSourceQuery
            {
                RequestId = "dqt-2026-01-01-2026-01-31",
                DateFrom = new DateOnly(2026, 1, 1),
                DateTo = new DateOnly(2026, 1, 31)
            };

            var adapter = CreateAdapter();
            await adapter.GetAllAsync(query, CancellationToken.None);

            captured.Should().NotBeNull();
            captured!.RequestId.Should().Be("dqt-2026-01-01-2026-01-31");
            captured.DateFrom.Should().Be(new DateTime(2026, 1, 1));
            captured.DateTo.Should().Be(new DateTime(2026, 1, 31));
            captured.InvoiceId.Should().BeNull();
            captured.Currency.Should().Be("CZK");
        }

        [Fact]
        public async Task GetAllAsync_FlattensBatchesAndMapsToDqtSnapshot()
        {
            var batch1 = new IssuedInvoiceDetailBatch
            {
                BatchId = "batch-1",
                Invoices = new List<IssuedInvoiceDetail>
                {
                    new IssuedInvoiceDetail
                    {
                        Code = "INV-200",
                        Price = new InvoicePrice { TotalWithVat = 1210m, TotalWithoutVat = 1000m },
                        Items = new List<IssuedInvoiceDetailItem>
                        {
                            new IssuedInvoiceDetailItem
                            {
                                Code = "PROD-A",
                                Amount = 2m,
                                ItemPrice = new InvoicePrice { WithVat = 121m, WithoutVat = 100m },
                                BuyPrice = new InvoicePrice()
                            }
                        }
                    }
                }
            };
            var batch2 = new IssuedInvoiceDetailBatch
            {
                BatchId = "batch-2",
                Invoices = new List<IssuedInvoiceDetail>
                {
                    new IssuedInvoiceDetail
                    {
                        Code = "INV-201",
                        Price = new InvoicePrice { TotalWithVat = 605m, TotalWithoutVat = 500m },
                        Items = new List<IssuedInvoiceDetailItem>()
                    }
                }
            };

            _inner
                .Setup(s => s.GetAllAsync(It.IsAny<IssuedInvoiceSourceQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<IssuedInvoiceDetailBatch> { batch1, batch2 });

            var query = new DqtInvoiceSourceQuery
            {
                RequestId = "dqt-req",
                DateFrom = new DateOnly(2026, 1, 1),
                DateTo = new DateOnly(2026, 1, 31)
            };

            var adapter = CreateAdapter();
            var result = await adapter.GetAllAsync(query, CancellationToken.None);

            result.Should().HaveCount(2);

            var first = result.Single(r => r.Code == "INV-200");
            first.TotalWithVat.Should().Be(1210m);
            first.TotalWithoutVat.Should().Be(1000m);
            first.Items.Should().ContainSingle();
            first.Items[0].Code.Should().Be("PROD-A");
            first.Items[0].Amount.Should().Be(2m);
            first.Items[0].WithVat.Should().Be(121m);
            first.Items[0].WithoutVat.Should().Be(100m);

            var second = result.Single(r => r.Code == "INV-201");
            second.TotalWithVat.Should().Be(605m);
            second.TotalWithoutVat.Should().Be(500m);
            second.Items.Should().BeEmpty();
        }
    }
    ```

11. **Create `InvoiceErpClientAdapterTests.cs`**:

    ```csharp
    using Anela.Heblo.Application.Features.Invoices.Infrastructure;
    using Anela.Heblo.Domain.Features.Invoices;
    using FluentAssertions;
    using Moq;
    using Xunit;

    namespace Anela.Heblo.Tests.Features.Invoices.Infrastructure;

    public class InvoiceErpClientAdapterTests
    {
        private readonly Mock<IIssuedInvoiceClient> _inner = new();

        private InvoiceErpClientAdapter CreateAdapter() => new(_inner.Object);

        [Fact]
        public async Task GetAllAsync_ForwardsFromToAndToken_ToInnerClient()
        {
            var from = new DateOnly(2026, 1, 1);
            var to = new DateOnly(2026, 1, 31);
            using var cts = new CancellationTokenSource();
            var ct = cts.Token;

            _inner
                .Setup(c => c.GetAllAsync(from, to, ct))
                .ReturnsAsync(new List<IssuedInvoiceDetail>());

            var adapter = CreateAdapter();
            await adapter.GetAllAsync(from, to, ct);

            _inner.Verify(c => c.GetAllAsync(from, to, ct), Times.Once);
        }

        [Fact]
        public async Task GetAllAsync_MapsInvoicesAndMultipleItems_ToDqtSnapshot()
        {
            var from = new DateOnly(2026, 1, 1);
            var to = new DateOnly(2026, 1, 31);

            var invoices = new List<IssuedInvoiceDetail>
            {
                new IssuedInvoiceDetail
                {
                    Code = "INV-300",
                    Price = new InvoicePrice { TotalWithVat = 3630m, TotalWithoutVat = 3000m },
                    Items = new List<IssuedInvoiceDetailItem>
                    {
                        new IssuedInvoiceDetailItem
                        {
                            Code = "PROD-X",
                            Amount = 4m,
                            ItemPrice = new InvoicePrice { WithVat = 242m, WithoutVat = 200m },
                            BuyPrice = new InvoicePrice()
                        },
                        new IssuedInvoiceDetailItem
                        {
                            Code = "PROD-Y",
                            Amount = 1m,
                            ItemPrice = new InvoicePrice { WithVat = 121m, WithoutVat = 100m },
                            BuyPrice = new InvoicePrice()
                        }
                    }
                }
            };

            _inner
                .Setup(c => c.GetAllAsync(from, to, It.IsAny<CancellationToken>()))
                .ReturnsAsync(invoices);

            var adapter = CreateAdapter();
            var result = await adapter.GetAllAsync(from, to, CancellationToken.None);

            result.Should().ContainSingle();
            var snapshot = result[0];
            snapshot.Code.Should().Be("INV-300");
            snapshot.TotalWithVat.Should().Be(3630m);
            snapshot.TotalWithoutVat.Should().Be(3000m);
            snapshot.Items.Should().HaveCount(2);

            snapshot.Items[0].Code.Should().Be("PROD-X");
            snapshot.Items[0].WithVat.Should().Be(242m);
            snapshot.Items[0].WithoutVat.Should().Be(200m);

            snapshot.Items[1].Code.Should().Be("PROD-Y");
            snapshot.Items[1].WithVat.Should().Be(121m);
            snapshot.Items[1].WithoutVat.Should().Be(100m);
        }

        [Fact]
        public async Task GetAllAsync_ReturnsEmptyList_WhenInnerClientReturnsEmpty()
        {
            var from = new DateOnly(2026, 1, 1);
            var to = new DateOnly(2026, 1, 31);

            _inner
                .Setup(c => c.GetAllAsync(from, to, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<IssuedInvoiceDetail>());

            var adapter = CreateAdapter();
            var result = await adapter.GetAllAsync(from, to, CancellationToken.None);

            result.Should().BeEmpty();
        }
    }
    ```

12. **Run the new adapter tests and confirm they pass.**

    ```bash
    dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceShoptetSourceAdapterTests|FullyQualifiedName~InvoiceErpClientAdapterTests"
    ```

    Expected: `5` tests run (2 + 3), `5` passed, `0` failed.

13. **Run the full DataQuality + Invoices test slice as a regression check.**

    ```bash
    dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.DataQuality|FullyQualifiedName~Features.Invoices"
    ```

    Expected: all tests pass, `0` failed.

14. **Commit.**

    ```bash
    git add backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IInvoiceShoptetSource.cs \
            backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IInvoiceErpClient.cs \
            backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtComparer.cs \
            backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceShoptetSourceAdapter.cs \
            backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceErpClientAdapter.cs \
            backend/test/Anela.Heblo.Tests/Features/DataQuality/InvoiceDqtComparerTests.cs \
            backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceShoptetSourceAdapterTests.cs \
            backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceErpClientAdapterTests.cs
    git commit -m "Route IInvoiceShoptetSource/IInvoiceErpClient through DataQuality-owned snapshot types"
    ```

---
