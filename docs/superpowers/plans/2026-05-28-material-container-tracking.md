# Material Container Tracking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Terminal-app workflow that lets warehouse operators label physical material containers with pre-printed `Mxxxxxxxx` barcodes at goods-receive time, recording who/when, the material, and the supplier lot.

**Architecture:** Rename existing `Ean` entity to `MaterialContainer` (single unified container concept), make Amount/Unit nullable, add `Status` enum (`Assigned`/`Discarded`), add nullable `PurchaseOrderLineId` for PO trace, store lot as free strings `MaterialCode` + `LotCode` (no FK — Abra Flexi is eventual source of truth for lots). New endpoint `GET /api/material-containers/last-used-lot` powers the "default to last lot used for this material" UX. New Terminal workflow under `/terminal/lot-identification` uses the mandatory `ScanInput` component with sticky material+lot context across container scans.

**Tech Stack:** .NET 8 (MediatR, FluentValidation, EF Core w/ PostgreSQL, xUnit + Moq), React 18 + TypeScript + Tailwind + React Query, Playwright for E2E.

**Spec reference:** `/Users/pajgrtondrej/.claude/plans/system-instruction-you-are-working-shimmying-moler.md`

---

## Phase 1 — Backend Rename `Ean` → `MaterialContainer`

The existing `Ean` entity is being repurposed as `MaterialContainer`. Tasks 1–4 do a pure, behavior-preserving rename across the 4 layers. Tests must remain green after each task. No fields change yet.

### Task 1: Rename in Domain layer

**Files:**
- Move: `backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/Ean.cs` → `MaterialContainer.cs`
- Move: `backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/IEanRepository.cs` → `IMaterialContainerRepository.cs`
- Move: `backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/IEanCodeGenerator.cs` → `IMaterialContainerCodeGenerator.cs`

- [ ] **Step 1: Rename `Ean.cs` file and class**

```csharp
// backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/MaterialContainer.cs
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

public class MaterialContainer : IEntity<int>
{
    protected MaterialContainer() { } // EF Core

    public MaterialContainer(string code, int lotId, decimal amount, string unit, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Code is required.", nameof(code));
        if (lotId <= 0) throw new ArgumentException("LotId must be positive.", nameof(lotId));
        if (amount <= 0) throw new ArgumentException("Amount must be positive.", nameof(amount));
        if (string.IsNullOrWhiteSpace(unit)) throw new ArgumentException("Unit is required.", nameof(unit));
        if (string.IsNullOrWhiteSpace(createdBy)) throw new ArgumentException("CreatedBy is required.", nameof(createdBy));

        Code = code;
        LotId = lotId;
        Amount = amount;
        Unit = unit;
        CreatedAt = DateTime.UtcNow;
        CreatedBy = createdBy;
    }

    public int Id { get; private set; }
    public string Code { get; private set; } = null!;
    public int LotId { get; private set; }
    public decimal Amount { get; private set; }
    public string Unit { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTime? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }
}
```

- [ ] **Step 2: Rename `IEanRepository.cs` → `IMaterialContainerRepository.cs`**

```csharp
// backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/IMaterialContainerRepository.cs
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

public interface IMaterialContainerRepository : IRepository<MaterialContainer, int>
{
    Task<MaterialContainer?> GetByCodeAsync(string code, CancellationToken ct);
    Task<bool> AnyByLotIdAsync(int lotId, CancellationToken ct);
    Task<PagedResult<MaterialContainer>> GetPaginatedAsync(int? lotId, string? materialCode, int page, int pageSize, CancellationToken ct);
}
```

- [ ] **Step 3: Rename `IEanCodeGenerator.cs` → `IMaterialContainerCodeGenerator.cs`**

Open the existing file and rename the interface verbatim (only the type name changes):

```csharp
// backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/IMaterialContainerCodeGenerator.cs
namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

public interface IMaterialContainerCodeGenerator
{
    Task<List<string>> GenerateAsync(int count, CancellationToken ct);
}
```

- [ ] **Step 4: Build the domain project**

Run: `cd backend && dotnet build src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj`
Expected: errors in dependent projects (Persistence, Application) — that's fine, we fix them in the next tasks. Domain itself must build clean.

- [ ] **Step 5: Commit (do NOT commit yet — wait for Tasks 2–4 to make the codebase compile)**

We'll batch the rename across all 4 layers into a single commit at the end of Task 4 so the build is green at every commit.

---

### Task 2: Rename in Persistence layer

**Files:**
- Move: `backend/src/Anela.Heblo.Persistence/Catalog/Inventory/EanConfiguration.cs` → `MaterialContainerConfiguration.cs`
- Move: `backend/src/Anela.Heblo.Persistence/Catalog/Inventory/EanRepository.cs` → `MaterialContainerRepository.cs`
- Move: `backend/src/Anela.Heblo.Persistence/Catalog/Inventory/EanCodeGenerator.cs` → `MaterialContainerCodeGenerator.cs`
- Move: `backend/src/Anela.Heblo.Persistence/Catalog/Inventory/NullEanCodeGenerator.cs` → `NullMaterialContainerCodeGenerator.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` — DI registration
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` — `DbSet<Ean>` → `DbSet<MaterialContainer>`

- [ ] **Step 1: Rename `EanConfiguration.cs` → `MaterialContainerConfiguration.cs`**

Keep table name `Eans` in this task — we rename the table in Task 5's migration alongside the schema change:

```csharp
// backend/src/Anela.Heblo.Persistence/Catalog/Inventory/MaterialContainerConfiguration.cs
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Catalog.Inventory;

public class MaterialContainerConfiguration : IEntityTypeConfiguration<MaterialContainer>
{
    public void Configure(EntityTypeBuilder<MaterialContainer> builder)
    {
        builder.ToTable("Eans", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).IsRequired().HasMaxLength(20);
        builder.Property(x => x.LotId).IsRequired();
        builder.Property(x => x.Amount).IsRequired().HasPrecision(18, 4);
        builder.Property(x => x.Unit).IsRequired().HasMaxLength(20);
        builder.Property(x => x.CreatedAt).IsRequired().HasColumnType("timestamp without time zone");
        builder.Property(x => x.CreatedBy).IsRequired().HasMaxLength(100);
        builder.Property(x => x.UpdatedAt).HasColumnType("timestamp without time zone");
        builder.Property(x => x.UpdatedBy).HasMaxLength(100);

        builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("IX_Eans_Code");
        builder.HasIndex(x => x.LotId).HasDatabaseName("IX_Eans_LotId");

        builder.HasOne<Lot>()
            .WithMany()
            .HasForeignKey(x => x.LotId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Eans_Lots_LotId");
    }
}
```

- [ ] **Step 2: Rename `EanRepository.cs` → `MaterialContainerRepository.cs`**

```csharp
// backend/src/Anela.Heblo.Persistence/Catalog/Inventory/MaterialContainerRepository.cs
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Persistence.Repositories;
using Anela.Heblo.Xcc.Persistance;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Catalog.Inventory;

public class MaterialContainerRepository : BaseRepository<MaterialContainer, int>, IMaterialContainerRepository
{
    public MaterialContainerRepository(ApplicationDbContext context) : base(context) { }

    public async Task<MaterialContainer?> GetByCodeAsync(string code, CancellationToken ct)
    {
        return await DbSet.FirstOrDefaultAsync(x => x.Code == code, ct);
    }

    public async Task<bool> AnyByLotIdAsync(int lotId, CancellationToken ct)
    {
        return await DbSet.AnyAsync(x => x.LotId == lotId, ct);
    }

    public async Task<PagedResult<MaterialContainer>> GetPaginatedAsync(
        int? lotId, string? materialCode, int page, int pageSize, CancellationToken ct)
    {
        var query = DbSet.AsQueryable();

        if (lotId.HasValue)
            query = query.Where(x => x.LotId == lotId.Value);

        if (!string.IsNullOrWhiteSpace(materialCode))
        {
            var lotIds = await Context.Set<Lot>()
                .Where(l => l.MaterialCode == materialCode)
                .Select(l => l.Id)
                .ToListAsync(ct);
            query = query.Where(x => lotIds.Contains(x.LotId));
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<MaterialContainer>
        {
            Items = items, TotalCount = totalCount, PageNumber = page, PageSize = pageSize
        };
    }
}
```

- [ ] **Step 3: Rename `EanCodeGenerator.cs` → `MaterialContainerCodeGenerator.cs`**

Open the existing file. Change class name `EanCodeGenerator` → `MaterialContainerCodeGenerator`, change implemented interface `IEanCodeGenerator` → `IMaterialContainerCodeGenerator`. Keep the body and the SQL sequence name (`ean_internal_seq`) unchanged for now — sequence renames happen in Task 5's migration.

- [ ] **Step 4: Rename `NullEanCodeGenerator.cs` → `NullMaterialContainerCodeGenerator.cs`**

Same mechanical rename: class name + implemented interface.

- [ ] **Step 5: Update `PersistenceModule.cs` DI registration**

Find the lines registering `IEanCodeGenerator` and `EanRepository` and rename:

```csharp
// In PersistenceModule.cs — replace the registration block for EAN
if (!useInMemory && connectionString != "InMemory" && dataSource != null)
{
    services.AddScoped<IMaterialContainerCodeGenerator, MaterialContainerCodeGenerator>();
}
else
{
    services.AddScoped<IMaterialContainerCodeGenerator, NullMaterialContainerCodeGenerator>();
}
```

- [ ] **Step 6: Update `ApplicationDbContext.cs`**

Find the `DbSet<Ean>` property and rename to `DbSet<MaterialContainer> MaterialContainers`. Also update any `OnModelCreating` reference to `EanConfiguration` → `MaterialContainerConfiguration`.

- [ ] **Step 7: Build the persistence project**

Run: `cd backend && dotnet build src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj`
Expected: clean build.

---

### Task 3: Rename in Application layer

**Files:**
- Move folder: `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/CreateEans/` → `CreateMaterialContainers/`
- Move folder: `.../UseCases/GetEanByCode/` → `GetMaterialContainerByCode/`
- Move folder: `.../UseCases/ListEans/` → `ListMaterialContainers/`
- Move folder: `.../UseCases/DeleteEan/` → `DiscardMaterialContainer/` (Task 10 changes behavior; this task only renames)
- Move: `.../Contracts/EanDto.cs` → `MaterialContainerDto.cs`
- Modify: `.../InventoryModule.cs` — re-register handlers + validator

- [ ] **Step 1: Rename `EanDto.cs` → `MaterialContainerDto.cs`**

```csharp
// backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/Contracts/MaterialContainerDto.cs
namespace Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;

public class MaterialContainerDto
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;
    public int LotId { get; set; }
    public decimal Amount { get; set; }
    public string Unit { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
}
```

- [ ] **Step 2: Rename `CreateEans` folder + types**

Inside the new `CreateMaterialContainers/` folder, rename:
- `CreateEansHandler.cs` → `CreateMaterialContainersHandler.cs`, class `CreateEansHandler` → `CreateMaterialContainersHandler`
- `CreateEansRequest.cs` → `CreateMaterialContainersRequest.cs`, class + inner item class (`CreateEanItem` → `CreateMaterialContainerItem`)
- `CreateEansResponse.cs` → `CreateMaterialContainersResponse.cs`, class name + collection property `Eans` → `Containers`
- `CreateEansRequestValidator.cs` → `CreateMaterialContainersRequestValidator.cs`, class name

Replace `EanDto` → `MaterialContainerDto` and `IEanRepository` → `IMaterialContainerRepository` and `IEanCodeGenerator` → `IMaterialContainerCodeGenerator` everywhere they appear in these files. Replace `new Ean(...)` → `new MaterialContainer(...)`. Replace `ErrorCodes.EanNotFound` → keep for now (we'll add `MaterialContainerNotFound` in Task 4 via shared constants; for the rename task leave the old enum value).

- [ ] **Step 3: Rename `GetEanByCode` folder + types**

Rename folder and rename inner classes from `GetEanByCode*` to `GetMaterialContainerByCode*`. Replace `EanDto` references with `MaterialContainerDto`. The response property `Ean` becomes `Container`. The repository call `GetEanByCodeHandler.MapToDto` → `GetMaterialContainerByCodeHandler.MapToDto` if you keep a static mapper.

Important: `CreateEansHandler.MapToDto` is referenced by `GetEanByCode` and `ListEans` handlers. After rename, those references become `CreateMaterialContainersHandler.MapToDto`.

- [ ] **Step 4: Rename `ListEans` folder + types**

Rename to `ListMaterialContainers`. Rename classes (`ListEansHandler` → `ListMaterialContainersHandler`, etc.). Response property `Eans` → `Containers`. Update `MapToDto` reference.

- [ ] **Step 5: Rename `DeleteEan` folder + types (behavior stays as hard delete for now)**

Rename to `DiscardMaterialContainer` (folder + class names). Behavior change to soft-delete is Task 10. For now: just rename `DeleteEanHandler` → `DiscardMaterialContainerHandler`, `DeleteEanRequest` → `DiscardMaterialContainerRequest`, `DeleteEanResponse` → `DiscardMaterialContainerResponse`. Body remains a hard delete.

- [ ] **Step 6: Update `InventoryModule.cs` DI registration**

```csharp
// backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/InventoryModule.cs
// Replace EAN-related registrations
services.AddScoped<IMaterialContainerRepository, MaterialContainerRepository>();
services.AddScoped<IValidator<CreateMaterialContainersRequest>, CreateMaterialContainersRequestValidator>();
services.AddScoped<IPipelineBehavior<CreateMaterialContainersRequest, CreateMaterialContainersResponse>,
    ValidationBehavior<CreateMaterialContainersRequest, CreateMaterialContainersResponse>>();
```

Delete the old `IEanRepository`/`CreateEansRequest` lines.

- [ ] **Step 7: Build the application project**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: clean build.

---

### Task 4: Rename controller, route, and tests; commit Phase 1 rename

**Files:**
- Move: `backend/src/Anela.Heblo.API/Controllers/EansController.cs` → `MaterialContainersController.cs`
- Move + rename all test files matching `*Ean*HandlerTests.cs` → `*MaterialContainer*HandlerTests.cs`
- Add new enum values: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` (look up file path — it's referenced via `ErrorCodes.EanNotFound`)

- [ ] **Step 1: Rename `EansController.cs` → `MaterialContainersController.cs` and the route**

```csharp
// backend/src/Anela.Heblo.API/Controllers/MaterialContainersController.cs
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateMaterialContainers;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DiscardMaterialContainer;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetMaterialContainerByCode;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListMaterialContainers;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[Route("api/material-containers")]
[ApiController]
public class MaterialContainersController : BaseApiController
{
    private readonly IMediator _mediator;

    public MaterialContainersController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<ActionResult<ListMaterialContainersResponse>> GetMaterialContainers(
        [FromQuery] int? lotId,
        [FromQuery] string? materialCode,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var request = new ListMaterialContainersRequest { LotId = lotId, MaterialCode = materialCode, Page = page, PageSize = pageSize };
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }

    [HttpGet("by-code/{code}")]
    public async Task<ActionResult<GetMaterialContainerByCodeResponse>> GetByCode(string code, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetMaterialContainerByCodeRequest { Code = code }, cancellationToken);
        return HandleResponse(response);
    }

    [HttpPost]
    public async Task<ActionResult<CreateMaterialContainersResponse>> Create(
        [FromBody] CreateMaterialContainersRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<DiscardMaterialContainerResponse>> Discard(int id, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new DiscardMaterialContainerRequest { Id = id }, cancellationToken);
        return HandleResponse(response);
    }
}
```

(Route changes to POST `/{id}/discard` happen in Task 10.)

- [ ] **Step 2: Rename all `*Ean*HandlerTests.cs` test files**

```bash
cd backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory
git mv CreateEansHandlerTests.cs CreateMaterialContainersHandlerTests.cs
git mv DeleteEanHandlerTests.cs DiscardMaterialContainerHandlerTests.cs
git mv GetEanByCodeHandlerTests.cs GetMaterialContainerByCodeHandlerTests.cs
git mv ListEansHandlerTests.cs ListMaterialContainersHandlerTests.cs
```

Inside each renamed file, find/replace verbatim:
- `Ean` → `MaterialContainer`
- `CreateEansHandler` → `CreateMaterialContainersHandler`
- `DeleteEanHandler` → `DiscardMaterialContainerHandler`
- `GetEanByCodeHandler` → `GetMaterialContainerByCodeHandler`
- `ListEansHandler` → `ListMaterialContainersHandler`
- `IEanRepository` → `IMaterialContainerRepository`
- `IEanCodeGenerator` → `IMaterialContainerCodeGenerator`
- `EanDto` → `MaterialContainerDto`
- `Eans` (property names, e.g. `result.Eans`) → `Containers`
- `_eanRepo` → `_containerRepo` (or similar local variable rename — apply uniformly)
- `_generator` for IMaterialContainerCodeGenerator stays the same name

Add `using` directives for the renamed namespaces.

- [ ] **Step 3: Add new error code (rename `EanNotFound` → `MaterialContainerNotFound`)**

Find `ErrorCodes` enum (search with `Grep` for `EanNotFound`). Rename the value `EanNotFound` → `MaterialContainerNotFound`. Update any handler still referencing the old name. Add (for upcoming tasks) two new values used later:
- `MaterialContainerCodeExists`
- `MaterialContainerCodeInvalidFormat`

These two are unused right now — they're for Task 9. Just adding the enum values now to keep the diff localized.

- [ ] **Step 4: Build the whole solution**

Run: `cd backend && dotnet build`
Expected: clean build.

- [ ] **Step 5: Run all tests**

Run: `cd backend && dotnet test --no-build`
Expected: all existing tests pass (no test behavior changed — pure rename).

- [ ] **Step 6: `dotnet format`**

Run: `cd backend && dotnet format`
Expected: clean exit.

- [ ] **Step 7: Commit**

```bash
cd backend
git add -A
git commit -m "refactor: rename Ean entity to MaterialContainer

Mechanical rename across Domain, Persistence, Application, API, and tests.
No schema or behavior changes — table still 'Eans', endpoints now under
/api/material-containers. Field additions and the schema rename come in
follow-up commits."
```

---

## Phase 2 — Schema & Behavior Changes

### Task 5: Add `MaterialContainerStatus` enum + `Status` column + rename table

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/MaterialContainerStatus.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/MaterialContainer.cs` — add property
- Modify: `backend/src/Anela.Heblo.Persistence/Catalog/Inventory/MaterialContainerConfiguration.cs` — map property, rename table, rename indexes, rename FK
- Create: new EF migration `XXXX_RenameEansToMaterialContainersAddStatus.cs`
- Modify: test `CreateMaterialContainersHandlerTests.cs` — assert `Status == Assigned` on new rows

- [ ] **Step 1: Write the failing test**

Add to `CreateMaterialContainersHandlerTests.cs`:

```csharp
[Fact]
public async Task Handle_ValidRequest_CreatesContainersWithAssignedStatus()
{
    // Arrange
    var lot = new Lot("MAT001", "L1", null, DateOnly.FromDateTime(DateTime.Today), null, "user");
    _lotRepo.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(lot);
    _generator.Setup(g => g.GenerateAsync(1, default))
        .ReturnsAsync(new List<string> { "INT-00000001" });
    List<MaterialContainer>? captured = null;
    _containerRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), default))
        .Callback<IEnumerable<MaterialContainer>, CancellationToken>((items, _) => captured = items.ToList())
        .ReturnsAsync((IEnumerable<MaterialContainer> items, CancellationToken _) => items);
    _containerRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(1);

