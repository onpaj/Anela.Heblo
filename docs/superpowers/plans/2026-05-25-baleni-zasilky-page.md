# Zásilky (Packages) Management Page — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a history/management page at `/baleni/zasilky` (Balení touch interface) listing every package created by the direct-packaging flow, with filtering, sorting, paging, delete and reprint-label actions.

**Architecture:** Introduce a new persisted `Package` aggregate in our DB written by `ScanPackingOrderHandler` after each successful Shoptet shipment creation. Add a paginated list endpoint + delete endpoint (delete calls `IShipmentClient.CancelShipmentAsync` and removes the row). Frontend replaces the existing `BaleniPlaceholder` with a touch-friendly grid that reuses the existing `printLabelPdf` utility for label reprinting.

**Tech Stack:** .NET 8 + MediatR + EF Core (PostgreSQL) + FluentValidation on backend; React + TypeScript + TanStack Query + Tailwind on frontend; tests via xUnit/FluentAssertions/Moq (BE) and React Testing Library/Vitest (FE).

---

## Context

The direct-packaging feature lets a user scan an order at the packing station, our backend calls Shoptet to create the shipment, and labels are printed. Today nothing about that event is stored locally — all package state lives in Shoptet. The packing operator has no way to look at recent shipments, find one they just packed, delete a mistaken shipment, or reprint a lost label. There is already a placeholder route `/baleni/zasilky` ("Zásilky" = Shipments/Packages) in the Balení touch interface waiting to be implemented.

This plan adds the missing persistence (one DB row per package created via our app) and a touch-friendly page that lists, filters, sorts, paginates, deletes, and reprints those packages.

### Verified facts from the codebase

- Shipment cancellation: `IShipmentClient.CancelShipmentAsync(Guid shipmentGuid, CancellationToken ct = default)` in `Anela.Heblo.Application.Features.ShipmentLabels`. (NOT `IShoptetShipmentClient`.)
- `BaseResponse` is abstract — handler responses inherit and use constructors (`new MyResponse(data)` for success, `new MyResponse(ErrorCodes.X)` for error). See `ResetOrderShipmentResponse.cs`.
- Packaging error code range is `30XX`. Current entries end at `3005` (`PackageLabelDownloadFailed`). We add `PackageNotFound = 3006`.
- `PackagingController` extends `BaseApiController` and uses `HandleResponse(response)` for ActionResult mapping (see existing actions for the pattern).
- `ICurrentUserService.GetCurrentUser()` returns a non-nullable `CurrentUser` with nullable `Email`.
- There is no `PackagingModule.cs` yet — `ScanPackingOrderRequestValidator` exists in `Validators/` but isn't registered. We will create the module in this plan and register both the new validator and the existing one.
- Carrier code at shipment-create time comes from `options[0].CarrierCode` in `ScanPackingOrderHandler` (not from the order entity). We must thread it into the persistence call.

---

## File Structure

### Backend — new files
- `backend/src/Anela.Heblo.Domain/Features/Packaging/Package.cs`
- `backend/src/Anela.Heblo.Domain/Features/Packaging/IPackageRepository.cs`
- `backend/src/Anela.Heblo.Persistence/Features/Packaging/PackageConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Repositories/Packaging/PackageRepository.cs`
- `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddPackageEntity.cs` (generated)
- `backend/src/Anela.Heblo.Application/Features/Packaging/PackagingModule.cs`
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackages/GetPackagesRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackages/GetPackagesResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackages/GetPackagesRequestValidator.cs`
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackages/GetPackagesHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/DeletePackage/DeletePackageRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/DeletePackage/DeletePackageResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/DeletePackage/DeletePackageHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/Packaging/GetPackagesHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Packaging/DeletePackageHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Packaging/ScanPackingOrderHandlerPackagePersistenceTests.cs`

### Backend — modified files
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — add `PackageNotFound = 3006`
- `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` — add `DbSet<Package>`
- `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` — register `IPackageRepository`
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs` — persist Package rows after success
- `backend/src/Anela.Heblo.API/Controllers/PackagingController.cs` — add `GET /packages` and `DELETE /packages/{id}`
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` (or equivalent composition root) — wire `AddPackagingModule(configuration)` if a module composition pattern is used

### Frontend — new files
- `frontend/src/components/baleni/zasilky/ZasilkyPage.tsx`
- `frontend/src/components/baleni/zasilky/ZasilkyFilters.tsx`
- `frontend/src/components/baleni/zasilky/ZasilkyTable.tsx`
- `frontend/src/components/baleni/zasilky/ZasilkyPagination.tsx`
- `frontend/src/components/baleni/zasilky/DeletePackageDialog.tsx`
- `frontend/src/api/hooks/usePackages.ts`
- `frontend/test/components/baleni/zasilky/ZasilkyPage.test.tsx`

### Frontend — modified files
- `frontend/src/App.tsx` — replace `<BaleniPlaceholder title="Zásilky" />` with `<ZasilkyPage />`

---

## Backend Tasks

### Task 1: Create `Package` domain entity

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Packaging/Package.cs`

- [ ] **Step 1: Write the entity**

```csharp
namespace Anela.Heblo.Domain.Features.Packaging;

