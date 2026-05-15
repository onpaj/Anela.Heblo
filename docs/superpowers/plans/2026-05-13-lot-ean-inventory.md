# Lot & EAN Inventory — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add persisted `Lot` and `Ean` entities to track physical warehouse containers, each linked to a Material (`CatalogAggregate` where `ProductType = Material`) and a Lot with expiration, with full CRUD REST API and server-side EAN code generation.

**Architecture:** Two independent aggregates (`Lot` and `Ean`) in `Catalog/Inventory/` module, following the PurchaseOrder vertical-slice pattern. `Lot` owns expiration and received metadata; `Ean` holds a server-generated internal code (format `INT-00000001`), container size, and a FK to `Lot`. Material link is a soft reference (string column, no EF FK because `CatalogAggregate` is cache-only, not EF-mapped).

**Tech Stack:** .NET 8, EF Core 8 + Npgsql (PostgreSQL), MediatR, FluentValidation, xUnit + Moq

---

## File Structure

### New files
```
Domain:
  backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/Lot.cs
  backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/Ean.cs
  backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/ILotRepository.cs
  backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/IEanRepository.cs

Persistence:
  backend/src/Anela.Heblo.Persistence/Catalog/Inventory/LotConfiguration.cs
  backend/src/Anela.Heblo.Persistence/Catalog/Inventory/EanConfiguration.cs
  backend/src/Anela.Heblo.Persistence/Catalog/Inventory/LotRepository.cs
  backend/src/Anela.Heblo.Persistence/Catalog/Inventory/EanRepository.cs

Application:
  backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/Contracts/LotDto.cs
  backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/Contracts/EanDto.cs
  backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/Services/IEanCodeGenerator.cs
  backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/Services/EanCodeGenerator.cs
  backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/InventoryModule.cs
  backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/CreateLot/{4 files}
  backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/GetLot/{3 files}
  backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/ListLots/{3 files}
  backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/UpdateLot/{4 files}
  backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/DeleteLot/{3 files}
  backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/CreateEans/{4 files}
  backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/GetEanByCode/{3 files}
  backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/ListEans/{3 files}
  backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/DeleteEan/{3 files}

API:
  backend/src/Anela.Heblo.API/Controllers/LotsController.cs
  backend/src/Anela.Heblo.API/Controllers/EansController.cs

Tests:
  backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/CreateLotHandlerTests.cs
  backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/GetLotHandlerTests.cs
  backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/ListLotsHandlerTests.cs
  backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/UpdateLotHandlerTests.cs
  backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/DeleteLotHandlerTests.cs
  backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/CreateEansHandlerTests.cs
  backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/GetEanByCodeHandlerTests.cs
  backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/ListEansHandlerTests.cs
  backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/DeleteEanHandlerTests.cs
```

### Modified files
```
backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs         — add error codes (28XX prefix)
backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs      — add DbSet<Lot>, DbSet<Ean>
backend/src/Anela.Heblo.Application/ApplicationModule.cs         — register InventoryModule
```

---

## Task 1: Add error codes + constants

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/InventoryConstants.cs`

- [ ] **Step 1: Add error codes to ErrorCodes.cs**

Open the file and add after the Smartsupp section (27XX) and before External Service errors (90XX):

```csharp
// Inventory module errors (28XX)
[HttpStatusCode(HttpStatusCode.NotFound)]
LotNotFound = 2801,
[HttpStatusCode(HttpStatusCode.NotFound)]
EanNotFound = 2802,
[HttpStatusCode(HttpStatusCode.Conflict)]
LotAlreadyExists = 2803,
[HttpStatusCode(HttpStatusCode.NotFound)]
InventoryMaterialNotFound = 2804,
[HttpStatusCode(HttpStatusCode.BadRequest)]
InventoryMaterialInvalidType = 2805,
[HttpStatusCode(HttpStatusCode.BadRequest)]
LotHasEans = 2806,
```

- [ ] **Step 2: Create InventoryConstants.cs**

```csharp
namespace Anela.Heblo.Application.Features.Catalog.Inventory;

public static class InventoryConstants
{
    public const int MaterialCodeMaxLength = 50;
    public const int LotCodeMaxLength = 100;
    public const int NotesMaxLength = 2000;
    public const int UnitMaxLength = 20;
    public const int UserNameMaxLength = 100;
    public const int EanCodeMaxLength = 20;
    public const string EanCodePrefix = "INT-";
    public const int EanCodePaddingWidth = 8;
}
```

- [ ] **Step 3: Build to verify**

```bash
cd /path/to/repo/backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-restore
```

Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs \
        backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/InventoryConstants.cs
git commit -m "feat: add inventory error codes and constants"
```

---

## Task 2: Domain entities + repository interfaces

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/Lot.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/Ean.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/ILotRepository.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/IEanRepository.cs`

- [ ] **Step 1: Create Lot.cs**

```csharp
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

public class Lot : IEntity<int>
{
    private Lot() { } // EF Core

    public Lot(string materialCode, string lotCode, DateOnly? expiration, DateOnly receivedDate, string? notes, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(materialCode)) throw new ArgumentException("MaterialCode is required.", nameof(materialCode));
        if (string.IsNullOrWhiteSpace(lotCode)) throw new ArgumentException("LotCode is required.", nameof(lotCode));
        if (string.IsNullOrWhiteSpace(createdBy)) throw new ArgumentException("CreatedBy is required.", nameof(createdBy));

        MaterialCode = materialCode;
        LotCode = lotCode;
        Expiration = expiration;
        ReceivedDate = receivedDate;
        Notes = notes;
        CreatedAt = DateTime.UtcNow;
        CreatedBy = createdBy;
    }

    public int Id { get; private set; }
    public string MaterialCode { get; private set; } = null!;
    public string LotCode { get; private set; } = null!;
    public DateOnly? Expiration { get; private set; }
    public DateOnly ReceivedDate { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTime? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public void Update(DateOnly? expiration, DateOnly receivedDate, string? notes, string updatedBy)
    {
        if (string.IsNullOrWhiteSpace(updatedBy)) throw new ArgumentException("UpdatedBy is required.", nameof(updatedBy));

        Expiration = expiration;
        ReceivedDate = receivedDate;
        Notes = notes;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }
}
```

- [ ] **Step 2: Create Ean.cs**

```csharp
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

public class Ean : IEntity<int>
{
    private Ean() { } // EF Core