    var request = new CreateMaterialContainersRequest
    {
        LotId = 1,
        Items = new List<CreateMaterialContainerItem> { new() { Amount = 25m, Unit = "kg" } }
    };

    // Act
    await _handler.Handle(request, default);

    // Assert
    Assert.NotNull(captured);
    Assert.Single(captured);
    Assert.Equal(MaterialContainerStatus.Assigned, captured[0].Status);
}
```

- [ ] **Step 2: Run the test to confirm it fails**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~CreateMaterialContainersHandlerTests.Handle_ValidRequest_CreatesContainersWithAssignedStatus" --no-build`
Expected: compile error (`MaterialContainerStatus` doesn't exist, `Status` property missing).

- [ ] **Step 3: Create the `MaterialContainerStatus` enum**

```csharp
// backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/MaterialContainerStatus.cs
namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

public enum MaterialContainerStatus
{
    Assigned = 0,
    Discarded = 1,
}
```

- [ ] **Step 4: Add `Status` property and ctor initialization to `MaterialContainer`**

In `MaterialContainer.cs`, add at the end of the ctor body:

```csharp
Status = MaterialContainerStatus.Assigned;
```

Add the property below the existing properties:

```csharp
public MaterialContainerStatus Status { get; private set; }
```

Also add a method for the future discard transition (used by Task 10):

```csharp
public void Discard(string updatedBy)
{
    if (string.IsNullOrWhiteSpace(updatedBy)) throw new ArgumentException("UpdatedBy is required.", nameof(updatedBy));
    if (Status == MaterialContainerStatus.Discarded) return;
    Status = MaterialContainerStatus.Discarded;
    UpdatedAt = DateTime.UtcNow;
    UpdatedBy = updatedBy;
}
```

- [ ] **Step 5: Map the `Status` property and rename table + indexes + FK in configuration**

```csharp
// backend/src/Anela.Heblo.Persistence/Catalog/Inventory/MaterialContainerConfiguration.cs
// REPLACE the whole Configure method with:
public void Configure(EntityTypeBuilder<MaterialContainer> builder)
{
    builder.ToTable("MaterialContainers", "public");

    builder.HasKey(x => x.Id);

    builder.Property(x => x.Code).IsRequired().HasMaxLength(20);
    builder.Property(x => x.LotId).IsRequired();
    builder.Property(x => x.Amount).IsRequired().HasPrecision(18, 4);
    builder.Property(x => x.Unit).IsRequired().HasMaxLength(20);
    builder.Property(x => x.Status).IsRequired().HasConversion<int>();
    builder.Property(x => x.CreatedAt).IsRequired().HasColumnType("timestamp without time zone");
    builder.Property(x => x.CreatedBy).IsRequired().HasMaxLength(100);
    builder.Property(x => x.UpdatedAt).HasColumnType("timestamp without time zone");
    builder.Property(x => x.UpdatedBy).HasMaxLength(100);

    builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("IX_MaterialContainers_Code");
    builder.HasIndex(x => x.LotId).HasDatabaseName("IX_MaterialContainers_LotId");

    builder.HasOne<Lot>()
        .WithMany()
        .HasForeignKey(x => x.LotId)
        .OnDelete(DeleteBehavior.Restrict)
        .HasConstraintName("FK_MaterialContainers_Lots_LotId");
}
```

- [ ] **Step 6: Generate the EF migration**

Run from `backend/`:

```bash
dotnet ef migrations add RenameEansToMaterialContainersAddStatus \
  --project src/Anela.Heblo.Persistence \
  --startup-project src/Anela.Heblo.API
```

This produces a migration that EF will think is "drop Eans, create MaterialContainers." Replace the generated `Up`/`Down` bodies with explicit rename SQL to preserve any data:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql("ALTER TABLE public.\"Eans\" RENAME TO \"MaterialContainers\";");
    migrationBuilder.Sql("ALTER INDEX public.\"IX_Eans_Code\" RENAME TO \"IX_MaterialContainers_Code\";");
    migrationBuilder.Sql("ALTER INDEX public.\"IX_Eans_LotId\" RENAME TO \"IX_MaterialContainers_LotId\";");
    migrationBuilder.Sql("ALTER TABLE public.\"MaterialContainers\" RENAME CONSTRAINT \"PK_Eans\" TO \"PK_MaterialContainers\";");
    migrationBuilder.Sql("ALTER TABLE public.\"MaterialContainers\" RENAME CONSTRAINT \"FK_Eans_Lots_LotId\" TO \"FK_MaterialContainers_Lots_LotId\";");
    migrationBuilder.Sql("ALTER SEQUENCE public.ean_internal_seq RENAME TO material_container_internal_seq;");

    migrationBuilder.AddColumn<int>(
        name: "Status",
        schema: "public",
        table: "MaterialContainers",
        type: "integer",
        nullable: false,
        defaultValue: 0);
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropColumn(name: "Status", schema: "public", table: "MaterialContainers");
    migrationBuilder.Sql("ALTER SEQUENCE public.material_container_internal_seq RENAME TO ean_internal_seq;");
    migrationBuilder.Sql("ALTER TABLE public.\"MaterialContainers\" RENAME CONSTRAINT \"FK_MaterialContainers_Lots_LotId\" TO \"FK_Eans_Lots_LotId\";");
    migrationBuilder.Sql("ALTER TABLE public.\"MaterialContainers\" RENAME CONSTRAINT \"PK_MaterialContainers\" TO \"PK_Eans\";");
    migrationBuilder.Sql("ALTER INDEX public.\"IX_MaterialContainers_LotId\" RENAME TO \"IX_Eans_LotId\";");
    migrationBuilder.Sql("ALTER INDEX public.\"IX_MaterialContainers_Code\" RENAME TO \"IX_Eans_Code\";");
    migrationBuilder.Sql("ALTER TABLE public.\"MaterialContainers\" RENAME TO \"Eans\";");
}
```

Also update the SQL constant inside `MaterialContainerCodeGenerator.cs` (originally `EanCodeGenerator.cs`) so the sequence name matches: change `ean_internal_seq` → `material_container_internal_seq`.

- [ ] **Step 7: Run the failing test to verify it now passes**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~CreateMaterialContainersHandlerTests.Handle_ValidRequest_CreatesContainersWithAssignedStatus" --no-build`
Expected: PASS.

- [ ] **Step 8: Run full suite + format**

Run: `cd backend && dotnet build && dotnet test --no-build && dotnet format`
Expected: all green.

- [ ] **Step 9: Migration roundtrip on local Postgres**

Verify the rename SQL works on a real PG database. Assuming `Heblo_TST` connection set in user secrets:

```bash
cd backend
dotnet ef database update --project src/Anela.Heblo.Persistence --startup-project src/Anela.Heblo.API
# Verify in psql:  \d public."MaterialContainers"   (table exists with Status column)
dotnet ef database update <previous-migration-name> --project src/Anela.Heblo.Persistence --startup-project src/Anela.Heblo.API
# Verify:  \d public."Eans"  (rolled back)
dotnet ef database update --project src/Anela.Heblo.Persistence --startup-project src/Anela.Heblo.API
# Final state: MaterialContainers exists
```

If `<previous-migration-name>` is unknown, list with `dotnet ef migrations list --project src/Anela.Heblo.Persistence --startup-project src/Anela.Heblo.API` and pick the one before `RenameEansToMaterialContainersAddStatus`.

- [ ] **Step 10: Commit**

```bash
cd backend
git add -A
git commit -m "feat: rename Eans table to MaterialContainers and add Status column

Adds MaterialContainerStatus enum (Assigned/Discarded) with Discarded soft-delete
helper on the entity. Migration renames table, indexes, FK, and the
ean_internal_seq sequence in place to preserve data.

Containers now default to Status=Assigned on creation."
```

---

### Task 6: Replace `LotId` FK with `MaterialCode` + `LotCode` string columns

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/MaterialContainer.cs` — ctor + properties
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/IMaterialContainerRepository.cs` — drop `AnyByLotIdAsync`, change `GetPaginatedAsync` signature
- Modify: `backend/src/Anela.Heblo.Persistence/Catalog/Inventory/MaterialContainerRepository.cs` — drop Lot lookup, query by MaterialCode/LotCode strings directly
- Modify: `backend/src/Anela.Heblo.Persistence/Catalog/Inventory/MaterialContainerConfiguration.cs` — drop LotId FK + index, add MaterialCode/LotCode columns + index
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/Contracts/MaterialContainerDto.cs` — replace `LotId` with `MaterialCode` + `LotCode`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/CreateMaterialContainers/CreateMaterialContainersRequest.cs` — drop top-level `LotId`, add `MaterialCode` + `LotCode` to each item
- Modify: `CreateMaterialContainersHandler.cs` — drop Lot lookup, use strings
- Modify: `CreateMaterialContainersRequestValidator.cs` — drop LotId rule, add MaterialCode/LotCode non-empty rules
- Modify: `GetMaterialContainerByCodeHandler.cs` — drop Lot lookup (lot codes are now on the container)
- Modify: `ListMaterialContainersHandler.cs` / `ListMaterialContainersRequest.cs` — adapt signature
- Modify: All Inventory handler tests — adapt
- Modify: `MaterialContainersController.cs` — drop `lotId` query param, keep `materialCode` + add `lotCode` query param
- Create: new EF migration `XXXX_ReplaceMaterialContainerLotIdWithStrings.cs`

- [ ] **Step 1: Write the failing test**

Add to `CreateMaterialContainersHandlerTests.cs`:

```csharp
[Fact]
public async Task Handle_ValidRequest_PersistsMaterialCodeAndLotCodeStrings()
{
    // Arrange
    _generator.Setup(g => g.GenerateAsync(1, default))
        .ReturnsAsync(new List<string> { "INT-00000001" });
    List<MaterialContainer>? captured = null;
    _containerRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), default))
        .Callback<IEnumerable<MaterialContainer>, CancellationToken>((items, _) => captured = items.ToList())
        .ReturnsAsync((IEnumerable<MaterialContainer> items, CancellationToken _) => items);
    _containerRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(1);

    var request = new CreateMaterialContainersRequest
    {
        Items = new List<CreateMaterialContainerItem>
        {
            new()
            {
                MaterialCode = "MAT001",
                LotCode = "SUPP-LOT-2026-04",
                Amount = 25m,
                Unit = "kg"
            }
        }
    };

    // Act
    var result = await _handler.Handle(request, default);

    // Assert
    Assert.True(result.Success);
    Assert.NotNull(captured);
    Assert.Equal("MAT001", captured[0].MaterialCode);
    Assert.Equal("SUPP-LOT-2026-04", captured[0].LotCode);
}
```

