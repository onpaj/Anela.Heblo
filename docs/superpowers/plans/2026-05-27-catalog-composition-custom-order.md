# Catalog Composition Custom Order Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user reorder ingredients on the "Složení" tab of the catalog detail card via drag-and-drop in an explicit edit mode, with the custom order persisted locally and overlaid on top of the Abra Flexi BoM.

**Architecture:** A new local table `ProductIngredientOrders` stores `(parentProductCode, ingredientProductCode, sortOrder)` triples. The existing `GET /api/catalog/{productCode}/composition` handler joins these records onto the live Abra Flexi BoM response and emits an `order` field per ingredient (contiguous 1..N, items without saved order sorted to the end). A new `PUT /api/catalog/{productCode}/composition/order` endpoint upserts the order list, deletes obsolete rows, and returns success. The frontend gains an Order column, an "Upravit pořadí" / Save / Cancel button group, and drag-and-drop reordering in edit mode using `@dnd-kit/sortable` (already installed).

**Tech Stack:** .NET 8, EF Core (PostgreSQL), MediatR, MVC controllers, xUnit + FluentAssertions + Moq, React + TypeScript, @tanstack/react-query, @dnd-kit/sortable, Jest + React Testing Library.

---

## File Structure

### Backend (new files)
- `backend/src/Anela.Heblo.Domain/Features/Catalog/ProductIngredientOrder.cs` — entity (PK Id, ParentProductCode, IngredientProductCode, SortOrder, UpdatedAt, UpdatedBy)
- `backend/src/Anela.Heblo.Domain/Features/Catalog/IProductIngredientOrderRepository.cs` — repo interface
- `backend/src/Anela.Heblo.Persistence/Catalog/ProductIngredientOrder/ProductIngredientOrderConfiguration.cs` — EF Core mapping
- `backend/src/Anela.Heblo.Persistence/Catalog/ProductIngredientOrder/ProductIngredientOrderRepository.cs` — repo implementation
- `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddProductIngredientOrder.cs` — EF migration (generated)
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/UpdateProductCompositionOrder/UpdateProductCompositionOrderRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/UpdateProductCompositionOrder/UpdateProductCompositionOrderResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/UpdateProductCompositionOrder/UpdateProductCompositionOrderHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductCompositionHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Catalog/UpdateProductCompositionOrderHandlerTests.cs`

### Backend (modified files)
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/IngredientDto.cs` — add `Order`
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/GetProductCompositionHandler.cs` — overlay + sort
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — DI registration
- `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` — DbSet
- `backend/src/Anela.Heblo.API/Controllers/CatalogController.cs` — PUT endpoint

### Frontend (new files)
- `frontend/src/components/catalog/detail/tabs/CompositionTabRow.tsx` — sortable row component
- `frontend/src/api/hooks/useUpdateProductCompositionOrder.ts` — mutation hook

### Frontend (modified files)
- `frontend/src/api/hooks/useCatalog.ts` — add `order` to `IngredientDto`
- `frontend/src/components/catalog/detail/tabs/CompositionTab.tsx` — Order column, edit mode, DnD
- `frontend/src/components/catalog/detail/tabs/__tests__/CompositionTab.test.tsx` — new test cases

---

## Conventions used in this plan

- All commits are `feat:` for new behavior, `test:` for test-only commits, per `~/.claude/rules/git-workflow.md`. No `@claude` co-author trailer (attribution disabled globally — `CLAUDE.md`).
- Per project rule: **DTOs are plain classes**, never C# records.
- Per project rule: **Frontend API hooks build absolute URLs as `${apiClient.baseUrl}${relativeUrl}`**.
- Per project rule: **Migrations are NOT applied automatically** — generation only.
- `dotnet format` runs after every backend change; `npm run lint` after every frontend change.

---

## Task 1: Domain entity `ProductIngredientOrder`

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Catalog/ProductIngredientOrder.cs`

- [ ] **Step 1: Create the entity**

```csharp
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Catalog;

public class ProductIngredientOrder : Entity<int>
{
    public string ParentProductCode { get; set; } = null!;
    public string IngredientProductCode { get; set; } = null!;
    public int SortOrder { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Catalog/ProductIngredientOrder.cs
git commit -m "feat: add ProductIngredientOrder domain entity"
```

---

## Task 2: Repository interface

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Catalog/IProductIngredientOrderRepository.cs`

- [ ] **Step 1: Create the interface**

```csharp
namespace Anela.Heblo.Domain.Features.Catalog;

public interface IProductIngredientOrderRepository
{
    Task<List<ProductIngredientOrder>> ListByParentAsync(
        string parentProductCode,
        CancellationToken cancellationToken = default);

    Task<ProductIngredientOrder> CreateAsync(
        ProductIngredientOrder entity,
        CancellationToken cancellationToken = default);