    public Ean(string code, int lotId, decimal amount, string unit, string createdBy)
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

- [ ] **Step 3: Create ILotRepository.cs**

```csharp
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

public interface ILotRepository : IRepository<Lot, int>
{
    Task<Lot?> GetByIdWithEansAsync(int id, CancellationToken ct);
    Task<PagedResult<Lot>> GetPaginatedAsync(string? materialCode, DateOnly? expirationFrom, DateOnly? expirationTo, int page, int pageSize, CancellationToken ct);
    Task<bool> ExistsAsync(string materialCode, string lotCode, CancellationToken ct);
}
```

Note: `PagedResult<T>` is in `Anela.Heblo.Xcc.Persistance` — add the using directive. It has `Items`, `TotalCount`, `PageNumber`, `PageSize`.

- [ ] **Step 4: Create IEanRepository.cs**

```csharp
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Catalog.Inventory;

public interface IEanRepository : IRepository<Ean, int>
{
    Task<Ean?> GetByCodeAsync(string code, CancellationToken ct);
    Task<bool> AnyByLotIdAsync(int lotId, CancellationToken ct);
    Task<PagedResult<Ean>> GetPaginatedAsync(int? lotId, string? materialCode, int page, int pageSize, CancellationToken ct);
}
```

- [ ] **Step 5: Build to verify**

```bash
cd /path/to/repo/backend && dotnet build src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj --no-restore
```

Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/
git commit -m "feat: add Lot and Ean domain entities with repository interfaces"
```

---

## Task 3: EF Core configurations

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Catalog/Inventory/LotConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Catalog/Inventory/EanConfiguration.cs`

- [ ] **Step 1: Create LotConfiguration.cs**

```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Catalog.Inventory;

public class LotConfiguration : IEntityTypeConfiguration<Lot>
{
    public void Configure(EntityTypeBuilder<Lot> builder)
    {
        builder.ToTable("Lots", "public");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseIdentityColumn();

        builder.Property(x => x.MaterialCode)
            .IsRequired()
            .HasMaxLength(InventoryConstants.MaterialCodeMaxLength);

        builder.Property(x => x.LotCode)
            .IsRequired()
            .HasMaxLength(InventoryConstants.LotCodeMaxLength);

        builder.Property(x => x.Expiration)
            .HasColumnType("date");

        builder.Property(x => x.ReceivedDate)
            .IsRequired()
            .HasColumnType("date");

        builder.Property(x => x.Notes)
            .HasMaxLength(InventoryConstants.NotesMaxLength);

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.CreatedBy)
            .IsRequired()
            .HasMaxLength(InventoryConstants.UserNameMaxLength);

        builder.Property(x => x.UpdatedAt)
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.UpdatedBy)
            .HasMaxLength(InventoryConstants.UserNameMaxLength);

        builder.HasIndex(x => new { x.MaterialCode, x.LotCode })
            .IsUnique()
            .HasDatabaseName("IX_Lots_MaterialCode_LotCode");

        builder.HasIndex(x => x.MaterialCode)
            .HasDatabaseName("IX_Lots_MaterialCode");
    }
}
```

- [ ] **Step 2: Create EanConfiguration.cs**

```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Catalog.Inventory;

public class EanConfiguration : IEntityTypeConfiguration<Ean>
{
    public void Configure(EntityTypeBuilder<Ean> builder)
    {
        builder.ToTable("Eans", "public");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseIdentityColumn();

        builder.Property(x => x.Code)
            .IsRequired()
            .HasMaxLength(InventoryConstants.EanCodeMaxLength);

        builder.Property(x => x.LotId)
            .IsRequired();

        builder.Property(x => x.Amount)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(x => x.Unit)
            .IsRequired()
            .HasMaxLength(InventoryConstants.UnitMaxLength);

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.CreatedBy)
            .IsRequired()
            .HasMaxLength(InventoryConstants.UserNameMaxLength);

        builder.Property(x => x.UpdatedAt)
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.UpdatedBy)
            .HasMaxLength(InventoryConstants.UserNameMaxLength);

        builder.HasIndex(x => x.Code)
            .IsUnique()
            .HasDatabaseName("IX_Eans_Code");

        builder.HasIndex(x => x.LotId)
            .HasDatabaseName("IX_Eans_LotId");

        // ON DELETE RESTRICT: deleting Lot while EANs exist raises an error
        builder.HasOne<Lot>()
            .WithMany()
            .HasForeignKey(x => x.LotId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Eans_Lots_LotId");
    }
}
```

- [ ] **Step 3: Add DbSets to ApplicationDbContext.cs**

Open `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` and add after the last existing `DbSet`:

```csharp
// Inventory
public DbSet<Lot> Lots { get; set; }
public DbSet<Ean> Eans { get; set; }
```

Add the using directive at the top of the file:
```csharp
using Anela.Heblo.Domain.Features.Catalog.Inventory;
```

The configurations are auto-discovered by `modelBuilder.ApplyConfigurationsFromAssembly(...)` — no further registration needed.

- [ ] **Step 4: Build persistence to verify**

```bash
cd /path/to/repo/backend && dotnet build src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj --no-restore
```

Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Catalog/Inventory/ \
        backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs
git commit -m "feat: add Lot and Ean EF Core configurations and DbSets"
```

---

## Task 4: Repository implementations

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Catalog/Inventory/LotRepository.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Catalog/Inventory/EanRepository.cs`

- [ ] **Step 1: Create LotRepository.cs**

```csharp
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Persistence.Repositories;
using Anela.Heblo.Xcc.Persistance;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Catalog.Inventory;

public class LotRepository : BaseRepository<Lot, int>, ILotRepository
{
    public LotRepository(ApplicationDbContext context) : base(context) { }

    public async Task<Lot?> GetByIdWithEansAsync(int id, CancellationToken ct)
    {
        return await DbSet
            .Include("_eans") // EF shadow navigation; use property name if Lot exposes a collection later
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<PagedResult<Lot>> GetPaginatedAsync(
        string? materialCode,
        DateOnly? expirationFrom,
        DateOnly? expirationTo,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var query = DbSet.AsQueryable();

        if (!string.IsNullOrWhiteSpace(materialCode))
            query = query.Where(x => x.MaterialCode == materialCode);

        if (expirationFrom.HasValue)
            query = query.Where(x => x.Expiration.HasValue && x.Expiration.Value >= expirationFrom.Value);

        if (expirationTo.HasValue)
            query = query.Where(x => x.Expiration.HasValue && x.Expiration.Value <= expirationTo.Value);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.ReceivedDate)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<Lot>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize
        };
    }

    public async Task<bool> ExistsAsync(string materialCode, string lotCode, CancellationToken ct)
    {
        return await DbSet.AnyAsync(x => x.MaterialCode == materialCode && x.LotCode == lotCode, ct);
    }
}
```

**Note on `GetByIdWithEansAsync`:** Since `Lot` and `Ean` are two independent aggregates (no navigation property on `Lot`), this method for now just returns the `Lot` without EANs. The `GetLotHandler` will separately query `IEanRepository.GetPaginatedAsync(lotId: id, ...)` to load the EANs. Update this method to simply:

```csharp
public async Task<Lot?> GetByIdWithEansAsync(int id, CancellationToken ct)
{
    return await DbSet.FirstOrDefaultAsync(x => x.Id == id, ct);
}
```

- [ ] **Step 2: Create EanRepository.cs**

```csharp
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Persistence.Repositories;
using Anela.Heblo.Xcc.Persistance;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Catalog.Inventory;

public class EanRepository : BaseRepository<Ean, int>, IEanRepository
{
    public EanRepository(ApplicationDbContext context) : base(context) { }

    public async Task<Ean?> GetByCodeAsync(string code, CancellationToken ct)
    {
        return await DbSet.FirstOrDefaultAsync(x => x.Code == code, ct);
    }

    public async Task<bool> AnyByLotIdAsync(int lotId, CancellationToken ct)
    {
        return await DbSet.AnyAsync(x => x.LotId == lotId, ct);
    }

    public async Task<PagedResult<Ean>> GetPaginatedAsync(
        int? lotId,
        string? materialCode,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var query = DbSet.AsQueryable();

        if (lotId.HasValue)
            query = query.Where(x => x.LotId == lotId.Value);

        if (!string.IsNullOrWhiteSpace(materialCode))
        {
            // Join through Lot to filter by MaterialCode
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

        return new PagedResult<Ean>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize
        };
    }
}
```

Note: `Context` is the protected field from `BaseRepository<Lot, int>` which exposes the `ApplicationDbContext`.

- [ ] **Step 3: Build to verify**

```bash
cd /path/to/repo/backend && dotnet build src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj --no-restore
```

Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Catalog/Inventory/LotRepository.cs \
        backend/src/Anela.Heblo.Persistence/Catalog/Inventory/EanRepository.cs
git commit -m "feat: add LotRepository and EanRepository implementations"
```

---

## Task 5: DTOs + EAN code generator

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/Contracts/LotDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/Contracts/EanDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/Services/IEanCodeGenerator.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/Services/EanCodeGenerator.cs`

- [ ] **Step 1: Create LotDto.cs**

DTOs must be classes, not records (per CLAUDE.md — OpenAPI client generator mishandles record parameter order).

```csharp
namespace Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;

public class LotDto
{
    public int Id { get; set; }
    public string MaterialCode { get; set; } = null!;
    public string LotCode { get; set; } = null!;
    public DateOnly? Expiration { get; set; }
    public DateOnly ReceivedDate { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
```

- [ ] **Step 2: Create EanDto.cs**

```csharp
namespace Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;

public class EanDto
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

- [ ] **Step 3: Create IEanCodeGenerator.cs**

```csharp
namespace Anela.Heblo.Application.Features.Catalog.Inventory.Services;

public interface IEanCodeGenerator
{
    Task<IReadOnlyList<string>> GenerateAsync(int count, CancellationToken ct);
}
```

- [ ] **Step 4: Create EanCodeGenerator.cs**

Uses `NpgsqlDataSource` (registered as singleton in `PersistenceModule`) to pull N sequential values from a Postgres sequence in a single round-trip.

```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory;
using Npgsql;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.Services;

public class EanCodeGenerator : IEanCodeGenerator
{
    private const string SequenceName = "ean_internal_seq";
    private readonly NpgsqlDataSource _dataSource;

    public EanCodeGenerator(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyList<string>> GenerateAsync(int count, CancellationToken ct)
    {
        if (count <= 0) return Array.Empty<string>();

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            $"SELECT nextval('{SequenceName}') FROM generate_series(1, $1)",
            conn);
        cmd.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Integer, count);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var codes = new List<string>(count);
        while (await reader.ReadAsync(ct))
        {
            var seq = reader.GetInt64(0);
            codes.Add($"{InventoryConstants.EanCodePrefix}{seq:D8}");
        }
        return codes;
    }
}
```

- [ ] **Step 5: Build to verify**

```bash
cd /path/to/repo/backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-restore
```

Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/Contracts/ \
        backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/Services/
git commit -m "feat: add LotDto, EanDto, and EanCodeGenerator"
```

---