Delete (or comment out for this task and re-write in Task 7) the older test `Handle_ValidRequest_GeneratesAndPersistsEans` — it relies on the now-removed `request.LotId` shape. Re-introduce its semantics in Step 9 of this task once the new shape is wired up.

- [ ] **Step 2: Run the test to confirm it fails**

Expected: compile error (`MaterialCode`/`LotCode` properties don't exist on `MaterialContainer` or on `CreateMaterialContainerItem`).

- [ ] **Step 3: Update `MaterialContainer` entity**

```csharp
// backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/MaterialContainer.cs
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

public class MaterialContainer : IEntity<int>
{
    protected MaterialContainer() { } // EF Core

    public MaterialContainer(
        string code, string materialCode, string lotCode,
        decimal amount, string unit, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(materialCode)) throw new ArgumentException("MaterialCode is required.", nameof(materialCode));
        if (string.IsNullOrWhiteSpace(lotCode)) throw new ArgumentException("LotCode is required.", nameof(lotCode));
        if (amount <= 0) throw new ArgumentException("Amount must be positive.", nameof(amount));
        if (string.IsNullOrWhiteSpace(unit)) throw new ArgumentException("Unit is required.", nameof(unit));
        if (string.IsNullOrWhiteSpace(createdBy)) throw new ArgumentException("CreatedBy is required.", nameof(createdBy));

        Code = code;
        MaterialCode = materialCode;
        LotCode = lotCode;
        Amount = amount;
        Unit = unit;
        CreatedAt = DateTime.UtcNow;
        CreatedBy = createdBy;
        Status = MaterialContainerStatus.Assigned;
    }

    public int Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string MaterialCode { get; private set; } = null!;
    public string LotCode { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string Unit { get; private set; } = null!;
    public MaterialContainerStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTime? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public void Discard(string updatedBy)
    {
        if (string.IsNullOrWhiteSpace(updatedBy)) throw new ArgumentException("UpdatedBy is required.", nameof(updatedBy));
        if (Status == MaterialContainerStatus.Discarded) return;
        Status = MaterialContainerStatus.Discarded;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }
}
```

`LotId` property is gone.

- [ ] **Step 4: Update the repository interface and implementation**

```csharp
// backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/IMaterialContainerRepository.cs
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

public interface IMaterialContainerRepository : IRepository<MaterialContainer, int>
{
    Task<MaterialContainer?> GetByCodeAsync(string code, CancellationToken ct);
    Task<PagedResult<MaterialContainer>> GetPaginatedAsync(
        string? materialCode, string? lotCode, int page, int pageSize, CancellationToken ct);
    Task<string?> GetLastUsedLotCodeForMaterialAsync(string materialCode, CancellationToken ct);
}
```

```csharp
// backend/src/Anela.Heblo.Persistence/Catalog/Inventory/MaterialContainerRepository.cs
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Persistence.Repositories;
using Anela.Heblo.Xcc.Persistance;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Catalog.Inventory;

public class MaterialContainerRepository : BaseRepository<MaterialContainer, int>, IMaterialContainerRepository
{
    public MaterialContainerRepository(ApplicationDbContext context) : base(context) { }

    public Task<MaterialContainer?> GetByCodeAsync(string code, CancellationToken ct)
        => DbSet.FirstOrDefaultAsync(x => x.Code == code, ct);

    public async Task<PagedResult<MaterialContainer>> GetPaginatedAsync(
        string? materialCode, string? lotCode, int page, int pageSize, CancellationToken ct)
    {
        var query = DbSet.AsQueryable();
        if (!string.IsNullOrWhiteSpace(materialCode))
            query = query.Where(x => x.MaterialCode == materialCode);
        if (!string.IsNullOrWhiteSpace(lotCode))
            query = query.Where(x => x.LotCode == lotCode);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<MaterialContainer>
        {
            Items = items, TotalCount = totalCount, PageNumber = page, PageSize = pageSize
        };
    }

    public async Task<string?> GetLastUsedLotCodeForMaterialAsync(string materialCode, CancellationToken ct)
    {
        return await DbSet
            .Where(x => x.MaterialCode == materialCode)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.LotCode)
            .FirstOrDefaultAsync(ct);
    }
}
```

The `GetLastUsedLotCodeForMaterialAsync` method here is used by Task 12; adding it now is harmless and keeps repository changes in a single commit.

- [ ] **Step 5: Update EF configuration**

```csharp
// backend/src/Anela.Heblo.Persistence/Catalog/Inventory/MaterialContainerConfiguration.cs
public void Configure(EntityTypeBuilder<MaterialContainer> builder)
{
    builder.ToTable("MaterialContainers", "public");

    builder.HasKey(x => x.Id);

    builder.Property(x => x.Code).IsRequired().HasMaxLength(20);
    builder.Property(x => x.MaterialCode).IsRequired().HasMaxLength(50);
    builder.Property(x => x.LotCode).IsRequired().HasMaxLength(100);
    builder.Property(x => x.Amount).IsRequired().HasPrecision(18, 4);
    builder.Property(x => x.Unit).IsRequired().HasMaxLength(20);
    builder.Property(x => x.Status).IsRequired().HasConversion<int>();
    builder.Property(x => x.CreatedAt).IsRequired().HasColumnType("timestamp without time zone");
    builder.Property(x => x.CreatedBy).IsRequired().HasMaxLength(100);
    builder.Property(x => x.UpdatedAt).HasColumnType("timestamp without time zone");
    builder.Property(x => x.UpdatedBy).HasMaxLength(100);

    builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("IX_MaterialContainers_Code");
    builder.HasIndex(x => new { x.MaterialCode, x.LotCode })
        .HasDatabaseName("IX_MaterialContainers_MaterialCode_LotCode");
    builder.HasIndex(x => new { x.MaterialCode, x.CreatedAt })
        .HasDatabaseName("IX_MaterialContainers_MaterialCode_CreatedAt");

    // No FK to Lots — supplier lot is free-string data; Abra Flexi is source of truth.
}
```

- [ ] **Step 6: Update DTO + request + handler + validator**

```csharp
// backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/Contracts/MaterialContainerDto.cs
namespace Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;

public class MaterialContainerDto
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;
    public string MaterialCode { get; set; } = null!;
    public string LotCode { get; set; } = null!;
    public decimal Amount { get; set; }
    public string Unit { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
}
```

```csharp
// CreateMaterialContainersRequest.cs
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateMaterialContainers;

public class CreateMaterialContainersRequest : IRequest<CreateMaterialContainersResponse>
{
    public List<CreateMaterialContainerItem> Items { get; set; } = new();
}

public class CreateMaterialContainerItem
{
    public string MaterialCode { get; set; } = null!;
    public string LotCode { get; set; } = null!;
    public decimal Amount { get; set; }
    public string Unit { get; set; } = null!;
}
```

```csharp
// CreateMaterialContainersHandler.cs — drop Lot lookup
public async Task<CreateMaterialContainersResponse> Handle(
    CreateMaterialContainersRequest request, CancellationToken cancellationToken)
{
    var currentUser = _currentUserService.GetCurrentUser();
    var createdBy = currentUser.Name ?? "System";

    var codes = await _codeGenerator.GenerateAsync(request.Items.Count, cancellationToken);
    var containers = request.Items
        .Select((item, i) => new MaterialContainer(
            codes[i], item.MaterialCode, item.LotCode, item.Amount, item.Unit, createdBy))
        .ToList();

    await _containerRepository.AddRangeAsync(containers, cancellationToken);
    await _containerRepository.SaveChangesAsync(cancellationToken);

    _logger.LogInformation("Created {Count} MaterialContainers", containers.Count);

    return new CreateMaterialContainersResponse
    {
        Containers = containers.Select(MapToDto).ToList()
    };
}

internal static MaterialContainerDto MapToDto(MaterialContainer c) => new()
{
    Id = c.Id,
    Code = c.Code,
    MaterialCode = c.MaterialCode,
    LotCode = c.LotCode,
    Amount = c.Amount,
    Unit = c.Unit,
    CreatedAt = c.CreatedAt,
    CreatedBy = c.CreatedBy
};
```

Remove `ILotRepository _lotRepository` from the constructor and field — no longer needed.

```csharp
// CreateMaterialContainersRequestValidator.cs
using FluentValidation;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateMaterialContainers;

public class CreateMaterialContainersRequestValidator : AbstractValidator<CreateMaterialContainersRequest>
{
    public CreateMaterialContainersRequestValidator()
    {
        RuleFor(x => x.Items).NotEmpty().WithMessage("At least one item is required.");
        RuleFor(x => x.Items.Count).LessThanOrEqualTo(500)
            .WithMessage("Cannot create more than 500 containers in one call.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.MaterialCode).NotEmpty().MaximumLength(50);
            item.RuleFor(i => i.LotCode).NotEmpty().MaximumLength(100);
            item.RuleFor(i => i.Amount).GreaterThan(0).WithMessage("Amount must be positive.");
            item.RuleFor(i => i.Unit).NotEmpty().MaximumLength(InventoryConstants.UnitMaxLength);
        });
    }
}
```

- [ ] **Step 7: Update `GetMaterialContainerByCodeHandler` to drop the Lot lookup**

```csharp
// GetMaterialContainerByCodeHandler.cs
public async Task<GetMaterialContainerByCodeResponse> Handle(
    GetMaterialContainerByCodeRequest request, CancellationToken cancellationToken)
{
    var container = await _containerRepository.GetByCodeAsync(request.Code, cancellationToken);
    if (container == null)
    {
        return new GetMaterialContainerByCodeResponse(ErrorCodes.MaterialContainerNotFound,
            new Dictionary<string, string> { { "Code", request.Code } });
    }
    return new GetMaterialContainerByCodeResponse
    {
        Container = CreateMaterialContainersHandler.MapToDto(container)
    };
}
```

Update `GetMaterialContainerByCodeResponse.cs`: drop the `Lot` property; rename `Ean` → `Container`. Remove the `ILotRepository` dependency from the constructor.

- [ ] **Step 8: Update `ListMaterialContainersRequest` + handler + controller query params**

```csharp
// ListMaterialContainersRequest.cs
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListMaterialContainers;

public class ListMaterialContainersRequest : IRequest<ListMaterialContainersResponse>
{
    public string? MaterialCode { get; set; }
    public string? LotCode { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
```

```csharp
// ListMaterialContainersHandler.cs — single line change in the call:
var result = await _containerRepository.GetPaginatedAsync(
    request.MaterialCode, request.LotCode, request.Page, request.PageSize, cancellationToken);
```

```csharp
// MaterialContainersController.cs — adjust the GET signature
[HttpGet]
public async Task<ActionResult<ListMaterialContainersResponse>> GetMaterialContainers(
    [FromQuery] string? materialCode,
    [FromQuery] string? lotCode,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 20,
    CancellationToken cancellationToken = default)
{
    var request = new ListMaterialContainersRequest
        { MaterialCode = materialCode, LotCode = lotCode, Page = page, PageSize = pageSize };
    return HandleResponse(await _mediator.Send(request, cancellationToken));
}
```

- [ ] **Step 9: Re-fix tests across all handler test files**

In each test file:
- Replace `new Ean(...)` constructions to use new signature `new MaterialContainer(code, materialCode, lotCode, amount, unit, createdBy)`.
- Drop `lot`/`_lotRepo` setup in `CreateMaterialContainersHandlerTests` (handler no longer queries Lot).
- Drop `lot`/`_lotRepo` setup in `GetMaterialContainerByCodeHandlerTests`. Update assertions: response has `Container` (no `Lot`).
- Update `ListMaterialContainersHandlerTests` to test paginated query by `MaterialCode` + `LotCode` strings instead of by `LotId`.

Re-add a positive end-to-end create test (replacing the one removed in Step 1):

```csharp
[Fact]
public async Task Handle_ValidRequest_GeneratesCodesAndPersistsContainers()
{
    // Arrange
    _generator.Setup(g => g.GenerateAsync(2, default))
        .ReturnsAsync(new List<string> { "INT-00000001", "INT-00000002" });
    _containerRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), default))
        .ReturnsAsync((IEnumerable<MaterialContainer> items, CancellationToken _) => items);
    _containerRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(2);

    var request = new CreateMaterialContainersRequest
    {
        Items = new List<CreateMaterialContainerItem>
        {
            new() { MaterialCode = "MAT001", LotCode = "L1", Amount = 25m, Unit = "kg" },
            new() { MaterialCode = "MAT001", LotCode = "L1", Amount = 25m, Unit = "kg" }
        }
    };

    // Act
    var result = await _handler.Handle(request, default);

    // Assert
    Assert.True(result.Success);
    Assert.Equal(2, result.Containers.Count);
    Assert.Equal("INT-00000001", result.Containers[0].Code);
    _generator.Verify(g => g.GenerateAsync(2, default), Times.Once);
}
```

Also remove the constructor field `_lotRepo` from the test class.

- [ ] **Step 10: Generate the migration**

```bash
cd backend
dotnet ef migrations add ReplaceMaterialContainerLotIdWithStrings \
  --project src/Anela.Heblo.Persistence \
  --startup-project src/Anela.Heblo.API
```

Replace generated body with explicit data-preserving SQL:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // Add new columns nullable first, populate from Lots, then make NOT NULL
    migrationBuilder.AddColumn<string>(
        name: "MaterialCode", schema: "public", table: "MaterialContainers",
        type: "character varying(50)", maxLength: 50, nullable: true);
    migrationBuilder.AddColumn<string>(
        name: "LotCode", schema: "public", table: "MaterialContainers",
        type: "character varying(100)", maxLength: 100, nullable: true);

    migrationBuilder.Sql(@"
        UPDATE public.""MaterialContainers"" mc
        SET ""MaterialCode"" = l.""MaterialCode"", ""LotCode"" = l.""LotCode""
        FROM public.""Lots"" l
        WHERE mc.""LotId"" = l.""Id"";
    ");

    migrationBuilder.AlterColumn<string>(
        name: "MaterialCode", schema: "public", table: "MaterialContainers",
        type: "character varying(50)", maxLength: 50, nullable: false,
        oldClrType: typeof(string), oldType: "character varying(50)", oldMaxLength: 50, oldNullable: true);
    migrationBuilder.AlterColumn<string>(
        name: "LotCode", schema: "public", table: "MaterialContainers",
        type: "character varying(100)", maxLength: 100, nullable: false,
        oldClrType: typeof(string), oldType: "character varying(100)", oldMaxLength: 100, oldNullable: true);

    migrationBuilder.DropForeignKey(
        name: "FK_MaterialContainers_Lots_LotId", schema: "public", table: "MaterialContainers");
    migrationBuilder.DropIndex(
        name: "IX_MaterialContainers_LotId", schema: "public", table: "MaterialContainers");
    migrationBuilder.DropColumn(
        name: "LotId", schema: "public", table: "MaterialContainers");

    migrationBuilder.CreateIndex(
        name: "IX_MaterialContainers_MaterialCode_LotCode",
        schema: "public", table: "MaterialContainers",
        columns: new[] { "MaterialCode", "LotCode" });
    migrationBuilder.CreateIndex(
        name: "IX_MaterialContainers_MaterialCode_CreatedAt",
        schema: "public", table: "MaterialContainers",
        columns: new[] { "MaterialCode", "CreatedAt" });
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropIndex(name: "IX_MaterialContainers_MaterialCode_CreatedAt",
        schema: "public", table: "MaterialContainers");
    migrationBuilder.DropIndex(name: "IX_MaterialContainers_MaterialCode_LotCode",
        schema: "public", table: "MaterialContainers");

    migrationBuilder.AddColumn<int>(name: "LotId", schema: "public", table: "MaterialContainers",
        type: "integer", nullable: false, defaultValue: 0);
    migrationBuilder.Sql(@"
        UPDATE public.""MaterialContainers"" mc
        SET ""LotId"" = l.""Id""
        FROM public.""Lots"" l
        WHERE mc.""MaterialCode"" = l.""MaterialCode"" AND mc.""LotCode"" = l.""LotCode"";
    ");
    migrationBuilder.CreateIndex(name: "IX_MaterialContainers_LotId",
        schema: "public", table: "MaterialContainers", column: "LotId");
    migrationBuilder.AddForeignKey(
        name: "FK_MaterialContainers_Lots_LotId", schema: "public", table: "MaterialContainers",
        column: "LotId", principalSchema: "public", principalTable: "Lots", principalColumn: "Id",
        onDelete: ReferentialAction.Restrict);

    migrationBuilder.DropColumn(name: "LotCode", schema: "public", table: "MaterialContainers");
    migrationBuilder.DropColumn(name: "MaterialCode", schema: "public", table: "MaterialContainers");
}
```

- [ ] **Step 11: Build + test + format + migration roundtrip**

```bash
cd backend
dotnet build
dotnet test --no-build
dotnet format
```

All green. Then roundtrip the migration on `Heblo_TST` (apply → revert → apply).

- [ ] **Step 12: Commit**

```bash
cd backend
git add -A
git commit -m "feat: store MaterialContainer lot reference as MaterialCode+LotCode strings

Drops the LotId FK to Lots in favour of free-string MaterialCode + LotCode
columns. Abra Flexi will become the source of truth for lots — Heblo records
the supplier lot code as received from the carton with no DB-side existence
check.

Adds GetLastUsedLotCodeForMaterialAsync on the repository (consumed by the
new endpoint in a follow-up commit) plus a supporting
(MaterialCode, CreatedAt) index."
```

---

### Task 7: Make `Amount` and `Unit` nullable

**Files:**
- Modify: `MaterialContainer.cs` — remove ctor validation for amount/unit; allow null
- Modify: `MaterialContainerConfiguration.cs` — drop `.IsRequired()`
- Modify: `MaterialContainerDto.cs` — change to nullable
- Modify: `CreateMaterialContainersRequest.cs` — `Amount` and `Unit` become nullable on the item
- Modify: `CreateMaterialContainersRequestValidator.cs` — switch to conditional rules
- Modify: `CreateMaterialContainersHandler.cs` — overload ctor or use a different builder for the nullable case
- Modify: tests — new test case "creates container without Amount/Unit"
- Create: migration `XXXX_MakeMaterialContainerAmountUnitNullable.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task Handle_ItemWithoutAmountOrUnit_CreatesContainer()
{
    // Arrange
    _generator.Setup(g => g.GenerateAsync(1, default))
        .ReturnsAsync(new List<string> { "INT-00000001" });
    List<MaterialContainer>? captured = null;
    _containerRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), default))
        .Callback<IEnumerable<MaterialContainer>, CancellationToken>((items, _) => captured = items.ToList())
        .ReturnsAsync((IEnumerable<MaterialContainer> items, CancellationToken _) => items);
    _containerRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(1);

    var request = new CreateMaterialContainersRequest
    {
        Items = new List<CreateMaterialContainerItem>
        {
            new() { MaterialCode = "MAT001", LotCode = "L1" } // No Amount, no Unit
        }
    };

    // Act
    var result = await _handler.Handle(request, default);

    // Assert
    Assert.True(result.Success);
    Assert.NotNull(captured);
    Assert.Null(captured[0].Amount);
    Assert.Null(captured[0].Unit);
}
```

- [ ] **Step 2: Run the test to confirm it fails**

Expected: compile errors (`Amount`/`Unit` are non-nullable on entity and DTO).

- [ ] **Step 3: Update entity**

```csharp
// MaterialContainer.cs — replace ctor and properties
public MaterialContainer(
    string code, string materialCode, string lotCode,
    decimal? amount, string? unit, string createdBy)
{
    if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Code is required.", nameof(code));
    if (string.IsNullOrWhiteSpace(materialCode)) throw new ArgumentException("MaterialCode is required.", nameof(materialCode));
    if (string.IsNullOrWhiteSpace(lotCode)) throw new ArgumentException("LotCode is required.", nameof(lotCode));
    if (amount is <= 0) throw new ArgumentException("Amount must be positive when provided.", nameof(amount));
    if (unit is not null && string.IsNullOrWhiteSpace(unit)) throw new ArgumentException("Unit must be non-empty when provided.", nameof(unit));
    if (string.IsNullOrWhiteSpace(createdBy)) throw new ArgumentException("CreatedBy is required.", nameof(createdBy));

    Code = code;
    MaterialCode = materialCode;
    LotCode = lotCode;
    Amount = amount;
    Unit = unit;
    CreatedAt = DateTime.UtcNow;
    CreatedBy = createdBy;
    Status = MaterialContainerStatus.Assigned;
}