public class Package
{
    public int Id { get; set; }
    public string OrderCode { get; set; } = null!;
    public string CustomerName { get; set; } = null!;
    public string PackageNumber { get; set; } = null!;
    public string? TrackingNumber { get; set; }
    public string ShippingProviderCode { get; set; } = null!;
    public string? ShippingProviderName { get; set; }
    public Guid ShipmentGuid { get; set; }
    public DateTimeOffset PackedAt { get; set; }
    public string? PackedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

- [ ] **Step 2: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Packaging/Package.cs
git commit -m "feat(packaging): add Package domain entity"
```

---

### Task 2: Define `IPackageRepository`

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Packaging/IPackageRepository.cs`

- [ ] **Step 1: Write the interface**

```csharp
namespace Anela.Heblo.Domain.Features.Packaging;

public interface IPackageRepository
{
    Task<(List<Package> Items, int TotalCount)> GetPaginatedAsync(
        string? orderCode,
        string? customerName,
        string? packageNumber,
        string? shippingProviderCode,
        DateTime? fromDate,
        DateTime? toDate,
        int pageNumber,
        int pageSize,
        string sortBy,
        bool sortDescending,
        CancellationToken cancellationToken = default);

    Task<Package?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task AddAsync(Package package, CancellationToken cancellationToken = default);
    Task DeleteAsync(Package package, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Packaging/IPackageRepository.cs
git commit -m "feat(packaging): add IPackageRepository contract"
```

---

### Task 3: EF Core mapping for `Package`

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Features/Packaging/PackageConfiguration.cs`

- [ ] **Step 1: Write the configuration**

```csharp
using Anela.Heblo.Domain.Features.Packaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Packaging;

public class PackageConfiguration : IEntityTypeConfiguration<Package>
{
    public void Configure(EntityTypeBuilder<Package> builder)
    {
        builder.ToTable("Packages", "public");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.OrderCode).IsRequired().HasMaxLength(50);
        builder.Property(p => p.CustomerName).IsRequired().HasMaxLength(255);
        builder.Property(p => p.PackageNumber).IsRequired().HasMaxLength(50);
        builder.Property(p => p.TrackingNumber).HasMaxLength(100);
        builder.Property(p => p.ShippingProviderCode).IsRequired().HasMaxLength(50);
        builder.Property(p => p.ShippingProviderName).HasMaxLength(100);
        builder.Property(p => p.ShipmentGuid).IsRequired();
        builder.Property(p => p.PackedAt).IsRequired();
        builder.Property(p => p.PackedBy).HasMaxLength(255);
        builder.Property(p => p.CreatedAt).IsRequired();
        builder.HasIndex(p => p.OrderCode);
        builder.HasIndex(p => p.PackedAt);
        builder.HasIndex(p => new { p.OrderCode, p.PackageNumber }).IsUnique();
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Features/Packaging/PackageConfiguration.cs
git commit -m "feat(packaging): add Package EF Core configuration"
```

---

### Task 4: Wire `DbSet<Package>` into `ApplicationDbContext`

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`

- [ ] **Step 1: Add the DbSet alongside other domain sets**

Add `using Anela.Heblo.Domain.Features.Packaging;` at the top if missing, then add the DbSet near the other domain sets (alphabetically or just before/after `PurchaseOrder` for findability):

```csharp
public DbSet<Package> Packages { get; set; } = null!;
```

- [ ] **Step 2: Verify auto-discovery (no manual `OnModelCreating` change needed)**

`ApplicationDbContext` already calls `modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);` — `PackageConfiguration` will be picked up automatically.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs
git commit -m "feat(packaging): register Packages DbSet"
```

---

### Task 5: Generate EF migration

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddPackageEntity.cs`

- [ ] **Step 1: Generate migration**

Run from `backend/`:

```bash
dotnet ef migrations add AddPackageEntity \
  --project src/Anela.Heblo.Persistence \
  --startup-project src/Anela.Heblo.API
```

- [ ] **Step 2: Inspect migration — must create only the `public."Packages"` table (no other diff)**

If the migration contains unrelated changes from drifted models, stop and ask the user. Otherwise proceed.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat(packaging): add migration for Packages table"
```

Note: Migrations are applied manually — do NOT run `dotnet ef database update` as part of this task. The user runs migrations themselves.

---

### Task 6: Implement `PackageRepository`

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Repositories/Packaging/PackageRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs`

- [ ] **Step 1: Write the implementation**

```csharp
using Anela.Heblo.Domain.Features.Packaging;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Repositories.Packaging;

public class PackageRepository : IPackageRepository
{
    private readonly ApplicationDbContext _db;

    public PackageRepository(ApplicationDbContext db) => _db = db;

    public async Task<(List<Package> Items, int TotalCount)> GetPaginatedAsync(
        string? orderCode,
        string? customerName,
        string? packageNumber,
        string? shippingProviderCode,
        DateTime? fromDate,
        DateTime? toDate,
        int pageNumber,
        int pageSize,
        string sortBy,
        bool sortDescending,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Package> q = _db.Packages.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(orderCode))
            q = q.Where(p => EF.Functions.ILike(p.OrderCode, $"%{orderCode}%"));
        if (!string.IsNullOrWhiteSpace(customerName))
            q = q.Where(p => EF.Functions.ILike(p.CustomerName, $"%{customerName}%"));
        if (!string.IsNullOrWhiteSpace(packageNumber))
            q = q.Where(p => EF.Functions.ILike(p.PackageNumber, $"%{packageNumber}%"));
        if (!string.IsNullOrWhiteSpace(shippingProviderCode))
            q = q.Where(p => p.ShippingProviderCode == shippingProviderCode);
        if (fromDate.HasValue)
            q = q.Where(p => p.PackedAt >= fromDate.Value);
        if (toDate.HasValue)
            q = q.Where(p => p.PackedAt <= toDate.Value);

        var total = await q.CountAsync(cancellationToken);

        q = (sortBy.ToLowerInvariant(), sortDescending) switch
        {
            ("ordercode",        true)  => q.OrderByDescending(p => p.OrderCode),
            ("ordercode",        false) => q.OrderBy(p => p.OrderCode),
            ("customername",     true)  => q.OrderByDescending(p => p.CustomerName),
            ("customername",     false) => q.OrderBy(p => p.CustomerName),
            ("packagenumber",    true)  => q.OrderByDescending(p => p.PackageNumber),
            ("packagenumber",    false) => q.OrderBy(p => p.PackageNumber),
            ("shippingprovider", true)  => q.OrderByDescending(p => p.ShippingProviderName ?? p.ShippingProviderCode),
            ("shippingprovider", false) => q.OrderBy(p => p.ShippingProviderName ?? p.ShippingProviderCode),
            (_,                  true)  => q.OrderByDescending(p => p.PackedAt),
            (_,                  false) => q.OrderBy(p => p.PackedAt),
        };

        var items = await q.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<Package?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _db.Packages.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task AddAsync(Package package, CancellationToken cancellationToken = default)
    {
        await _db.Packages.AddAsync(package, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Package package, CancellationToken cancellationToken = default)
    {
        _db.Packages.Remove(package);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 2: Register in `PersistenceModule.cs`**

Open `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs`, find the line `services.AddScoped<IPurchaseOrderRepository, PurchaseOrderRepository>();` and add directly below:

```csharp
services.AddScoped<IPackageRepository, PackageRepository>();
```

Add the relevant `using` for `Anela.Heblo.Domain.Features.Packaging;` and `Anela.Heblo.Persistence.Repositories.Packaging;` if needed.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Repositories/Packaging/ backend/src/Anela.Heblo.Persistence/PersistenceModule.cs
git commit -m "feat(packaging): add PackageRepository"
```

---

### Task 7: Add `PackageNotFound` error code

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`

- [ ] **Step 1: Add the entry to the Packaging block (30XX)**

Find the comment `// Packaging module errors (30XX)` block (around the bottom of the file). After the last existing entry (`PackageLabelDownloadFailed = 3005`), add:

```csharp
    [HttpStatusCode(HttpStatusCode.NotFound)]
    PackageNotFound = 3006,
```

- [ ] **Step 2: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs
git commit -m "feat(packaging): add PackageNotFound error code"
```

---

### Task 8: `GetPackages` request, response, validator

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackages/GetPackagesRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackages/GetPackagesResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackages/GetPackagesRequestValidator.cs`

- [ ] **Step 1: Request DTO (class, NOT record — see CLAUDE.md)**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetPackages;

public class GetPackagesRequest : IRequest<GetPackagesResponse>
{
    public string? OrderCode { get; set; }
    public string? CustomerName { get; set; }
    public string? PackageNumber { get; set; }
    public string? ShippingProviderCode { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string SortBy { get; set; } = "PackedAt";
    public bool SortDescending { get; set; } = true;
}
```

- [ ] **Step 2: Response DTO (inherits `BaseResponse`, constructor-based like sibling responses)**

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetPackages;

public class GetPackagesResponse : BaseResponse
{
    public List<PackageDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }

    public GetPackagesResponse() { }
    public GetPackagesResponse(ErrorCodes errorCode) : base(errorCode) { }
}

public class PackageDto
{
    public int Id { get; set; }
    public string OrderCode { get; set; } = null!;
    public string CustomerName { get; set; } = null!;
    public string PackageNumber { get; set; } = null!;
    public string? TrackingNumber { get; set; }
    public string ShippingProviderCode { get; set; } = null!;
    public string? ShippingProviderName { get; set; }
    public DateTimeOffset PackedAt { get; set; }
    public string? PackedBy { get; set; }
}
```

- [ ] **Step 3: Validator**

```csharp
using FluentValidation;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetPackages;

public class GetPackagesRequestValidator : AbstractValidator<GetPackagesRequest>
{
    public GetPackagesRequestValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}
```

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackages/
git commit -m "feat(packaging): add GetPackages request/response/validator"
```

---

### Task 9: `GetPackagesHandler` + tests

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackages/GetPackagesHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Packaging/GetPackagesHandlerTests.cs`

- [ ] **Step 1: Write the failing tests first**

```csharp
using Anela.Heblo.Application.Features.Packaging.UseCases.GetPackages;
using Anela.Heblo.Domain.Features.Packaging;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Packaging;

public class GetPackagesHandlerTests
{
    private static Package MakePackage(int id, string orderCode = "ORD1", string customer = "Alice",
        string packageNumber = "PKG-1", DateTimeOffset? packedAt = null, string providerCode = "PPL")
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
        (List<Package> Items, int TotalCount) result)
    {
        repo = new Mock<IPackageRepository>();
        repo.Setup(r => r.GetPaginatedAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return new GetPackagesHandler(repo.Object);
    }

    [Fact]
    public async Task Handle_MapsItemsAndPagingFields()
    {
        // Arrange
        var packages = new List<Package> { MakePackage(1), MakePackage(2) };
        var sut = MakeSut(out _, (packages, 5));
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
        var sut = MakeSut(out var repo, (new List<Package>(), 0));
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
        await sut.Handle(request, CancellationToken.None);

        // Assert
        repo.Verify(r => r.GetPaginatedAsync(
            "ORD42", "Bob", null, null,
            new DateTime(2026, 5, 1), new DateTime(2026, 5, 31),
            3, 10,
            "CustomerName", false,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsEmptyList_WhenRepositoryReturnsNoItems()
    {
        var sut = MakeSut(out _, (new List<Package>(), 0));
        var response = await sut.Handle(new GetPackagesRequest(), CancellationToken.None);
        response.Items.Should().BeEmpty();
        response.TotalCount.Should().Be(0);
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure (handler does not exist)**

```bash
cd backend && dotnet test --filter FullyQualifiedName~GetPackagesHandlerTests
```

Expected: build error or `GetPackagesHandler` not found.

- [ ] **Step 3: Write the handler**

```csharp
using Anela.Heblo.Domain.Features.Packaging;
using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetPackages;

public class GetPackagesHandler : IRequestHandler<GetPackagesRequest, GetPackagesResponse>
{
    private readonly IPackageRepository _repo;