## Task 6: CreateLot handler (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/CreateLotHandlerTests.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/CreateLot/CreateLotRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/CreateLot/CreateLotResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/CreateLot/CreateLotHandler.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/CreateLot/CreateLotRequestValidator.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateLot;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class CreateLotHandlerTests
{
    private readonly Mock<ILotRepository> _lotRepo;
    private readonly Mock<ICatalogRepository> _catalogRepo;
    private readonly Mock<ICurrentUserService> _currentUser;
    private readonly CreateLotHandler _handler;

    public CreateLotHandlerTests()
    {
        _lotRepo = new Mock<ILotRepository>();
        _catalogRepo = new Mock<ICatalogRepository>();
        _currentUser = new Mock<ICurrentUserService>();

        _currentUser.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("user-1", "Test User", "test@anela.cz", true));

        _handler = new CreateLotHandler(
            NullLogger<CreateLotHandler>.Instance,
            _lotRepo.Object,
            _catalogRepo.Object,
            _currentUser.Object);
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesLotAndReturnsSuccess()
    {
        // Arrange
        var material = CreateMaterialCatalogItem("MAT001");
        _catalogRepo.Setup(r => r.GetByIdAsync("MAT001", default)).ReturnsAsync(material);
        _lotRepo.Setup(r => r.ExistsAsync("MAT001", "LOT-ABC", default)).ReturnsAsync(false);
        _lotRepo.Setup(r => r.AddAsync(It.IsAny<Lot>(), default)).ReturnsAsync((Lot l, CancellationToken _) => l);
        _lotRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(1);

        var request = new CreateLotRequest
        {
            MaterialCode = "MAT001",
            LotCode = "LOT-ABC",
            Expiration = new DateOnly(2027, 6, 30),
            ReceivedDate = new DateOnly(2026, 5, 13),
            Notes = "Test lot"
        };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("MAT001", result.Lot.MaterialCode);
        Assert.Equal("LOT-ABC", result.Lot.LotCode);
        _lotRepo.Verify(r => r.AddAsync(It.IsAny<Lot>(), default), Times.Once);
        _lotRepo.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_MaterialNotFound_ReturnsInventoryMaterialNotFound()
    {
        // Arrange
        _catalogRepo.Setup(r => r.GetByIdAsync("MISSING", default)).ReturnsAsync((CatalogAggregate?)null);

        var request = new CreateLotRequest { MaterialCode = "MISSING", LotCode = "L1", ReceivedDate = DateOnly.FromDateTime(DateTime.Today) };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InventoryMaterialNotFound, result.ErrorCode);
        _lotRepo.Verify(r => r.AddAsync(It.IsAny<Lot>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_MaterialIsNotMaterialType_ReturnsInventoryMaterialInvalidType()
    {
        // Arrange
        var product = CreateCatalogItem("PROD001", ProductType.Product);
        _catalogRepo.Setup(r => r.GetByIdAsync("PROD001", default)).ReturnsAsync(product);

        var request = new CreateLotRequest { MaterialCode = "PROD001", LotCode = "L1", ReceivedDate = DateOnly.FromDateTime(DateTime.Today) };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InventoryMaterialInvalidType, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_DuplicateLot_ReturnsLotAlreadyExists()
    {
        // Arrange
        var material = CreateMaterialCatalogItem("MAT001");
        _catalogRepo.Setup(r => r.GetByIdAsync("MAT001", default)).ReturnsAsync(material);
        _lotRepo.Setup(r => r.ExistsAsync("MAT001", "LOT-DUP", default)).ReturnsAsync(true);

        var request = new CreateLotRequest { MaterialCode = "MAT001", LotCode = "LOT-DUP", ReceivedDate = DateOnly.FromDateTime(DateTime.Today) };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.LotAlreadyExists, result.ErrorCode);
    }

    private static CatalogAggregate CreateMaterialCatalogItem(string productCode)
        => CreateCatalogItem(productCode, ProductType.Material);

    private static CatalogAggregate CreateCatalogItem(string productCode, ProductType productType)
    {
        // Use whatever constructor/factory CatalogAggregate exposes.
        // If it has no public constructor, use reflection or check the test pattern in other catalog tests.
        // Typically: new CatalogAggregate(productCode) and then set ProductType via a method.
        // Check backend/test/Anela.Heblo.Tests/Features/Catalog/ for the existing pattern.
        throw new NotImplementedException("Replace with actual CatalogAggregate construction — check existing catalog tests.");
    }
}
```

**BEFORE running the test:** Check `backend/test/Anela.Heblo.Tests/Features/Catalog/` for how existing tests construct a `CatalogAggregate`. Adapt `CreateCatalogItem` to match.

- [ ] **Step 2: Run test to verify it fails (expected: compile error)**

```bash
cd /path/to/repo/backend && dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~CreateLotHandlerTests" 2>&1 | head -30
```

Expected: Compile error — `CreateLotHandler`, `CreateLotRequest`, `CreateLotResponse` not found.

- [ ] **Step 3: Create CreateLotRequest.cs**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateLot;

public class CreateLotRequest : IRequest<CreateLotResponse>
{
    public string MaterialCode { get; set; } = null!;
    public string LotCode { get; set; } = null!;
    public DateOnly? Expiration { get; set; }
    public DateOnly ReceivedDate { get; set; }
    public string? Notes { get; set; }
}
```

- [ ] **Step 4: Create CreateLotResponse.cs**

```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateLot;

public class CreateLotResponse : BaseResponse
{
    public LotDto Lot { get; set; } = null!;

    public CreateLotResponse() : base() { }
    public CreateLotResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

- [ ] **Step 5: Create CreateLotHandler.cs**

```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateLot;

public class CreateLotHandler : IRequestHandler<CreateLotRequest, CreateLotResponse>
{
    private readonly ILogger<CreateLotHandler> _logger;
    private readonly ILotRepository _lotRepository;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ICurrentUserService _currentUserService;

    public CreateLotHandler(
        ILogger<CreateLotHandler> logger,
        ILotRepository lotRepository,
        ICatalogRepository catalogRepository,
        ICurrentUserService currentUserService)
    {
        _logger = logger;
        _lotRepository = lotRepository;
        _catalogRepository = catalogRepository;
        _currentUserService = currentUserService;
    }

    public async Task<CreateLotResponse> Handle(CreateLotRequest request, CancellationToken cancellationToken)
    {
        var material = await _catalogRepository.GetByIdAsync(request.MaterialCode, cancellationToken);
        if (material == null)
        {
            _logger.LogWarning("Material {MaterialCode} not found", request.MaterialCode);
            return new CreateLotResponse(ErrorCodes.InventoryMaterialNotFound,
                new Dictionary<string, string> { { "MaterialCode", request.MaterialCode } });
        }

        if (material.ProductType != ProductType.Material)
        {
            _logger.LogWarning("Material {MaterialCode} is not of type Material (is {Type})", request.MaterialCode, material.ProductType);
            return new CreateLotResponse(ErrorCodes.InventoryMaterialInvalidType,
                new Dictionary<string, string> { { "MaterialCode", request.MaterialCode }, { "ProductType", material.ProductType.ToString() } });
        }

        if (await _lotRepository.ExistsAsync(request.MaterialCode, request.LotCode, cancellationToken))
        {
            _logger.LogWarning("Lot ({MaterialCode}, {LotCode}) already exists", request.MaterialCode, request.LotCode);
            return new CreateLotResponse(ErrorCodes.LotAlreadyExists,
                new Dictionary<string, string> { { "MaterialCode", request.MaterialCode }, { "LotCode", request.LotCode } });
        }

        var currentUser = _currentUserService.GetCurrentUser();
        var createdBy = currentUser.Name ?? "System";

        var lot = new Lot(request.MaterialCode, request.LotCode, request.Expiration, request.ReceivedDate, request.Notes, createdBy);
        await _lotRepository.AddAsync(lot, cancellationToken);
        await _lotRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Lot {LotId} ({MaterialCode}/{LotCode}) created", lot.Id, lot.MaterialCode, lot.LotCode);

        return new CreateLotResponse { Lot = MapToDto(lot) };
    }

    internal static LotDto MapToDto(Lot lot) => new()
    {
        Id = lot.Id,
        MaterialCode = lot.MaterialCode,
        LotCode = lot.LotCode,
        Expiration = lot.Expiration,
        ReceivedDate = lot.ReceivedDate,
        Notes = lot.Notes,
        CreatedAt = lot.CreatedAt,
        CreatedBy = lot.CreatedBy,
        UpdatedAt = lot.UpdatedAt,
        UpdatedBy = lot.UpdatedBy
    };
}
```

- [ ] **Step 6: Create CreateLotRequestValidator.cs**

```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateLot;

public class CreateLotRequestValidator : AbstractValidator<CreateLotRequest>
{
    public CreateLotRequestValidator()
    {
        RuleFor(x => x.MaterialCode)
            .NotEmpty().WithMessage("MaterialCode is required.")
            .MaximumLength(InventoryConstants.MaterialCodeMaxLength);

        RuleFor(x => x.LotCode)
            .NotEmpty().WithMessage("LotCode is required.")
            .MaximumLength(InventoryConstants.LotCodeMaxLength);

        RuleFor(x => x.ReceivedDate)
            .NotEmpty().WithMessage("ReceivedDate is required.");

        RuleFor(x => x.Notes)
            .MaximumLength(InventoryConstants.NotesMaxLength);
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

```bash
cd /path/to/repo/backend && dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~CreateLotHandlerTests" -v
```

Expected: All tests pass (fix `CreateCatalogItem` helper first).

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/CreateLot/ \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/CreateLotHandlerTests.cs
git commit -m "feat: add CreateLot handler with TDD tests"
```

---

## Task 7: GetLot + ListLots handlers (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/GetLotHandlerTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/ListLotsHandlerTests.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/GetLot/{3 files}`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/ListLots/{3 files}`

- [ ] **Step 1: Write GetLotHandlerTests.cs**

```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetLot;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Xcc.Persistance;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class GetLotHandlerTests
{
    private readonly Mock<ILotRepository> _lotRepo = new();
    private readonly Mock<IEanRepository> _eanRepo = new();
    private readonly GetLotHandler _handler;

    public GetLotHandlerTests()
    {
        _handler = new GetLotHandler(NullLogger<GetLotHandler>.Instance, _lotRepo.Object, _eanRepo.Object);
    }

    [Fact]
    public async Task Handle_ExistingLot_ReturnsDtoWithEans()
    {
        // Arrange
        var lot = new Lot("MAT001", "LOT-A", new DateOnly(2027, 1, 1), new DateOnly(2026, 5, 13), null, "user");
        _lotRepo.Setup(r => r.GetByIdWithEansAsync(1, default)).ReturnsAsync(lot);

        var eans = new PagedResult<Ean>
        {
            Items = new List<Ean>
            {
                new Ean("INT-00000001", 1, 25m, "kg", "user"),
                new Ean("INT-00000002", 1, 25m, "kg", "user")
            },
            TotalCount = 2, PageNumber = 1, PageSize = 100
        };
        _eanRepo.Setup(r => r.GetPaginatedAsync(1, null, 1, 100, default)).ReturnsAsync(eans);

        // Act
        var result = await _handler.Handle(new GetLotRequest { Id = 1 }, default);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("MAT001", result.Lot.MaterialCode);
        Assert.Equal(2, result.Eans.Count);
        Assert.Equal("INT-00000001", result.Eans[0].Code);
    }

    [Fact]
    public async Task Handle_MissingLot_ReturnsLotNotFound()
    {
        // Arrange
        _lotRepo.Setup(r => r.GetByIdWithEansAsync(99, default)).ReturnsAsync((Lot?)null);

        // Act
        var result = await _handler.Handle(new GetLotRequest { Id = 99 }, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.LotNotFound, result.ErrorCode);
    }
}
```

- [ ] **Step 2: Write ListLotsHandlerTests.cs**

```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListLots;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Xcc.Persistance;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class ListLotsHandlerTests
{
    private readonly Mock<ILotRepository> _lotRepo = new();
    private readonly ListLotsHandler _handler;

    public ListLotsHandlerTests()
    {
        _handler = new ListLotsHandler(NullLogger<ListLotsHandler>.Instance, _lotRepo.Object);
    }

    [Fact]
    public async Task Handle_FilterByMaterialCode_DelegatesToRepository()
    {
        // Arrange
        var pagedResult = new PagedResult<Lot>
        {
            Items = new List<Lot> { new Lot("MAT001", "L1", null, DateOnly.FromDateTime(DateTime.Today), null, "user") },
            TotalCount = 1, PageNumber = 1, PageSize = 20
        };
        _lotRepo.Setup(r => r.GetPaginatedAsync("MAT001", null, null, 1, 20, default)).ReturnsAsync(pagedResult);

        var request = new ListLotsRequest { MaterialCode = "MAT001", Page = 1, PageSize = 20 };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Lots);
    }
}
```

- [ ] **Step 3: Run tests to confirm they fail (compile error)**

```bash
cd /path/to/repo/backend && dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~GetLotHandlerTests|FullyQualifiedName~ListLotsHandlerTests" 2>&1 | head -10
```

Expected: Compile error.

- [ ] **Step 4: Create GetLot use case**

`GetLotRequest.cs`:
```csharp
using MediatR;
namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetLot;
public class GetLotRequest : IRequest<GetLotResponse> { public int Id { get; set; } }
```

`GetLotResponse.cs`:
```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Shared;
namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetLot;
public class GetLotResponse : BaseResponse
{
    public LotDto Lot { get; set; } = null!;
    public List<EanDto> Eans { get; set; } = new();
    public GetLotResponse() : base() { }
    public GetLotResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
```

`GetLotHandler.cs`:
```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateLot;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetLot;

public class GetLotHandler : IRequestHandler<GetLotRequest, GetLotResponse>
{
    private readonly ILogger<GetLotHandler> _logger;
    private readonly ILotRepository _lotRepository;
    private readonly IEanRepository _eanRepository;

    public GetLotHandler(ILogger<GetLotHandler> logger, ILotRepository lotRepository, IEanRepository eanRepository)
    {
        _logger = logger;
        _lotRepository = lotRepository;
        _eanRepository = eanRepository;
    }

    public async Task<GetLotResponse> Handle(GetLotRequest request, CancellationToken cancellationToken)
    {
        var lot = await _lotRepository.GetByIdWithEansAsync(request.Id, cancellationToken);
        if (lot == null)
        {
            _logger.LogWarning("Lot {Id} not found", request.Id);
            return new GetLotResponse(ErrorCodes.LotNotFound, new Dictionary<string, string> { { "Id", request.Id.ToString() } });
        }

        var eanPage = await _eanRepository.GetPaginatedAsync(request.Id, null, 1, 100, cancellationToken);
        var eanDtos = eanPage.Items.Select(e => new EanDto
        {
            Id = e.Id,
            Code = e.Code,
            LotId = e.LotId,
            Amount = e.Amount,
            Unit = e.Unit,
            CreatedAt = e.CreatedAt,
            CreatedBy = e.CreatedBy
        }).ToList();

        return new GetLotResponse { Lot = CreateLotHandler.MapToDto(lot), Eans = eanDtos };
    }
}
```

- [ ] **Step 5: Create ListLots use case**

`ListLotsRequest.cs`:
```csharp
using MediatR;
namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListLots;
public class ListLotsRequest : IRequest<ListLotsResponse>
{
    public string? MaterialCode { get; set; }
    public DateOnly? ExpirationFrom { get; set; }
    public DateOnly? ExpirationTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
```

`ListLotsResponse.cs`:
```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Shared;
namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListLots;
public class ListLotsResponse : BaseResponse
{
    public List<LotDto> Lots { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public ListLotsResponse() : base() { }
    public ListLotsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
```

`ListLotsHandler.cs`:
```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateLot;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListLots;

public class ListLotsHandler : IRequestHandler<ListLotsRequest, ListLotsResponse>
{
    private readonly ILogger<ListLotsHandler> _logger;
    private readonly ILotRepository _lotRepository;

    public ListLotsHandler(ILogger<ListLotsHandler> logger, ILotRepository lotRepository)
    {
        _logger = logger;
        _lotRepository = lotRepository;
    }

    public async Task<ListLotsResponse> Handle(ListLotsRequest request, CancellationToken cancellationToken)
    {
        var result = await _lotRepository.GetPaginatedAsync(
            request.MaterialCode,
            request.ExpirationFrom,
            request.ExpirationTo,
            request.Page,
            request.PageSize,
            cancellationToken);

        return new ListLotsResponse
        {
            Lots = result.Items.Select(CreateLotHandler.MapToDto).ToList(),
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize
        };
    }
}
```

- [ ] **Step 6: Run tests — all should pass**

```bash
cd /path/to/repo/backend && dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~GetLotHandlerTests|FullyQualifiedName~ListLotsHandlerTests" -v
```

Expected: All pass.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/GetLot/ \
        backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/ListLots/ \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/GetLotHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/ListLotsHandlerTests.cs
git commit -m "feat: add GetLot and ListLots handlers with TDD tests"
```

---

## Task 8: UpdateLot + DeleteLot handlers (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/UpdateLotHandlerTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/DeleteLotHandlerTests.cs`
- Create: UseCases/UpdateLot/ and UseCases/DeleteLot/ (3-4 files each)

- [ ] **Step 1: Write UpdateLotHandlerTests.cs**

```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.UpdateLot;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class UpdateLotHandlerTests
{
    private readonly Mock<ILotRepository> _lotRepo = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly UpdateLotHandler _handler;

    public UpdateLotHandlerTests()
    {
        _currentUser.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("u1", "Test User", null, true));
        _handler = new UpdateLotHandler(NullLogger<UpdateLotHandler>.Instance, _lotRepo.Object, _currentUser.Object);
    }

    [Fact]
    public async Task Handle_ExistingLot_UpdatesMutableFields()
    {
        // Arrange
        var lot = new Lot("MAT001", "L1", null, new DateOnly(2026, 5, 1), null, "user");
        _lotRepo.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(lot);
        _lotRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(1);

        var request = new UpdateLotRequest
        {
            Id = 1,
            Expiration = new DateOnly(2027, 12, 31),
            ReceivedDate = new DateOnly(2026, 5, 13),
            Notes = "Updated notes"
        };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(new DateOnly(2027, 12, 31), result.Lot.Expiration);
        Assert.Equal("Updated notes", result.Lot.Notes);
        _lotRepo.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_MissingLot_ReturnsLotNotFound()
    {
        // Arrange
        _lotRepo.Setup(r => r.GetByIdAsync(99, default)).ReturnsAsync((Lot?)null);

        // Act
        var result = await _handler.Handle(new UpdateLotRequest { Id = 99, ReceivedDate = DateOnly.FromDateTime(DateTime.Today) }, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.LotNotFound, result.ErrorCode);
    }
}
```

- [ ] **Step 2: Write DeleteLotHandlerTests.cs**

```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DeleteLot;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class DeleteLotHandlerTests
{
    private readonly Mock<ILotRepository> _lotRepo = new();
    private readonly Mock<IEanRepository> _eanRepo = new();
    private readonly DeleteLotHandler _handler;

    public DeleteLotHandlerTests()
    {
        _handler = new DeleteLotHandler(NullLogger<DeleteLotHandler>.Instance, _lotRepo.Object, _eanRepo.Object);
    }

    [Fact]
    public async Task Handle_LotWithNoEans_DeletesSuccessfully()
    {
        // Arrange
        var lot = new Lot("MAT001", "L1", null, DateOnly.FromDateTime(DateTime.Today), null, "user");
        _lotRepo.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(lot);
        _eanRepo.Setup(r => r.AnyByLotIdAsync(1, default)).ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(new DeleteLotRequest { Id = 1 }, default);

        // Assert
        Assert.True(result.Success);
        _lotRepo.Verify(r => r.DeleteAsync(lot, default), Times.Once);
        _lotRepo.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_LotWithEans_ReturnsLotHasEans()
    {
        // Arrange
        var lot = new Lot("MAT001", "L1", null, DateOnly.FromDateTime(DateTime.Today), null, "user");
        _lotRepo.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(lot);
        _eanRepo.Setup(r => r.AnyByLotIdAsync(1, default)).ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(new DeleteLotRequest { Id = 1 }, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.LotHasEans, result.ErrorCode);
        _lotRepo.Verify(r => r.DeleteAsync(It.IsAny<Lot>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_MissingLot_ReturnsLotNotFound()
    {
        // Arrange
        _lotRepo.Setup(r => r.GetByIdAsync(99, default)).ReturnsAsync((Lot?)null);

        // Act
        var result = await _handler.Handle(new DeleteLotRequest { Id = 99 }, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.LotNotFound, result.ErrorCode);
    }
}
```

- [ ] **Step 3: Create UpdateLot use case**

`UpdateLotRequest.cs`:
```csharp
using MediatR;
namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.UpdateLot;
public class UpdateLotRequest : IRequest<UpdateLotResponse>
{
    public int Id { get; set; }
    public DateOnly? Expiration { get; set; }
    public DateOnly ReceivedDate { get; set; }
    public string? Notes { get; set; }
}
```

`UpdateLotResponse.cs`:
```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Shared;
namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.UpdateLot;
public class UpdateLotResponse : BaseResponse
{
    public LotDto Lot { get; set; } = null!;
    public UpdateLotResponse() : base() { }
    public UpdateLotResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
```

`UpdateLotHandler.cs`:
```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateLot;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.UpdateLot;

public class UpdateLotHandler : IRequestHandler<UpdateLotRequest, UpdateLotResponse>
{
    private readonly ILogger<UpdateLotHandler> _logger;
    private readonly ILotRepository _lotRepository;
    private readonly ICurrentUserService _currentUserService;

    public UpdateLotHandler(ILogger<UpdateLotHandler> logger, ILotRepository lotRepository, ICurrentUserService currentUserService)
    {
        _logger = logger;
        _lotRepository = lotRepository;
        _currentUserService = currentUserService;
    }

    public async Task<UpdateLotResponse> Handle(UpdateLotRequest request, CancellationToken cancellationToken)
    {
        var lot = await _lotRepository.GetByIdAsync(request.Id, cancellationToken);
        if (lot == null)
        {
            _logger.LogWarning("Lot {Id} not found for update", request.Id);
            return new UpdateLotResponse(ErrorCodes.LotNotFound, new Dictionary<string, string> { { "Id", request.Id.ToString() } });
        }

        var currentUser = _currentUserService.GetCurrentUser();
        lot.Update(request.Expiration, request.ReceivedDate, request.Notes, currentUser.Name ?? "System");
        await _lotRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Lot {Id} updated", lot.Id);
        return new UpdateLotResponse { Lot = CreateLotHandler.MapToDto(lot) };
    }
}
```

`UpdateLotRequestValidator.cs`:
```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory;
using FluentValidation;
namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.UpdateLot;
public class UpdateLotRequestValidator : AbstractValidator<UpdateLotRequest>
{
    public UpdateLotRequestValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Id is required.");
        RuleFor(x => x.ReceivedDate).NotEmpty().WithMessage("ReceivedDate is required.");
        RuleFor(x => x.Notes).MaximumLength(InventoryConstants.NotesMaxLength);
    }
}
```

- [ ] **Step 4: Create DeleteLot use case**

`DeleteLotRequest.cs`:
```csharp
using MediatR;
namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DeleteLot;
public class DeleteLotRequest : IRequest<DeleteLotResponse> { public int Id { get; set; } }
```

`DeleteLotResponse.cs`:
```csharp
using Anela.Heblo.Application.Shared;
namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DeleteLot;
public class DeleteLotResponse : BaseResponse
{
    public DeleteLotResponse() : base() { }
    public DeleteLotResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
```

`DeleteLotHandler.cs`:
```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DeleteLot;

public class DeleteLotHandler : IRequestHandler<DeleteLotRequest, DeleteLotResponse>
{
    private readonly ILogger<DeleteLotHandler> _logger;
    private readonly ILotRepository _lotRepository;
    private readonly IEanRepository _eanRepository;

    public DeleteLotHandler(ILogger<DeleteLotHandler> logger, ILotRepository lotRepository, IEanRepository eanRepository)
    {
        _logger = logger;
        _lotRepository = lotRepository;
        _eanRepository = eanRepository;
    }

    public async Task<DeleteLotResponse> Handle(DeleteLotRequest request, CancellationToken cancellationToken)
    {
        var lot = await _lotRepository.GetByIdAsync(request.Id, cancellationToken);
        if (lot == null)
        {
            _logger.LogWarning("Lot {Id} not found for delete", request.Id);
            return new DeleteLotResponse(ErrorCodes.LotNotFound, new Dictionary<string, string> { { "Id", request.Id.ToString() } });
        }

        if (await _eanRepository.AnyByLotIdAsync(request.Id, cancellationToken))
        {
            _logger.LogWarning("Cannot delete lot {Id} — it still has EANs", request.Id);
            return new DeleteLotResponse(ErrorCodes.LotHasEans, new Dictionary<string, string> { { "Id", request.Id.ToString() } });
        }

        await _lotRepository.DeleteAsync(lot, cancellationToken);
        await _lotRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Lot {Id} deleted", request.Id);
        return new DeleteLotResponse();
    }
}
```

- [ ] **Step 5: Run all lot handler tests**

```bash
cd /path/to/repo/backend && dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~Catalog.Inventory" -v
```

Expected: All green.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/UpdateLot/ \
        backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/DeleteLot/ \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/UpdateLotHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/DeleteLotHandlerTests.cs
git commit -m "feat: add UpdateLot and DeleteLot handlers with TDD tests"
```

---

## Task 9: CreateEans + GetEanByCode handlers (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/CreateEansHandlerTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/GetEanByCodeHandlerTests.cs`
- Create: UseCases/CreateEans/ and UseCases/GetEanByCode/

- [ ] **Step 1: Write CreateEansHandlerTests.cs**

```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.Services;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateEans;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class CreateEansHandlerTests
{
    private readonly Mock<IEanRepository> _eanRepo = new();
    private readonly Mock<ILotRepository> _lotRepo = new();
    private readonly Mock<IEanCodeGenerator> _generator = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly CreateEansHandler _handler;

    public CreateEansHandlerTests()
    {
        _currentUser.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("u1", "Test User", null, true));
        _handler = new CreateEansHandler(
            NullLogger<CreateEansHandler>.Instance,
            _eanRepo.Object,
            _lotRepo.Object,
            _generator.Object,
            _currentUser.Object);
    }

    [Fact]
    public async Task Handle_ValidRequest_GeneratesAndPersistsEans()
    {
        // Arrange
        var lot = new Lot("MAT001", "L1", null, DateOnly.FromDateTime(DateTime.Today), null, "user");
        _lotRepo.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(lot);
        _generator.Setup(g => g.GenerateAsync(2, default))
            .ReturnsAsync(new List<string> { "INT-00000001", "INT-00000002" });
        _eanRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<Ean>>(), default))
            .ReturnsAsync((IEnumerable<Ean> eans, CancellationToken _) => eans);
        _eanRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(2);

        var request = new CreateEansRequest
        {
            LotId = 1,
            Items = new List<CreateEanItem>
            {
                new() { Amount = 25m, Unit = "kg" },
                new() { Amount = 25m, Unit = "kg" }
            }
        };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Eans.Count);
        Assert.Equal("INT-00000001", result.Eans[0].Code);
        Assert.Equal("INT-00000002", result.Eans[1].Code);
        _generator.Verify(g => g.GenerateAsync(2, default), Times.Once);
        _eanRepo.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<Ean>>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_LotNotFound_ReturnsLotNotFound()
    {
        // Arrange
        _lotRepo.Setup(r => r.GetByIdAsync(99, default)).ReturnsAsync((Lot?)null);

        var request = new CreateEansRequest { LotId = 99, Items = new List<CreateEanItem> { new() { Amount = 1m, Unit = "kg" } } };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.LotNotFound, result.ErrorCode);
        _generator.Verify(g => g.GenerateAsync(It.IsAny<int>(), default), Times.Never);
    }
}
```

- [ ] **Step 2: Write GetEanByCodeHandlerTests.cs**

```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetEanByCode;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class GetEanByCodeHandlerTests
{
    private readonly Mock<IEanRepository> _eanRepo = new();
    private readonly Mock<ILotRepository> _lotRepo = new();
    private readonly GetEanByCodeHandler _handler;

    public GetEanByCodeHandlerTests()
    {
        _handler = new GetEanByCodeHandler(NullLogger<GetEanByCodeHandler>.Instance, _eanRepo.Object, _lotRepo.Object);
    }

    [Fact]
    public async Task Handle_ExistingCode_ReturnsEanWithLot()
    {
        // Arrange
        var ean = new Ean("INT-00000001", 1, 25m, "kg", "user");
        var lot = new Lot("MAT001", "L1", new DateOnly(2027, 6, 1), DateOnly.FromDateTime(DateTime.Today), null, "user");
        _eanRepo.Setup(r => r.GetByCodeAsync("INT-00000001", default)).ReturnsAsync(ean);
        _lotRepo.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(lot);

        // Act
        var result = await _handler.Handle(new GetEanByCodeRequest { Code = "INT-00000001" }, default);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("INT-00000001", result.Ean.Code);
        Assert.Equal("MAT001", result.Lot.MaterialCode);
    }

    [Fact]
    public async Task Handle_MissingCode_ReturnsEanNotFound()
    {
        // Arrange
        _eanRepo.Setup(r => r.GetByCodeAsync("MISSING", default)).ReturnsAsync((Ean?)null);

        // Act
        var result = await _handler.Handle(new GetEanByCodeRequest { Code = "MISSING" }, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.EanNotFound, result.ErrorCode);
    }
}
```

- [ ] **Step 3: Create CreateEans use case**

`CreateEansRequest.cs`:
```csharp
using MediatR;
namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateEans;
public class CreateEansRequest : IRequest<CreateEansResponse>
{
    public int LotId { get; set; }
    public List<CreateEanItem> Items { get; set; } = new();
}
public class CreateEanItem
{
    public decimal Amount { get; set; }
    public string Unit { get; set; } = null!;
}
```

`CreateEansResponse.cs`:
```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Shared;
namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateEans;
public class CreateEansResponse : BaseResponse
{
    public List<EanDto> Eans { get; set; } = new();
    public CreateEansResponse() : base() { }
    public CreateEansResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
```

`CreateEansHandler.cs`:
```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Features.Catalog.Inventory.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateEans;

public class CreateEansHandler : IRequestHandler<CreateEansRequest, CreateEansResponse>
{
    private readonly ILogger<CreateEansHandler> _logger;
    private readonly IEanRepository _eanRepository;
    private readonly ILotRepository _lotRepository;
    private readonly IEanCodeGenerator _eanCodeGenerator;
    private readonly ICurrentUserService _currentUserService;

    public CreateEansHandler(
        ILogger<CreateEansHandler> logger,
        IEanRepository eanRepository,
        ILotRepository lotRepository,
        IEanCodeGenerator eanCodeGenerator,
        ICurrentUserService currentUserService)
    {
        _logger = logger;
        _eanRepository = eanRepository;
        _lotRepository = lotRepository;
        _eanCodeGenerator = eanCodeGenerator;
        _currentUserService = currentUserService;
    }

    public async Task<CreateEansResponse> Handle(CreateEansRequest request, CancellationToken cancellationToken)
    {
        var lot = await _lotRepository.GetByIdAsync(request.LotId, cancellationToken);
        if (lot == null)
        {
            _logger.LogWarning("Lot {LotId} not found for EAN creation", request.LotId);
            return new CreateEansResponse(ErrorCodes.LotNotFound, new Dictionary<string, string> { { "LotId", request.LotId.ToString() } });
        }

        var currentUser = _currentUserService.GetCurrentUser();
        var createdBy = currentUser.Name ?? "System";

        var codes = await _eanCodeGenerator.GenerateAsync(request.Items.Count, cancellationToken);
        var eans = request.Items
            .Select((item, i) => new Ean(codes[i], request.LotId, item.Amount, item.Unit, createdBy))
            .ToList();

        await _eanRepository.AddRangeAsync(eans, cancellationToken);
        await _eanRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created {Count} EANs for Lot {LotId}", eans.Count, request.LotId);

        return new CreateEansResponse
        {
            Eans = eans.Select(e => new EanDto
            {
                Id = e.Id,
                Code = e.Code,
                LotId = e.LotId,
                Amount = e.Amount,
                Unit = e.Unit,
                CreatedAt = e.CreatedAt,
                CreatedBy = e.CreatedBy
            }).ToList()
        };
    }
}
```

`CreateEansRequestValidator.cs`:
```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory;
using FluentValidation;
namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateEans;
public class CreateEansRequestValidator : AbstractValidator<CreateEansRequest>
{
    public CreateEansRequestValidator()
    {
        RuleFor(x => x.LotId).GreaterThan(0).WithMessage("LotId is required.");
        RuleFor(x => x.Items).NotEmpty().WithMessage("At least one item is required.");
        RuleFor(x => x.Items.Count).LessThanOrEqualTo(500).WithMessage("Cannot create more than 500 EANs in one call.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Amount).GreaterThan(0).WithMessage("Amount must be positive.");
            item.RuleFor(i => i.Unit).NotEmpty().MaximumLength(InventoryConstants.UnitMaxLength);
        });
    }
}
```

- [ ] **Step 4: Create GetEanByCode use case**

`GetEanByCodeRequest.cs`:
```csharp
using MediatR;
namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetEanByCode;
public class GetEanByCodeRequest : IRequest<GetEanByCodeResponse> { public string Code { get; set; } = null!; }
```

`GetEanByCodeResponse.cs`:
```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Shared;
namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetEanByCode;
public class GetEanByCodeResponse : BaseResponse
{
    public EanDto Ean { get; set; } = null!;
    public LotDto Lot { get; set; } = null!;
    public GetEanByCodeResponse() : base() { }
    public GetEanByCodeResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
```

`GetEanByCodeHandler.cs`:
```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateLot;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetEanByCode;

public class GetEanByCodeHandler : IRequestHandler<GetEanByCodeRequest, GetEanByCodeResponse>
{
    private readonly ILogger<GetEanByCodeHandler> _logger;
    private readonly IEanRepository _eanRepository;
    private readonly ILotRepository _lotRepository;

    public GetEanByCodeHandler(ILogger<GetEanByCodeHandler> logger, IEanRepository eanRepository, ILotRepository lotRepository)
    {
        _logger = logger;
        _eanRepository = eanRepository;
        _lotRepository = lotRepository;
    }

    public async Task<GetEanByCodeResponse> Handle(GetEanByCodeRequest request, CancellationToken cancellationToken)
    {
        var ean = await _eanRepository.GetByCodeAsync(request.Code, cancellationToken);
        if (ean == null)
        {
            _logger.LogWarning("EAN code {Code} not found", request.Code);
            return new GetEanByCodeResponse(ErrorCodes.EanNotFound, new Dictionary<string, string> { { "Code", request.Code } });
        }

        var lot = await _lotRepository.GetByIdAsync(ean.LotId, cancellationToken);
        if (lot == null)
        {
            _logger.LogError("Orphaned EAN {Id} — lot {LotId} missing", ean.Id, ean.LotId);
            return new GetEanByCodeResponse(ErrorCodes.LotNotFound, new Dictionary<string, string> { { "LotId", ean.LotId.ToString() } });
        }

        return new GetEanByCodeResponse
        {
            Ean = new EanDto { Id = ean.Id, Code = ean.Code, LotId = ean.LotId, Amount = ean.Amount, Unit = ean.Unit, CreatedAt = ean.CreatedAt, CreatedBy = ean.CreatedBy },
            Lot = CreateLotHandler.MapToDto(lot)
        };
    }
}
```

- [ ] **Step 5: Run tests**

```bash
cd /path/to/repo/backend && dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~Catalog.Inventory" -v
```

Expected: All green.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/CreateEans/ \
        backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/GetEanByCode/ \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/CreateEansHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/GetEanByCodeHandlerTests.cs
git commit -m "feat: add CreateEans and GetEanByCode handlers with TDD tests"
```

---

## Task 10: ListEans + DeleteEan handlers (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/ListEansHandlerTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/DeleteEanHandlerTests.cs`
- Create: UseCases/ListEans/ and UseCases/DeleteEan/

- [ ] **Step 1: Write ListEansHandlerTests.cs**

```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListEans;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Xcc.Persistance;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class ListEansHandlerTests
{
    private readonly Mock<IEanRepository> _eanRepo = new();
    private readonly ListEansHandler _handler;

    public ListEansHandlerTests()
    {
        _handler = new ListEansHandler(NullLogger<ListEansHandler>.Instance, _eanRepo.Object);
    }

    [Fact]
    public async Task Handle_FilterByLotId_DelegatesToRepository()
    {
        // Arrange
        var paged = new PagedResult<Ean>
        {
            Items = new List<Ean> { new Ean("INT-00000001", 5, 25m, "kg", "user") },
            TotalCount = 1, PageNumber = 1, PageSize = 20
        };
        _eanRepo.Setup(r => r.GetPaginatedAsync(5, null, 1, 20, default)).ReturnsAsync(paged);

        // Act
        var result = await _handler.Handle(new ListEansRequest { LotId = 5, Page = 1, PageSize = 20 }, default);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Eans);
    }
}
```

- [ ] **Step 2: Write DeleteEanHandlerTests.cs**

```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DeleteEan;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class DeleteEanHandlerTests
{
    private readonly Mock<IEanRepository> _eanRepo = new();
    private readonly DeleteEanHandler _handler;

    public DeleteEanHandlerTests()
    {
        _handler = new DeleteEanHandler(NullLogger<DeleteEanHandler>.Instance, _eanRepo.Object);
    }

    [Fact]
    public async Task Handle_ExistingEan_DeletesSuccessfully()
    {
        // Arrange
        var ean = new Ean("INT-00000001", 1, 25m, "kg", "user");
        _eanRepo.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(ean);

        // Act
        var result = await _handler.Handle(new DeleteEanRequest { Id = 1 }, default);

        // Assert
        Assert.True(result.Success);
        _eanRepo.Verify(r => r.DeleteAsync(ean, default), Times.Once);
        _eanRepo.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_MissingEan_ReturnsEanNotFound()
    {
        // Arrange
        _eanRepo.Setup(r => r.GetByIdAsync(99, default)).ReturnsAsync((Ean?)null);

        // Act
        var result = await _handler.Handle(new DeleteEanRequest { Id = 99 }, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.EanNotFound, result.ErrorCode);
    }
}
```

- [ ] **Step 3: Create ListEans use case**

`ListEansRequest.cs`:
```csharp
using MediatR;
namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListEans;
public class ListEansRequest : IRequest<ListEansResponse>
{
    public int? LotId { get; set; }
    public string? MaterialCode { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
```

`ListEansResponse.cs`:
```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Shared;
namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListEans;
public class ListEansResponse : BaseResponse
{
    public List<EanDto> Eans { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public ListEansResponse() : base() { }
    public ListEansResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
```

`ListEansHandler.cs`:
```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListEans;

public class ListEansHandler : IRequestHandler<ListEansRequest, ListEansResponse>
{
    private readonly ILogger<ListEansHandler> _logger;
    private readonly IEanRepository _eanRepository;

    public ListEansHandler(ILogger<ListEansHandler> logger, IEanRepository eanRepository)
    {
        _logger = logger;
        _eanRepository = eanRepository;
    }

    public async Task<ListEansResponse> Handle(ListEansRequest request, CancellationToken cancellationToken)
    {
        var result = await _eanRepository.GetPaginatedAsync(
            request.LotId,
            request.MaterialCode,
            request.Page,
            request.PageSize,
            cancellationToken);

        return new ListEansResponse
        {
            Eans = result.Items.Select(e => new EanDto
            {
                Id = e.Id, Code = e.Code, LotId = e.LotId, Amount = e.Amount,
                Unit = e.Unit, CreatedAt = e.CreatedAt, CreatedBy = e.CreatedBy
            }).ToList(),
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize
        };
    }
}
```

- [ ] **Step 4: Create DeleteEan use case**

`DeleteEanRequest.cs`:
```csharp
using MediatR;
namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DeleteEan;
public class DeleteEanRequest : IRequest<DeleteEanResponse> { public int Id { get; set; } }
```

`DeleteEanResponse.cs`:
```csharp
using Anela.Heblo.Application.Shared;
namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DeleteEan;
public class DeleteEanResponse : BaseResponse
{
    public DeleteEanResponse() : base() { }
    public DeleteEanResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
```

`DeleteEanHandler.cs`:
```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DeleteEan;

public class DeleteEanHandler : IRequestHandler<DeleteEanRequest, DeleteEanResponse>
{
    private readonly ILogger<DeleteEanHandler> _logger;
    private readonly IEanRepository _eanRepository;

    public DeleteEanHandler(ILogger<DeleteEanHandler> logger, IEanRepository eanRepository)
    {
        _logger = logger;
        _eanRepository = eanRepository;
    }

    public async Task<DeleteEanResponse> Handle(DeleteEanRequest request, CancellationToken cancellationToken)
    {
        var ean = await _eanRepository.GetByIdAsync(request.Id, cancellationToken);
        if (ean == null)
        {
            _logger.LogWarning("EAN {Id} not found for delete", request.Id);
            return new DeleteEanResponse(ErrorCodes.EanNotFound, new Dictionary<string, string> { { "Id", request.Id.ToString() } });
        }

        await _eanRepository.DeleteAsync(ean, cancellationToken);
        await _eanRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("EAN {Id} deleted", request.Id);
        return new DeleteEanResponse();
    }
}
```

- [ ] **Step 5: Run all inventory tests**

```bash
cd /path/to/repo/backend && dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~Catalog.Inventory" -v
```

Expected: All green.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/ListEans/ \
        backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/DeleteEan/ \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/ListEansHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/DeleteEanHandlerTests.cs
git commit -m "feat: add ListEans and DeleteEan handlers with TDD tests"
```

---

## Task 11: DI module + controllers

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/InventoryModule.cs`
- Create: `backend/src/Anela.Heblo.API/Controllers/LotsController.cs`
- Create: `backend/src/Anela.Heblo.API/Controllers/EansController.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — call `AddInventoryModule()`

- [ ] **Step 1: Create InventoryModule.cs**

Pattern mirrors `CatalogModule.cs` and `PurchaseModule.cs`. Read those files to confirm exact DI registration syntax if unclear.

```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.Services;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateEans;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateLot;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.UpdateLot;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Persistence.Catalog.Inventory;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Anela.Heblo.Application.Common.Behaviors;

namespace Anela.Heblo.Application.Features.Catalog.Inventory;

public static class InventoryModule
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services)
    {
        // Repositories
        services.AddScoped<ILotRepository, LotRepository>();
        services.AddScoped<IEanRepository, EanRepository>();

        // Services
        services.AddScoped<IEanCodeGenerator, EanCodeGenerator>();

        // Validators
        services.AddScoped<IValidator<CreateLotRequest>, CreateLotRequestValidator>();
        services.AddScoped<IValidator<UpdateLotRequest>, UpdateLotRequestValidator>();
        services.AddScoped<IValidator<CreateEansRequest>, CreateEansRequestValidator>();

        // Validation pipeline behaviors
        services.AddScoped<IPipelineBehavior<CreateLotRequest, CreateLotResponse>, ValidationBehavior<CreateLotRequest, CreateLotResponse>>();
        services.AddScoped<IPipelineBehavior<UpdateLotRequest, UpdateLotResponse>, ValidationBehavior<UpdateLotRequest, UpdateLotResponse>>();
        services.AddScoped<IPipelineBehavior<CreateEansRequest, CreateEansResponse>, ValidationBehavior<CreateEansRequest, CreateEansResponse>>();

        return services;
    }
}
```

**Note:** `ValidationBehavior<TRequest, TResponse>` is the existing MediatR validation pipeline behavior from `Anela.Heblo.Application.Common.Behaviors`. Check `CatalogModule.cs` or another module for the correct namespace.

- [ ] **Step 2: Register InventoryModule in ApplicationModule.cs**

Open `backend/src/Anela.Heblo.Application/ApplicationModule.cs` and add after the existing module registrations (e.g., after `services.AddCatalogModule(configuration)`):

```csharp
services.AddInventoryModule();
```

Add the using directive if needed:
```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory;
```

- [ ] **Step 3: Create LotsController.cs**

```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateLot;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DeleteLot;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetLot;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListLots;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.UpdateLot;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[Route("api/lots")]
[ApiController]
public class LotsController : BaseApiController
{
    private readonly IMediator _mediator;

    public LotsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<ListLotsResponse>> GetLots(
        [FromQuery] string? materialCode,
        [FromQuery] DateOnly? expirationFrom,
        [FromQuery] DateOnly? expirationTo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var request = new ListLotsRequest
        {
            MaterialCode = materialCode,
            ExpirationFrom = expirationFrom,
            ExpirationTo = expirationTo,
            Page = page,
            PageSize = pageSize
        };
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<GetLotResponse>> GetLotById(int id, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetLotRequest { Id = id }, cancellationToken);
        return HandleResponse(response);
    }

    [HttpPost]
    public async Task<ActionResult<CreateLotResponse>> CreateLot(
        [FromBody] CreateLotRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<UpdateLotResponse>> UpdateLot(
        int id,
        [FromBody] UpdateLotRequest request,
        CancellationToken cancellationToken)
    {
        request.Id = id;
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<DeleteLotResponse>> DeleteLot(int id, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new DeleteLotRequest { Id = id }, cancellationToken);
        return HandleResponse(response);
    }
}
```

- [ ] **Step 4: Create EansController.cs**

```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateEans;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DeleteEan;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetEanByCode;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListEans;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[Route("api/eans")]
[ApiController]
public class EansController : BaseApiController
{
    private readonly IMediator _mediator;

    public EansController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<ListEansResponse>> GetEans(
        [FromQuery] int? lotId,
        [FromQuery] string? materialCode,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var request = new ListEansRequest { LotId = lotId, MaterialCode = materialCode, Page = page, PageSize = pageSize };
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }

    [HttpGet("by-code/{code}")]
    public async Task<ActionResult<GetEanByCodeResponse>> GetEanByCode(string code, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetEanByCodeRequest { Code = code }, cancellationToken);
        return HandleResponse(response);
    }

    [HttpPost]
    public async Task<ActionResult<CreateEansResponse>> CreateEans(
        [FromBody] CreateEansRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<DeleteEanResponse>> DeleteEan(int id, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new DeleteEanRequest { Id = id }, cancellationToken);
        return HandleResponse(response);
    }
}
```

- [ ] **Step 5: Full build**

```bash
cd /path/to/repo/backend && dotnet build --no-restore
```

Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/InventoryModule.cs \
        backend/src/Anela.Heblo.Application/ApplicationModule.cs \
        backend/src/Anela.Heblo.API/Controllers/LotsController.cs \
        backend/src/Anela.Heblo.API/Controllers/EansController.cs
git commit -m "feat: add InventoryModule DI registration and Lots/Eans controllers"
```

---

## Task 12: EF Migration + format + verification

**Files:**
- Generated: `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddLotAndEanInventory.cs`

- [ ] **Step 1: Generate the migration**

First check `docs/development/setup.md` for the exact EF commands used in this project. Typically:

```bash
cd /path/to/repo/backend && dotnet ef migrations add AddLotAndEanInventory \
  --project src/Anela.Heblo.Persistence \
  --startup-project src/Anela.Heblo.API \
  --output-dir Migrations
```

This generates a migration file at `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddLotAndEanInventory.cs`.

Expected: Migration file created. The `Up()` method should contain `CreateTable` calls for `Lots` and `Eans` plus the unique index creation.

- [ ] **Step 2: Add the Postgres sequence to the migration**

Open the generated `<timestamp>_AddLotAndEanInventory.cs` and add before the `CreateTable("Lots", ...)` call in `Up()`:

```csharp
migrationBuilder.Sql("CREATE SEQUENCE ean_internal_seq START WITH 1 INCREMENT BY 1 NO CYCLE;");
```

Add to `Down()` (after dropping the `Eans` table):

```csharp
migrationBuilder.Sql("DROP SEQUENCE IF EXISTS ean_internal_seq;");
```

- [ ] **Step 3: Apply migration to local database**

```bash
cd /path/to/repo/backend && dotnet ef database update \
  --project src/Anela.Heblo.Persistence \
  --startup-project src/Anela.Heblo.API
```

Expected: `Done. No further migrations needed to apply.`

- [ ] **Step 4: Smoke-check schema in Postgres**

Connect to local Postgres (check `backend/src/Anela.Heblo.API/appsettings.Development.json` or user secrets for connection string) and run:

```sql
\d+ public."Lots"
\d+ public."Eans"
\d ean_internal_seq
```

Expected:
- `Lots` table with columns: `Id`, `MaterialCode`, `LotCode`, `Expiration`, `ReceivedDate`, `Notes`, `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`
- Unique index on `(MaterialCode, LotCode)`
- `Eans` table with FK to `Lots.Id` (RESTRICT)
- Unique index on `Code`
- Sequence `ean_internal_seq` exists

- [ ] **Step 5: Run dotnet format**

```bash
cd /path/to/repo/backend && dotnet format
```

Expected: `Formatted code successfully.` or `No issues found.`

- [ ] **Step 6: Run all inventory tests one final time**

```bash
cd /path/to/repo/backend && dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~Catalog.Inventory" -v
```

Expected: All pass.

- [ ] **Step 7: Run full test suite**

```bash
cd /path/to/repo/backend && dotnet test --no-restore -v 2>&1 | tail -30
```

Expected: No regressions. All pre-existing tests still pass.

- [ ] **Step 8: Build frontend (auto-generates TS API client)**

```bash
cd /path/to/repo/frontend && npm run build
```

Expected: Build succeeds. New endpoints appear in `frontend/src/api/generated/api-client.ts` (search for `LotsController` or `/api/lots`).

- [ ] **Step 9: Commit migration**

```bash
git add backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat: add EF migration for Lots and Eans tables with ean_internal_seq sequence"
```

---

## Verification checklist

Manual smoke test via Swagger at `https://localhost:5001/swagger`:

- [ ] `POST /api/lots` — body `{ "materialCode": "<valid Material ProductCode>", "lotCode": "TEST-001", "receivedDate": "2026-05-13" }` → 200 with lot id
- [ ] `POST /api/eans` — body `{ "lotId": <id>, "items": [{ "amount": 25.0, "unit": "kg" }, { "amount": 25.0, "unit": "kg" }] }` → 200 with `["INT-00000001", "INT-00000002"]` codes
- [ ] `GET /api/eans/by-code/INT-00000001` → 200 with ean + lot + materialCode
- [ ] `DELETE /api/lots/<id>` while EANs exist → 400 `LotHasEans`
- [ ] `DELETE /api/eans/<id>` for both EANs → 200
- [ ] `DELETE /api/lots/<id>` after EANs deleted → 200
- [ ] `POST /api/lots` with duplicate `(materialCode, lotCode)` → 409 `LotAlreadyExists`
- [ ] `POST /api/lots` with a `materialCode` of type `Product` (not Material) → 400 `InventoryMaterialInvalidType`

---

## Troubleshooting

**`EanCodeGenerator` build error (Npgsql namespace):** If `NpgsqlTypes` is not found, check `using Npgsql;` and `using NpgsqlTypes = Npgsql.NpgsqlDbType;` or use `cmd.Parameters.Add("", NpgsqlDbType.Integer).Value = count;` with `using Npgsql;`.

**`CatalogAggregate` construction in tests:** Check `backend/test/Anela.Heblo.Tests/Features/Catalog/` for existing tests that construct a `CatalogAggregate`. If there is no public constructor, you may need a test factory or reflection. This is the one place tests may need to adapt to the actual `CatalogAggregate` internal API.

**`ValidationBehavior` namespace:** If `Anela.Heblo.Application.Common.Behaviors` is wrong, check `CatalogModule.cs` for the actual namespace — it's registered there for multiple handlers.

**Migration `DateOnly` column type:** If EF Core doesn't auto-map `DateOnly` to `date`, the generated migration may show `nvarchar` or `text`. Add `.HasColumnType("date")` in the configurations (already specified in Task 3). Regenerate the migration if needed.