public decimal? Amount { get; private set; }
public string? Unit { get; private set; }
```

- [ ] **Step 4: Update EF configuration**

```csharp
builder.Property(x => x.Amount).HasPrecision(18, 4);   // drop .IsRequired()
builder.Property(x => x.Unit).HasMaxLength(20);        // drop .IsRequired()
```

- [ ] **Step 5: Update DTO, request item, handler, validator**

```csharp
// MaterialContainerDto.cs
public decimal? Amount { get; set; }
public string? Unit { get; set; }
```

```csharp
// CreateMaterialContainerItem
public decimal? Amount { get; set; }
public string? Unit { get; set; }
```

```csharp
// Handler — Update the constructor call to pass nullable values
new MaterialContainer(codes[i], item.MaterialCode, item.LotCode, item.Amount, item.Unit, createdBy)
```

```csharp
// Validator — conditional rules on Amount/Unit
item.RuleFor(i => i.Amount).GreaterThan(0).When(i => i.Amount.HasValue);
item.RuleFor(i => i.Unit).NotEmpty().MaximumLength(InventoryConstants.UnitMaxLength).When(i => i.Unit != null);
```

- [ ] **Step 6: Generate the migration**

```bash
cd backend
dotnet ef migrations add MakeMaterialContainerAmountUnitNullable \
  --project src/Anela.Heblo.Persistence \
  --startup-project src/Anela.Heblo.API
```

The generated migration should be two `AlterColumn` calls turning `Amount` and `Unit` nullable. Verify and accept as-is.

- [ ] **Step 7: Run the new test**

```bash
cd backend
dotnet test --filter "FullyQualifiedName~Handle_ItemWithoutAmountOrUnit" --no-build
```
Expected: PASS.

- [ ] **Step 8: Full suite + format**

```bash
cd backend && dotnet build && dotnet test --no-build && dotnet format
```

- [ ] **Step 9: Commit**

```bash
git add -A && git commit -m "feat: make MaterialContainer Amount and Unit optional

Per-container weight is deferred to a future phase. v1 receive flow does
not collect amount or unit — the operator just labels what material/lot
is in the container. Existing rows keep their values; new rows may omit
both fields."
```

---

### Task 8: Add nullable `PurchaseOrderLineId` FK on `MaterialContainer`

**Files:**
- Modify: `MaterialContainer.cs` — new optional ctor parameter
- Modify: `MaterialContainerConfiguration.cs` — new column + FK
- Modify: `MaterialContainerDto.cs`
- Modify: `CreateMaterialContainerItem` — add nullable `PurchaseOrderLineId`
- Modify: `CreateMaterialContainersHandler.cs` — pass through
- Modify: `CreateMaterialContainersRequestValidator.cs` — must reference an `InTransit` PO line if provided
- Add: `IPurchaseOrderRepository.GetLineByIdAsync(int, CancellationToken)` if not present (search to confirm)
- Modify: tests
- Create: migration `XXXX_AddMaterialContainerPurchaseOrderLineId.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task Handle_ItemWithPurchaseOrderLineId_PersistsLink()
{
    // Arrange
    var line = TestPurchaseOrderLineBuilder.InTransit(id: 42, materialId: "MAT001");
    _poRepo.Setup(r => r.GetLineByIdAsync(42, default)).ReturnsAsync(line);
    _generator.Setup(g => g.GenerateAsync(1, default)).ReturnsAsync(new List<string> { "INT-00000001" });
    List<MaterialContainer>? captured = null;
    _containerRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), default))
        .Callback<IEnumerable<MaterialContainer>, CancellationToken>((items, _) => captured = items.ToList())
        .ReturnsAsync((IEnumerable<MaterialContainer> items, CancellationToken _) => items);
    _containerRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(1);

    var request = new CreateMaterialContainersRequest
    {
        Items = new List<CreateMaterialContainerItem>
        {
            new() { MaterialCode = "MAT001", LotCode = "L1", PurchaseOrderLineId = 42 }
        }
    };

    // Act
    var result = await _handler.Handle(request, default);

    // Assert
    Assert.True(result.Success);
    Assert.Equal(42, captured![0].PurchaseOrderLineId);
}
```

You'll need a small builder for test PO lines. Add to the test project:

```csharp
// backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/TestPurchaseOrderLineBuilder.cs
using Anela.Heblo.Domain.Features.Purchase;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