    Task<ProductIngredientOrder> UpdateAsync(
        ProductIngredientOrder entity,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Catalog/IProductIngredientOrderRepository.cs
git commit -m "feat: add IProductIngredientOrderRepository interface"
```

---

## Task 3: EF Core configuration

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Catalog/ProductIngredientOrder/ProductIngredientOrderConfiguration.cs`

- [ ] **Step 1: Create the configuration**

```csharp
using Anela.Heblo.Domain.Features.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Catalog.ProductIngredientOrder;

public class ProductIngredientOrderConfiguration : IEntityTypeConfiguration<Domain.Features.Catalog.ProductIngredientOrder>
{
    public void Configure(EntityTypeBuilder<Domain.Features.Catalog.ProductIngredientOrder> builder)
    {
        builder.ToTable("ProductIngredientOrders", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ParentProductCode)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.IngredientProductCode)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.SortOrder)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.UpdatedBy)
            .HasMaxLength(100);

        builder.HasIndex(x => x.ParentProductCode)
            .HasDatabaseName("IX_ProductIngredientOrders_ParentProductCode");

        builder.HasIndex(x => new { x.ParentProductCode, x.IngredientProductCode })
            .IsUnique()
            .HasDatabaseName("UX_ProductIngredientOrders_Parent_Ingredient");
    }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Catalog/ProductIngredientOrder/ProductIngredientOrderConfiguration.cs
git commit -m "feat: add EF Core configuration for ProductIngredientOrder"
```

---

## Task 4: Repository implementation

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Catalog/ProductIngredientOrder/ProductIngredientOrderRepository.cs`

- [ ] **Step 1: Create the repository**

```csharp
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Catalog.ProductIngredientOrder;

public class ProductIngredientOrderRepository
    : BaseRepository<Domain.Features.Catalog.ProductIngredientOrder, int>,
      IProductIngredientOrderRepository
{
    public ProductIngredientOrderRepository(ApplicationDbContext context)
        : base(context)
    {
    }

    public async Task<List<Domain.Features.Catalog.ProductIngredientOrder>> ListByParentAsync(
        string parentProductCode,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(x => x.ParentProductCode == parentProductCode)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);
    }
}
```

Note: `CreateAsync`, `UpdateAsync`, and `DeleteAsync` are provided by `BaseRepository<T, TKey>`. If a method signature in `BaseRepository` does not match the interface exactly, add a forwarding override here. Verify by inspecting `backend/src/Anela.Heblo.Persistence/Repositories/BaseRepository.cs` after step 2 fails.

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj`
Expected: Build succeeded. If `IProductIngredientOrderRepository` methods are not satisfied by `BaseRepository`, read `BaseRepository.cs` and add explicit overrides that delegate to the base.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Catalog/ProductIngredientOrder/ProductIngredientOrderRepository.cs
git commit -m "feat: add ProductIngredientOrderRepository"
```

---

## Task 5: Register DbSet and DI

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs:59`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs:42`

- [ ] **Step 1: Add DbSet to ApplicationDbContext**

In `ApplicationDbContext.cs`, find the `// Catalog module` section (around line 58) and add directly under the existing `ManufactureDifficultySettings` DbSet:

```csharp
    // Catalog module
    public DbSet<ManufactureDifficultySetting> ManufactureDifficultySettings { get; set; } = null!;
    public DbSet<ProductIngredientOrder> ProductIngredientOrders { get; set; } = null!;
```

- [ ] **Step 2: Register the repository in CatalogModule**

In `CatalogModule.cs`, find the existing line:

```csharp
        services.AddTransient<IManufactureDifficultyRepository, ManufactureDifficultyRepository>();
```

and add directly below it:

```csharp
        services.AddTransient<IProductIngredientOrderRepository, ProductIngredientOrderRepository>();
```

You may need to add `using Anela.Heblo.Persistence.Catalog.ProductIngredientOrder;` to the imports.

- [ ] **Step 3: Build the solution**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
git commit -m "feat: register ProductIngredientOrder DbSet and repository"
```

---

## Task 6: Generate EF migration

**Files:**
- Generated: `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddProductIngredientOrder.cs` (+ Designer)

- [ ] **Step 1: Generate migration**

Run from the repo root:

```bash
dotnet ef migrations add AddProductIngredientOrder \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API
```

Expected: New migration files appear under `backend/src/Anela.Heblo.Persistence/Migrations/`. The `Up()` method should create a `ProductIngredientOrders` table with the columns and indexes defined in Task 3.

If the `dotnet ef` tool is missing, install with: `dotnet tool install --global dotnet-ef` and retry.

- [ ] **Step 2: Inspect migration**

Open the newly generated migration file. Verify:
- `CreateTable("ProductIngredientOrders", ...)` is present with all columns (`Id`, `ParentProductCode`, `IngredientProductCode`, `SortOrder`, `UpdatedAt`, `UpdatedBy`).
- Unique index on `(ParentProductCode, IngredientProductCode)`.
- Standard index on `ParentProductCode`.
- No unintended changes (e.g., column drops elsewhere). If you see unrelated changes, abort with `dotnet ef migrations remove` and investigate.

- [ ] **Step 3: Build to confirm migration compiles**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat: add EF migration for ProductIngredientOrders table"
```

> **Note (per `CLAUDE.md`):** The migration is **not** applied automatically. The user runs `dotnet ef database update` manually against the target environment when ready.

---

## Task 7: Add `Order` field to `IngredientDto`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/IngredientDto.cs`

- [ ] **Step 1: Add the `Order` property**

Replace the file with:

```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;

public class IngredientDto
{
    [JsonPropertyName("productCode")]
    public string ProductCode { get; set; }

    [JsonPropertyName("productName")]
    public string ProductName { get; set; }

    [JsonPropertyName("amount")]
    public double Amount { get; set; }

    [JsonPropertyName("unit")]
    public string Unit { get; set; }

    [JsonPropertyName("order")]
    public int Order { get; set; }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/IngredientDto.cs
git commit -m "feat: add Order field to IngredientDto"
```

---

## Task 8: GET handler overlay — failing test first

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductCompositionHandlerTests.cs`

- [ ] **Step 1: Write the test class with three behaviors**

```csharp
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class GetProductCompositionHandlerTests
{
    private readonly Mock<IManufactureClient> _manufactureClientMock = new();
    private readonly Mock<IProductIngredientOrderRepository> _orderRepoMock = new();
    private readonly GetProductCompositionHandler _handler;

    public GetProductCompositionHandlerTests()
    {
        _handler = new GetProductCompositionHandler(
            _manufactureClientMock.Object,
            _orderRepoMock.Object);
    }

    [Fact]
    public async Task Handle_NoSavedOrder_AssignsContiguousOrder()
    {
        // Arrange
        var template = new ManufactureTemplate
        {
            ProductCode = "PRD1",
            Ingredients = new List<Ingredient>
            {
                new() { ProductCode = "A", ProductName = "Alpha", Amount = 10 },
                new() { ProductCode = "B", ProductName = "Beta",  Amount = 20 },
            }
        };
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _orderRepoMock
            .Setup(x => x.ListByParentAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductIngredientOrder>());

        // Act
        var response = await _handler.Handle(
            new GetProductCompositionRequest { ProductCode = "PRD1" },
            CancellationToken.None);

        // Assert
        response.Ingredients.Should().HaveCount(2);
        response.Ingredients.Select(i => i.Order).Should().Equal(1, 2);
    }

    [Fact]
    public async Task Handle_SavedOrderApplied_SortsByCustomOrder()
    {
        // Arrange
        var template = new ManufactureTemplate
        {
            ProductCode = "PRD1",
            Ingredients = new List<Ingredient>
            {
                new() { ProductCode = "A", ProductName = "Alpha", Amount = 10 },
                new() { ProductCode = "B", ProductName = "Beta",  Amount = 20 },
                new() { ProductCode = "C", ProductName = "Gamma", Amount = 30 },
            }
        };
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _orderRepoMock
            .Setup(x => x.ListByParentAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductIngredientOrder>
            {
                new() { ParentProductCode = "PRD1", IngredientProductCode = "C", SortOrder = 1 },
                new() { ParentProductCode = "PRD1", IngredientProductCode = "A", SortOrder = 2 },
                new() { ParentProductCode = "PRD1", IngredientProductCode = "B", SortOrder = 3 },
            });

        // Act
        var response = await _handler.Handle(
            new GetProductCompositionRequest { ProductCode = "PRD1" },
            CancellationToken.None);

        // Assert
        response.Ingredients.Select(i => i.ProductCode).Should().Equal("C", "A", "B");
        response.Ingredients.Select(i => i.Order).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Handle_NewIngredientNotInSavedOrder_AppearsLast()
    {
        // Arrange
        var template = new ManufactureTemplate
        {
            ProductCode = "PRD1",
            Ingredients = new List<Ingredient>
            {
                new() { ProductCode = "A", ProductName = "Alpha", Amount = 10 },
                new() { ProductCode = "B", ProductName = "Beta",  Amount = 20 },
                new() { ProductCode = "NEW", ProductName = "Newcomer", Amount = 5 },
            }
        };
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _orderRepoMock
            .Setup(x => x.ListByParentAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductIngredientOrder>
            {
                new() { ParentProductCode = "PRD1", IngredientProductCode = "B", SortOrder = 1 },
                new() { ParentProductCode = "PRD1", IngredientProductCode = "A", SortOrder = 2 },
            });

        // Act
        var response = await _handler.Handle(
            new GetProductCompositionRequest { ProductCode = "PRD1" },
            CancellationToken.None);

        // Assert
        response.Ingredients.Last().ProductCode.Should().Be("NEW");
        response.Ingredients.Last().Order.Should().Be(3);
    }

    [Fact]
    public async Task Handle_TemplateNull_ReturnsEmptyList()
    {
        _manufactureClientMock
            .Setup(x => x.GetManufactureTemplateAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureTemplate?)null);

        var response = await _handler.Handle(
            new GetProductCompositionRequest { ProductCode = "PRD1" },
            CancellationToken.None);

        response.Ingredients.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run the test — expect compile failure**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~GetProductCompositionHandlerTests`
Expected: FAIL — handler constructor does not yet accept `IProductIngredientOrderRepository` (compile error or runtime test failure).

---

## Task 9: Make the GET handler test pass — overlay implementation

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/GetProductCompositionHandler.cs`

- [ ] **Step 1: Replace handler with overlay logic**

```csharp
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;

public class GetProductCompositionHandler
    : IRequestHandler<GetProductCompositionRequest, GetProductCompositionResponse>
{
    private readonly IManufactureClient _manufactureClient;
    private readonly IProductIngredientOrderRepository _orderRepository;

    public GetProductCompositionHandler(
        IManufactureClient manufactureClient,
        IProductIngredientOrderRepository orderRepository)
    {
        _manufactureClient = manufactureClient;
        _orderRepository = orderRepository;
    }

    public async Task<GetProductCompositionResponse> Handle(
        GetProductCompositionRequest request,
        CancellationToken cancellationToken)
    {
        var template = await _manufactureClient.GetManufactureTemplateAsync(
            request.ProductCode,
            cancellationToken);

        if (template == null)
        {
            return new GetProductCompositionResponse
            {
                Ingredients = new List<IngredientDto>()
            };
        }

        var savedOrders = await _orderRepository.ListByParentAsync(
            request.ProductCode,
            cancellationToken);

        var orderByCode = savedOrders.ToDictionary(
            x => x.IngredientProductCode,
            x => x.SortOrder);

        var sorted = template.Ingredients
            .Select(i => new
            {
                Ingredient = i,
                Rank = orderByCode.TryGetValue(i.ProductCode, out var s) ? s : int.MaxValue
            })
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Ingredient.ProductName)
            .Select((x, index) => new IngredientDto
            {
                ProductCode = x.Ingredient.ProductCode,
                ProductName = x.Ingredient.ProductName,
                Amount = x.Ingredient.Amount,
                Unit = "g",
                Order = index + 1
            })
            .ToList();

        return new GetProductCompositionResponse
        {
            Ingredients = sorted
        };
    }
}
```

- [ ] **Step 2: Run tests — expect PASS**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~GetProductCompositionHandlerTests`
Expected: 4 tests PASS.

- [ ] **Step 3: Format**

Run: `dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.Application backend/test/Anela.Heblo.Tests`

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/GetProductCompositionHandler.cs backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductCompositionHandlerTests.cs
git commit -m "feat: overlay saved order on GetProductComposition response"
```

---

## Task 10: PUT request / response types

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/UpdateProductCompositionOrder/UpdateProductCompositionOrderRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/UpdateProductCompositionOrder/UpdateProductCompositionOrderResponse.cs`

- [ ] **Step 1: Create the request**

```csharp
using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder;

public class UpdateProductCompositionOrderRequest
    : IRequest<UpdateProductCompositionOrderResponse>
{
    [Required]
    public string ProductCode { get; set; } = string.Empty;

    public List<IngredientOrderItem> Order { get; set; } = new();
}

public class IngredientOrderItem
{
    [Required]
    public string IngredientProductCode { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}
```

- [ ] **Step 2: Create the response**

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder;

public class UpdateProductCompositionOrderResponse : BaseResponse
{
    public int UpdatedCount { get; set; }
}
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/UpdateProductCompositionOrder/
git commit -m "feat: add UpdateProductCompositionOrder request and response types"
```

---

## Task 11: PUT handler — failing test first

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/UpdateProductCompositionOrderHandlerTests.cs`

- [ ] **Step 1: Write the test class**

```csharp
using Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class UpdateProductCompositionOrderHandlerTests
{
    private readonly Mock<IProductIngredientOrderRepository> _repoMock = new();
    private readonly Mock<ICurrentUserService> _userMock = new();
    private readonly Mock<ILogger<UpdateProductCompositionOrderHandler>> _loggerMock = new();
    private readonly UpdateProductCompositionOrderHandler _handler;

    public UpdateProductCompositionOrderHandlerTests()
    {
        _userMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("u1", "Tester", "t@e.cz", true));

        _handler = new UpdateProductCompositionOrderHandler(
            _repoMock.Object,
            _userMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_NoExistingRows_CreatesAllRequestedRows()
    {
        _repoMock
            .Setup(x => x.ListByParentAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductIngredientOrder>());

        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PRD1",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "A", SortOrder = 1 },
                new() { IngredientProductCode = "B", SortOrder = 2 },
            }
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Success.Should().BeTrue();
        response.UpdatedCount.Should().Be(2);
        _repoMock.Verify(
            x => x.CreateAsync(It.IsAny<ProductIngredientOrder>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_ExistingRowsUpdated_NoDeletes()
    {
        _repoMock
            .Setup(x => x.ListByParentAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductIngredientOrder>
            {
                new() { Id = 1, ParentProductCode = "PRD1", IngredientProductCode = "A", SortOrder = 5 },
                new() { Id = 2, ParentProductCode = "PRD1", IngredientProductCode = "B", SortOrder = 6 },
            });

        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PRD1",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "A", SortOrder = 1 },
                new() { IngredientProductCode = "B", SortOrder = 2 },
            }
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Success.Should().BeTrue();
        _repoMock.Verify(
            x => x.UpdateAsync(It.IsAny<ProductIngredientOrder>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        _repoMock.Verify(
            x => x.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ObsoleteRows_AreDeleted()
    {
        _repoMock
            .Setup(x => x.ListByParentAsync("PRD1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductIngredientOrder>
            {
                new() { Id = 1, ParentProductCode = "PRD1", IngredientProductCode = "A", SortOrder = 1 },
                new() { Id = 2, ParentProductCode = "PRD1", IngredientProductCode = "OBSOLETE", SortOrder = 2 },
            });

        var request = new UpdateProductCompositionOrderRequest
        {
            ProductCode = "PRD1",
            Order = new List<IngredientOrderItem>
            {
                new() { IngredientProductCode = "A", SortOrder = 1 },
            }
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Success.Should().BeTrue();
        _repoMock.Verify(x => x.DeleteAsync(2, It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run test — expect compile failure**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~UpdateProductCompositionOrderHandlerTests`
Expected: FAIL — `UpdateProductCompositionOrderHandler` does not exist yet.

---

## Task 12: Make the PUT handler test pass

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/UpdateProductCompositionOrder/UpdateProductCompositionOrderHandler.cs`

- [ ] **Step 1: Implement the handler**

```csharp
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder;

public class UpdateProductCompositionOrderHandler
    : IRequestHandler<UpdateProductCompositionOrderRequest, UpdateProductCompositionOrderResponse>
{
    private readonly IProductIngredientOrderRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<UpdateProductCompositionOrderHandler> _logger;

    public UpdateProductCompositionOrderHandler(
        IProductIngredientOrderRepository repository,
        ICurrentUserService currentUserService,
        ILogger<UpdateProductCompositionOrderHandler> logger)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<UpdateProductCompositionOrderResponse> Handle(
        UpdateProductCompositionOrderRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await _repository.ListByParentAsync(request.ProductCode, cancellationToken);
        var existingByCode = existing.ToDictionary(x => x.IngredientProductCode);
        var requestedCodes = request.Order
            .Select(x => x.IngredientProductCode)
            .ToHashSet();

        var user = _currentUserService.GetCurrentUser();
        var updatedBy = user.IsAuthenticated && !string.IsNullOrEmpty(user.Name)
            ? user.Name
            : "System";
        var now = DateTime.UtcNow;

        // Delete obsolete rows
        foreach (var row in existing.Where(x => !requestedCodes.Contains(x.IngredientProductCode)))
        {
            _logger.LogInformation(
                "Deleting obsolete ingredient order row {Id} for {Parent}/{Ingredient}",
                row.Id, request.ProductCode, row.IngredientProductCode);
            await _repository.DeleteAsync(row.Id, cancellationToken);
        }

        // Upsert requested rows
        var changes = 0;
        foreach (var item in request.Order)
        {
            if (existingByCode.TryGetValue(item.IngredientProductCode, out var current))
            {
                current.SortOrder = item.SortOrder;
                current.UpdatedAt = now;
                current.UpdatedBy = updatedBy;
                await _repository.UpdateAsync(current, cancellationToken);
            }
            else
            {
                await _repository.CreateAsync(new ProductIngredientOrder
                {
                    ParentProductCode = request.ProductCode,
                    IngredientProductCode = item.IngredientProductCode,
                    SortOrder = item.SortOrder,
                    UpdatedAt = now,
                    UpdatedBy = updatedBy
                }, cancellationToken);
            }
            changes++;
        }

        return new UpdateProductCompositionOrderResponse
        {
            UpdatedCount = changes
        };
    }
}
```

- [ ] **Step 2: Run tests — expect PASS**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~UpdateProductCompositionOrderHandlerTests`
Expected: 3 tests PASS.

- [ ] **Step 3: Format**

Run: `dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.Application backend/test/Anela.Heblo.Tests`

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/UpdateProductCompositionOrder/UpdateProductCompositionOrderHandler.cs backend/test/Anela.Heblo.Tests/Features/Catalog/UpdateProductCompositionOrderHandlerTests.cs
git commit -m "feat: add UpdateProductCompositionOrder handler with upsert/delete logic"
```

---

## Task 13: Expose PUT endpoint in `CatalogController`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/CatalogController.cs`

- [ ] **Step 1: Add using and action**

At the top of `CatalogController.cs`, add the using directive next to the others:

```csharp
using Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder;
```

Then add the action directly after the existing `GetComposition` method (after line 54):

```csharp
[HttpPut("{productCode}/composition/order")]
public async Task<ActionResult<UpdateProductCompositionOrderResponse>> UpdateCompositionOrder(
    string productCode,
    [FromBody] UpdateProductCompositionOrderRequest request)
{
    request.ProductCode = productCode;
    var response = await _mediator.Send(request);
    return HandleResponse(response);
}
```

- [ ] **Step 2: Build the solution**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Build succeeded.

- [ ] **Step 3: Run all backend tests**

Run: `dotnet test backend/Anela.Heblo.sln`
Expected: All tests PASS. If anything outside the new code broke, investigate — pre-existing failures should not be ignored.

- [ ] **Step 4: Format**

Run: `dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.API`

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/CatalogController.cs
git commit -m "feat: add PUT /api/catalog/{productCode}/composition/order endpoint"
```

---

## Task 14: Regenerate OpenAPI clients

**Files:**
- Modified by build: `frontend/src/api/generated/api-client.ts`
- (Possibly) `backend/src/Anela.Heblo.Client/`

- [ ] **Step 1: Build the backend (triggers client generation)**

Per `docs/development/api-client-generation.md`, the TypeScript client is auto-generated on backend build.

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Build succeeded.

- [ ] **Step 2: Verify the generated client includes the new endpoint and field**

Run: `grep -n "updateCompositionOrder\\|UpdateProductCompositionOrderRequest" frontend/src/api/generated/api-client.ts | head -5`
Expected: At least one match for each.

Run: `grep -n "order" frontend/src/api/generated/api-client.ts | grep -i ingredient | head -3`
Expected: A line on `IngredientDto` showing the new `order` property.

If matches are missing, the codegen step did not pick up the new types. Confirm the backend project that hosts Swagger is the same one being built, and rebuild.

- [ ] **Step 3: Commit generated client**

```bash
git add frontend/src/api/generated/api-client.ts
# Add any other regenerated client files (e.g., backend C# generated client) if changed
git commit -m "chore: regenerate OpenAPI client for composition order endpoint"
```

---

## Task 15: Frontend hook — `useUpdateProductCompositionOrder`

**Files:**
- Create: `frontend/src/api/hooks/useUpdateProductCompositionOrder.ts`
- Modify: `frontend/src/api/hooks/useCatalog.ts` (extend the local `IngredientDto`)

- [ ] **Step 1: Extend the local `IngredientDto` interface**

In `useCatalog.ts`, find the existing interface (around line 179):

```ts
export interface IngredientDto {
  productCode: string;
  productName: string;
  amount: number;
  unit: string;
}
```

Replace with:

```ts
export interface IngredientDto {
  productCode: string;
  productName: string;
  amount: number;
  unit: string;
  order: number;
}
```

- [ ] **Step 2: Create the mutation hook**

```ts
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

export interface IngredientOrderItem {
  ingredientProductCode: string;
  sortOrder: number;
}

export interface UpdateProductCompositionOrderPayload {
  productCode: string;
  order: IngredientOrderItem[];
}

export const useUpdateProductCompositionOrder = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (payload: UpdateProductCompositionOrderPayload) => {
      const apiClient = getAuthenticatedApiClient();
      const relativeUrl = `/api/catalog/${encodeURIComponent(payload.productCode)}/composition/order`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ order: payload.order }),
      });
      if (!response.ok) {
        const text = await response.text();
        throw new Error(`Update composition order failed: ${response.status} ${text}`);
      }
      return response.json();
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({
        queryKey: [...QUERY_KEYS.catalog, 'composition', variables.productCode],
      });
    },
  });
};
```

- [ ] **Step 3: Type-check**

Run: `npm --prefix frontend run build`
Expected: Build succeeded (or at least no errors in the new files; if pre-existing errors block, capture and report).

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/hooks/useUpdateProductCompositionOrder.ts frontend/src/api/hooks/useCatalog.ts
git commit -m "feat: add useUpdateProductCompositionOrder hook"
```

---

## Task 16: Sortable row component

**Files:**
- Create: `frontend/src/components/catalog/detail/tabs/CompositionTabRow.tsx`

- [ ] **Step 1: Create the row**

```tsx
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { GripVertical } from 'lucide-react';
import type { IngredientDto } from '../../../../api/hooks/useCatalog';

interface CompositionTabRowProps {
  ingredient: IngredientDto;
  displayOrder: number;
  isEditMode: boolean;
}

export const CompositionTabRow: React.FC<CompositionTabRowProps> = ({
  ingredient,
  displayOrder,
  isEditMode,
}) => {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } =
    useSortable({ id: ingredient.productCode });

  const style: React.CSSProperties = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.6 : 1,
  };

  return (
    <tr
      ref={setNodeRef}
      style={style}
      className={`hover:bg-gray-50 ${isDragging ? 'bg-indigo-50' : ''}`}
    >
      {isEditMode && (
        <td className="py-3 px-2 w-8 text-gray-400 cursor-grab active:cursor-grabbing" {...attributes} {...listeners}>
          <GripVertical className="h-4 w-4" />
        </td>
      )}
      <td className="py-3 px-4 text-right text-gray-700 w-16">{displayOrder}</td>
      <td className="py-3 px-4 text-gray-900">{ingredient.productName}</td>
      <td className="py-3 px-4 text-gray-900 font-medium">{ingredient.productCode}</td>
      <td className="py-3 px-4 text-right text-gray-900 font-medium">
        {ingredient.amount.toLocaleString('cs-CZ', {
          minimumFractionDigits: 2,
          maximumFractionDigits: 4,
        })}
      </td>
    </tr>
  );
};
```

- [ ] **Step 2: Lint**

Run: `npm --prefix frontend run lint -- src/components/catalog/detail/tabs/CompositionTabRow.tsx`
Expected: No errors.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/catalog/detail/tabs/CompositionTabRow.tsx
git commit -m "feat: add sortable CompositionTabRow component"
```

---

## Task 17: Composition tab — Order column, edit mode, DnD, save/cancel

**Files:**
- Modify: `frontend/src/components/catalog/detail/tabs/CompositionTab.tsx`

- [ ] **Step 1: Replace `CompositionTab.tsx`**

```tsx
import React, { useEffect, useState } from 'react';
import { Loader2, AlertCircle, Beaker, Pencil, Save, X } from 'lucide-react';
import {
  DndContext,
  closestCenter,
  PointerSensor,
  useSensor,
  useSensors,
  DragEndEvent,
} from '@dnd-kit/core';
import {
  SortableContext,
  arrayMove,
  verticalListSortingStrategy,
} from '@dnd-kit/sortable';
import { useProductComposition } from '../../../../api/hooks/useCatalog';
import { useUpdateProductCompositionOrder } from '../../../../api/hooks/useUpdateProductCompositionOrder';
import type { IngredientDto } from '../../../../api/hooks/useCatalog';
import { CompositionTabRow } from './CompositionTabRow';

interface CompositionTabProps {
  productCode: string;
}

const CompositionTab: React.FC<CompositionTabProps> = ({ productCode }) => {
  const { data, isLoading, error } = useProductComposition(productCode);
  const updateOrder = useUpdateProductCompositionOrder();

  const [isEditMode, setIsEditMode] = useState(false);
  const [draftOrder, setDraftOrder] = useState<IngredientDto[] | null>(null);
  const [sortConfig, setSortConfig] = useState<{
    key: keyof IngredientDto;
    direction: 'asc' | 'desc';
  } | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);

  const ingredients = React.useMemo(() => data?.ingredients ?? [], [data?.ingredients]);

  // When data refreshes while not editing, clear any stale draft
  useEffect(() => {
    if (!isEditMode) {
      setDraftOrder(null);
    }
  }, [ingredients, isEditMode]);

  const sortedIngredients = React.useMemo(() => {
    if (isEditMode) {
      return draftOrder ?? ingredients;
    }
    if (!sortConfig) return ingredients; // server already sorted by custom order

    const sorted = [...ingredients].sort((a, b) => {
      const aValue = a[sortConfig.key];
      const bValue = b[sortConfig.key];

      if (typeof aValue === 'number' && typeof bValue === 'number') {
        return sortConfig.direction === 'asc' ? aValue - bValue : bValue - aValue;
      }
      const aString = String(aValue);
      const bString = String(bValue);
      return sortConfig.direction === 'asc'
        ? aString.localeCompare(bString, 'cs')
        : bString.localeCompare(aString, 'cs');
    });

    return sorted;
  }, [ingredients, sortConfig, isEditMode, draftOrder]);

  const handleSort = (key: keyof IngredientDto) => {
    if (isEditMode) return;
    setSortConfig((current) => {
      if (!current || current.key !== key) return { key, direction: 'asc' };
      if (current.direction === 'asc') return { key, direction: 'desc' };
      return null;
    });
  };

  const getSortIcon = (key: keyof IngredientDto) => {
    if (!sortConfig || sortConfig.key !== key) return null;
    return sortConfig.direction === 'asc' ? ' ↑' : ' ↓';
  };

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } }),
  );

  const handleDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;
    if (!over || active.id === over.id) return;

    setDraftOrder((current) => {
      const list = current ?? ingredients;
      const oldIndex = list.findIndex((i) => i.productCode === active.id);
      const newIndex = list.findIndex((i) => i.productCode === over.id);
      if (oldIndex < 0 || newIndex < 0) return current;
      return arrayMove(list, oldIndex, newIndex);
    });
  };

  const enterEditMode = () => {
    setDraftOrder([...ingredients]);
    setSortConfig(null);
    setSaveError(null);
    setIsEditMode(true);
  };

  const cancelEdit = () => {
    setDraftOrder(null);
    setSaveError(null);
    setIsEditMode(false);
  };

  const saveOrder = async () => {
    if (!draftOrder) {
      setIsEditMode(false);
      return;
    }
    setSaveError(null);
    try {
      await updateOrder.mutateAsync({
        productCode,
        order: draftOrder.map((ing, idx) => ({
          ingredientProductCode: ing.productCode,
          sortOrder: idx + 1,
        })),
      });
      setIsEditMode(false);
      setDraftOrder(null);
    } catch (err) {
      setSaveError(err instanceof Error ? err.message : 'Uložení se nezdařilo');
    }
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <div className="text-gray-500">Načítání složení...</div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600">
          <AlertCircle className="h-5 w-5" />
          <div>Chyba při načítání složení: {(error as any).message}</div>
        </div>
      </div>
    );
  }

  if (ingredients.length === 0) {
    return (
      <div className="text-center py-12 bg-gray-50 rounded-lg">
        <Beaker className="h-12 w-12 mx-auto mb-3 text-gray-300" />
        <p className="text-gray-500 mb-2">Tento produkt nemá definované složení</p>
        <p className="text-sm text-gray-400">Výrobní šablona pro tento produkt neexistuje</p>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h3 className="text-lg font-medium text-gray-900 flex items-center">
          <Beaker className="h-5 w-5 mr-2 text-gray-500" />
          Složení ({sortedIngredients.length} ingrediencí)
        </h3>
        <div className="flex items-center space-x-2">
          {!isEditMode && (
            <button
              type="button"
              onClick={enterEditMode}
              className="inline-flex items-center px-3 py-1.5 text-sm border border-gray-300 rounded-md text-gray-700 bg-white hover:bg-gray-50"
            >
              <Pencil className="h-4 w-4 mr-1.5" />
              Upravit pořadí
            </button>
          )}
          {isEditMode && (
            <>
              <button
                type="button"
                onClick={saveOrder}
                disabled={updateOrder.isPending}
                className="inline-flex items-center px-3 py-1.5 text-sm rounded-md text-white bg-indigo-600 hover:bg-indigo-700 disabled:opacity-60"
              >
                <Save className="h-4 w-4 mr-1.5" />
                Uložit
              </button>
              <button
                type="button"
                onClick={cancelEdit}
                disabled={updateOrder.isPending}
                className="inline-flex items-center px-3 py-1.5 text-sm border border-gray-300 rounded-md text-gray-700 bg-white hover:bg-gray-50"
              >
                <X className="h-4 w-4 mr-1.5" />
                Zrušit
              </button>
            </>
          )}
        </div>
      </div>

      {saveError && (
        <div className="flex items-center space-x-2 px-3 py-2 text-sm text-red-700 bg-red-50 border border-red-200 rounded">
          <AlertCircle className="h-4 w-4" />
          <span>{saveError}</span>
        </div>
      )}

      <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
        <div className="h-96 overflow-y-auto">
          <table className="w-full text-sm">
            <thead className="sticky top-0 z-10 bg-gray-50 border-b border-gray-200">
              <tr>
                {isEditMode && <th className="w-8 py-3 px-2" />}
                <th className="text-right py-3 px-4 font-medium text-gray-700 w-16">#</th>
                <th
                  className={`text-left py-3 px-4 font-medium text-gray-700 ${isEditMode ? '' : 'cursor-pointer hover:bg-gray-100'}`}
                  onClick={() => handleSort('productName')}
                >
                  Název{getSortIcon('productName')}
                </th>
                <th
                  className={`text-left py-3 px-4 font-medium text-gray-700 ${isEditMode ? '' : 'cursor-pointer hover:bg-gray-100'}`}
                  onClick={() => handleSort('productCode')}
                >
                  Kód{getSortIcon('productCode')}
                </th>
                <th
                  className={`text-right py-3 px-4 font-medium text-gray-700 ${isEditMode ? '' : 'cursor-pointer hover:bg-gray-100'}`}
                  onClick={() => handleSort('amount')}
                >
                  Množství{getSortIcon('amount')}
                </th>
              </tr>
            </thead>
            <DndContext
              sensors={sensors}
              collisionDetection={closestCenter}
              onDragEnd={handleDragEnd}
            >
              <SortableContext
                items={sortedIngredients.map((i) => i.productCode)}
                strategy={verticalListSortingStrategy}
              >
                <tbody className="divide-y divide-gray-100">
                  {sortedIngredients.map((ingredient, index) => (
                    <CompositionTabRow
                      key={ingredient.productCode}
                      ingredient={ingredient}
                      displayOrder={isEditMode ? index + 1 : ingredient.order}
                      isEditMode={isEditMode}
                    />
                  ))}
                </tbody>
              </SortableContext>
            </DndContext>
          </table>
        </div>
      </div>
    </div>
  );
};

export default CompositionTab;
```

- [ ] **Step 2: Type-check**

Run: `npm --prefix frontend run build`
Expected: Build succeeded.

- [ ] **Step 3: Lint**

Run: `npm --prefix frontend run lint`
Expected: No new errors. (Pre-existing warnings unrelated to these files may be ignored.)

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/catalog/detail/tabs/CompositionTab.tsx
git commit -m "feat: composition tab edit mode with drag-and-drop reordering"
```

---

## Task 18: Frontend tests — edit mode, drag-and-drop save, cancel

**Files:**
- Modify: `frontend/src/components/catalog/detail/tabs/__tests__/CompositionTab.test.tsx`

- [ ] **Step 1: Extend the existing test file**

Replace the existing test file content with:

```tsx
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import CompositionTab from '../CompositionTab';
import { useProductComposition } from '../../../../../api/hooks/useCatalog';
import { useUpdateProductCompositionOrder } from '../../../../../api/hooks/useUpdateProductCompositionOrder';

jest.mock('../../../../../api/hooks/useCatalog');
jest.mock('../../../../../api/hooks/useUpdateProductCompositionOrder');

const mockUseProductComposition = useProductComposition as jest.MockedFunction<
  typeof useProductComposition
>;
const mockUseUpdateOrder = useUpdateProductCompositionOrder as jest.MockedFunction<
  typeof useUpdateProductCompositionOrder
>;

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
};

const sampleIngredients = [
  { productCode: 'ING001', productName: 'Bisabolol', amount: 50.5, unit: 'g', order: 1 },
  { productCode: 'ING002', productName: 'Vitamin E', amount: 100.25, unit: 'g', order: 2 },
];

beforeEach(() => {
  mockUseUpdateOrder.mockReturnValue({
    mutateAsync: jest.fn().mockResolvedValue({ success: true }),
    isPending: false,
  } as any);
});

describe('CompositionTab', () => {
  it('shows loading state', () => {
    mockUseProductComposition.mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
    } as any);
    render(<CompositionTab productCode="TEST001" />, { wrapper: createWrapper() });
    expect(screen.getByText(/Načítání složení/i)).toBeInTheDocument();
  });

  it('shows error state', () => {
    mockUseProductComposition.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: new Error('Failed to load'),
    } as any);
    render(<CompositionTab productCode="TEST001" />, { wrapper: createWrapper() });
    expect(screen.getByText(/Chyba při načítání složení/i)).toBeInTheDocument();
  });

  it('shows empty state when no ingredients', () => {
    mockUseProductComposition.mockReturnValue({
      data: { ingredients: [] },
      isLoading: false,
      error: null,
    } as any);
    render(<CompositionTab productCode="TEST001" />, { wrapper: createWrapper() });
    expect(
      screen.getByText(/Tento produkt nemá definované složení/i),
    ).toBeInTheDocument();
  });

  it('displays ingredients with order column', () => {
    mockUseProductComposition.mockReturnValue({
      data: { ingredients: sampleIngredients },
      isLoading: false,
      error: null,
    } as any);
    render(<CompositionTab productCode="TEST001" />, { wrapper: createWrapper() });
    expect(screen.getByText('Bisabolol')).toBeInTheDocument();
    expect(screen.getByText('ING001')).toBeInTheDocument();
    // Order numbers visible
    expect(screen.getByText('1')).toBeInTheDocument();
    expect(screen.getByText('2')).toBeInTheDocument();
  });

  it('shows "Upravit pořadí" button by default', () => {
    mockUseProductComposition.mockReturnValue({
      data: { ingredients: sampleIngredients },
      isLoading: false,
      error: null,
    } as any);
    render(<CompositionTab productCode="TEST001" />, { wrapper: createWrapper() });
    expect(screen.getByRole('button', { name: /Upravit pořadí/i })).toBeInTheDocument();
  });

  it('enters edit mode and shows Uložit / Zrušit', () => {
    mockUseProductComposition.mockReturnValue({
      data: { ingredients: sampleIngredients },
      isLoading: false,
      error: null,
    } as any);
    render(<CompositionTab productCode="TEST001" />, { wrapper: createWrapper() });

    fireEvent.click(screen.getByRole('button', { name: /Upravit pořadí/i }));

    expect(screen.getByRole('button', { name: /Uložit/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Zrušit/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Upravit pořadí/i })).not.toBeInTheDocument();
  });

  it('Zrušit exits edit mode without calling mutation', () => {
    const mutateAsync = jest.fn();
    mockUseUpdateOrder.mockReturnValue({ mutateAsync, isPending: false } as any);
    mockUseProductComposition.mockReturnValue({
      data: { ingredients: sampleIngredients },
      isLoading: false,
      error: null,
    } as any);
    render(<CompositionTab productCode="TEST001" />, { wrapper: createWrapper() });

    fireEvent.click(screen.getByRole('button', { name: /Upravit pořadí/i }));
    fireEvent.click(screen.getByRole('button', { name: /Zrušit/i }));

    expect(mutateAsync).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: /Upravit pořadí/i })).toBeInTheDocument();
  });

  it('Uložit calls mutation with current draft order', async () => {
    const mutateAsync = jest.fn().mockResolvedValue({ success: true });
    mockUseUpdateOrder.mockReturnValue({ mutateAsync, isPending: false } as any);
    mockUseProductComposition.mockReturnValue({
      data: { ingredients: sampleIngredients },
      isLoading: false,
      error: null,
    } as any);
    render(<CompositionTab productCode="TEST001" />, { wrapper: createWrapper() });

    fireEvent.click(screen.getByRole('button', { name: /Upravit pořadí/i }));
    fireEvent.click(screen.getByRole('button', { name: /Uložit/i }));

    await waitFor(() => expect(mutateAsync).toHaveBeenCalledTimes(1));
    expect(mutateAsync).toHaveBeenCalledWith({
      productCode: 'TEST001',
      order: [
        { ingredientProductCode: 'ING001', sortOrder: 1 },
        { ingredientProductCode: 'ING002', sortOrder: 2 },
      ],
    });
  });
});
```

- [ ] **Step 2: Run frontend tests**

Run: `npm --prefix frontend test -- CompositionTab.test`
Expected: All 8 tests PASS.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/catalog/detail/tabs/__tests__/CompositionTab.test.tsx
git commit -m "test: edit mode, save, and cancel for CompositionTab"
```

---

## Task 19: Final verification

- [ ] **Step 1: Backend final build + tests**

Run: `dotnet build backend/Anela.Heblo.sln && dotnet test backend/Anela.Heblo.sln`
Expected: All green.

- [ ] **Step 2: Backend format**

Run: `dotnet format backend/Anela.Heblo.sln`
Expected: No changes (already formatted).

- [ ] **Step 3: Frontend build + lint + test**

Run: `npm --prefix frontend run build && npm --prefix frontend run lint && npm --prefix frontend test`
Expected: All green.

- [ ] **Step 4: Manual smoke (no automation — requires running stack)**

If a local stack is available:

1. Apply the new migration: `dotnet ef database update --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API`. (Per `CLAUDE.md`, the user runs this — don't execute automatically.)
2. Start backend + frontend.
3. Open a catalog detail page for a product with a known BoM (e.g., a finished product code from `frontend/test/e2e/fixtures/test-data.ts`).
4. Switch to "Složení" tab.
5. Verify the "#" column displays sequential numbers and the existing column header sorts still work.
6. Click "Upravit pořadí" → drag handles appear, column header sorts are disabled.
7. Drag a row to a new position; the # numbers update live.
8. Click "Uložit" → button group reverts to "Upravit pořadí"; reload the page; verify the new order persists.
9. Re-enter edit mode and click "Zrušit" after a drag — verify the draft is discarded and the saved order is restored.
10. Sanity check: column header sort works in read mode and is suppressed in edit mode.

- [ ] **Step 5: Push the branch**

```bash
git push -u origin HEAD
```

---

## Out of scope (deferred)

- E2E Playwright scenario for the new flow. The project rule states E2E runs nightly, not in PR CI; add later in a dedicated `frontend/test/e2e/catalog/` scenario once the feature is merged. Stub:
  - Open product detail with composition.
  - Enter edit mode, drag the first ingredient down one slot, save.
  - Reload, verify new top item.

- `Unit` field is still hard-coded to `"g"` in the GET handler — pre-existing TODO, unchanged here.

- No new authorization gate added; `[Authorize]` at the controller level is sufficient per requirements decision.

---

## Self-review checklist (completed by plan author)

- ✅ Every spec requirement (Order column, edit mode, drag-and-drop, persistence, new ingredients to end, column sort still works, any authenticated user can edit) maps to a task.
- ✅ No "TBD" / "implement later" / generic "handle edge cases" — every step shows the actual code.
- ✅ Type names consistent across tasks: `IngredientDto.order` (frontend) / `IngredientDto.Order` (backend) / `IngredientOrderItem.IngredientProductCode` everywhere.
- ✅ Method names consistent: `useUpdateProductCompositionOrder`, `UpdateProductCompositionOrderHandler`, `IProductIngredientOrderRepository.ListByParentAsync`.
- ✅ All file paths absolute from repo root; conform to `docs/architecture/filesystem.md` patterns (mirroring `ManufactureDifficultySetting`).
- ✅ Per-project rules respected: DTOs are classes, absolute URLs in API hooks, migrations not auto-applied, no `@claude` co-author trailer.