    public GetPackagesHandler(IPackageRepository repo) => _repo = repo;

    public async Task<GetPackagesResponse> Handle(GetPackagesRequest request, CancellationToken cancellationToken)
    {
        var (items, total) = await _repo.GetPaginatedAsync(
            request.OrderCode,
            request.CustomerName,
            request.PackageNumber,
            request.ShippingProviderCode,
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
                ShippingProviderName = p.ShippingProviderName,
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

- [ ] **Step 4: Run tests — expect PASS**

```bash
cd backend && dotnet test --filter FullyQualifiedName~GetPackagesHandlerTests
```

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackages/GetPackagesHandler.cs backend/test/Anela.Heblo.Tests/Features/Packaging/GetPackagesHandlerTests.cs
git commit -m "feat(packaging): add GetPackagesHandler with tests"
```

---

### Task 10: `DeletePackage` use case + tests

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/DeletePackage/DeletePackageRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/DeletePackage/DeletePackageResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/DeletePackage/DeletePackageHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Packaging/DeletePackageHandlerTests.cs`

- [ ] **Step 1: Write request and response (classes, not records)**

`DeletePackageRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.DeletePackage;

public class DeletePackageRequest : IRequest<DeletePackageResponse>
{
    public int Id { get; set; }
}
```

`DeletePackageResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.DeletePackage;

public class DeletePackageResponse : BaseResponse
{
    public bool Deleted { get; set; }

    public DeletePackageResponse(bool deleted)
    {
        Deleted = deleted;
    }

    public DeletePackageResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

- [ ] **Step 2: Write failing tests**

```csharp
using Anela.Heblo.Application.Features.Packaging.UseCases.DeletePackage;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Packaging;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Packaging;

public class DeletePackageHandlerTests
{
    private static (DeletePackageHandler Sut, Mock<IPackageRepository> Repo, Mock<IShipmentClient> Client)
        MakeSut(Package? loaded)
    {
        var repo = new Mock<IPackageRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(loaded);
        var client = new Mock<IShipmentClient>();
        var logger = NullLogger<DeletePackageHandler>.Instance;
        return (new DeletePackageHandler(repo.Object, client.Object, logger), repo, client);
    }

    private static Package SamplePackage(int id = 7) => new()
    {
        Id = id,
        OrderCode = "ORD-1",
        CustomerName = "Alice",
        PackageNumber = "PKG-1",
        ShippingProviderCode = "PPL",
        ShipmentGuid = Guid.NewGuid(),
        PackedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenPackageMissing()
    {
        var (sut, repo, client) = MakeSut(loaded: null);

        var response = await sut.Handle(new DeletePackageRequest { Id = 999 }, CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.PackageNotFound);
        response.Deleted.Should().BeFalse();
        client.Verify(c => c.CancelShipmentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.DeleteAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CallsCancelShipment_AndDeletesRow_OnSuccess()
    {
        var package = SamplePackage();
        var (sut, repo, client) = MakeSut(loaded: package);

        var response = await sut.Handle(new DeletePackageRequest { Id = package.Id }, CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Deleted.Should().BeTrue();
        client.Verify(c => c.CancelShipmentAsync(package.ShipmentGuid, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.DeleteAsync(package, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_StillDeletesRow_WhenShoptetCancelThrows()
    {
        var package = SamplePackage();
        var (sut, repo, client) = MakeSut(loaded: package);
        client.Setup(c => c.CancelShipmentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("shoptet 500"));

        var response = await sut.Handle(new DeletePackageRequest { Id = package.Id }, CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Deleted.Should().BeTrue();
        repo.Verify(r => r.DeleteAsync(package, It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 3: Run tests — expect compile failure (handler not yet defined)**

```bash
cd backend && dotnet test --filter FullyQualifiedName~DeletePackageHandlerTests
```

- [ ] **Step 4: Write the handler**

```csharp
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Packaging;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.DeletePackage;

public class DeletePackageHandler : IRequestHandler<DeletePackageRequest, DeletePackageResponse>
{
    private readonly IPackageRepository _repo;
    private readonly IShipmentClient _shipmentClient;
    private readonly ILogger<DeletePackageHandler> _logger;

    public DeletePackageHandler(
        IPackageRepository repo,
        IShipmentClient shipmentClient,
        ILogger<DeletePackageHandler> logger)
    {
        _repo = repo;
        _shipmentClient = shipmentClient;
        _logger = logger;
    }

    public async Task<DeletePackageResponse> Handle(DeletePackageRequest request, CancellationToken cancellationToken)
    {
        var package = await _repo.GetByIdAsync(request.Id, cancellationToken);
        if (package is null)
            return new DeletePackageResponse(ErrorCodes.PackageNotFound);

        try
        {
            await _shipmentClient.CancelShipmentAsync(package.ShipmentGuid, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to cancel Shoptet shipment {ShipmentGuid} for package {PackageId}; deleting local row anyway",
                package.ShipmentGuid, package.Id);
        }

        await _repo.DeleteAsync(package, cancellationToken);
        return new DeletePackageResponse(deleted: true);
    }
}
```

- [ ] **Step 5: Run tests — expect PASS**

```bash
cd backend && dotnet test --filter FullyQualifiedName~DeletePackageHandlerTests
```

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/DeletePackage/ backend/test/Anela.Heblo.Tests/Features/Packaging/DeletePackageHandlerTests.cs
git commit -m "feat(packaging): add DeletePackageHandler with tests"
```

---

### Task 11: Persist `Package` rows from `ScanPackingOrderHandler`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Packaging/ScanPackingOrderHandlerPackagePersistenceTests.cs`

- [ ] **Step 1: Inject `IPackageRepository` and `ICurrentUserService`**

Update the constructor — add fields:

```csharp
private readonly IPackageRepository _packageRepository;
private readonly ICurrentUserService _currentUserService;
```

Add constructor parameters at the end (keep existing parameters in their order):

```csharp
public ScanPackingOrderHandler(
    IShipmentClient shipmentClient,
    IPackingOrderClient orderClient,
    IEshopOrderClient eshopOrderClient,
    IOptions<ShipmentLabelsSettings> shipmentSettings,
    IOptions<ShoptetOrdersSettings> orderSettings,
    ILogger<ScanPackingOrderHandler> logger,
    IPackageRepository packageRepository,
    ICurrentUserService currentUserService)
{
    _shipmentClient = shipmentClient;
    _orderClient = orderClient;
    _eshopOrderClient = eshopOrderClient;
    _shipmentSettings = shipmentSettings.Value;
    _orderSettings = orderSettings.Value;
    _logger = logger;
    _packageRepository = packageRepository;
    _currentUserService = currentUserService;
}
```

Add usings: `Anela.Heblo.Domain.Features.Packaging;`, `Anela.Heblo.Domain.Features.Users;`.

- [ ] **Step 2: After the success branch (where `createdShipment` is returned), persist one row per package**

Locate the final `return new ScanPackingOrderResponse(orderData, new ScanShipmentData { ... AlreadyExisted = false });` block at the bottom of `Handle()`. Immediately before that final `return`, insert:

```csharp
await PersistPackagesAsync(
    request.OrderCode,
    orderData.CustomerName,
    command.CarrierCode,
    createdShipment.ShipmentGuid,
    packages,
    cancellationToken: ct);
```

Then add the helper method below `BuildShippingAddress`:

```csharp
private async Task PersistPackagesAsync(
    string orderCode,
    string customerName,
    string carrierCode,
    Guid shipmentGuid,
    IReadOnlyList<ScanShipmentPackage> packages,
    CancellationToken cancellationToken)
{
    var now = DateTimeOffset.UtcNow;
    var packedBy = _currentUserService.GetCurrentUser().Email;

    foreach (var pkg in packages)
    {
        try
        {
            await _packageRepository.AddAsync(new Package
            {
                OrderCode = orderCode,
                CustomerName = customerName,
                PackageNumber = pkg.Name,
                TrackingNumber = pkg.TrackingNumber,
                ShippingProviderCode = carrierCode,
                ShippingProviderName = null,
                ShipmentGuid = shipmentGuid,
                PackedAt = now,
                PackedBy = packedBy,
                CreatedAt = now,
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to persist Package row for order {OrderCode} package {PackageName}",
                orderCode, pkg.Name);
        }
    }
}
```

Persistence only runs on the new-shipment success branch (after `CreateShipmentAsync` succeeds). The `existingShipment is not null` branch and the ineligible branch are intentionally skipped — those rows either already exist locally or pre-date this feature.

If a row fails to insert (e.g. unique-index hit from a duplicate scan), we log a warning and continue — the scan itself succeeds regardless.

- [ ] **Step 3: Write tests**

```csharp
using Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Packaging;

public class ScanPackingOrderHandlerPackagePersistenceTests
{
    private static ScanPackingOrderHandler MakeSut(
        out Mock<IPackageRepository> packageRepo,
        Mock<IShipmentClient>? shipmentClient = null,
        Mock<IPackingOrderClient>? orderClient = null,
        PackingOrder? order = null,
        IReadOnlyList<ShipmentLabel>? existingLabels = null,
        IReadOnlyList<ShipmentLabel>? newLabels = null,
        IReadOnlyList<ShippingOption>? options = null,
        ShoptetOrdersSettings? orderSettings = null)
    {
        packageRepo = new Mock<IPackageRepository>();
        shipmentClient ??= new Mock<IShipmentClient>();
        orderClient ??= new Mock<IPackingOrderClient>();
        var eshopClient = new Mock<IEshopOrderClient>();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(c => c.GetCurrentUser())
            .Returns(new CurrentUser("uid-1", "Operator", "op@example.com", IsAuthenticated: true));

        orderClient.Setup(c => c.GetPackingOrderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        shipmentClient.SetupSequence(c => c.GetLabelsByOrderCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingLabels ?? Array.Empty<ShipmentLabel>())
            .ReturnsAsync(newLabels ?? Array.Empty<ShipmentLabel>());
        shipmentClient.Setup(c => c.GetShippingOptionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(options ?? new[] { new ShippingOption { CarrierCode = "PPL" } });
        shipmentClient.Setup(c => c.CreateShipmentAsync(It.IsAny<CreateShipmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedShipment { ShipmentGuid = Guid.NewGuid() });

        var shipmentSettings = Options.Create(new ShipmentLabelsSettings
        {
            MinPackageWeightGrams = 100,
            DefaultPackageWidthMm = 100,
            DefaultPackageHeightMm = 100,
            DefaultPackageDepthMm = 100,
        });
        var ordSettings = Options.Create(orderSettings ?? new ShoptetOrdersSettings { PackingStateId = 1, PackedStateId = 2 });

        return new ScanPackingOrderHandler(
            shipmentClient.Object,
            orderClient.Object,
            eshopClient.Object,
            shipmentSettings,
            ordSettings,
            NullLogger<ScanPackingOrderHandler>.Instance,
            packageRepo.Object,
            currentUser.Object);
    }

    private static PackingOrder MakeOrder(int statusId = 1) => new()
    {
        Code = "ORD-1",
        CustomerName = "Alice",
        ShippingMethodName = "PPL",
        StatusId = statusId,
        Items = new List<PackingOrderItem>
        {
            new() { WeightGrams = 500, Quantity = 1 },
        },
    };

    [Fact]
    public async Task Handle_PersistsOnePackageRowPerCreatedPackage()
    {
        var newLabels = new List<ShipmentLabel>
        {
            new() { PackageName = "PKG-1", ShipmentGuid = Guid.NewGuid(), TrackingNumber = "TRK1" },
            new() { PackageName = "PKG-2", ShipmentGuid = Guid.NewGuid(), TrackingNumber = "TRK2" },
        };
        var sut = MakeSut(out var repo, order: MakeOrder(), newLabels: newLabels);

        var response = await sut.Handle(new ScanPackingOrderRequest { OrderCode = "ORD-1" }, CancellationToken.None);

        response.Success.Should().BeTrue();
        repo.Verify(r => r.AddAsync(It.Is<Package>(p =>
            p.OrderCode == "ORD-1" && p.CustomerName == "Alice" && p.ShippingProviderCode == "PPL"
            && p.PackedBy == "op@example.com"),
            It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_DoesNotPersist_WhenShipmentAlreadyExisted()
    {
        var existingLabels = new List<ShipmentLabel>
        {
            new() { PackageName = "PKG-1", ShipmentGuid = Guid.NewGuid() },
        };
        var sut = MakeSut(out var repo, order: MakeOrder(), existingLabels: existingLabels);

        var response = await sut.Handle(new ScanPackingOrderRequest { OrderCode = "ORD-1" }, CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Shipment!.AlreadyExisted.Should().BeTrue();
        repo.Verify(r => r.AddAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DoesNotFailScan_WhenPersistenceThrows()
    {
        var newLabels = new List<ShipmentLabel>
        {
            new() { PackageName = "PKG-1", ShipmentGuid = Guid.NewGuid() },
        };
        var sut = MakeSut(out var repo, order: MakeOrder(), newLabels: newLabels);
        repo.Setup(r => r.AddAsync(It.IsAny<Package>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("duplicate key"));

        var response = await sut.Handle(new ScanPackingOrderRequest { OrderCode = "ORD-1" }, CancellationToken.None);

        response.Success.Should().BeTrue();
    }
}
```

> Implementer note: confirm exact property names on `ShipmentLabel`, `PackingOrder`, `PackingOrderItem`, `CreateShipmentCommand`, `CreatedShipment`, `ShippingOption` by Read on the existing types before running the tests — adjust the test factory if a property name differs. The test file is the *only* place this matters; the handler change uses fields that are already proven in the existing handler source.

- [ ] **Step 4: Run packaging tests until green**

```bash
cd backend && dotnet test --filter FullyQualifiedName~Packaging
```

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs backend/test/Anela.Heblo.Tests/Features/Packaging/ScanPackingOrderHandlerPackagePersistenceTests.cs
git commit -m "feat(packaging): persist Package row on successful scan"
```

---

### Task 12: Create `PackagingModule.cs` and register validators

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/PackagingModule.cs`
- Modify: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` (or the composition root)

- [ ] **Step 1: Write the module — mirror `ShipmentLabelsModule.cs`**

```csharp
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Packaging.UseCases.GetPackages;
using Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder;
using Anela.Heblo.Application.Features.Packaging.Validators;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Packaging;

public static class PackagingModule
{
    public static IServiceCollection AddPackagingModule(this IServiceCollection services)
    {
        services.AddScoped<IValidator<ScanPackingOrderRequest>, ScanPackingOrderRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<ScanPackingOrderRequest, ScanPackingOrderResponse>,
            ValidationBehavior<ScanPackingOrderRequest, ScanPackingOrderResponse>>();

        services.AddScoped<IValidator<GetPackagesRequest>, GetPackagesRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<GetPackagesRequest, GetPackagesResponse>,
            ValidationBehavior<GetPackagesRequest, GetPackagesResponse>>();

        return services;
    }
}
```

- [ ] **Step 2: Wire it into the composition root**

Open `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`. Find where `AddShipmentLabelsModule(configuration)` is called. Add directly below:

```csharp
services.AddPackagingModule();
```

Add `using Anela.Heblo.Application.Features.Packaging;` at the top of the file.

If `AddShipmentLabelsModule` is called in a different composition file, mirror the pattern there instead.

- [ ] **Step 3: Build**

```bash
cd backend && dotnet build
```

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/PackagingModule.cs backend/src/Anela.Heblo.API/
git commit -m "feat(packaging): add PackagingModule and register validators"
```

---

### Task 13: API endpoints in `PackagingController`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/PackagingController.cs`

- [ ] **Step 1: Add `GET /api/packaging/packages` and `DELETE /api/packaging/packages/{id}`**

Add the necessary usings at the top:

```csharp
using Anela.Heblo.Application.Features.Packaging.UseCases.DeletePackage;
using Anela.Heblo.Application.Features.Packaging.UseCases.GetPackages;
```

Add the actions inside the class (after the existing `GetPackageLabelPdf` action):

```csharp
[HttpGet("packages")]
public async Task<ActionResult<GetPackagesResponse>> GetPackages(
    [FromQuery] GetPackagesRequest request,
    CancellationToken cancellationToken)
{
    var response = await _mediator.Send(request, cancellationToken);
    return HandleResponse(response);
}

[HttpDelete("packages/{id:int}")]
public async Task<ActionResult<DeletePackageResponse>> DeletePackage(
    [FromRoute] int id,
    CancellationToken cancellationToken)
{
    var response = await _mediator.Send(new DeletePackageRequest { Id = id }, cancellationToken);
    return HandleResponse(response);
}
```

The controller already has `[Authorize]` on the class — the new actions inherit it. `HandleResponse(response)` already maps `BaseResponse.ErrorCode` to the correct HTTP status via `BaseApiController`.

- [ ] **Step 2: Build & verify Swagger**

```bash
cd backend && dotnet build
```

Start the API locally and confirm the new endpoints appear in Swagger at `/swagger`.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/PackagingController.cs
git commit -m "feat(packaging): expose GET /packages and DELETE /packages/{id}"
```

---

### Task 14: Backend validation gate

- [ ] **Step 1: Run `dotnet build` + `dotnet format`**

```bash
cd backend && dotnet build && dotnet format --verify-no-changes
```

- [ ] **Step 2: Run full packaging test suite**

```bash
cd backend && dotnet test --filter FullyQualifiedName~Packaging
```

- [ ] **Step 3: If clean, no commit needed — proceed to frontend tasks**

---

## Frontend Tasks

### Task 15: Regenerate API client

- [ ] **Step 1: Trigger client regeneration via build**

```bash
cd frontend && npm run build
```

- [ ] **Step 2: Verify generated TypeScript contains the new operations**

```bash
grep -r "getPackages\|deletePackage" frontend/src/api/generated/ | head
```

Expected: matches under the `PackagingApi` (or equivalently named) class.

- [ ] **Step 3: Commit the regenerated client**

```bash
git add frontend/src/api/generated/
git commit -m "feat(packaging): regenerate API client with package endpoints"
```

---

### Task 16: TanStack Query hooks for packages

**Files:**
- Create: `frontend/src/api/hooks/usePackages.ts`

- [ ] **Step 1: Implement query + mutation hooks**

```typescript
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";

export type PackageDto = {
  id: number;
  orderCode: string;
  customerName: string;
  packageNumber: string;
  trackingNumber?: string;
  shippingProviderCode: string;
  shippingProviderName?: string;
  packedAt: string;
  packedBy?: string;
};

export type GetPackagesRequest = {
  orderCode?: string;
  customerName?: string;
  packageNumber?: string;
  shippingProviderCode?: string;
  fromDate?: string;
  toDate?: string;
  pageNumber: number;
  pageSize: number;
  sortBy: string;
  sortDescending: boolean;
};

export type GetPackagesResponse = {
  items: PackageDto[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
};

export const packageKeys = {
  all: ["packages"] as const,
  list: (req: GetPackagesRequest) => [...packageKeys.all, "list", req] as const,
};

export const usePackagesQuery = (request: GetPackagesRequest) =>
  useQuery({
    queryKey: packageKeys.list(request),
    queryFn: async (): Promise<GetPackagesResponse> => {
      const apiClient = getAuthenticatedApiClient(false) as any;
      const params = new URLSearchParams();
      Object.entries(request).forEach(([k, v]) => {
        if (v !== undefined && v !== null && v !== "") params.append(k, String(v));
      });
      const url = `${apiClient.baseUrl}/api/packaging/packages?${params.toString()}`;
      const res = await apiClient.http.fetch(url, {
        method: "GET",
        headers: { Accept: "application/json" },
      });
      if (!res.ok) throw new Error(`Failed to load packages: ${res.status}`);
      return res.json();
    },
    staleTime: 1000 * 30,
  });

export const useDeletePackageMutation = () => {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) => {
      const apiClient = getAuthenticatedApiClient(false) as any;
      const url = `${apiClient.baseUrl}/api/packaging/packages/${id}`;
      const res = await apiClient.http.fetch(url, {
        method: "DELETE",
        headers: { Accept: "application/json" },
      });
      if (!res.ok) throw new Error(`Failed to delete package: ${res.status}`);
      return res.json();
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: packageKeys.all });
    },
  });
};
```

Per project CLAUDE.md, API hooks must use absolute URLs (`apiClient.baseUrl + relativeUrl`) — relative URLs hit the dev port 3001 instead of 5001. Mirror the convention from `usePurchaseOrders.ts` (read it first if anything above looks unfamiliar).

- [ ] **Step 2: Commit**

```bash
git add frontend/src/api/hooks/usePackages.ts
git commit -m "feat(packaging): add usePackages query and delete hooks"
```

---

### Task 17: `ZasilkyTable` component

**Files:**
- Create: `frontend/src/components/baleni/zasilky/ZasilkyTable.tsx`

- [ ] **Step 1: Implement the table**

```typescript
import { Printer, Trash2 } from "lucide-react";
import type { PackageDto } from "../../../api/hooks/usePackages";

export type ZasilkySortBy =
  | "OrderCode"
  | "CustomerName"
  | "PackageNumber"
  | "ShippingProvider"
  | "PackedAt";

interface Props {
  items: PackageDto[];
  sortBy: ZasilkySortBy;
  sortDescending: boolean;
  onSortChange: (sortBy: ZasilkySortBy) => void;
  onReprint: (pkg: PackageDto) => void;
  onDelete: (pkg: PackageDto) => void;
}

export function ZasilkyTable({
  items,
  sortBy,
  sortDescending,
  onSortChange,
  onReprint,
  onDelete,
}: Props) {
  const indicator = (col: ZasilkySortBy) =>
    sortBy === col ? (sortDescending ? " ↓" : " ↑") : "";

  return (
    <table className="min-w-full text-base">
      <thead className="bg-slate-100 sticky top-0">
        <tr>
          <th className="px-4 py-3 text-left cursor-pointer" onClick={() => onSortChange("OrderCode")}>
            Objednávka{indicator("OrderCode")}
          </th>
          <th className="px-4 py-3 text-left cursor-pointer" onClick={() => onSortChange("CustomerName")}>
            Zákazník{indicator("CustomerName")}
          </th>
          <th className="px-4 py-3 text-left cursor-pointer" onClick={() => onSortChange("PackageNumber")}>
            Balík{indicator("PackageNumber")}
          </th>
          <th className="px-4 py-3 text-left">Sledovací č.</th>
          <th className="px-4 py-3 text-left cursor-pointer" onClick={() => onSortChange("ShippingProvider")}>
            Dopravce{indicator("ShippingProvider")}
          </th>
          <th className="px-4 py-3 text-left cursor-pointer" onClick={() => onSortChange("PackedAt")}>
            Zabaleno{indicator("PackedAt")}
          </th>
          <th className="px-4 py-3 text-right">Akce</th>
        </tr>
      </thead>
      <tbody>
        {items.map((p) => (
          <tr key={p.id} className="border-t hover:bg-slate-50">
            <td className="px-4 py-3 font-mono">{p.orderCode}</td>
            <td className="px-4 py-3">{p.customerName}</td>
            <td className="px-4 py-3">{p.packageNumber}</td>
            <td className="px-4 py-3 font-mono text-sm">{p.trackingNumber ?? "—"}</td>
            <td className="px-4 py-3">{p.shippingProviderName ?? p.shippingProviderCode}</td>
            <td className="px-4 py-3">{new Date(p.packedAt).toLocaleString("cs-CZ")}</td>
            <td className="px-4 py-3 text-right">
              <div className="inline-flex gap-2">
                <button
                  type="button"
                  onClick={() => onReprint(p)}
                  className="inline-flex items-center gap-1 px-3 py-2 rounded bg-indigo-600 text-white"
                >
                  <Printer className="w-4 h-4" /> Tisk
                </button>
                <button
                  type="button"
                  onClick={() => onDelete(p)}
                  className="inline-flex items-center gap-1 px-3 py-2 rounded bg-red-600 text-white"
                >
                  <Trash2 className="w-4 h-4" /> Smazat
                </button>
              </div>
            </td>
          </tr>
        ))}
        {items.length === 0 && (
          <tr>
            <td className="px-4 py-8 text-center text-slate-500" colSpan={7}>
              Žádné zásilky.
            </td>
          </tr>
        )}
      </tbody>
    </table>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add frontend/src/components/baleni/zasilky/ZasilkyTable.tsx
git commit -m "feat(packaging): ZasilkyTable component"
```

---

### Task 18: `ZasilkyFilters` component

**Files:**
- Create: `frontend/src/components/baleni/zasilky/ZasilkyFilters.tsx`

- [ ] **Step 1: Implement debounced filter inputs**

```typescript
import { useEffect, useState } from "react";

export interface FilterValues {
  orderCode: string;
  customerName: string;
  packageNumber: string;
  shippingProviderCode: string;
  fromDate: string;
  toDate: string;
}

interface Props {
  value: FilterValues;
  onChange: (value: FilterValues) => void;
}

export function ZasilkyFilters({ value, onChange }: Props) {
  const [local, setLocal] = useState<FilterValues>(value);

  useEffect(() => {
    const t = setTimeout(() => onChange(local), 300);
    return () => clearTimeout(t);
  }, [local, onChange]);

  const update =
    (k: keyof FilterValues) =>
    (e: React.ChangeEvent<HTMLInputElement>) =>
      setLocal({ ...local, [k]: e.target.value });

  return (
    <div className="grid grid-cols-2 md:grid-cols-6 gap-3 p-4 bg-slate-50 border-b">
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
      <input
        className="px-3 py-2 border rounded"
        placeholder="Dopravce (kód)"
        value={local.shippingProviderCode}
        onChange={update("shippingProviderCode")}
      />
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
    </div>
  );
}
```

Per the CLAUDE.md immutability rule, `setLocal({ ...local, ... })` returns a new object.

- [ ] **Step 2: Commit**

```bash
git add frontend/src/components/baleni/zasilky/ZasilkyFilters.tsx
git commit -m "feat(packaging): ZasilkyFilters component"
```

---

### Task 19: `ZasilkyPagination` component

**Files:**
- Create: `frontend/src/components/baleni/zasilky/ZasilkyPagination.tsx`

- [ ] **Step 1: Implement pager**

```typescript
interface Props {
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  onPageChange: (page: number) => void;
}

export function ZasilkyPagination({ pageNumber, pageSize, totalCount, onPageChange }: Props) {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  return (
    <div className="flex items-center justify-between p-4 border-t bg-white">
      <span className="text-slate-600">
        Celkem {totalCount} · strana {pageNumber} / {totalPages}
      </span>
      <div className="inline-flex gap-2">
        <button
          type="button"
          disabled={pageNumber <= 1}
          onClick={() => onPageChange(pageNumber - 1)}
          className="px-4 py-2 rounded border disabled:opacity-50"
        >
          Předchozí
        </button>
        <button
          type="button"
          disabled={pageNumber >= totalPages}
          onClick={() => onPageChange(pageNumber + 1)}
          className="px-4 py-2 rounded border disabled:opacity-50"
        >
          Další
        </button>
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add frontend/src/components/baleni/zasilky/ZasilkyPagination.tsx
git commit -m "feat(packaging): ZasilkyPagination component"
```

---

### Task 20: `DeletePackageDialog`

**Files:**
- Create: `frontend/src/components/baleni/zasilky/DeletePackageDialog.tsx`

- [ ] **Step 1: Implement the confirm modal**

```typescript
import { AlertTriangle } from "lucide-react";
import type { PackageDto } from "../../../api/hooks/usePackages";

interface Props {
  pkg: PackageDto | null;
  isDeleting: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

export function DeletePackageDialog({ pkg, isDeleting, onConfirm, onCancel }: Props) {
  if (!pkg) return null;
  return (
    <div className="fixed inset-0 bg-black/40 z-50 flex items-center justify-center p-4">
      <div className="bg-white rounded-lg shadow-xl max-w-md w-full p-6">
        <div className="flex items-start gap-3">
          <AlertTriangle className="w-6 h-6 text-red-600 flex-shrink-0" />
          <div>
            <h3 className="text-lg font-semibold">Smazat zásilku?</h3>
            <p className="mt-2 text-slate-600">
              Smaže zásilku <strong>{pkg.packageNumber}</strong> pro objednávku{" "}
              <strong>{pkg.orderCode}</strong> a zruší ji v Shoptetu. Akci nelze vrátit.
            </p>
          </div>
        </div>
        <div className="mt-6 flex justify-end gap-2">
          <button
            type="button"
            onClick={onCancel}
            disabled={isDeleting}
            className="px-4 py-2 rounded border"
          >
            Zrušit
          </button>
          <button
            type="button"
            onClick={onConfirm}
            disabled={isDeleting}
            className="px-4 py-2 rounded bg-red-600 text-white disabled:opacity-50"
          >
            {isDeleting ? "Maže se..." : "Smazat"}
          </button>
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add frontend/src/components/baleni/zasilky/DeletePackageDialog.tsx
git commit -m "feat(packaging): DeletePackageDialog component"
```

---

### Task 21: `ZasilkyPage` — main composition

**Files:**
- Create: `frontend/src/components/baleni/zasilky/ZasilkyPage.tsx`

- [ ] **Step 1: Verify `printLabelPdf` signature**

```bash
sed -n '1,40p' frontend/src/components/baleni/printLabelPdf.ts
```

If the function signature differs from `printLabelPdf(orderCode, label, onAfterPrint)` with `label.packageName` required, adjust the call below accordingly.

- [ ] **Step 2: Write the page**

```typescript
import { useCallback, useMemo, useState } from "react";
import { useToastContext } from "../../../contexts/ToastContext";
import {
  useDeletePackageMutation,
  usePackagesQuery,
  type PackageDto,
} from "../../../api/hooks/usePackages";
import { printLabelPdf } from "../printLabelPdf";
import { ZasilkyFilters, type FilterValues } from "./ZasilkyFilters";
import { ZasilkyTable, type ZasilkySortBy } from "./ZasilkyTable";
import { ZasilkyPagination } from "./ZasilkyPagination";
import { DeletePackageDialog } from "./DeletePackageDialog";

const PAGE_SIZE = 20;

export function ZasilkyPage() {
  const { showSuccess, showError } = useToastContext();
  const [filters, setFilters] = useState<FilterValues>({
    orderCode: "",
    customerName: "",
    packageNumber: "",
    shippingProviderCode: "",
    fromDate: "",
    toDate: "",
  });
  const [pageNumber, setPageNumber] = useState(1);
  const [sortBy, setSortBy] = useState<ZasilkySortBy>("PackedAt");
  const [sortDescending, setSortDescending] = useState(true);
  const [pendingDelete, setPendingDelete] = useState<PackageDto | null>(null);

  const request = useMemo(
    () => ({
      orderCode: filters.orderCode || undefined,
      customerName: filters.customerName || undefined,
      packageNumber: filters.packageNumber || undefined,
      shippingProviderCode: filters.shippingProviderCode || undefined,
      fromDate: filters.fromDate || undefined,
      toDate: filters.toDate || undefined,
      pageNumber,
      pageSize: PAGE_SIZE,
      sortBy,
      sortDescending,
    }),
    [filters, pageNumber, sortBy, sortDescending],
  );

  const { data, isLoading, isError } = usePackagesQuery(request);
  const deleteMutation = useDeletePackageMutation();

  const handleSortChange = useCallback(
    (col: ZasilkySortBy) => {
      if (col === sortBy) {
        setSortDescending((d) => !d);
      } else {
        setSortBy(col);
        setSortDescending(true);
      }
    },
    [sortBy],
  );

  const handleFiltersChange = useCallback((next: FilterValues) => {
    setFilters(next);
    setPageNumber(1);
  }, []);

  const handleReprint = useCallback(
    (pkg: PackageDto) => {
      printLabelPdf(pkg.orderCode, { packageName: pkg.packageNumber } as any, () => {
        showSuccess("Tisk", `Štítek balíku ${pkg.packageNumber} odeslán na tiskárnu.`);
      });
    },
    [showSuccess],
  );

  const confirmDelete = useCallback(async () => {
    if (!pendingDelete) return;
    try {
      await deleteMutation.mutateAsync(pendingDelete.id);
      showSuccess("Smazáno", `Zásilka ${pendingDelete.packageNumber} byla smazána.`);
      setPendingDelete(null);
    } catch (e) {
      showError("Chyba", e instanceof Error ? e.message : "Smazání selhalo.");
    }
  }, [deleteMutation, pendingDelete, showSuccess, showError]);

  return (
    <div className="flex flex-col h-full bg-white">
      <ZasilkyFilters value={filters} onChange={handleFiltersChange} />
      <div className="flex-1 overflow-auto">
        {isLoading && <div className="p-8 text-center text-slate-500">Načítám…</div>}
        {isError && (
          <div className="p-8 text-center text-red-600">Nepodařilo se načíst zásilky.</div>
        )}
        {data && (
          <ZasilkyTable
            items={data.items}
            sortBy={sortBy}
            sortDescending={sortDescending}
            onSortChange={handleSortChange}
            onReprint={handleReprint}
            onDelete={setPendingDelete}
          />
        )}
      </div>
      {data && (
        <ZasilkyPagination
          pageNumber={pageNumber}
          pageSize={PAGE_SIZE}
          totalCount={data.totalCount}
          onPageChange={setPageNumber}
        />
      )}
      <DeletePackageDialog
        pkg={pendingDelete}
        isDeleting={deleteMutation.isPending}
        onConfirm={confirmDelete}
        onCancel={() => setPendingDelete(null)}
      />
    </div>
  );
}
```

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/baleni/zasilky/ZasilkyPage.tsx
git commit -m "feat(packaging): ZasilkyPage composition"
```

---

### Task 22: Wire `ZasilkyPage` into the Balení route

**Files:**
- Modify: `frontend/src/App.tsx`

- [ ] **Step 1: Find the placeholder**

```bash
grep -n "BaleniPlaceholder title=\"Zásilky\"" frontend/src/App.tsx
```

- [ ] **Step 2: Replace with the real page**

Locate the line:

```typescript
<Route path="zasilky" element={<BaleniPlaceholder title="Zásilky" />} />
```

Replace with:

```typescript
<Route path="zasilky" element={<ZasilkyPage />} />
```

Add the import near the other Balení imports:

```typescript
import { ZasilkyPage } from "./components/baleni/zasilky/ZasilkyPage";
```

- [ ] **Step 3: Commit**

```bash
git add frontend/src/App.tsx
git commit -m "feat(packaging): mount ZasilkyPage at /baleni/zasilky"
```

---

### Task 23: Frontend tests — `ZasilkyPage`

**Files:**
- Create: `frontend/test/components/baleni/zasilky/ZasilkyPage.test.tsx`

- [ ] **Step 1: Write tests**

```typescript
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import type { PackageDto } from "../../../../src/api/hooks/usePackages";

const mockUsePackages = vi.fn();
const mockUseDelete = vi.fn();
const mockPrint = vi.fn();
const mockToast = { showSuccess: vi.fn(), showError: vi.fn() };

vi.mock("../../../../src/api/hooks/usePackages", () => ({
  usePackagesQuery: (...args: unknown[]) => mockUsePackages(...args),
  useDeletePackageMutation: () => mockUseDelete(),
}));

vi.mock("../../../../src/components/baleni/printLabelPdf", () => ({
  printLabelPdf: (...args: unknown[]) => mockPrint(...args),
}));

vi.mock("../../../../src/contexts/ToastContext", () => ({
  useToastContext: () => mockToast,
}));

import { ZasilkyPage } from "../../../../src/components/baleni/zasilky/ZasilkyPage";

const samplePackage: PackageDto = {
  id: 1,
  orderCode: "ORD-1",
  customerName: "Alice",
  packageNumber: "PKG-1",
  trackingNumber: "TRK-1",
  shippingProviderCode: "PPL",
  packedAt: "2026-05-25T10:00:00Z",
};

describe("ZasilkyPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseDelete.mockReturnValue({ mutateAsync: vi.fn().mockResolvedValue({}), isPending: false });
  });

  it("renders loading state", () => {
    mockUsePackages.mockReturnValue({ data: undefined, isLoading: true, isError: false });
    render(<ZasilkyPage />);
    expect(screen.getByText(/Načítám/)).toBeInTheDocument();
  });

  it("renders error state", () => {
    mockUsePackages.mockReturnValue({ data: undefined, isLoading: false, isError: true });
    render(<ZasilkyPage />);
    expect(screen.getByText(/Nepodařilo se načíst zásilky/)).toBeInTheDocument();
  });

  it("renders items when query succeeds", () => {
    mockUsePackages.mockReturnValue({
      data: { items: [samplePackage], totalCount: 1, pageNumber: 1, pageSize: 20 },
      isLoading: false,
      isError: false,
    });
    render(<ZasilkyPage />);
    expect(screen.getByText("ORD-1")).toBeInTheDocument();
    expect(screen.getByText("Alice")).toBeInTheDocument();
  });

  it("calls printLabelPdf when reprint clicked", () => {
    mockUsePackages.mockReturnValue({
      data: { items: [samplePackage], totalCount: 1, pageNumber: 1, pageSize: 20 },
      isLoading: false,
      isError: false,
    });
    render(<ZasilkyPage />);
    fireEvent.click(screen.getByRole("button", { name: /Tisk/ }));
    expect(mockPrint).toHaveBeenCalledWith(
      "ORD-1",
      expect.objectContaining({ packageName: "PKG-1" }),
      expect.any(Function),
    );
  });

  it("opens delete dialog and calls mutation on confirm", async () => {
    const mutateAsync = vi.fn().mockResolvedValue({});
    mockUseDelete.mockReturnValue({ mutateAsync, isPending: false });
    mockUsePackages.mockReturnValue({
      data: { items: [samplePackage], totalCount: 1, pageNumber: 1, pageSize: 20 },
      isLoading: false,
      isError: false,
    });
    render(<ZasilkyPage />);
    fireEvent.click(screen.getByRole("button", { name: /Smazat/ }));
    expect(screen.getByText(/Smazat zásilku\?/)).toBeInTheDocument();
    fireEvent.click(screen.getAllByRole("button", { name: /Smazat/ })[1]);
    await waitFor(() => expect(mutateAsync).toHaveBeenCalledWith(1));
  });
});
```

> If a test fails due to a different module path (e.g. ToastContext exports a hook with a different name), update the mock target — don't change production code to fit the test.

- [ ] **Step 2: Run tests until green**

```bash
cd frontend && npx vitest run test/components/baleni/zasilky/ZasilkyPage.test.tsx
```

- [ ] **Step 3: Commit**

```bash
git add frontend/test/components/baleni/zasilky/ZasilkyPage.test.tsx
git commit -m "test(packaging): ZasilkyPage tests"
```

---

### Task 24: Frontend validation gate

- [ ] **Step 1: Build + lint**

```bash
cd frontend && npm run build && npm run lint
```

- [ ] **Step 2: Fix any issues; no commit if clean**

---

## Verification (end-to-end)

1. Apply the migration manually:
   ```bash
   cd backend && dotnet ef database update \
     --project src/Anela.Heblo.Persistence \
     --startup-project src/Anela.Heblo.API
   ```
2. Start backend + frontend in dev mode.
3. From the Balení touch interface, scan a test order → verify shipment is created and label prints.
4. Open the DB; confirm one `Packages` row per package was inserted with correct order code, customer name, package number, tracking number, carrier code, packed-at, packed-by.
5. Navigate to **Zásilky** (`/baleni/zasilky`).
   - Newly created packages appear at the top (sorted by PackedAt desc).
   - Each filter narrows the grid correctly (debounced ~300 ms).
   - Sortable column headers toggle sort direction and update the indicator.
   - Pagination buttons disable correctly at boundaries.
6. Click **Tisk** on a row → confirm the PDF is silently sent to the configured printer (or opens in a new tab on fallback).
7. Click **Smazat** on a row → dialog appears → on confirm:
   - The row disappears from the grid (cache invalidated).
   - The shipment is cancelled in Shoptet (verify in Shoptet admin or via API).
   - Toast shows success.
8. `cd backend && dotnet test --filter FullyQualifiedName~Packaging` — all green.
9. `cd frontend && npm test -- zasilky` — all green.
10. `dotnet format` and `npm run lint` — clean.

## Out of scope

- Historical backfill of packages created in Shoptet before this change shipped.
- Scheduled sync from Shoptet — `Packages` is written only by `ScanPackingOrderHandler`.
- Bulk delete / bulk reprint.
- Per-package audit log beyond `PackedBy`.
- A separate admin (non-touch) Zásilky page in the main app.