internal static class TestPurchaseOrderLineBuilder
{
    public static PurchaseOrderLine InTransit(int id, string materialId)
    {
        // Use reflection-friendly factory if PurchaseOrderLine has a public ctor;
        // otherwise use the existing test factory in Anela.Heblo.Tests.Features.Purchase
        // and copy the pattern. See PurchaseOrder* test files for a working example.
        throw new NotImplementedException(
            "Implement using the same pattern as existing purchase order tests — " +
            "search backend/test for 'new PurchaseOrderLine' to find the canonical builder.");
    }
}
```

Before implementing the builder, **read** one of the existing PO-related test files (`Grep` for `new PurchaseOrderLine(` under `backend/test/`) and copy that construction pattern. If a builder already exists somewhere in the test project, prefer importing it directly.

- [ ] **Step 2: Run the test to confirm it fails**

Expected: compile error (`PurchaseOrderLineId` doesn't exist on the entity or request item).

- [ ] **Step 3: Update entity**

```csharp
// MaterialContainer.cs — add nullable property and accept in ctor
public MaterialContainer(
    string code, string materialCode, string lotCode,
    decimal? amount, string? unit, string createdBy,
    int? purchaseOrderLineId = null)
{
    // existing guards…
    Code = code;
    MaterialCode = materialCode;
    LotCode = lotCode;
    Amount = amount;
    Unit = unit;
    CreatedAt = DateTime.UtcNow;
    CreatedBy = createdBy;
    Status = MaterialContainerStatus.Assigned;
    PurchaseOrderLineId = purchaseOrderLineId;
}

public int? PurchaseOrderLineId { get; private set; }
```

- [ ] **Step 4: Update EF configuration**

```csharp
builder.Property(x => x.PurchaseOrderLineId);

builder.HasOne<PurchaseOrderLine>()
    .WithMany()
    .HasForeignKey(x => x.PurchaseOrderLineId)
    .OnDelete(DeleteBehavior.Restrict)
    .HasConstraintName("FK_MaterialContainers_PurchaseOrderLines_PurchaseOrderLineId");

builder.HasIndex(x => x.PurchaseOrderLineId)
    .HasDatabaseName("IX_MaterialContainers_PurchaseOrderLineId");
```

You'll need `using Anela.Heblo.Domain.Features.Purchase;` for the `PurchaseOrderLine` reference.

- [ ] **Step 5: Update DTO + request item + handler**

```csharp
// MaterialContainerDto.cs — add
public int? PurchaseOrderLineId { get; set; }
```

```csharp
// CreateMaterialContainerItem — add
public int? PurchaseOrderLineId { get; set; }
```

```csharp
// CreateMaterialContainersHandler.cs — adjust the entity construction:
new MaterialContainer(
    codes[i], item.MaterialCode, item.LotCode,
    item.Amount, item.Unit, createdBy,
    item.PurchaseOrderLineId)
```

```csharp
// Mapper — add to MapToDto
PurchaseOrderLineId = c.PurchaseOrderLineId
```

- [ ] **Step 6: Add validator rule for PO line existence + status**

The handler should call into the PO repository (assume `IPurchaseOrderRepository` exists; if not, search and find the canonical purchase-order data access). Inject it in the handler and validate at the start:

```csharp
// CreateMaterialContainersHandler.cs — before generating codes
foreach (var item in request.Items.Where(i => i.PurchaseOrderLineId.HasValue))
{
    var line = await _poRepo.GetLineByIdAsync(item.PurchaseOrderLineId!.Value, cancellationToken);
    if (line == null)
    {
        return new CreateMaterialContainersResponse(
            ErrorCodes.PurchaseOrderLineNotFound,
            new Dictionary<string, string> { { "PurchaseOrderLineId", item.PurchaseOrderLineId.Value.ToString() } });
    }
    // Optional: verify PO status — fetch the parent order and ensure InTransit.
    // If GetLineByIdAsync returns the parent ID, fetch parent; reuse existing helper.
}
```

If `GetLineByIdAsync` doesn't exist on the PO repository, add it (interface + implementation + brief test). Search first: `Grep` for `PurchaseOrderLine` under `backend/src` to find the existing data access pattern. Reuse rather than invent.

Add `ErrorCodes.PurchaseOrderLineNotFound` to the enum if missing.

- [ ] **Step 7: Generate migration**

```bash
cd backend
dotnet ef migrations add AddMaterialContainerPurchaseOrderLineId \
  --project src/Anela.Heblo.Persistence \
  --startup-project src/Anela.Heblo.API
```

Inspect that it adds the column + FK + index. No SQL edits needed.

- [ ] **Step 8: Run test + full suite + format**

```bash
cd backend && dotnet build && dotnet test --no-build && dotnet format
```

- [ ] **Step 9: Commit**

```bash
git add -A && git commit -m "feat: link MaterialContainer to PurchaseOrderLine

Optional FK with restrict-on-delete. When operators receive in PO mode, the
container row carries the line ID so future queries can trace which order a
container came from. Freeform receives leave the field null."
```

---

### Task 9: Add `Mxxxxxxxx` format validation; accept `Code` from request

The current handler uses `IMaterialContainerCodeGenerator` to mint codes. Per the spec, the operator scans a pre-printed barcode, so the **client** supplies the code. The generator stays in the codebase (registered, available for future admin/pool generation) but is no longer called by the create flow.

**Files:**
- Modify: `CreateMaterialContainerItem` — add required `Code`
- Modify: `CreateMaterialContainersHandler.cs` — drop the generator call; use `item.Code`; check uniqueness
- Modify: `CreateMaterialContainersRequestValidator.cs` — regex rule for `Code`
- Modify: tests — new cases for invalid format and duplicate code

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task Handle_InvalidCodeFormat_ReturnsValidationError()
{
    var request = new CreateMaterialContainersRequest
    {
        Items = new List<CreateMaterialContainerItem>
        {
            new() { Code = "BADCODE", MaterialCode = "MAT001", LotCode = "L1" }
        }
    };

    var validator = new CreateMaterialContainersRequestValidator();
    var result = await validator.ValidateAsync(request);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName.EndsWith("Code") && e.ErrorMessage.Contains("M"));
}

[Fact]
public async Task Handle_DuplicateCode_ReturnsCodeExistsError()
{
    // Arrange
    _containerRepo.Setup(r => r.GetByCodeAsync("M00000001", default))
        .ReturnsAsync(new MaterialContainer("M00000001", "MAT001", "L1", null, null, "user"));

    var request = new CreateMaterialContainersRequest
    {
        Items = new List<CreateMaterialContainerItem>
        {
            new() { Code = "M00000001", MaterialCode = "MAT001", LotCode = "L1" }
        }
    };

    // Act
    var result = await _handler.Handle(request, default);

    // Assert
    Assert.False(result.Success);
    Assert.Equal(ErrorCodes.MaterialContainerCodeExists, result.ErrorCode);
}
```

- [ ] **Step 2: Run the tests to confirm they fail**

Expected: compile error (`Code` not on item) or wrong behavior (no uniqueness check).

- [ ] **Step 3: Add `Code` to request item**

```csharp
// CreateMaterialContainerItem
public string Code { get; set; } = null!;
```

- [ ] **Step 4: Update handler to use the provided code**

```csharp
// CreateMaterialContainersHandler.cs
public async Task<CreateMaterialContainersResponse> Handle(
    CreateMaterialContainersRequest request, CancellationToken cancellationToken)
{
    // Uniqueness check
    foreach (var item in request.Items)
    {
        var existing = await _containerRepository.GetByCodeAsync(item.Code, cancellationToken);
        if (existing != null)
        {
            return new CreateMaterialContainersResponse(
                ErrorCodes.MaterialContainerCodeExists,
                new Dictionary<string, string>
                {
                    { "Code", item.Code },
                    { "MaterialCode", existing.MaterialCode },
                    { "LotCode", existing.LotCode },
                    { "Status", existing.Status.ToString() }
                });
        }
    }

    // PO line check (Task 8)
    foreach (var item in request.Items.Where(i => i.PurchaseOrderLineId.HasValue))
    {
        var line = await _poRepo.GetLineByIdAsync(item.PurchaseOrderLineId!.Value, cancellationToken);
        if (line == null)
            return new CreateMaterialContainersResponse(ErrorCodes.PurchaseOrderLineNotFound,
                new Dictionary<string, string> { { "PurchaseOrderLineId", item.PurchaseOrderLineId.Value.ToString() } });
    }

    var currentUser = _currentUserService.GetCurrentUser();
    var createdBy = currentUser.Name ?? "System";

    var containers = request.Items
        .Select(item => new MaterialContainer(
            item.Code, item.MaterialCode, item.LotCode,
            item.Amount, item.Unit, createdBy, item.PurchaseOrderLineId))
        .ToList();

    await _containerRepository.AddRangeAsync(containers, cancellationToken);
    await _containerRepository.SaveChangesAsync(cancellationToken);

    return new CreateMaterialContainersResponse
    {
        Containers = containers.Select(MapToDto).ToList()
    };
}
```

Drop the `IMaterialContainerCodeGenerator _codeGenerator` constructor parameter + field (no longer used by this handler).

- [ ] **Step 5: Add regex rule in validator**

```csharp
// CreateMaterialContainersRequestValidator.cs — inside RuleForEach.ChildRules
item.RuleFor(i => i.Code)
    .NotEmpty()
    .Matches(@"^M\d{8}$")
    .WithMessage("Code must match format M followed by 8 digits (e.g. M00000001).");
```

- [ ] **Step 6: Update existing tests that previously relied on the generator**

Anywhere a test set up `_generator.Setup(g => g.GenerateAsync(...))` and asserted that the handler returned generated codes — replace with assertions that the handler returns the codes from the request.

Remove `_generator` field and constructor parameter from the test class constructor.

- [ ] **Step 7: Run failing tests, then full suite**

```bash
cd backend
dotnet test --filter "FullyQualifiedName~CreateMaterialContainersHandlerTests" --no-build
dotnet build && dotnet test --no-build && dotnet format
```

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "feat: accept Mxxxxxxxx codes from caller and enforce format/uniqueness

The Terminal receive flow scans pre-printed labels; the handler no longer
mints codes via IMaterialContainerCodeGenerator. The generator remains in
the codebase for future admin-pool scenarios.

Validator rejects non-conforming codes; duplicate codes return
MaterialContainerCodeExists with the existing assignment in the payload so
the UI can show 'already assigned to MAT001/lot L1'."
```

---

### Task 10: Convert delete to soft-discard

**Files:**
- Modify: `DiscardMaterialContainerHandler.cs` — use `container.Discard(updatedBy)` + save
- Modify: `MaterialContainersController.cs` — route `DELETE /{id}` → `POST /{id}/discard`
- Modify: `DiscardMaterialContainerHandlerTests.cs` — assert status flip, not removal
- Modify: `MaterialContainerRepository.cs` — duplicate-check logic must consider discarded rows separately (already handled by `GetByCodeAsync` returning any row; the handler distinguishes via `existing.Status`)

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task Handle_ExistingContainer_FlipsStatusToDiscardedAndSetsAudit()
{
    // Arrange
    var container = new MaterialContainer("M00000001", "MAT001", "L1", null, null, "alice");
    _containerRepo.Setup(r => r.GetByIdAsync(7, default)).ReturnsAsync(container);
    _containerRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(1);
    _currentUser.Setup(x => x.GetCurrentUser())
        .Returns(new CurrentUser("u2", "bob", null, true));

    // Act
    var result = await _handler.Handle(new DiscardMaterialContainerRequest { Id = 7 }, default);

    // Assert
    Assert.True(result.Success);
    Assert.Equal(MaterialContainerStatus.Discarded, container.Status);
    Assert.Equal("bob", container.UpdatedBy);
    Assert.NotNull(container.UpdatedAt);
    _containerRepo.Verify(r => r.DeleteAsync(It.IsAny<MaterialContainer>(), default), Times.Never);
}
```

- [ ] **Step 2: Run the test to confirm it fails**

Expected: current handler still calls `DeleteAsync`, so test fails.

- [ ] **Step 3: Rewrite handler**

```csharp
// backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/DiscardMaterialContainer/DiscardMaterialContainerHandler.cs
public async Task<DiscardMaterialContainerResponse> Handle(
    DiscardMaterialContainerRequest request, CancellationToken cancellationToken)
{
    var container = await _containerRepository.GetByIdAsync(request.Id, cancellationToken);
    if (container == null)
    {
        return new DiscardMaterialContainerResponse(
            ErrorCodes.MaterialContainerNotFound,
            new Dictionary<string, string> { { "Id", request.Id.ToString() } });
    }

    var currentUser = _currentUserService.GetCurrentUser();
    var updatedBy = currentUser.Name ?? "System";
    container.Discard(updatedBy);

    await _containerRepository.SaveChangesAsync(cancellationToken);
    _logger.LogInformation("MaterialContainer {Id} discarded by {User}", request.Id, updatedBy);
    return new DiscardMaterialContainerResponse();
}
```

Inject `ICurrentUserService` in the constructor.

- [ ] **Step 4: Update controller route**

```csharp
// MaterialContainersController.cs — replace [HttpDelete("{id:int}")] with:
[HttpPost("{id:int}/discard")]
public async Task<ActionResult<DiscardMaterialContainerResponse>> Discard(
    int id, CancellationToken cancellationToken)
{
    var response = await _mediator.Send(new DiscardMaterialContainerRequest { Id = id }, cancellationToken);
    return HandleResponse(response);
}
```

- [ ] **Step 5: Update duplicate-code error path in `CreateMaterialContainersHandler`**

The error payload from Task 9 already includes `Status`. Confirm the message distinguishes `Assigned` vs `Discarded` so the frontend (Task 19+) can show the right Czech text.

- [ ] **Step 6: Run tests + format**

```bash
cd backend && dotnet build && dotnet test --no-build && dotnet format
```

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat: soft-discard MaterialContainer via POST /{id}/discard

Replaces hard DELETE with a status transition. Audit (UpdatedAt/UpdatedBy)
captures who discarded the row. v1 has no Terminal UI to call this — the
endpoint exists for forward compatibility and admin use."
```

---

### Task 11: Add `GET /api/material-containers/last-used-lot`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/GetLastUsedLotForMaterial/GetLastUsedLotForMaterialRequest.cs`
- Create: `…/GetLastUsedLotForMaterial/GetLastUsedLotForMaterialResponse.cs`
- Create: `…/GetLastUsedLotForMaterial/GetLastUsedLotForMaterialHandler.cs`
- Modify: `MaterialContainersController.cs` — add `GET /last-used-lot`
- Create: `GetLastUsedLotForMaterialHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/GetLastUsedLotForMaterialHandlerTests.cs
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetLastUsedLotForMaterial;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class GetLastUsedLotForMaterialHandlerTests
{
    private readonly Mock<IMaterialContainerRepository> _repo = new();
    private readonly GetLastUsedLotForMaterialHandler _handler;

    public GetLastUsedLotForMaterialHandlerTests()
    {
        _handler = new GetLastUsedLotForMaterialHandler(
            NullLogger<GetLastUsedLotForMaterialHandler>.Instance, _repo.Object);
    }

    [Fact]
    public async Task Handle_NoContainersForMaterial_ReturnsNullLotCode()
    {
        _repo.Setup(r => r.GetLastUsedLotCodeForMaterialAsync("MAT001", default))
            .ReturnsAsync((string?)null);

        var result = await _handler.Handle(
            new GetLastUsedLotForMaterialRequest { MaterialCode = "MAT001" }, default);

        Assert.True(result.Success);
        Assert.Null(result.LotCode);
    }

    [Fact]
    public async Task Handle_HasContainers_ReturnsMostRecentLotCode()
    {
        _repo.Setup(r => r.GetLastUsedLotCodeForMaterialAsync("MAT001", default))
            .ReturnsAsync("LOT-2026-04");

        var result = await _handler.Handle(
            new GetLastUsedLotForMaterialRequest { MaterialCode = "MAT001" }, default);

        Assert.True(result.Success);
        Assert.Equal("LOT-2026-04", result.LotCode);
    }
}
```

- [ ] **Step 2: Run the tests to confirm they fail**

Expected: compile errors (no handler / request / response types).

- [ ] **Step 3: Create request, response, handler**

```csharp
// GetLastUsedLotForMaterialRequest.cs
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetLastUsedLotForMaterial;

public class GetLastUsedLotForMaterialRequest : IRequest<GetLastUsedLotForMaterialResponse>
{
    public string MaterialCode { get; set; } = null!;
}
```

```csharp
// GetLastUsedLotForMaterialResponse.cs
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetLastUsedLotForMaterial;

public class GetLastUsedLotForMaterialResponse : BaseResponse
{
    public string? LotCode { get; set; }
    public GetLastUsedLotForMaterialResponse() : base() { }
    public GetLastUsedLotForMaterialResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

```csharp
// GetLastUsedLotForMaterialHandler.cs
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetLastUsedLotForMaterial;

public class GetLastUsedLotForMaterialHandler
    : IRequestHandler<GetLastUsedLotForMaterialRequest, GetLastUsedLotForMaterialResponse>
{
    private readonly ILogger<GetLastUsedLotForMaterialHandler> _logger;
    private readonly IMaterialContainerRepository _repo;

    public GetLastUsedLotForMaterialHandler(
        ILogger<GetLastUsedLotForMaterialHandler> logger,
        IMaterialContainerRepository repo)
    {
        _logger = logger;
        _repo = repo;
    }

    public async Task<GetLastUsedLotForMaterialResponse> Handle(
        GetLastUsedLotForMaterialRequest request, CancellationToken cancellationToken)
    {
        var lotCode = await _repo.GetLastUsedLotCodeForMaterialAsync(request.MaterialCode, cancellationToken);
        return new GetLastUsedLotForMaterialResponse { LotCode = lotCode };
    }
}
```

- [ ] **Step 4: Add controller endpoint**

```csharp
// MaterialContainersController.cs — add
[HttpGet("last-used-lot")]
public async Task<ActionResult<GetLastUsedLotForMaterialResponse>> GetLastUsedLot(
    [FromQuery] string materialCode, CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(materialCode))
        return BadRequest();
    var response = await _mediator.Send(
        new GetLastUsedLotForMaterialRequest { MaterialCode = materialCode }, cancellationToken);
    return HandleResponse(response);
}
```

Add the `using` directive.

- [ ] **Step 5: Run tests + format**

```bash
cd backend && dotnet build && dotnet test --no-build && dotnet format
```

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: add GET /api/material-containers/last-used-lot endpoint

Returns the lot code from the most-recently-created container for a given
material. Powers the Terminal receive UI's 'pre-fill last-used lot' default."
```

---

### Task 12: Add `PurchaseOrderStatus.Received` + status transition

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrderStatus.cs` — add `Received`
- Modify: existing `UpdatePurchaseOrderStatusHandler.cs` — allow `InTransit → Received → Completed` transitions
- Modify: corresponding handler tests
- (No migration — enum value is just a new int)

- [ ] **Step 1: Locate the existing handler**

Run: `Grep` for `UpdatePurchaseOrderStatusRequest` in `backend/src/`. Read the handler file. Confirm what's already there before making changes.

- [ ] **Step 2: Write the failing test**

Add a test asserting the `InTransit → Received` and `Received → Completed` transitions are allowed:

```csharp
[Fact]
public async Task Handle_TransitionFromInTransitToReceived_Succeeds()
{
    var po = TestPurchaseOrderBuilder.WithStatus(PurchaseOrderStatus.InTransit, id: 5);
    _poRepo.Setup(r => r.GetByIdAsync(5, default)).ReturnsAsync(po);
    _poRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(1);

    var result = await _handler.Handle(
        new UpdatePurchaseOrderStatusRequest { Id = 5, Status = PurchaseOrderStatus.Received }, default);

    Assert.True(result.Success);
    Assert.Equal(PurchaseOrderStatus.Received, po.Status);
}

