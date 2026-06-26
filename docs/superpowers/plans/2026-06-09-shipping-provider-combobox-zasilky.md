# Shipping‑Provider Combobox on the Zásilky Page — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On the packaging **Zásilky** page (`/baleni/zasilky`), replace the free‑text shipping‑provider filter with a combobox of the 4 carrier groups (Zásilkovna, PPL, GLS, Osobní odběr) and show the friendly carrier name in the table column instead of the raw Shoptet shipping ID.

**Architecture:** `Package.ShippingProviderCode` stores a Shoptet shipping‑ID string (e.g. `"21"`, `"6"`) and `ShippingProviderName` is always `null`. The existing `ShippingMethodRegistry` (Shoptet adapter) already maps each shipping ID → `Carriers` enum. We filter by `Carriers` (translated to its set of shipping‑ID codes for the DB query) and resolve each stored code back to a carrier display name on read. No data migration, no new API endpoint — the 4 carriers already exist as the generated `Carriers` TS enum plus `CARRIER_LABELS`.

**Tech Stack:** .NET 8 (MediatR, EF Core, FluentValidation), xUnit + Moq + FluentAssertions; React + TypeScript, TanStack Query, Jest + Testing Library.

**Scope note:** The `/baleni/statistiky` route is an unimplemented placeholder and is **out of scope** (per product decision). Display + filter are both carrier‑level (not per shipping method).

---

## File Structure

**Backend — created**
- `backend/src/Anela.Heblo.Domain/Features/Logistics/CarrierExtensions.cs` — `Carriers` → Czech display name.
- `backend/test/Anela.Heblo.Tests/Features/Logistics/CarrierExtensionsTests.cs`

**Backend — modified**
- `backend/src/Anela.Heblo.Domain/Features/Logistics/IShippingMethodCatalog.cs` — add 2 lookup methods.
- `backend/src/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShippingMethodCatalog.cs` — implement them.
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackages/GetPackagesRequest.cs` — `ShippingProviderCode` → `Carrier`.
- `backend/src/Anela.Heblo.Domain/Features/Packaging/IPackageRepository.cs` — code param → list.
- `backend/src/Anela.Heblo.Persistence/Repositories/Packaging/PackageRepository.cs` — `IN`‑style filter.
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackages/GetPackagesHandler.cs` — inject catalog, resolve codes + display name.
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShippingMethodCatalogTests.cs` — new method tests.
- `backend/test/Anela.Heblo.Tests/Features/Packaging/GetPackagesHandlerTests.cs` — updated mocks + new tests.

**Frontend — created**
- `frontend/src/constants/carrierLabels.ts` — shared `CARRIER_LABELS`.

**Frontend — modified**
- `frontend/src/components/customer/cooling/CarrierCoolingMatrix.tsx` — import shared labels (DRY).
- `frontend/src/api/hooks/usePackages.ts` — request field `shippingProviderCode` → `carrier`.
- `frontend/src/components/baleni/zasilky/ZasilkyFilters.tsx` — text input → `<select>`.
- `frontend/src/components/baleni/zasilky/ZasilkyPage.tsx` — filter state + request mapping.
- `frontend/src/components/baleni/zasilky/__tests__/ZasilkyFilters.test.tsx`
- `frontend/src/components/baleni/zasilky/__tests__/ZasilkyPage.test.tsx`

`ZasilkyTable.tsx` is unchanged — it already renders `p.shippingProviderName ?? p.shippingProviderCode`, which now shows the resolved name.

**Carrier → shipping‑ID reference (from `ShippingMethodRegistry`):**
- Zasilkovna: `21, 15, 385, 370, 373, 388, 487, 481`
- PPL: `6, 80, 86, 358, 361, 379`
- GLS: `97, 109, 489`
- Osobak: `4`

---

## Task 1: Carrier display names (Domain)

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Logistics/CarrierExtensions.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Logistics/CarrierExtensionsTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Features/Logistics/CarrierExtensionsTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.Logistics;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics;

public class CarrierExtensionsTests
{
    [Theory]
    [InlineData(Carriers.Zasilkovna, "Zásilkovna")]
    [InlineData(Carriers.PPL, "PPL")]
    [InlineData(Carriers.GLS, "GLS")]
    [InlineData(Carriers.Osobak, "Osobní odběr")]
    public void GetDisplayName_ReturnsCzechLabel(Carriers carrier, string expected)
    {
        carrier.GetDisplayName().Should().Be(expected);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~CarrierExtensionsTests"`