[Fact]
public async Task Handle_TransitionFromReceivedToCompleted_Succeeds()
{
    var po = TestPurchaseOrderBuilder.WithStatus(PurchaseOrderStatus.Received, id: 5);
    _poRepo.Setup(r => r.GetByIdAsync(5, default)).ReturnsAsync(po);
    _poRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(1);

    var result = await _handler.Handle(
        new UpdatePurchaseOrderStatusRequest { Id = 5, Status = PurchaseOrderStatus.Completed }, default);

    Assert.True(result.Success);
    Assert.Equal(PurchaseOrderStatus.Completed, po.Status);
}
```

If `TestPurchaseOrderBuilder` does not exist, locate the canonical PO test setup file (`Grep` `new PurchaseOrder(`) and reuse its construction. **Do not invent a builder** — match the existing pattern.

- [ ] **Step 3: Run the tests to confirm they fail**

Expected: compile error (`Received` not in enum) or wrong behavior (transition rejected).

- [ ] **Step 4: Add the enum value**

```csharp
// PurchaseOrderStatus.cs
namespace Anela.Heblo.Domain.Features.Purchase;

public enum PurchaseOrderStatus
{
    Draft,
    InTransit,
    Received,
    Completed
}
```

Numeric values shift: `Completed` was `2`, now `3`. **Check for any persisted integer comparisons or migration data**. Run `Grep` for `(int)PurchaseOrderStatus` and inspect each call site; verify nothing stores the int form. If any database column persists this enum as int, **add a migration** that bumps existing `2` (Completed) rows to `3` before the code change. If the column is varchar (enum-as-string), no migration is needed — just code.

To check column type: read the `PurchaseOrder` EF configuration (search `IEntityTypeConfiguration<PurchaseOrder>`).

- [ ] **Step 5: Update transition validation in the handler**

In `UpdatePurchaseOrderStatusHandler.cs`, find the existing transition guard. Extend it to allow:
- `InTransit → Received`
- `Received → Completed`
- (Keep existing transitions intact.)

Refuse `Received → InTransit`, `Received → Draft`, etc., consistent with the existing pattern.

- [ ] **Step 6: Run tests + format**

```bash
cd backend && dotnet build && dotnet test --no-build && dotnet format
```

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat: add PurchaseOrderStatus.Received

New status sits between InTransit and Completed. Operators move POs into
Received once all containers have been labeled at the warehouse; Completed
remains the post-invoice closure state."
```

---

## Phase 3 — Frontend Terminal Workflow

### Task 13: Regenerate OpenAPI client

**Files:**
- Modify: `frontend/src/api/generated/api-client.ts` (auto-generated)

- [ ] **Step 1: Start the backend so the OpenAPI doc is reachable**

Read `docs/development/api-client-generation.md` to find the exact regen command. It's typically `cd frontend && npm run generate-api` or similar.

- [ ] **Step 2: Run the regen**

Run the documented command. The generator writes a new `api-client.ts` reflecting the rename + new endpoints + new DTO shape.

- [ ] **Step 3: Verify the frontend still builds**

```bash
cd frontend && npm run build
```
Expected: clean build. If there are TypeScript errors elsewhere, they're from frontend code that referenced the old `Ean*` types. Search and fix (likely zero hits per earlier exploration).

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "chore: regenerate OpenAPI client for MaterialContainer rename"
```

---

### Task 14: Add `useCreateMaterialContainers` hook + test

**Files:**
- Create: `frontend/src/api/hooks/useMaterialContainers.ts`
- Create: `frontend/src/api/hooks/__tests__/useMaterialContainers.test.ts`

- [ ] **Step 1: Write the failing test**

```typescript
// frontend/src/api/hooks/__tests__/useMaterialContainers.test.ts
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useCreateMaterialContainers } from '../useMaterialContainers';

jest.mock('../../client', () => ({
  getAuthenticatedApiClient: () => ({
    baseUrl: 'http://api',
    http: {
      fetch: jest.fn().mockResolvedValue({
        json: () => Promise.resolve({ success: true, containers: [{ id: 1, code: 'M00000001' }] })
      } as Response)
    }
  }),
  QUERY_KEYS: { materialContainers: ['materialContainers'] as const }
}));

const wrapper = ({ children }: { children: React.ReactNode }) => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
};

test('useCreateMaterialContainers posts items and returns response', async () => {
  const { result } = renderHook(() => useCreateMaterialContainers(), { wrapper });
  result.current.mutate({
    items: [{ code: 'M00000001', materialCode: 'MAT001', lotCode: 'L1' }]
  });
  await waitFor(() => expect(result.current.isSuccess).toBe(true));
  expect(result.current.data?.containers?.[0].code).toBe('M00000001');
});
```

- [ ] **Step 2: Run the test to confirm it fails**

Run: `cd frontend && npm test -- useMaterialContainers`
Expected: import error.

- [ ] **Step 3: Implement the hook**

```typescript
// frontend/src/api/hooks/useMaterialContainers.ts
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
import { BaseResponse } from '../../types/errors';

interface ApiClientWithInternals {
  baseUrl: string;
  http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
}

const JSON_HEADERS = { 'Content-Type': 'application/json' };

export interface CreateMaterialContainerItem {
  code: string;
  materialCode: string;
  lotCode: string;
  amount?: number;
  unit?: string;
  purchaseOrderLineId?: number;
}

export interface MaterialContainerDto {
  id: number;
  code: string;
  materialCode: string;
  lotCode: string;
  amount?: number;
  unit?: string;
  purchaseOrderLineId?: number;
  createdAt: string;
  createdBy: string;
}

export interface CreateMaterialContainersResult extends BaseResponse {
  containers?: MaterialContainerDto[];
}

const request = async <T>(path: string, init: RequestInit): Promise<T> => {
  const client = getAuthenticatedApiClient() as unknown as ApiClientWithInternals;
  const response = await client.http.fetch(`${client.baseUrl}${path}`, init);
  return (await response.json()) as T;
};

export const useCreateMaterialContainers = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: { items: CreateMaterialContainerItem[] }): Promise<CreateMaterialContainersResult> =>
      request<CreateMaterialContainersResult>('/api/material-containers', {
        method: 'POST',
        headers: JSON_HEADERS,
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.materialContainers });
    },
  });
};

export const useMaterialContainerByCode = (code: string | null) =>
  useQuery({
    enabled: !!code,
    queryKey: ['materialContainers', 'by-code', code],
    queryFn: () =>
      request<{ container?: MaterialContainerDto } & BaseResponse>(
        `/api/material-containers/by-code/${encodeURIComponent(code!)}`,
        { method: 'GET' }
      ),
  });

export const useLastUsedLotForMaterial = (materialCode: string | null) =>
  useQuery({
    enabled: !!materialCode,
    queryKey: ['materialContainers', 'last-used-lot', materialCode],
    queryFn: () =>
      request<{ lotCode?: string } & BaseResponse>(
        `/api/material-containers/last-used-lot?materialCode=${encodeURIComponent(materialCode!)}`,
        { method: 'GET' }
      ),
  });
```

If `QUERY_KEYS.materialContainers` doesn't exist on the client module, add it:

```typescript
// frontend/src/api/client.ts — extend QUERY_KEYS
materialContainers: ['materialContainers'] as const,
```

- [ ] **Step 4: Run the test**

Run: `cd frontend && npm test -- useMaterialContainers`
Expected: PASS.

- [ ] **Step 5: Lint + commit**

```bash
cd frontend && npm run lint
git add -A
git commit -m "feat(fe): add material container hooks (create, get-by-code, last-used-lot)"
```

---

### Task 15: Update purchase-order status hook

**Files:**
- Search: find existing PO status hook (likely `frontend/src/api/hooks/usePurchaseOrders.ts` or similar)
- Modify: add or update an `useUpdatePurchaseOrderStatus` hook to accept `Received`

- [ ] **Step 1: Locate**

Run: `Grep` for `purchase-orders/.*status` in `frontend/src/api/hooks/`. If a `useUpdatePurchaseOrderStatus` already exists, this task is just verifying it forwards the regenerated DTO's `Received` value correctly (no code changes needed — TypeScript will surface the new enum value). If no such hook exists, create one.

- [ ] **Step 2: If creating, add the hook**

```typescript
// frontend/src/api/hooks/usePurchaseOrders.ts (extend or create)
export type PurchaseOrderStatus = 'Draft' | 'InTransit' | 'Received' | 'Completed';

export interface UpdatePurchaseOrderStatusInput {
  id: number;
  status: PurchaseOrderStatus;
}

export const useUpdatePurchaseOrderStatus = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: UpdatePurchaseOrderStatusInput): Promise<BaseResponse> =>
      request<BaseResponse>(`/api/purchase-orders/${input.id}/status`, {
        method: 'PUT',
        headers: JSON_HEADERS,
        body: JSON.stringify({ id: input.id, status: input.status }),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['purchaseOrders'] });
    },
  });
};
```

Reuse the existing `request` helper if it's in the same file. Otherwise import from a shared location.

- [ ] **Step 3: Add a basic test (only if creating new hook)**

Mirror the test from Task 14, mocking a successful response.

- [ ] **Step 4: Lint + commit**

```bash
cd frontend && npm run build && npm run lint
git add -A
git commit -m "feat(fe): support PurchaseOrderStatus 'Received' in status update hook"
```

---

### Task 16: Build `LotIdentificationHome` (mode pick)

**Files:**
- Create: `frontend/src/components/terminal/lot-identification/LotIdentificationHome.tsx`
- Create: `frontend/src/components/terminal/lot-identification/__tests__/LotIdentificationHome.test.tsx`

- [ ] **Step 1: Write the failing test**

```typescript
// __tests__/LotIdentificationHome.test.tsx
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import LotIdentificationHome from '../LotIdentificationHome';

const renderHome = () =>
  render(
    <MemoryRouter>
      <LotIdentificationHome />
    </MemoryRouter>
  );

test('shows two mode tiles', () => {
  renderHome();
  expect(screen.getByText(/Příjem podle objednávky/i)).toBeInTheDocument();
  expect(screen.getByText(/Volný příjem/i)).toBeInTheDocument();
});

test('PO mode tile links to /terminal/lot-identification/po', () => {
  renderHome();
  const link = screen.getByRole('link', { name: /Příjem podle objednávky/i });
  expect(link).toHaveAttribute('href', '/terminal/lot-identification/po');
});

test('freeform mode tile links to /terminal/lot-identification/freeform', () => {
  renderHome();
  const link = screen.getByRole('link', { name: /Volný příjem/i });
  expect(link).toHaveAttribute('href', '/terminal/lot-identification/freeform');
});
```

- [ ] **Step 2: Run the test to confirm it fails**

```bash
cd frontend && npm test -- LotIdentificationHome
```
Expected: import error.

- [ ] **Step 3: Implement**

```tsx
// frontend/src/components/terminal/lot-identification/LotIdentificationHome.tsx
import { Link } from 'react-router-dom';
import { ClipboardList, PackagePlus, ChevronRight } from 'lucide-react';
import { useScreenView } from '../../../telemetry/useScreenView';

interface Tile {
  id: string;
  title: string;
  description: string;
  href: string;
  icon: React.ElementType;
}

const TILES: Tile[] = [
  {
    id: 'po',
    title: 'Příjem podle objednávky',
    description: 'Vyberte objednávku a štítkujte přijaté kontejnery',
    href: '/terminal/lot-identification/po',
    icon: ClipboardList,
  },
  {
    id: 'freeform',
    title: 'Volný příjem',
    description: 'Štítkujte kontejnery bez vazby na objednávku',
    href: '/terminal/lot-identification/freeform',
    icon: PackagePlus,
  },
];

const LotIdentificationHome = () => {
  useScreenView('Terminal', 'LotIdentificationHome');
  return (
    <div className="space-y-3 pt-2">
      <h1 className="text-xl font-bold text-neutral-slate">Identifikace šarže</h1>
      {TILES.map(({ id, title, description, href, icon: Icon }) => (
        <Link
          key={id}
          to={href}
          data-testid={`lot-id-tile-${id}`}
          className="flex items-center gap-4 bg-white border border-border-light rounded-xl p-4 shadow-soft hover:border-primary-blue hover:shadow-hover transition-all min-h-[72px]"
        >
          <div className="flex-shrink-0 w-12 h-12 bg-secondary-blue-pale rounded-xl flex items-center justify-center">
            <Icon className="h-6 w-6 text-primary-blue" />
          </div>
          <div className="flex-1 min-w-0">
            <p className="text-base font-semibold text-neutral-slate">{title}</p>
            <p className="text-sm text-neutral-gray mt-0.5">{description}</p>
          </div>
          <ChevronRight className="h-5 w-5 text-neutral-gray flex-shrink-0" />
        </Link>
      ))}
    </div>
  );
};

export default LotIdentificationHome;
```

- [ ] **Step 4: Run the test**

Run: `cd frontend && npm test -- LotIdentificationHome`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(fe): add LotIdentificationHome mode-pick screen"
```

---

### Task 17: Build `PoPickStep` (lists `InTransit` POs) and `PoLinePickStep`

**Files:**
- Create: `frontend/src/components/terminal/lot-identification/PoPickStep.tsx`
- Create: `.../PoLinePickStep.tsx`
- Create: `.../__tests__/PoPickStep.test.tsx`
- Create: `.../__tests__/PoLinePickStep.test.tsx`
- Re-use: an existing PO list hook if present (search `Grep` for `usePurchaseOrders`)

- [ ] **Step 1: Write the failing tests**

```typescript
// PoPickStep.test.tsx
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import PoPickStep from '../PoPickStep';

jest.mock('../../../../api/hooks/usePurchaseOrders', () => ({
  usePurchaseOrdersList: () => ({
    data: { items: [
      { id: 1, orderNumber: 'PO-001', supplierName: 'Acme', status: 'InTransit' },
      { id: 2, orderNumber: 'PO-002', supplierName: 'Beta', status: 'InTransit' },
    ]},
    isLoading: false,
  })
}));

const renderStep = () => {
  const client = new QueryClient();
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <PoPickStep />
      </MemoryRouter>
    </QueryClientProvider>
  );
};

test('renders one row per InTransit PO', () => {
  renderStep();
  expect(screen.getByText('PO-001')).toBeInTheDocument();
  expect(screen.getByText('PO-002')).toBeInTheDocument();
});

test('PO row links to /terminal/lot-identification/po/{id}', () => {
  renderStep();
  expect(screen.getByRole('link', { name: /PO-001/ }))
    .toHaveAttribute('href', '/terminal/lot-identification/po/1');
});
```

```typescript
// PoLinePickStep.test.tsx
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import PoLinePickStep from '../PoLinePickStep';

jest.mock('../../../../api/hooks/usePurchaseOrders', () => ({
  usePurchaseOrderDetail: (_id: number) => ({
    data: {
      id: 1,
      orderNumber: 'PO-001',
      lines: [
        { id: 10, materialId: 'MAT001', materialName: 'Olive Oil', quantity: 100 },
        { id: 11, materialId: 'MAT002', materialName: 'Shea Butter', quantity: 50 },
      ]
    },
    isLoading: false,
  })
}));

test('renders one row per PO line and links to the lot entry step', () => {
  const client = new QueryClient();
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/terminal/lot-identification/po/1']}>
        <Routes>
          <Route path="/terminal/lot-identification/po/:id" element={<PoLinePickStep />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
  expect(screen.getByText('MAT001')).toBeInTheDocument();
  expect(screen.getByRole('link', { name: /Olive Oil/ }))
    .toHaveAttribute('href', '/terminal/lot-identification/po/1/line/10');
});
```

- [ ] **Step 2: Run the tests to confirm they fail**

```bash
cd frontend && npm test -- "PoPickStep|PoLinePickStep"
```

- [ ] **Step 3: Implement `PoPickStep.tsx`**

```tsx
// PoPickStep.tsx
import { Link } from 'react-router-dom';
import { ChevronRight, ClipboardList } from 'lucide-react';
import { usePurchaseOrdersList } from '../../../api/hooks/usePurchaseOrders';
import { useScreenView } from '../../../telemetry/useScreenView';

const PoPickStep = () => {
  useScreenView('Terminal', 'LotIdentificationPoPick');
  const { data, isLoading } = usePurchaseOrdersList({ status: 'InTransit' });

  if (isLoading) return <p className="text-sm text-neutral-gray">Načítám objednávky…</p>;
  if (!data?.items?.length) return <p className="text-sm text-neutral-gray">Žádné objednávky v přepravě.</p>;

  return (
    <div className="space-y-3 pt-2">
      <h2 className="text-lg font-semibold text-neutral-slate">Vyberte objednávku</h2>
      {data.items.map((po) => (
        <Link
          key={po.id}
          to={`/terminal/lot-identification/po/${po.id}`}
          className="flex items-center gap-3 bg-white border border-border-light rounded-xl p-3 hover:border-primary-blue"
        >
          <ClipboardList className="h-5 w-5 text-primary-blue flex-shrink-0" />
          <div className="flex-1 min-w-0">
            <p className="font-semibold text-neutral-slate">{po.orderNumber}</p>
            <p className="text-sm text-neutral-gray">{po.supplierName}</p>
          </div>
          <ChevronRight className="h-5 w-5 text-neutral-gray" />
        </Link>
      ))}
    </div>
  );
};

export default PoPickStep;
```

If `usePurchaseOrdersList` doesn't accept a status filter today, extend it (or use whatever the existing hook is — read first, then extend minimally).

- [ ] **Step 4: Implement `PoLinePickStep.tsx`**

```tsx
// PoLinePickStep.tsx
import { Link, useParams } from 'react-router-dom';
import { ChevronRight } from 'lucide-react';
import { usePurchaseOrderDetail } from '../../../api/hooks/usePurchaseOrders';
import { useScreenView } from '../../../telemetry/useScreenView';

const PoLinePickStep = () => {
  useScreenView('Terminal', 'LotIdentificationPoLinePick');
  const { id } = useParams<{ id: string }>();
  const poId = id ? parseInt(id, 10) : 0;
  const { data, isLoading } = usePurchaseOrderDetail(poId);

  if (isLoading || !data) return <p className="text-sm text-neutral-gray">Načítám…</p>;

  return (
    <div className="space-y-3 pt-2">
      <h2 className="text-lg font-semibold text-neutral-slate">{data.orderNumber} — položky</h2>
      {data.lines.map((line) => (
        <Link
          key={line.id}
          to={`/terminal/lot-identification/po/${poId}/line/${line.id}`}
          className="flex items-center gap-3 bg-white border border-border-light rounded-xl p-3 hover:border-primary-blue"
        >
          <div className="flex-1 min-w-0">
            <p className="font-semibold text-neutral-slate">{line.materialName}</p>
            <p className="text-sm text-neutral-gray">{line.materialId} · {line.quantity}</p>
          </div>
          <ChevronRight className="h-5 w-5 text-neutral-gray" />
        </Link>
      ))}
    </div>
  );
};

export default PoLinePickStep;
```

- [ ] **Step 5: Run tests + lint**

```bash
cd frontend && npm test -- "PoPickStep|PoLinePickStep" && npm run lint
```

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(fe): add PoPickStep and PoLinePickStep for PO receive flow"
```

---

### Task 18: Build `FreeformMaterialStep` and `LotEntryStep`

**Files:**
- Create: `frontend/src/components/terminal/lot-identification/FreeformMaterialStep.tsx`
- Create: `.../LotEntryStep.tsx`
- Create: `.../__tests__/FreeformMaterialStep.test.tsx`
- Create: `.../__tests__/LotEntryStep.test.tsx`

`FreeformMaterialStep` accepts a material code (scanned or typed via `ScanInput`) and navigates to the lot-entry step. `LotEntryStep` displays the chosen material, pre-fills the last-used lot, and on submit pushes the operator into the scan-loop step with material + lot in context (via route params).

- [ ] **Step 1: Write failing tests**

```typescript
// FreeformMaterialStep.test.tsx
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import FreeformMaterialStep from '../FreeformMaterialStep';

const mockNavigate = jest.fn();
jest.mock('react-router-dom', () => ({
  ...jest.requireActual('react-router-dom'),
  useNavigate: () => mockNavigate,
}));

beforeEach(() => mockNavigate.mockReset());

test('on scan, navigates to lot-entry step with material code in URL', () => {
  render(<MemoryRouter><FreeformMaterialStep /></MemoryRouter>);
  const input = screen.getByRole('textbox') as HTMLInputElement;
  fireEvent.change(input, { target: { value: 'mat001' } });
  fireEvent.submit(input.form!);
  expect(mockNavigate).toHaveBeenCalledWith('/terminal/lot-identification/freeform/MAT001/lot');
});
```

```typescript
// LotEntryStep.test.tsx
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import LotEntryStep from '../LotEntryStep';

const mockNavigate = jest.fn();
jest.mock('react-router-dom', () => ({
  ...jest.requireActual('react-router-dom'),
  useNavigate: () => mockNavigate,
}));

jest.mock('../../../../api/hooks/useMaterialContainers', () => ({
  useLastUsedLotForMaterial: () => ({ data: { lotCode: 'LOT-2026-04' }, isLoading: false })
}));

beforeEach(() => mockNavigate.mockReset());

const renderStep = (initial = '/terminal/lot-identification/freeform/MAT001/lot') => {
  const client = new QueryClient();
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[initial]}>
        <Routes>
          <Route path="/terminal/lot-identification/freeform/:material/lot" element={<LotEntryStep mode="freeform" />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
};

test('pre-fills lot input with last-used lot for the material', async () => {
  renderStep();
  const input = await screen.findByRole('textbox') as HTMLInputElement;
  await waitFor(() => expect(input.value).toBe('LOT-2026-04'));
});