Expected: FAIL — does not compile, `Carriers` has no `GetDisplayName`.

- [ ] **Step 3: Write minimal implementation**

Create `backend/src/Anela.Heblo.Domain/Features/Logistics/CarrierExtensions.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Logistics;

public static class CarrierExtensions
{
    public static string GetDisplayName(this Carriers carrier) => carrier switch
    {
        Carriers.Zasilkovna => "Zásilkovna",
        Carriers.PPL => "PPL",
        Carriers.GLS => "GLS",
        Carriers.Osobak => "Osobní odběr",
        _ => carrier.ToString(),
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~CarrierExtensionsTests"`
Expected: PASS (4 cases).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Logistics/CarrierExtensions.cs \
        backend/test/Anela.Heblo.Tests/Features/Logistics/CarrierExtensionsTests.cs
git commit -m "feat(logistics): add Carriers.GetDisplayName extension"
```

---

## Task 2: Carrier‑code lookups in the shipping catalog

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Logistics/IShippingMethodCatalog.cs`
- Modify: `backend/src/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShippingMethodCatalog.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShippingMethodCatalogTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to `ShippingMethodCatalogTests.cs` (inside the class, before the closing brace):

```csharp
    [Fact]
    public void GetShippingCodesForCarrier_Ppl_ReturnsAllPplShippingIds()
    {
        var result = _sut.GetShippingCodesForCarrier(Carriers.PPL);

        result.Should().BeEquivalentTo(new[] { "6", "80", "86", "358", "361", "379" });
    }

    [Fact]
    public void GetShippingCodesForCarrier_Osobak_ReturnsSingleId()
    {
        var result = _sut.GetShippingCodesForCarrier(Carriers.Osobak);

        result.Should().BeEquivalentTo(new[] { "4" });
    }

    [Theory]
    [InlineData("21", Carriers.Zasilkovna)]
    [InlineData("6", Carriers.PPL)]
    [InlineData("97", Carriers.GLS)]
    [InlineData("4", Carriers.Osobak)]
    public void ResolveCarrier_KnownId_ReturnsCarrier(string code, Carriers expected)
    {
        _sut.ResolveCarrier(code).Should().Be(expected);
    }

    [Theory]
    [InlineData("999")]
    [InlineData("abc")]
    [InlineData("")]
    public void ResolveCarrier_UnknownOrNonNumeric_ReturnsNull(string code)
    {
        _sut.ResolveCarrier(code).Should().BeNull();
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ShippingMethodCatalogTests"`
Expected: FAIL — `IShippingMethodCatalog`/`ShippingMethodCatalog` have no `GetShippingCodesForCarrier`/`ResolveCarrier` (does not compile).

- [ ] **Step 3: Extend the interface**

Replace the full contents of `IShippingMethodCatalog.cs` with:

```csharp
namespace Anela.Heblo.Domain.Features.Logistics;

public interface IShippingMethodCatalog
{
    IReadOnlyList<(Carriers Carrier, DeliveryHandling Handling)> GetAvailableDeliveryOptions();

    IReadOnlyList<string> GetShippingCodesForCarrier(Carriers carrier);

    Carriers? ResolveCarrier(string shippingProviderCode);
}
```

- [ ] **Step 4: Implement in the catalog**

In `ShippingMethodCatalog.cs`, add these two methods inside the class (after `GetAvailableDeliveryOptions`):

```csharp
    public IReadOnlyList<string> GetShippingCodesForCarrier(Carriers carrier)
    {
        return ShippingMethodRegistry.ShippingList
            .Where(m => m.Carrier == carrier)
            .Select(m => m.Id.ToString())
            .ToList()
            .AsReadOnly();
    }

    public Carriers? ResolveCarrier(string shippingProviderCode)
    {
        if (!int.TryParse(shippingProviderCode, out var id))
            return null;

        return ShippingMethodRegistry.ShippingList
            .FirstOrDefault(m => m.Id == id)?.Carrier;
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ShippingMethodCatalogTests"`
Expected: PASS (all, including the 5 pre‑existing `GetAvailableDeliveryOptions` tests).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Logistics/IShippingMethodCatalog.cs \
        backend/src/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShippingMethodCatalog.cs \
        backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShippingMethodCatalogTests.cs
git commit -m "feat(logistics): add carrier<->shipping-code lookups to catalog"
```

---

## Task 3: Filter packages by carrier + resolve display name

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackages/GetPackagesRequest.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Packaging/IPackageRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Repositories/Packaging/PackageRepository.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackages/GetPackagesHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Packaging/GetPackagesHandlerTests.cs`

- [ ] **Step 1: Update + extend the handler tests (drives the change)**

Replace the full contents of `GetPackagesHandlerTests.cs` with:

```csharp
using Anela.Heblo.Application.Features.Packaging.UseCases.GetPackages;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Packaging;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Packaging;

public class GetPackagesHandlerTests
{
    private static Package MakePackage(int id, string orderCode = "ORD1", string customer = "Alice",
        string packageNumber = "PKG-1", DateTimeOffset? packedAt = null, string providerCode = "6")
        => new()
        {
            Id = id,
            OrderCode = orderCode,
            CustomerName = customer,
            PackageNumber = packageNumber,
            ShippingProviderCode = providerCode,
            ShipmentGuid = Guid.NewGuid(),
            PackedAt = packedAt ?? new DateTimeOffset(2026, 5, 25, 10, 0, 0, TimeSpan.Zero),
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static GetPackagesHandler MakeSut(out Mock<IPackageRepository> repo,
        out Mock<IShippingMethodCatalog> catalog,
        (List<Package> Items, int TotalCount) result)
    {
        repo = new Mock<IPackageRepository>();
        repo.Setup(r => r.GetPaginatedAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        catalog = new Mock<IShippingMethodCatalog>();
        return new GetPackagesHandler(repo.Object, catalog.Object);
    }

    [Fact]
    public async Task Handle_MapsItemsAndPagingFields()
    {
        // Arrange
        var packages = new List<Package> { MakePackage(1), MakePackage(2) };
        var sut = MakeSut(out _, out _, (packages, 5));
        var request = new GetPackagesRequest { PageNumber = 1, PageSize = 2 };

        // Act
        var response = await sut.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Items.Should().HaveCount(2);
        response.Items[0].Id.Should().Be(1);
        response.TotalCount.Should().Be(5);
        response.PageNumber.Should().Be(1);
        response.PageSize.Should().Be(2);
    }

    [Fact]
    public async Task Handle_ForwardsFiltersAndSortToRepository()
    {
        // Arrange
        var sut = MakeSut(out var repo, out _, (new List<Package>(), 0));
        var request = new GetPackagesRequest
        {
            OrderCode = "ORD42",
            CustomerName = "Bob",
            FromDate = new DateTime(2026, 5, 1),
            ToDate = new DateTime(2026, 5, 31),
            SortBy = "CustomerName",
            SortDescending = false,
            PageNumber = 3,
            PageSize = 10,
        };

        // Act
        var response = await sut.Handle(request, CancellationToken.None);

        // Assert — no carrier filter => null codes list
        repo.Verify(r => r.GetPaginatedAsync(
            "ORD42", "Bob", null, (IReadOnlyList<string>?)null,
            new DateTime(2026, 5, 1), new DateTime(2026, 5, 31),
            3, 10,
            "CustomerName", false,
            It.IsAny<CancellationToken>()), Times.Once);
        response.Success.Should().BeTrue();
        response.Items.Should().BeEmpty();
        response.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_ResolvesCarrierToShippingCodes_AndForwardsToRepository()
    {
        // Arrange
        var sut = MakeSut(out var repo, out var catalog, (new List<Package>(), 0));
        var codes = new[] { "6", "80" };
        catalog.Setup(c => c.GetShippingCodesForCarrier(Carriers.PPL)).Returns(codes);
        var request = new GetPackagesRequest { Carrier = Carriers.PPL };

        // Act
        await sut.Handle(request, CancellationToken.None);

        // Assert
        catalog.Verify(c => c.GetShippingCodesForCarrier(Carriers.PPL), Times.Once);
        repo.Verify(r => r.GetPaginatedAsync(
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            codes,
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PopulatesShippingProviderName_FromResolvedCarrier()
    {
        // Arrange — stored code "6" resolves to PPL
        var sut = MakeSut(out _, out var catalog, (new List<Package> { MakePackage(1, providerCode: "6") }, 1));
        catalog.Setup(c => c.ResolveCarrier("6")).Returns(Carriers.PPL);
        var request = new GetPackagesRequest { PageNumber = 1, PageSize = 20 };

        // Act
        var response = await sut.Handle(request, CancellationToken.None);

        // Assert
        response.Items[0].ShippingProviderCode.Should().Be("6");
        response.Items[0].ShippingProviderName.Should().Be("PPL");
    }

    [Fact]
    public async Task Handle_LeavesProviderNameNull_WhenCarrierUnresolved()
    {
        // Arrange — unknown code, catalog returns null (default mock behavior)
        var sut = MakeSut(out _, out _, (new List<Package> { MakePackage(1, providerCode: "999") }, 1));

        // Act
        var response = await sut.Handle(new GetPackagesRequest(), CancellationToken.None);

        // Assert
        response.Items[0].ShippingProviderName.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ReturnsEmptyList_WhenRepositoryReturnsNoItems()
    {
        var sut = MakeSut(out _, out _, (new List<Package>(), 0));
        var response = await sut.Handle(new GetPackagesRequest(), CancellationToken.None);
        response.Items.Should().BeEmpty();
        response.TotalCount.Should().Be(0);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~GetPackagesHandlerTests"`
Expected: FAIL — does not compile (`GetPackagesRequest.Carrier` missing, `GetPaginatedAsync` signature mismatch, handler ctor takes one arg).

- [ ] **Step 3: Update the request DTO**

Replace the full contents of `GetPackagesRequest.cs` with:

```csharp
using Anela.Heblo.Domain.Features.Logistics;
using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetPackages;

public class GetPackagesRequest : IRequest<GetPackagesResponse>
{
    public string? OrderCode { get; set; }
    public string? CustomerName { get; set; }
    public string? PackageNumber { get; set; }
    public Carriers? Carrier { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string SortBy { get; set; } = "PackedAt";
    public bool SortDescending { get; set; } = true;
}
```

- [ ] **Step 4: Update the repository interface**

In `IPackageRepository.cs`, change the 4th parameter of `GetPaginatedAsync` from
`string? shippingProviderCode,` to `IReadOnlyList<string>? shippingProviderCodes,`. Full method:

```csharp
    Task<(List<Package> Items, int TotalCount)> GetPaginatedAsync(
        string? orderCode,
        string? customerName,
        string? packageNumber,
        IReadOnlyList<string>? shippingProviderCodes,
        DateTime? fromDate,
        DateTime? toDate,
        int pageNumber,
        int pageSize,
        string sortBy,
        bool sortDescending,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 5: Update the repository implementation**

In `PackageRepository.cs`: change the 4th parameter name in the signature to
`IReadOnlyList<string>? shippingProviderCodes,` and replace the filter block:

```csharp
        if (!string.IsNullOrWhiteSpace(shippingProviderCode))
            q = q.Where(p => p.ShippingProviderCode == shippingProviderCode);
```

with:

```csharp
        if (shippingProviderCodes is { Count: > 0 })
            q = q.Where(p => shippingProviderCodes.Contains(p.ShippingProviderCode));
```

(Leave the sorting `switch` block unchanged.)

- [ ] **Step 6: Update the handler**

Replace the full contents of `GetPackagesHandler.cs` with:

```csharp
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Packaging;
using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetPackages;

public class GetPackagesHandler : IRequestHandler<GetPackagesRequest, GetPackagesResponse>
{
    private readonly IPackageRepository _repo;
    private readonly IShippingMethodCatalog _shippingCatalog;

    public GetPackagesHandler(IPackageRepository repo, IShippingMethodCatalog shippingCatalog)
    {
        _repo = repo;
        _shippingCatalog = shippingCatalog;
    }

    public async Task<GetPackagesResponse> Handle(GetPackagesRequest request, CancellationToken cancellationToken)
    {
        var shippingProviderCodes = request.Carrier.HasValue
            ? _shippingCatalog.GetShippingCodesForCarrier(request.Carrier.Value)
            : null;

        var (items, total) = await _repo.GetPaginatedAsync(
            request.OrderCode,
            request.CustomerName,
            request.PackageNumber,
            shippingProviderCodes,
            request.FromDate,
            request.ToDate,
            request.PageNumber,
            request.PageSize,
            request.SortBy,
            request.SortDescending,
            cancellationToken);

        return new GetPackagesResponse
        {
            Items = items.Select(p => new PackageDto
            {
                Id = p.Id,
                OrderCode = p.OrderCode,
                CustomerName = p.CustomerName,
                PackageNumber = p.PackageNumber,
                TrackingNumber = p.TrackingNumber,
                ShippingProviderCode = p.ShippingProviderCode,
                ShippingProviderName = _shippingCatalog.ResolveCarrier(p.ShippingProviderCode)?.GetDisplayName()
                    ?? p.ShippingProviderName,
                PackedAt = p.PackedAt,
                PackedBy = p.PackedBy,
            }).ToList(),
            TotalCount = total,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
        };
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GetPackagesHandlerTests"`
Expected: PASS (7 tests).

- [ ] **Step 8: Build the whole backend + format**

Run: `dotnet build` then `dotnet format`
Expected: build succeeds (confirms no other caller of `GetPaginatedAsync` or `GetPackagesRequest.ShippingProviderCode` broke).

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackages/GetPackagesRequest.cs \
        backend/src/Anela.Heblo.Domain/Features/Packaging/IPackageRepository.cs \
        backend/src/Anela.Heblo.Persistence/Repositories/Packaging/PackageRepository.cs \
        backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackages/GetPackagesHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Packaging/GetPackagesHandlerTests.cs
git commit -m "feat(packaging): filter packages by carrier and resolve provider name"
```

---

## Task 4: Shared carrier labels (frontend, DRY)

**Files:**
- Create: `frontend/src/constants/carrierLabels.ts`
- Modify: `frontend/src/components/customer/cooling/CarrierCoolingMatrix.tsx`

- [ ] **Step 1: Create the shared constant**

Create `frontend/src/constants/carrierLabels.ts`:

```typescript
import { Carriers } from "../api/generated/api-client";

export const CARRIER_LABELS: Record<Carriers, string> = {
  [Carriers.Zasilkovna]: "Zásilkovna",
  [Carriers.PPL]: "PPL",
  [Carriers.GLS]: "GLS",
  [Carriers.Osobak]: "Osobní odběr",
};
```

- [ ] **Step 2: Import it in CarrierCoolingMatrix**

In `frontend/src/components/customer/cooling/CarrierCoolingMatrix.tsx`, delete the local
`CARRIER_LABELS` definition (lines 18–23) and add an import near the top (after the existing imports):

```typescript
import { CARRIER_LABELS } from "../../../constants/carrierLabels";
```

- [ ] **Step 3: Verify the cooling matrix still builds + its tests pass**

Run: `cd frontend && npx tsc --noEmit && npx jest CarrierCooling`
Expected: type‑check passes; CarrierCooling tests pass (or "no tests found" if none — acceptable).

- [ ] **Step 4: Commit**

```bash
git add frontend/src/constants/carrierLabels.ts \
        frontend/src/components/customer/cooling/CarrierCoolingMatrix.tsx
git commit -m "refactor(frontend): extract CARRIER_LABELS to shared constant"
```

---

## Task 5: Send carrier filter from the packages hook

**Files:**
- Modify: `frontend/src/api/hooks/usePackages.ts`

- [ ] **Step 1: Update the request type**

In `usePackages.ts`, in `GetPackagesRequest` replace `shippingProviderCode?: string;` with `carrier?: string;`.

- [ ] **Step 2: Update the query‑param builder**

In the same file, replace:

```typescript
      if (request.shippingProviderCode)
        params.append("ShippingProviderCode", request.shippingProviderCode);
```

with:

```typescript
      if (request.carrier) params.append("Carrier", request.carrier);
```

- [ ] **Step 3: Type‑check**

Run: `cd frontend && npx tsc --noEmit`
Expected: FAIL pointing at `ZasilkyPage.tsx` (`shippingProviderCode` no longer on the request) — this is expected and fixed in Task 7. (If it passes, even better.)

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/hooks/usePackages.ts
git commit -m "feat(packaging): send Carrier filter from packages query hook"
```

---

## Task 6: Replace the filter text input with a combobox

**Files:**
- Modify: `frontend/src/components/baleni/zasilky/ZasilkyFilters.tsx`
- Test: `frontend/src/components/baleni/zasilky/__tests__/ZasilkyFilters.test.tsx`

- [ ] **Step 1: Update the filter tests (drives the change)**

In `ZasilkyFilters.test.tsx` make these edits:

Replace `emptyFilters` (lines 5–12):

```typescript
const emptyFilters = {
  orderCode: "",
  customerName: "",
  packageNumber: "",
  carrier: "",
  fromDate: "",
  toDate: "",
};
```

In the "renders all filter inputs and the search button" test, replace
`expect(screen.getByPlaceholderText("Dopravce (kód)")).toBeInTheDocument();` with:

```typescript
    expect(screen.getByRole("combobox")).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Všichni dopravci" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Zásilkovna" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Osobní odběr" })).toBeInTheDocument();
```

Replace the "includes all field values in the onChange payload" test body with:

```typescript
  it("includes all field values in the onChange payload", () => {
    const onChange = jest.fn();
    render(<ZasilkyFilters value={emptyFilters} onChange={onChange} />);

    fireEvent.change(screen.getByPlaceholderText("Objednávka"), { target: { value: "O1" } });
    fireEvent.change(screen.getByPlaceholderText("Zákazník"), { target: { value: "Bob" } });
    fireEvent.change(screen.getByPlaceholderText("Číslo balíku"), { target: { value: "P1" } });
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "PPL" } });
    fireEvent.click(screen.getByRole("button", { name: "Hledat" }));

    expect(onChange).toHaveBeenCalledWith({
      orderCode: "O1",
      customerName: "Bob",
      packageNumber: "P1",
      carrier: "PPL",
      fromDate: "",
      toDate: "",
    });
  });
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && npx jest ZasilkyFilters`
Expected: FAIL — no combobox/options; `carrier` not in payload.

- [ ] **Step 3: Implement the combobox**

Replace the full contents of `ZasilkyFilters.tsx` with:

```tsx
import { useState } from "react";
import { Carriers } from "../../../api/generated/api-client";
import { CARRIER_LABELS } from "../../../constants/carrierLabels";

export interface FilterValues {
  orderCode: string;
  customerName: string;
  packageNumber: string;
  carrier: string;
  fromDate: string;
  toDate: string;
}

interface Props {
  value: FilterValues;
  onChange: (value: FilterValues) => void;
}

export function ZasilkyFilters({ value, onChange }: Props) {
  const [local, setLocal] = useState<FilterValues>(value);

  const update =
    (k: keyof FilterValues) =>
    (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) =>
      setLocal((prev) => ({ ...prev, [k]: e.target.value }));

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    onChange(local);
  };

  return (
    <form onSubmit={handleSubmit}>
      <div className="grid grid-cols-2 md:grid-cols-6 gap-3 p-4 pb-3 bg-slate-50 border-b">
        <input
          className="px-3 py-2 border rounded"
          placeholder="Objednávka"
          value={local.orderCode}
          onChange={update("orderCode")}
        />
        <input
          className="px-3 py-2 border rounded"
          placeholder="Zákazník"
          value={local.customerName}
          onChange={update("customerName")}
        />
        <input
          className="px-3 py-2 border rounded"
          placeholder="Číslo balíku"
          value={local.packageNumber}
          onChange={update("packageNumber")}
        />
        <select
          className="px-3 py-2 border rounded bg-white"
          value={local.carrier}
          onChange={update("carrier")}
        >
          <option value="">Všichni dopravci</option>
          {(Object.entries(CARRIER_LABELS) as [Carriers, string][]).map(
            ([code, label]) => (
              <option key={code} value={code}>
                {label}
              </option>
            ),
          )}
        </select>
        <input
          type="date"
          className="px-3 py-2 border rounded"
          value={local.fromDate}
          onChange={update("fromDate")}
        />
        <input
          type="date"
          className="px-3 py-2 border rounded"
          value={local.toDate}
          onChange={update("toDate")}
        />
        <button
          type="submit"
          className="col-span-2 md:col-span-6 px-6 py-2 rounded bg-blue-600 text-white font-medium hover:bg-blue-700 active:bg-blue-800"
        >
          Hledat
        </button>
      </div>
    </form>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd frontend && npx jest ZasilkyFilters`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/baleni/zasilky/ZasilkyFilters.tsx \
        frontend/src/components/baleni/zasilky/__tests__/ZasilkyFilters.test.tsx
git commit -m "feat(packaging): carrier combobox in Zasilky filters"
```

---

## Task 7: Wire the carrier filter through ZasilkyPage

**Files:**
- Modify: `frontend/src/components/baleni/zasilky/ZasilkyPage.tsx`
- Test: `frontend/src/components/baleni/zasilky/__tests__/ZasilkyPage.test.tsx`

- [ ] **Step 1: Update the page test fixture + add a name‑rendering assertion**

In `ZasilkyPage.test.tsx`, update `samplePackage` (lines 29–37) to include a resolved name:

```typescript
const samplePackage: PackageDto = {
  id: 1,
  orderCode: "ORD-1",
  customerName: "Alice",
  packageNumber: "PKG-1",
  trackingNumber: "TRK-1",
  shippingProviderCode: "6",
  shippingProviderName: "PPL",
  packedAt: "2026-05-25T10:00:00Z",
};
```

In the "renders package rows when query succeeds" test, add after the `PKG-1` assertion:

```typescript
    expect(screen.getByText("PPL")).toBeInTheDocument();
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && npx jest ZasilkyPage`
Expected: FAIL — `ZasilkyPage` still initializes `shippingProviderCode` (TS/compile error in the test run) or the `PPL` cell is missing.

- [ ] **Step 3: Update the initial filter state**

In `ZasilkyPage.tsx`, in the `useState<FilterValues>({...})` initializer replace
`shippingProviderCode: "",` with `carrier: "",`.

- [ ] **Step 4: Update the request memo**

In the `request` `useMemo`, replace
`shippingProviderCode: filters.shippingProviderCode || undefined,` with
`carrier: filters.carrier || undefined,`.

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd frontend && npx jest ZasilkyPage`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/baleni/zasilky/ZasilkyPage.tsx \
        frontend/src/components/baleni/zasilky/__tests__/ZasilkyPage.test.tsx
git commit -m "feat(packaging): wire carrier filter through Zasilky page"
```

---

## Task 8: Full verification

- [ ] **Step 1: Backend build, format, test**

Run: `dotnet build && dotnet format && dotnet test`
Expected: build + format clean; all tests pass.

- [ ] **Step 2: Frontend build + lint**

Run: `cd frontend && npm run build && npm run lint`
Expected: build succeeds (regenerates the OpenAPI TS client with the new `Carrier` enum query param; `ShippingProviderCode` removed from the packages request) and lint is clean.

- [ ] **Step 3: Frontend full test run**

Run: `cd frontend && npm test -- --watch=false`
Expected: all tests pass.

- [ ] **Step 4: Manual / E2E sanity (staging)**

- Open `/baleni/zasilky`.
- Confirm the **Dopravce** filter is a dropdown: "Všichni dopravci", "Zásilkovna", "PPL", "GLS", "Osobní odběr".
- Select "PPL" → list shows only PPL packages; the **Dopravce** column shows "PPL" (not `6`/`80`).
- Clear to "Všichni dopravci" → all packages return; each row's column shows a carrier name (raw code only for IDs absent from the registry).

---

## Self‑Review Notes

- **Spec coverage:** combobox of existing providers (Task 6), backed by the 4 carrier groups (Tasks 1–2, frontend Task 4); friendly name in column (Task 3 handler resolution); filter applies carrier→codes (Task 3 + 5 + 7). Statistiky placeholder explicitly excluded.
- **Type consistency:** repo method `GetPaginatedAsync` 4th param is `IReadOnlyList<string>? shippingProviderCodes` in both interface and impl, and the handler test mock matches; request field is `Carrier` (`Carriers?`) end‑to‑end; frontend `FilterValues.carrier` ↔ hook `GetPackagesRequest.carrier` ↔ query param `"Carrier"`.
- **Known limitation (unchanged on purpose):** sorting by "ShippingProvider" still orders by the stored code in the DB (the display name is computed on read, not persisted), so a sorted page groups roughly by ID rather than strictly by carrier label. Out of scope for this change.
- **Graceful fallback:** codes not present in `ShippingMethodRegistry` resolve to `null` → column falls back to the raw code.
```