test('on submit, navigates to scan-loop step with material+lot in URL', () => {
  renderStep();
  const input = screen.getByRole('textbox') as HTMLInputElement;
  fireEvent.change(input, { target: { value: 'LOT-2026-05' } });
  fireEvent.submit(input.form!);
  expect(mockNavigate).toHaveBeenCalledWith(
    '/terminal/lot-identification/freeform/MAT001/lot/LOT-2026-05/scan'
  );
});
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
cd frontend && npm test -- "FreeformMaterialStep|LotEntryStep"
```

- [ ] **Step 3: Implement `FreeformMaterialStep.tsx`**

```tsx
// FreeformMaterialStep.tsx
import { useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import ScanInput from '../ScanInput';
import { useScreenView } from '../../../telemetry/useScreenView';

const FreeformMaterialStep = () => {
  useScreenView('Terminal', 'LotIdentificationFreeformMaterial');
  const navigate = useNavigate();
  const handleScan = useCallback((value: string) => {
    navigate(`/terminal/lot-identification/freeform/${encodeURIComponent(value)}/lot`);
  }, [navigate]);

  return (
    <div className="space-y-4 pt-2">
      <h2 className="text-lg font-semibold text-neutral-slate">Kód materiálu</h2>
      <ScanInput label="Naskenujte nebo zadejte kód materiálu" onScan={handleScan} />
    </div>
  );
};

export default FreeformMaterialStep;
```

- [ ] **Step 4: Implement `LotEntryStep.tsx`**

```tsx
// LotEntryStep.tsx
import { useEffect, useState, useCallback } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import ScanInput from '../ScanInput';
import { useLastUsedLotForMaterial } from '../../../api/hooks/useMaterialContainers';
import { useScreenView } from '../../../telemetry/useScreenView';

interface LotEntryStepProps {
  mode: 'freeform' | 'po';
}

const LotEntryStep = ({ mode }: LotEntryStepProps) => {
  useScreenView('Terminal', 'LotIdentificationLotEntry');
  const navigate = useNavigate();
  const params = useParams<{ material: string; id?: string; lineId?: string }>();
  const materialCode = params.material ?? '';
  const { data } = useLastUsedLotForMaterial(materialCode);
  const [lotValue, setLotValue] = useState('');

  useEffect(() => {
    if (data?.lotCode) setLotValue(data.lotCode);
  }, [data?.lotCode]);

  const handleScan = useCallback((value: string) => {
    const lot = value.trim();
    if (!lot) return;
    if (mode === 'freeform') {
      navigate(`/terminal/lot-identification/freeform/${encodeURIComponent(materialCode)}/lot/${encodeURIComponent(lot)}/scan`);
    } else {
      navigate(`/terminal/lot-identification/po/${params.id}/line/${params.lineId}/lot/${encodeURIComponent(lot)}/scan`);
    }
  }, [mode, materialCode, navigate, params.id, params.lineId]);

  // ScanInput is stateless about default values — render a controlled wrapper
  return (
    <div className="space-y-4 pt-2">
      <h2 className="text-lg font-semibold text-neutral-slate">
        Šarže pro materiál {materialCode}
      </h2>
      <ScanInput
        label="Šarže (z etikety dodavatele)"
        onScan={handleScan}
        // ScanInput does not currently support a default value prop.
        // For the prefill, render an inline input note instead, OR
        // extend ScanInput with a `defaultValue` prop in a small follow-up.
      />
      {lotValue && (
        <p className="text-sm text-neutral-gray">
          Poslední použitá šarže: <span className="font-mono">{lotValue}</span>
        </p>
      )}
    </div>
  );
};

export default LotEntryStep;
```

**IMPORTANT — sub-task:** ScanInput as currently designed does not accept a `defaultValue` prop. Either:
- (a) Add a `defaultValue?: string` prop to `ScanInput` (preferred; small additive change), wire through `useState`, and use it here, OR
- (b) Implement a controlled wrapper in `LotEntryStep` that renders a separate input (breaks the mandatory-pattern from `memory/patterns/terminal-scan-input.md`).

Choose (a). Add the prop:

```typescript
// ScanInput.tsx — add to props interface
defaultValue?: string;

// in the component body, initialize state with the default if provided
const [value, setValue] = useState(defaultValue ?? '');
```

Then use in `LotEntryStep`:

```tsx
<ScanInput
  label="Šarže (z etikety dodavatele)"
  onScan={handleScan}
  defaultValue={data?.lotCode ?? ''}
/>
```

Update the matching `ScanInput` test (if there is one explicitly testing default value behavior) — otherwise no test churn.

Re-run the `LotEntryStep` test from Step 1 — the input pre-fill assertion should now pass.

- [ ] **Step 5: Run tests + lint**

```bash
cd frontend && npm test -- "FreeformMaterialStep|LotEntryStep|ScanInput" && npm run lint
```

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(fe): add FreeformMaterialStep and LotEntryStep with last-used-lot pre-fill

Extends ScanInput with a defaultValue prop so the lot-entry step can pre-populate
with the last lot used for the chosen material."
```

---

### Task 19: Build `ContainerScanLoop`

**Files:**
- Create: `frontend/src/components/terminal/lot-identification/ContainerScanLoop.tsx`
- Create: `.../__tests__/ContainerScanLoop.test.tsx`

The scan loop is the heart of the workflow. Reads material+lot from URL params (and `poLineId` if present). For each scan: validates format, calls `useCreateMaterialContainers`, shows success toast, refocuses input. Header shows sticky context. "Hotovo" button navigates back to a summary/finish step.

- [ ] **Step 1: Write failing tests**

```typescript
// ContainerScanLoop.test.tsx
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import ContainerScanLoop from '../ContainerScanLoop';

const mockMutate = jest.fn();
jest.mock('../../../../api/hooks/useMaterialContainers', () => ({
  useCreateMaterialContainers: () => ({
    mutate: mockMutate,
    isPending: false,
    reset: jest.fn(),
  })
}));

beforeEach(() => mockMutate.mockReset());

const renderLoop = (path: string) => {
  const client = new QueryClient();
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route
            path="/terminal/lot-identification/freeform/:material/lot/:lot/scan"
            element={<ContainerScanLoop mode="freeform" />}
          />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
};

test('header shows sticky material + lot context', () => {
  renderLoop('/terminal/lot-identification/freeform/MAT001/lot/L1/scan');
  expect(screen.getByText(/MAT001/)).toBeInTheDocument();
  expect(screen.getByText(/L1/)).toBeInTheDocument();
});

test('valid scan posts a single-item create request', async () => {
  renderLoop('/terminal/lot-identification/freeform/MAT001/lot/L1/scan');
  const input = screen.getByRole('textbox') as HTMLInputElement;
  fireEvent.change(input, { target: { value: 'M00000001' } });
  fireEvent.submit(input.form!);
  await waitFor(() => {
    expect(mockMutate).toHaveBeenCalledWith(
      expect.objectContaining({
        items: [expect.objectContaining({ code: 'M00000001', materialCode: 'MAT001', lotCode: 'L1' })]
      }),
      expect.anything()
    );
  });
});

test('invalid format does not call the API', async () => {
  renderLoop('/terminal/lot-identification/freeform/MAT001/lot/L1/scan');
  const input = screen.getByRole('textbox') as HTMLInputElement;
  fireEvent.change(input, { target: { value: 'BADCODE' } });
  fireEvent.submit(input.form!);
  await new Promise((r) => setTimeout(r, 50));
  expect(mockMutate).not.toHaveBeenCalled();
});
```

- [ ] **Step 2: Run failing tests**

```bash
cd frontend && npm test -- ContainerScanLoop
```

- [ ] **Step 3: Implement**

```tsx
// ContainerScanLoop.tsx
import { useCallback, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { CheckCircle } from 'lucide-react';
import ScanInput from '../ScanInput';
import { useCreateMaterialContainers } from '../../../api/hooks/useMaterialContainers';
import { useScreenView } from '../../../telemetry/useScreenView';

interface ContainerScanLoopProps {
  mode: 'freeform' | 'po';
}

const CODE_FORMAT = /^M\d{8}$/;

const ContainerScanLoop = ({ mode }: ContainerScanLoopProps) => {
  useScreenView('Terminal', 'LotIdentificationScanLoop');
  const params = useParams<{ material: string; lot: string; id?: string; lineId?: string }>();
  const materialCode = params.material ?? '';
  const lotCode = params.lot ?? '';
  const poLineId = params.lineId ? parseInt(params.lineId, 10) : undefined;
  const navigate = useNavigate();

  const [count, setCount] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const create = useCreateMaterialContainers();

  const handleScan = useCallback(
    (raw: string) => {
      const code = raw.trim();
      if (!CODE_FORMAT.test(code)) {
        setError('Neplatný formát kódu (očekáváno M + 8 číslic).');
        return;
      }
      setError(null);
      create.mutate(
        { items: [{ code, materialCode, lotCode, purchaseOrderLineId: poLineId }] },
        {
          onSuccess: (data) => {
            if (data.success) {
              setCount((c) => c + 1);
            } else if (data.errorCode === 'MaterialContainerCodeExists') {
              const params = data.params ?? {};
              const status = params.Status === 'Discarded'
                ? 'Tento kód byl vyřazen, použijte nový štítek.'
                : `Kód ${code} je již přiřazen k materiálu ${params.MaterialCode} / šarži ${params.LotCode}.`;
              setError(status);
            } else {
              setError('Chyba při ukládání kontejneru.');
            }
          },
        }
      );
    },
    [materialCode, lotCode, poLineId, create]
  );

  const handleFinish = () => {
    if (mode === 'po') {
      navigate(`/terminal/lot-identification/po/${params.id}/finish`);
    } else {
      navigate('/terminal/lot-identification');
    }
  };

  return (
    <div className="space-y-4 pt-2">
      <div className="bg-secondary-blue-pale border border-primary-blue rounded-xl p-3 space-y-1">
        <p className="text-xs text-neutral-gray">Materiál</p>
        <p className="font-mono font-semibold text-neutral-slate">{materialCode}</p>
        <p className="text-xs text-neutral-gray mt-1">Šarže</p>
        <p className="font-mono font-semibold text-neutral-slate">{lotCode}</p>
        <p className="text-xs text-neutral-gray mt-2">Naskenováno: <span className="font-semibold">{count}</span></p>
      </div>

      <ScanInput
        label="Kód kontejneru (Mxxxxxxxx)"
        onScan={handleScan}
        loading={create.isPending}
        suppressKeyboard
        allowKeyboardToggle
      />

      {error && (
        <p role="alert" className="text-sm text-red-600">{error}</p>
      )}

      {count > 0 && !error && !create.isPending && (
        <p className="text-sm text-green-600 flex items-center gap-1">
          <CheckCircle className="h-4 w-4" /> Uloženo
        </p>
      )}

      <button
        type="button"
        onClick={handleFinish}
        className="w-full h-12 bg-primary-blue text-white rounded-xl font-semibold hover:bg-primary-blue-dark"
      >
        Hotovo
      </button>
    </div>
  );
};

export default ContainerScanLoop;
```

`data.errorCode` and `data.params` shapes come from the existing `BaseResponse` envelope — verify by reading `frontend/src/types/errors.ts`. Adjust property names if they differ.

- [ ] **Step 4: Run tests + lint + commit**

```bash
cd frontend && npm test -- ContainerScanLoop && npm run lint
git add -A
git commit -m "feat(fe): add ContainerScanLoop with sticky material+lot context

Validates Mxxxxxxxx format client-side before posting. Handles duplicate-code
errors with distinct messages for Assigned vs Discarded existing rows."
```

---

### Task 20: Build `FinishPoStep`

**Files:**
- Create: `frontend/src/components/terminal/lot-identification/FinishPoStep.tsx`
- Create: `.../__tests__/FinishPoStep.test.tsx`

After "Hotovo" in PO mode, ask whether to flip the PO to `Received`.

- [ ] **Step 1: Write failing test**

```typescript
// FinishPoStep.test.tsx
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import FinishPoStep from '../FinishPoStep';

const mockMutate = jest.fn();
jest.mock('../../../../api/hooks/usePurchaseOrders', () => ({
  useUpdatePurchaseOrderStatus: () => ({ mutate: mockMutate, isPending: false }),
}));

beforeEach(() => mockMutate.mockReset());

test('clicking confirm flips PO to Received', async () => {
  const client = new QueryClient();
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/terminal/lot-identification/po/1/finish']}>
        <Routes>
          <Route path="/terminal/lot-identification/po/:id/finish" element={<FinishPoStep />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
  fireEvent.click(screen.getByRole('button', { name: /označit jako přijatou/i }));
  await waitFor(() =>
    expect(mockMutate).toHaveBeenCalledWith(
      expect.objectContaining({ id: 1, status: 'Received' }),
      expect.anything()
    )
  );
});
```

- [ ] **Step 2: Run test (fail)**

- [ ] **Step 3: Implement**

```tsx
// FinishPoStep.tsx
import { useNavigate, useParams } from 'react-router-dom';
import { useUpdatePurchaseOrderStatus } from '../../../api/hooks/usePurchaseOrders';
import { useScreenView } from '../../../telemetry/useScreenView';

const FinishPoStep = () => {
  useScreenView('Terminal', 'LotIdentificationFinishPo');
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const poId = id ? parseInt(id, 10) : 0;
  const update = useUpdatePurchaseOrderStatus();

  const handleConfirm = () => {
    update.mutate(
      { id: poId, status: 'Received' },
      {
        onSuccess: (data) => {
          if (data.success) navigate('/terminal/lot-identification');
        }
      }
    );
  };

  const handleSkip = () => navigate('/terminal/lot-identification');

  return (
    <div className="space-y-4 pt-2">
      <h2 className="text-lg font-semibold text-neutral-slate">Označit objednávku jako přijatou?</h2>
      <button
        type="button"
        disabled={update.isPending}
        onClick={handleConfirm}
        className="w-full h-12 bg-primary-blue text-white rounded-xl font-semibold disabled:opacity-50"
      >
        Označit jako přijatou
      </button>
      <button
        type="button"
        onClick={handleSkip}
        className="w-full h-12 border border-border-light text-neutral-slate rounded-xl"
      >
        Ponechat ve stavu „V přepravě“
      </button>
    </div>
  );
};

export default FinishPoStep;
```

- [ ] **Step 4: Run tests + commit**

```bash
cd frontend && npm test -- FinishPoStep && npm run lint
git add -A
git commit -m "feat(fe): add FinishPoStep prompting to mark PO as Received"
```

---

### Task 21: Wire routes; activate Terminal tile

**Files:**
- Modify: `frontend/src/App.tsx`
- Modify: `frontend/src/components/terminal/TerminalHome.tsx`

- [ ] **Step 1: Add nested routes**

In `App.tsx` find the `<Route path="lot-identification" element={<ComingSoonPage … />} />` and replace with:

```tsx
<Route path="lot-identification">
  <Route index element={<LotIdentificationHome />} />
  <Route path="po" element={<PoPickStep />} />
  <Route path="po/:id" element={<PoLinePickStep />} />
  <Route path="po/:id/line/:lineId/lot/:lot/scan" element={<ContainerScanLoop mode="po" />} />
  <Route path="po/:id/finish" element={<FinishPoStep />} />
  <Route path="freeform" element={<FreeformMaterialStep />} />
  <Route path="freeform/:material/lot" element={<LotEntryStep mode="freeform" />} />
  <Route path="freeform/:material/lot/:lot/scan" element={<ContainerScanLoop mode="freeform" />} />
</Route>
```

Add the relevant imports at the top of `App.tsx`. (For the PO line → lot-entry transition, decide whether to use a dedicated route `po/:id/line/:lineId/lot` rendering `LotEntryStep mode="po"`. Add it for symmetry:)

```tsx
<Route path="po/:id/line/:lineId/lot" element={<LotEntryStep mode="po" />} />
```

And in `PoLinePickStep` adjust the link target to match (link to `…/line/:lineId/lot` instead of `…/line/:lineId`). Re-run the `PoLinePickStep` test and update its assertion accordingly.

- [ ] **Step 2: Flip the Terminal tile to active**

```tsx
// frontend/src/components/terminal/TerminalHome.tsx — change comingSoon for lot-identification
{
  id: 'lot-identification',
  title: 'Identifikace šarže',
  description: 'Evidujte šarže při příjmu a sledujte spotřebu ve výrobě',
  href: '/terminal/lot-identification',
  icon: Tag,
  comingSoon: false,   // was: true
},
```

- [ ] **Step 3: Build + tests + lint**

```bash
cd frontend && npm run build && npm test && npm run lint
```

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(fe): wire /terminal/lot-identification routes and activate Terminal tile"
```

---

### Task 22: Playwright E2E spec

**Files:**
- Create: `frontend/test/e2e/terminal/lot-identification.spec.ts`
- Modify: `frontend/test/e2e/fixtures/test-data.ts` — add `materialContainer` and any required PO fixture entries

- [ ] **Step 1: Add fixtures**

Read `frontend/test/e2e/fixtures/test-data.ts` end-to-end. Add:

```typescript
export interface MaterialContainerFixture {
  codePrefix: string;   // e.g. 'M999' — tests append a unique suffix per run
  materialCode: string; // an existing material in staging
  lotCode: string;      // a synthetic lot code generated per run
}

export const materialContainerFixtures: MaterialContainerFixture = {
  codePrefix: 'M999',
  materialCode: 'SUR001', // replace with an actual staging material code from docs
  lotCode: 'E2E-LOT',
};
```

Pick a real material code from `docs/testing/test-data-fixtures.md` (the staging-data reference). If a suitable material isn't listed, document one in that file as part of this task.

- [ ] **Step 2: Write the spec**

```typescript
// frontend/test/e2e/terminal/lot-identification.spec.ts
import { test, expect } from '@playwright/test';
import { navigateToApp } from '../helpers/e2e-auth-helper';
import { materialContainerFixtures } from '../fixtures/test-data';

test.describe('Terminal — Identifikace šarže', () => {
  test.beforeEach(async ({ page }) => {
    await navigateToApp(page);
    await page.goto('/terminal/lot-identification');
  });

  test('freeform receive: scan two containers for the same material+lot', async ({ page }) => {
    // Unique code per run to avoid collisions
    const codes = [
      `${materialContainerFixtures.codePrefix}${String(Date.now()).slice(-5)}1`.padEnd(9, '0'),
      `${materialContainerFixtures.codePrefix}${String(Date.now()).slice(-5)}2`.padEnd(9, '0'),
    ];
    const lot = `${materialContainerFixtures.lotCode}-${Date.now()}`;

    await page.getByTestId('lot-id-tile-freeform').click();
    await page.getByRole('textbox').fill(materialContainerFixtures.materialCode);
    await page.getByRole('textbox').press('Enter');

    await page.getByRole('textbox').fill(lot);
    await page.getByRole('textbox').press('Enter');

    for (const code of codes) {
      await page.getByRole('textbox').fill(code);
      await page.getByRole('textbox').press('Enter');
      await expect(page.getByText(/Uloženo/)).toBeVisible();
    }
    await expect(page.getByText(/Naskenováno:\s*2/)).toBeVisible();
  });

  test('duplicate code shows the assigned-to message', async ({ page }) => {
    const code = `M999${String(Date.now()).slice(-5)}5`.padEnd(9, '0');
    const lot = `DUP-LOT-${Date.now()}`;

    await page.getByTestId('lot-id-tile-freeform').click();
    await page.getByRole('textbox').fill(materialContainerFixtures.materialCode);
    await page.getByRole('textbox').press('Enter');
    await page.getByRole('textbox').fill(lot);
    await page.getByRole('textbox').press('Enter');

    // First scan succeeds
    await page.getByRole('textbox').fill(code);
    await page.getByRole('textbox').press('Enter');
    await expect(page.getByText(/Uloženo/)).toBeVisible();

    // Second scan of same code fails
    await page.getByRole('textbox').fill(code);
    await page.getByRole('textbox').press('Enter');
    await expect(page.getByRole('alert')).toContainText(/je již přiřazen/);
  });

  test('invalid code format is rejected client-side', async ({ page }) => {
    await page.getByTestId('lot-id-tile-freeform').click();
    await page.getByRole('textbox').fill(materialContainerFixtures.materialCode);
    await page.getByRole('textbox').press('Enter');
    await page.getByRole('textbox').fill('SOMELOT');
    await page.getByRole('textbox').press('Enter');

    await page.getByRole('textbox').fill('BADCODE');
    await page.getByRole('textbox').press('Enter');
    await expect(page.getByRole('alert')).toContainText(/Neplatný formát/);
  });

  test('last-used lot pre-fills on next visit for same material', async ({ page }) => {
    const lot = `LASTUSED-${Date.now()}`;
    const code = `M998${String(Date.now()).slice(-5)}1`.padEnd(9, '0');

    // First receive
    await page.getByTestId('lot-id-tile-freeform').click();
    await page.getByRole('textbox').fill(materialContainerFixtures.materialCode);
    await page.getByRole('textbox').press('Enter');
    await page.getByRole('textbox').fill(lot);
    await page.getByRole('textbox').press('Enter');
    await page.getByRole('textbox').fill(code);
    await page.getByRole('textbox').press('Enter');
    await expect(page.getByText(/Uloženo/)).toBeVisible();

    // Return to start, re-enter same material → lot should pre-fill
    await page.goto('/terminal/lot-identification');
    await page.getByTestId('lot-id-tile-freeform').click();
    await page.getByRole('textbox').fill(materialContainerFixtures.materialCode);
    await page.getByRole('textbox').press('Enter');
    await expect(page.getByRole('textbox')).toHaveValue(lot);
  });
});
```

- [ ] **Step 3: Run E2E against staging**

```bash
./scripts/run-playwright-tests.sh --grep "Identifikace šarže"
```

Expected: all four tests pass against staging. If they fail, do not silently disable — fix the underlying issue.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "test(e2e): add Playwright spec for Terminal Identifikace šarže workflow"
```

---

## Verification Checklist (final pass before opening PR)

- [ ] `cd backend && dotnet build` clean
- [ ] `cd backend && dotnet test` all green; new tests cover handlers, validator, repo query, and status transitions
- [ ] `cd backend && dotnet format --verify-no-changes`
- [ ] Migration roundtrip on `Heblo_TST` for all three migrations: apply → revert to previous → apply again. Final schema matches snapshot.
- [ ] `cd frontend && npm run build` clean
- [ ] `cd frontend && npm test` all green
- [ ] `cd frontend && npm run lint` clean
- [ ] `frontend/src/api/generated/api-client.ts` regenerated and committed
- [ ] `./scripts/run-playwright-tests.sh --grep "Identifikace šarže"` passes against staging
- [ ] Manual smoke (staging Terminal app on a phone or browser): freeform receive of two containers, duplicate-code error, PO receive happy path including the `Received` status flip
- [ ] Database spot check on `Heblo_TST`: `SELECT "Code","MaterialCode","LotCode","Status","CreatedBy","CreatedAt" FROM "MaterialContainers" ORDER BY "Id" DESC LIMIT 5;` confirms recent inserts have `Status=0`, `CreatedBy` = current user, `CreatedAt` ≈ now
- [ ] Open the PR; reference the spec file in the description; add the `@claude` review marker per repo convention
