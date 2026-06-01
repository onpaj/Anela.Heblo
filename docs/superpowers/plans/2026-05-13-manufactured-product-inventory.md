# Manufactured Product Inventory Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Track on-hand manufactured products lot-by-lot; write them down when a manufacture order completes; let transport-box packers pick from that list (deducting stock); restore when items are removed; allow manual edits.

**Architecture:** New domain aggregate `ManufacturedProductInventoryItem` (per lot row, embedded audit log) mirrors the `GiftPackageManufactureLog` pattern. The aggregate is written by `UpdateManufactureOrderStatusHandler` on → Completed and consumed/restored by the transport-box item handlers. Frontend adds a "Manufactured" tab to the transport box add-item UI and a standalone management page.

**Tech Stack:** .NET 8 + EF Core + PostgreSQL, MediatR, AutoMapper, React + TanStack Query, Tailwind CSS, xUnit + FluentAssertions + Moq.

---

## File Map

### New backend files
- `backend/src/Anela.Heblo.Domain/Features/Manufacture/Inventory/InventoryChangeType.cs`
- `backend/src/Anela.Heblo.Domain/Features/Manufacture/Inventory/ManufacturedProductInventoryLog.cs`
- `backend/src/Anela.Heblo.Domain/Features/Manufacture/Inventory/ManufacturedProductInventoryItem.cs`
- `backend/src/Anela.Heblo.Domain/Features/Manufacture/Inventory/IManufacturedProductInventoryRepository.cs`
- `backend/src/Anela.Heblo.Persistence/Manufacture/Inventory/ManufacturedProductInventoryItemConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Manufacture/Inventory/ManufacturedProductInventoryLogConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Manufacture/Inventory/ManufacturedProductInventoryRepository.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/ManufacturedProductInventoryItemDto.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/ManufacturedProductInventoryLogDto.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufacturedProductInventoryMappingProfile.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufacturedInventory/{Request,Response,Handler}.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CreateManufacturedInventoryItem/{Request,Response,Handler}.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufacturedInventoryItem/{Request,Response,Handler}.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/DeleteManufacturedInventoryItem/{Request,Response,Handler}.cs`
- `backend/src/Anela.Heblo.API/Controllers/ManufacturedProductInventoryController.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Inventory/ManufacturedProductInventoryItemTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Inventory/GetManufacturedInventoryHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Inventory/CrudManufacturedInventoryHandlerTests.cs`
- `frontend/src/api/hooks/useManufacturedProductInventory.ts`
- `frontend/src/components/manufacture/pages/ManufacturedProductInventoryPage.tsx`

### Modified backend files
- `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxItem.cs` — add `LotNumber`, `ExpirationDate`, `SourceInventoryId`
- `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBox.cs` — extend `AddItem`; change `Reset` to return cleared items
- `backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/TransportBoxItemConfiguration.cs` — new nullable columns
- `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` — two new DbSets
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — codes 1215, 1216
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs` — register new repository
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs` — write-down on → Completed
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/AddItemToBox/AddItemToBoxRequest.cs` — optional lot fields
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/AddItemToBox/AddItemToBoxHandler.cs` — consume inventory
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/RemoveItemFromBox/RemoveItemFromBoxHandler.cs` — restore inventory
- `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/TransportBoxItemDto.cs` — add lot fields
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderStatusHandlerTests.cs` — extend
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/AddItemToBoxHandlerTests.cs` — create + extend
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/RemoveItemFromBoxHandlerTests.cs` — create + extend

### Modified frontend files
- `frontend/src/App.tsx` — add route `/manufacturing/product-inventory`
- `frontend/src/components/Layout/Sidebar.tsx` — add "Sklad výroby" link
- `frontend/src/components/transport/box-detail/TransportBoxItems.tsx` — Manufactured tab
- `frontend/src/api/hooks/useTransportBoxes.ts` — update `useAddItemToBox` mutation params

---

## Task 1: Domain — InventoryChangeType enum + ManufacturedProductInventoryLog entity

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Manufacture/Inventory/InventoryChangeType.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Manufacture/Inventory/ManufacturedProductInventoryLog.cs`

- [ ] **Step 1: Create InventoryChangeType.cs**

```csharp
namespace Anela.Heblo.Domain.Features.Manufacture.Inventory;

public enum InventoryChangeType
{
    InitialWriteDown = 1,
    ConsumedByTransportBox = 2,
    RestoredFromTransportBox = 3,
    ManualAdjustment = 4,
    ManualRemoval = 5,
    ManualAddition = 6
}
```

- [ ] **Step 2: Create ManufacturedProductInventoryLog.cs**

```csharp
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Manufacture.Inventory;

public class ManufacturedProductInventoryLog : Entity<int>
{
    public int InventoryItemId { get; private set; }
    public InventoryChangeType ChangeType { get; private set; }
    public decimal AmountDelta { get; private set; }
    public decimal AmountAfter { get; private set; }
    public string? ReferenceType { get; private set; }
    public string? ReferenceId { get; private set; }
    public string? Note { get; private set; }
    public DateTime Timestamp { get; private set; }
    public string User { get; private set; } = null!;

    public ManufacturedProductInventoryLog(
        InventoryChangeType changeType,
        decimal amountDelta,
        decimal amountAfter,
        string user,
        DateTime timestamp,
        string? referenceType = null,
        string? referenceId = null,
        string? note = null)
    {
        ChangeType = changeType;
        AmountDelta = amountDelta;
        AmountAfter = amountAfter;
        User = user;
        Timestamp = timestamp;
        ReferenceType = referenceType;
        ReferenceId = referenceId;
        Note = note;
    }

    private ManufacturedProductInventoryLog() { }
}
```

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Manufacture/Inventory/
git commit -m "feat: add InventoryChangeType enum and ManufacturedProductInventoryLog entity"
```

---

## Task 2: Domain — ManufacturedProductInventoryItem aggregate + repository interface

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Manufacture/Inventory/ManufacturedProductInventoryItem.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Manufacture/Inventory/IManufacturedProductInventoryRepository.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Inventory/ManufacturedProductInventoryItemTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.Manufacture.Inventory;

public class ManufacturedProductInventoryItemTests
{
    private static ManufacturedProductInventoryItem CreateItem(decimal amount = 10m) =>
        new("PROD-001", "Test Product", amount, "user1", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            lotNumber: "LOT-001", expirationDate: new DateOnly(2027, 6, 1), manufactureOrderId: 42);

    [Fact]
    public void Constructor_SetsFieldsAndCreatesInitialLog()
    {
        var item = CreateItem(10m);

        item.ProductCode.Should().Be("PROD-001");
        item.Amount.Should().Be(10m);
        item.LotNumber.Should().Be("LOT-001");
        item.ManufactureOrderId.Should().Be(42);
        item.Log.Should().HaveCount(1);
        item.Log[0].ChangeType.Should().Be(InventoryChangeType.InitialWriteDown);
        item.Log[0].AmountDelta.Should().Be(10m);
        item.Log[0].AmountAfter.Should().Be(10m);
    }

    [Fact]
    public void Consume_ReducesAmountAndAddsLog()
    {
        var item = CreateItem(10m);
        var ts = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        item.Consume(3m, "packer1", ts, transportBoxId: 7);

        item.Amount.Should().Be(7m);
        item.Log.Should().HaveCount(2);
        var log = item.Log[1];
        log.ChangeType.Should().Be(InventoryChangeType.ConsumedByTransportBox);
        log.AmountDelta.Should().Be(-3m);
        log.AmountAfter.Should().Be(7m);
        log.ReferenceType.Should().Be("TransportBox");
        log.ReferenceId.Should().Be("7");
    }

    [Fact]
    public void Consume_WhenAmountExceedsStock_Throws()
    {
        var item = CreateItem(5m);

        var act = () => item.Consume(6m, "packer1",
            new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Insufficient*");
    }

    [Fact]
    public void Restore_IncreasesAmountAndAddsLog()
    {
        var item = CreateItem(10m);
        var ts = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        item.Consume(3m, "u", ts);

        item.Restore(3m, "u", ts, transportBoxId: 7);

        item.Amount.Should().Be(10m);
        item.Log.Last().ChangeType.Should().Be(InventoryChangeType.RestoredFromTransportBox);
        item.Log.Last().AmountDelta.Should().Be(3m);
    }

    [Fact]
    public void ManualAdjust_SetsNewAmountAndAddsLog()
    {
        var item = CreateItem(10m);
        var ts = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        item.ManualAdjust(15m, "admin", ts, note: "recount");

        item.Amount.Should().Be(15m);
        item.Log.Last().ChangeType.Should().Be(InventoryChangeType.ManualAdjustment);
        item.Log.Last().AmountDelta.Should().Be(5m);
        item.Log.Last().Note.Should().Be("recount");
    }

    [Fact]
    public void ManualRemove_ZeroesAmountAndAddsLog()
    {
        var item = CreateItem(10m);
        var ts = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        item.ManualRemove("admin", ts, note: "expired");

        item.Amount.Should().Be(0m);
        item.Log.Last().ChangeType.Should().Be(InventoryChangeType.ManualRemoval);
        item.Log.Last().AmountDelta.Should().Be(-10m);
    }
}
```

- [ ] **Step 2: Run tests — expect failure (type not defined)**

```bash
cd backend && dotnet test --filter "FullyQualifiedName~ManufacturedProductInventoryItemTests" --no-build 2>&1 | tail -20
```

Expected: compilation error — `ManufacturedProductInventoryItem` not found.

- [ ] **Step 3: Create ManufacturedProductInventoryItem.cs**

```csharp
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Manufacture.Inventory;

public class ManufacturedProductInventoryItem : Entity<int>
{
    private readonly List<ManufacturedProductInventoryLog> _log = new();

    public string ProductCode { get; private set; } = null!;
    public string ProductName { get; private set; } = null!;
    public string? LotNumber { get; private set; }
    public DateOnly? ExpirationDate { get; private set; }
    public decimal Amount { get; private set; }
    public int? ManufactureOrderId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTime? LastModifiedAt { get; private set; }
    public string? LastModifiedBy { get; private set; }

    public IReadOnlyList<ManufacturedProductInventoryLog> Log => _log;

    public ManufacturedProductInventoryItem(
        string productCode,
        string productName,
        decimal amount,
        string createdBy,
        DateTime createdAt,
        string? lotNumber = null,
        DateOnly? expirationDate = null,
        int? manufactureOrderId = null)
    {
        ProductCode = productCode;
        ProductName = productName;
        Amount = amount;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        LotNumber = lotNumber;
        ExpirationDate = expirationDate;
        ManufactureOrderId = manufactureOrderId;

        _log.Add(new ManufacturedProductInventoryLog(
            InventoryChangeType.InitialWriteDown,
            amountDelta: amount,
            amountAfter: amount,
            user: createdBy,
            timestamp: createdAt,
            referenceType: manufactureOrderId.HasValue ? "ManufactureOrder" : null,
            referenceId: manufactureOrderId?.ToString()));
    }

    private ManufacturedProductInventoryItem() { }

    public void Consume(decimal amount, string user, DateTime timestamp, int? transportBoxId = null)
    {
        if (amount > Amount)
            throw new InvalidOperationException(
                $"Insufficient manufactured inventory for {ProductCode}. Available: {Amount}, requested: {amount}.");

        Amount -= amount;
        LastModifiedAt = timestamp;
        LastModifiedBy = user;
        _log.Add(new ManufacturedProductInventoryLog(
            InventoryChangeType.ConsumedByTransportBox, -amount, Amount, user, timestamp,
            transportBoxId.HasValue ? "TransportBox" : null, transportBoxId?.ToString()));
    }

    public void Restore(decimal amount, string user, DateTime timestamp, int? transportBoxId = null)
    {
        Amount += amount;
        LastModifiedAt = timestamp;
        LastModifiedBy = user;
        _log.Add(new ManufacturedProductInventoryLog(
            InventoryChangeType.RestoredFromTransportBox, amount, Amount, user, timestamp,
            transportBoxId.HasValue ? "TransportBox" : null, transportBoxId?.ToString()));
    }

    public void ManualAdjust(decimal newAmount, string user, DateTime timestamp, string? note = null)
    {
        var delta = newAmount - Amount;
        Amount = newAmount;
        LastModifiedAt = timestamp;
        LastModifiedBy = user;
        _log.Add(new ManufacturedProductInventoryLog(
            InventoryChangeType.ManualAdjustment, delta, Amount, user, timestamp, note: note));
    }

    public void ManualRemove(string user, DateTime timestamp, string? note = null)
    {
        var delta = -Amount;
        Amount = 0;
        LastModifiedAt = timestamp;
        LastModifiedBy = user;
        _log.Add(new ManufacturedProductInventoryLog(
            InventoryChangeType.ManualRemoval, delta, 0m, user, timestamp, note: note));
    }
}
```

- [ ] **Step 4: Create IManufacturedProductInventoryRepository.cs**

```csharp
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Manufacture.Inventory;

public interface IManufacturedProductInventoryRepository : IRepository<ManufacturedProductInventoryItem, int>
{
    Task<ManufacturedProductInventoryItem?> GetByIdWithLogsAsync(int id, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<ManufacturedProductInventoryItem> Items, int TotalCount)> GetPagedListAsync(
        ManufacturedInventoryFilter filter, CancellationToken cancellationToken = default);
}

public class ManufacturedInventoryFilter
{
    public string? Search { get; set; }
    public bool OnlyWithStock { get; set; } = false;
    public int? ManufactureOrderId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
```

- [ ] **Step 5: Run domain tests — expect PASS**

```bash
cd backend && dotnet test --filter "FullyQualifiedName~ManufacturedProductInventoryItemTests"
```

Expected: 5 tests pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Manufacture/Inventory/ \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/Inventory/ManufacturedProductInventoryItemTests.cs
git commit -m "feat: add ManufacturedProductInventoryItem aggregate with domain methods"
```

---

## Task 3: Domain — Extend TransportBoxItem and TransportBox

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxItem.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBox.cs`

- [ ] **Step 1: Update TransportBoxItem.cs** — add three nullable fields and update constructor

Replace the existing file content with:

```csharp
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Logistics.Transport;

public class TransportBoxItem : Entity<int>
{
    public string ProductCode { get; private set; }
    public string ProductName { get; private set; }
    public double Amount { get; private set; }
    public DateTime DateAdded { get; private set; }
    public string UserAdded { get; private set; }
    public string? LotNumber { get; private set; }
    public DateOnly? ExpirationDate { get; private set; }
    public int? SourceInventoryId { get; private set; }

    public TransportBoxItem(
        string productCode,
        string productName,
        double amount,
        DateTime dateAdded,
        string userAdded,
        string? lotNumber = null,
        DateOnly? expirationDate = null,
        int? sourceInventoryId = null)
    {
        ProductCode = productCode;
        ProductName = productName;
        Amount = amount;
        DateAdded = dateAdded;
        UserAdded = userAdded;
        LotNumber = lotNumber;
        ExpirationDate = expirationDate;
        SourceInventoryId = sourceInventoryId;
    }
}
```

- [ ] **Step 2: Update TransportBox.AddItem in TransportBox.cs**

Find the existing `AddItem` method:
```csharp
public TransportBoxItem AddItem(string productCode, string productName, double amount, DateTime date, string userName)
{
    CheckState(TransportBoxState.Opened, TransportBoxState.Opened);
    var newItem = new TransportBoxItem(productCode, productName, amount, date, userName);
    _items.Add(newItem);
    return newItem;
}
```

Replace with:
```csharp
public TransportBoxItem AddItem(
    string productCode,
    string productName,
    double amount,
    DateTime date,
    string userName,
    string? lotNumber = null,
    DateOnly? expirationDate = null,
    int? sourceInventoryId = null)
{
    CheckState(TransportBoxState.Opened, TransportBoxState.Opened);
    var newItem = new TransportBoxItem(productCode, productName, amount, date, userName,
        lotNumber, expirationDate, sourceInventoryId);
    _items.Add(newItem);
    return newItem;
}
```

- [ ] **Step 3: Update TransportBox.Reset to return cleared items**

Find:
```csharp
public void Reset(DateTime date, string userName)
{
    // According to specification: Reset is allowed only from Opened state  
    _items.Clear();
    Code = null;
    ChangeState(TransportBoxState.New, date, userName, TransportBoxState.Opened);
}
```

Replace with:
```csharp
public IReadOnlyList<TransportBoxItem> Reset(DateTime date, string userName)
{
    var itemsBeforeClear = _items.ToList();
    _items.Clear();
    Code = null;
    ChangeState(TransportBoxState.New, date, userName, TransportBoxState.Opened);
    return itemsBeforeClear;
}
```

- [ ] **Step 4: Fix the static transition delegate that calls Reset** — the delegate in the static constructor ignores the return value, which is fine since it's `Action<TransportBox, DateTime, string>`. No change needed there.

- [ ] **Step 5: Build to verify no compilation errors**

```bash
cd backend && dotnet build --no-restore 2>&1 | grep -E "error|warning" | head -20
```

Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxItem.cs \
        backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBox.cs
git commit -m "feat: extend TransportBoxItem with lot/source fields; update AddItem + Reset"
```

---

## Task 4: Persistence — EF configurations, DbContext, migration, repository

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Manufacture/Inventory/ManufacturedProductInventoryItemConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Manufacture/Inventory/ManufacturedProductInventoryLogConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Manufacture/Inventory/ManufacturedProductInventoryRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/TransportBoxItemConfiguration.cs`

- [ ] **Step 1: Create ManufacturedProductInventoryItemConfiguration.cs**

```csharp
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Manufacture.Inventory;

public class ManufacturedProductInventoryItemConfiguration : IEntityTypeConfiguration<ManufacturedProductInventoryItem>
{
    public void Configure(EntityTypeBuilder<ManufacturedProductInventoryItem> builder)
    {
        builder.ToTable("ManufacturedProductInventoryItems", "public");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.ProductCode).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ProductName).HasMaxLength(500).IsRequired();
        builder.Property(x => x.LotNumber).HasMaxLength(100);
        builder.Property(x => x.Amount).HasColumnType("numeric(18,4)").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(500).IsRequired();
        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp without time zone").IsRequired();
        builder.Property(x => x.LastModifiedAt)
            .HasColumnType("timestamp without time zone");
        builder.Property(x => x.LastModifiedBy).HasMaxLength(500);

        builder.HasMany(x => x.Log)
            .WithOne()
            .HasForeignKey(l => l.InventoryItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.ProductCode)
            .HasDatabaseName("IX_ManufacturedProductInventoryItems_ProductCode");
        builder.HasIndex(new[] { "ProductCode", "LotNumber" })
            .HasDatabaseName("IX_ManufacturedProductInventoryItems_ProductCode_LotNumber");
        builder.HasIndex(x => x.ManufactureOrderId)
            .HasDatabaseName("IX_ManufacturedProductInventoryItems_ManufactureOrderId");
    }
}
```

- [ ] **Step 2: Create ManufacturedProductInventoryLogConfiguration.cs**

```csharp
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Manufacture.Inventory;

public class ManufacturedProductInventoryLogConfiguration : IEntityTypeConfiguration<ManufacturedProductInventoryLog>
{
    public void Configure(EntityTypeBuilder<ManufacturedProductInventoryLog> builder)
    {
        builder.ToTable("ManufacturedProductInventoryLogs", "public");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.ChangeType).HasConversion<int>().IsRequired();
        builder.Property(x => x.AmountDelta).HasColumnType("numeric(18,4)").IsRequired();
        builder.Property(x => x.AmountAfter).HasColumnType("numeric(18,4)").IsRequired();
        builder.Property(x => x.User).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ReferenceType).HasMaxLength(100);
        builder.Property(x => x.ReferenceId).HasMaxLength(100);
        builder.Property(x => x.Note).HasMaxLength(1000);
        builder.Property(x => x.Timestamp)
            .HasColumnType("timestamp without time zone").IsRequired();

        builder.HasIndex(x => x.InventoryItemId)
            .HasDatabaseName("IX_ManufacturedProductInventoryLogs_InventoryItemId");
    }
}
```

- [ ] **Step 3: Update TransportBoxItemConfiguration.cs** — add the three new nullable columns

In `Configure`, after the existing `builder.Property(x => x.DateAdded)...` block, add:

```csharp
builder.Property(x => x.LotNumber).HasMaxLength(100);
builder.Property(x => x.ExpirationDate);
builder.Property(x => x.SourceInventoryId);
```

- [ ] **Step 4: Update ApplicationDbContext.cs** — add two using statements and two DbSets

Add at top with other using statements:
```csharp
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
```

Add two DbSets in the "Manufacture Order Management module" region:
```csharp
// Manufactured Product Inventory module
public DbSet<ManufacturedProductInventoryItem> ManufacturedProductInventoryItems { get; set; } = null!;
public DbSet<ManufacturedProductInventoryLog> ManufacturedProductInventoryLogs { get; set; } = null!;
```

- [ ] **Step 5: Create ManufacturedProductInventoryRepository.cs**

```csharp
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Manufacture.Inventory;

public class ManufacturedProductInventoryRepository
    : BaseRepository<ManufacturedProductInventoryItem, int>, IManufacturedProductInventoryRepository
{
    public ManufacturedProductInventoryRepository(ApplicationDbContext context) : base(context) { }

    public async Task<ManufacturedProductInventoryItem?> GetByIdWithLogsAsync(
        int id, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(x => x.Log)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<(IReadOnlyList<ManufacturedProductInventoryItem> Items, int TotalCount)> GetPagedListAsync(
        ManufacturedInventoryFilter filter, CancellationToken cancellationToken = default)
    {
        var query = DbSet.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(x =>
                x.ProductCode.Contains(filter.Search) ||
                x.ProductName.Contains(filter.Search));

        if (filter.OnlyWithStock)
            query = query.Where(x => x.Amount > 0);

        if (filter.ManufactureOrderId.HasValue)
            query = query.Where(x => x.ManufactureOrderId == filter.ManufactureOrderId);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(x => x.ExpirationDate)
            .ThenBy(x => x.ProductCode)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
```

- [ ] **Step 6: Build to verify**

```bash
cd backend && dotnet build 2>&1 | grep -E "^.*error" | head -10
```

Expected: 0 errors.

- [ ] **Step 7: Create the migration**

```bash
cd backend/src/Anela.Heblo.Persistence && \
  dotnet ef migrations add AddManufacturedProductInventory \
  --startup-project ../Anela.Heblo.API \
  --output-dir Migrations
```

Expected: New migration file created in `Migrations/`.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/ backend/src/Anela.Heblo.Domain/
git commit -m "feat: add EF configuration, repository, and migration for ManufacturedProductInventory"
```

---

## Task 5: Application — DTOs, mapping profile, ErrorCodes, ManufactureModule

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/ManufacturedProductInventoryItemDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/ManufacturedProductInventoryLogDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufacturedProductInventoryMappingProfile.cs`
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs`

- [ ] **Step 1: Create ManufacturedProductInventoryItemDto.cs**

```csharp
namespace Anela.Heblo.Application.Features.Manufacture.Contracts;

public class ManufacturedProductInventoryItemDto
{
    public int Id { get; set; }
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public string? LotNumber { get; set; }
    public DateOnly? ExpirationDate { get; set; }
    public decimal Amount { get; set; }
    public int? ManufactureOrderId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
    public DateTime? LastModifiedAt { get; set; }
    public string? LastModifiedBy { get; set; }
    public List<ManufacturedProductInventoryLogDto> Log { get; set; } = new();
}
```

- [ ] **Step 2: Create ManufacturedProductInventoryLogDto.cs**

```csharp
namespace Anela.Heblo.Application.Features.Manufacture.Contracts;

public class ManufacturedProductInventoryLogDto
{
    public int Id { get; set; }
    public string ChangeType { get; set; } = null!;
    public decimal AmountDelta { get; set; }
    public decimal AmountAfter { get; set; }
    public string? ReferenceType { get; set; }
    public string? ReferenceId { get; set; }
    public string? Note { get; set; }
    public DateTime Timestamp { get; set; }
    public string User { get; set; } = null!;
}
```

- [ ] **Step 3: Create ManufacturedProductInventoryMappingProfile.cs**

```csharp
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using AutoMapper;

namespace Anela.Heblo.Application.Features.Manufacture;

public class ManufacturedProductInventoryMappingProfile : Profile
{
    public ManufacturedProductInventoryMappingProfile()
    {
        CreateMap<ManufacturedProductInventoryItem, ManufacturedProductInventoryItemDto>()
            .ForMember(d => d.Log, o => o.MapFrom(s => s.Log));

        CreateMap<ManufacturedProductInventoryLog, ManufacturedProductInventoryLogDto>()
            .ForMember(d => d.ChangeType, o => o.MapFrom(s => s.ChangeType.ToString()));
    }
}
```

- [ ] **Step 4: Add ErrorCodes 1215 and 1216 to ErrorCodes.cs**

After `InvalidScheduleDateOrder = 1214,` add:

```csharp
[HttpStatusCode(HttpStatusCode.NotFound)]
ManufacturedInventoryItemNotFound = 1215,
[HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
ManufacturedInventoryInsufficientStock = 1216,
```

- [ ] **Step 5: Update ManufactureModule.cs** — register repository

Add at the top of the usings:
```csharp
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Persistence.Manufacture.Inventory;
```

In `AddManufactureModule`, after `services.AddScoped<IManufactureOrderRepository, ManufactureOrderRepository>();` add:
```csharp
services.AddScoped<IManufacturedProductInventoryRepository, ManufacturedProductInventoryRepository>();
```

- [ ] **Step 6: Build**

```bash
cd backend && dotnet build 2>&1 | grep -E "^.*error" | head -10
```

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/ \
        backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs
git commit -m "feat: add inventory DTOs, mapping profile, error codes, and module registration"
```

---

## Task 6: Application — GetManufacturedInventory use case

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufacturedInventory/GetManufacturedInventoryRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufacturedInventory/GetManufacturedInventoryResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufacturedInventory/GetManufacturedInventoryHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Inventory/GetManufacturedInventoryHandlerTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufacturedInventory;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using AutoMapper;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.Manufacture.Inventory;

public class GetManufacturedInventoryHandlerTests
{
    private readonly Mock<IManufacturedProductInventoryRepository> _repoMock = new();
    private readonly IMapper _mapper;

    public GetManufacturedInventoryHandlerTests()
    {
        var config = new MapperConfiguration(cfg =>
            cfg.AddProfile<ManufacturedProductInventoryMappingProfile>());
        _mapper = config.CreateMapper();
    }

    [Fact]
    public async Task Handle_ReturnsPagedItems()
    {
        var items = new List<ManufacturedProductInventoryItem>
        {
            new("PROD-001", "Product One", 10m, "user", DateTime.UtcNow)
        };
        _repoMock.Setup(r => r.GetPagedListAsync(It.IsAny<ManufacturedInventoryFilter>(), default))
            .ReturnsAsync((items, 1));

        var handler = new GetManufacturedInventoryHandler(_repoMock.Object, _mapper);
        var response = await handler.Handle(new GetManufacturedInventoryRequest(), default);

        response.Success.Should().BeTrue();
        response.Items.Should().HaveCount(1);
        response.Items[0].ProductCode.Should().Be("PROD-001");
        response.TotalCount.Should().Be(1);
    }
}
```

- [ ] **Step 2: Run test — expect compilation failure**

```bash
cd backend && dotnet build 2>&1 | grep error | head -5
```

- [ ] **Step 3: Create GetManufacturedInventoryRequest.cs**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufacturedInventory;

public class GetManufacturedInventoryRequest : IRequest<GetManufacturedInventoryResponse>
{
    public string? Search { get; set; }
    public bool OnlyWithStock { get; set; } = false;
    public int? ManufactureOrderId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
```

- [ ] **Step 4: Create GetManufacturedInventoryResponse.cs**

```csharp
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufacturedInventory;

public class GetManufacturedInventoryResponse : BaseResponse
{
    public List<ManufacturedProductInventoryItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
```

- [ ] **Step 5: Create GetManufacturedInventoryHandler.cs**

```csharp
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using AutoMapper;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufacturedInventory;

public class GetManufacturedInventoryHandler
    : IRequestHandler<GetManufacturedInventoryRequest, GetManufacturedInventoryResponse>
{
    private readonly IManufacturedProductInventoryRepository _repository;
    private readonly IMapper _mapper;

    public GetManufacturedInventoryHandler(
        IManufacturedProductInventoryRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<GetManufacturedInventoryResponse> Handle(
        GetManufacturedInventoryRequest request, CancellationToken cancellationToken)
    {
        var filter = new ManufacturedInventoryFilter
        {
            Search = request.Search,
            OnlyWithStock = request.OnlyWithStock,
            ManufactureOrderId = request.ManufactureOrderId,
            Page = request.Page,
            PageSize = request.PageSize
        };

        var (items, totalCount) = await _repository.GetPagedListAsync(filter, cancellationToken);

        return new GetManufacturedInventoryResponse
        {
            Items = _mapper.Map<List<ManufacturedProductInventoryItemDto>>(items),
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }
}
```

- [ ] **Step 6: Run test — expect PASS**

```bash
cd backend && dotnet test --filter "FullyQualifiedName~GetManufacturedInventoryHandlerTests"
```

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufacturedInventory/ \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/Inventory/GetManufacturedInventoryHandlerTests.cs
git commit -m "feat: add GetManufacturedInventory use case"
```

---

## Task 7: Application — Create/Update/Delete use cases

**Files:**
- Create: `UseCases/CreateManufacturedInventoryItem/{Request,Response,Handler}.cs`
- Create: `UseCases/UpdateManufacturedInventoryItem/{Request,Response,Handler}.cs`
- Create: `UseCases/DeleteManufacturedInventoryItem/{Request,Response,Handler}.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Inventory/CrudManufacturedInventoryHandlerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using Anela.Heblo.Application.Features.Manufacture.UseCases.CreateManufacturedInventoryItem;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufacturedInventoryItem;
using Anela.Heblo.Application.Features.Manufacture.UseCases.DeleteManufacturedInventoryItem;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Domain.Features.Users;
using AutoMapper;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.Manufacture.Inventory;

public class CrudManufacturedInventoryHandlerTests
{
    private readonly Mock<IManufacturedProductInventoryRepository> _repoMock = new();
    private readonly Mock<ICurrentUserService> _userMock = new();
    private readonly IMapper _mapper;
    private readonly TimeProvider _time = TimeProvider.System;

    public CrudManufacturedInventoryHandlerTests()
    {
        _userMock.Setup(u => u.GetCurrentUser()).Returns(new CurrentUser { Name = "testuser" });
        var config = new MapperConfiguration(cfg =>
            cfg.AddProfile<ManufacturedProductInventoryMappingProfile>());
        _mapper = config.CreateMapper();
    }

    [Fact]
    public async Task Create_ReturnsSuccessWithDto()
    {
        _repoMock.Setup(r => r.AddAsync(It.IsAny<ManufacturedProductInventoryItem>(), default))
            .ReturnsAsync((ManufacturedProductInventoryItem item, CancellationToken _) => item);

        var handler = new CreateManufacturedInventoryItemHandler(_repoMock.Object, _userMock.Object, _mapper, _time);
        var response = await handler.Handle(new CreateManufacturedInventoryItemRequest
        {
            ProductCode = "P-001", ProductName = "Prod", Amount = 5m
        }, default);

        response.Success.Should().BeTrue();
        response.Item!.ProductCode.Should().Be("P-001");
        _repoMock.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Update_ItemNotFound_ReturnsNotFound()
    {
        _repoMock.Setup(r => r.GetByIdWithLogsAsync(99, default))
            .ReturnsAsync((ManufacturedProductInventoryItem?)null);

        var handler = new UpdateManufacturedInventoryItemHandler(_repoMock.Object, _userMock.Object, _time);
        var response = await handler.Handle(
            new UpdateManufacturedInventoryItemRequest { Id = 99, NewAmount = 5m }, default);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ManufacturedInventoryItemNotFound);
    }

    [Fact]
    public async Task Update_CallsManualAdjustAndSaves()
    {
        var item = new ManufacturedProductInventoryItem("P-001", "Prod", 10m, "u",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _repoMock.Setup(r => r.GetByIdWithLogsAsync(1, default)).ReturnsAsync(item);

        var handler = new UpdateManufacturedInventoryItemHandler(_repoMock.Object, _userMock.Object, _time);
        var response = await handler.Handle(
            new UpdateManufacturedInventoryItemRequest { Id = 1, NewAmount = 20m, Note = "found more" }, default);

        response.Success.Should().BeTrue();
        item.Amount.Should().Be(20m);
        _repoMock.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Delete_CallsManualRemoveAndSaves()
    {
        var item = new ManufacturedProductInventoryItem("P-001", "Prod", 10m, "u",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _repoMock.Setup(r => r.GetByIdWithLogsAsync(1, default)).ReturnsAsync(item);

        var handler = new DeleteManufacturedInventoryItemHandler(_repoMock.Object, _userMock.Object, _time);
        var response = await handler.Handle(
            new DeleteManufacturedInventoryItemRequest { Id = 1 }, default);

        response.Success.Should().BeTrue();
        item.Amount.Should().Be(0m);
        _repoMock.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }
}
```

- [ ] **Step 2: Create CreateManufacturedInventoryItem files**

`CreateManufacturedInventoryItemRequest.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CreateManufacturedInventoryItem;

public class CreateManufacturedInventoryItemRequest : IRequest<CreateManufacturedInventoryItemResponse>
{
    [Required] public string ProductCode { get; set; } = null!;
    [Required] public string ProductName { get; set; } = null!;
    [Range(0.0001, double.MaxValue)] public decimal Amount { get; set; }
    public string? LotNumber { get; set; }
    public DateOnly? ExpirationDate { get; set; }
    public string? Note { get; set; }
}
```

`CreateManufacturedInventoryItemResponse.cs`:
```csharp
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CreateManufacturedInventoryItem;

public class CreateManufacturedInventoryItemResponse : BaseResponse
{
    public ManufacturedProductInventoryItemDto? Item { get; set; }
}
```

`CreateManufacturedInventoryItemHandler.cs`:
```csharp
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Domain.Features.Users;
using AutoMapper;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CreateManufacturedInventoryItem;

public class CreateManufacturedInventoryItemHandler
    : IRequestHandler<CreateManufacturedInventoryItemRequest, CreateManufacturedInventoryItemResponse>
{
    private readonly IManufacturedProductInventoryRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMapper _mapper;
    private readonly TimeProvider _timeProvider;

    public CreateManufacturedInventoryItemHandler(
        IManufacturedProductInventoryRepository repository,
        ICurrentUserService currentUserService,
        IMapper mapper,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _mapper = mapper;
        _timeProvider = timeProvider;
    }

    public async Task<CreateManufacturedInventoryItemResponse> Handle(
        CreateManufacturedInventoryItemRequest request, CancellationToken cancellationToken)
    {
        var user = _currentUserService.GetCurrentUser().Name;
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var item = new ManufacturedProductInventoryItem(
            request.ProductCode, request.ProductName, request.Amount,
            user, now, request.LotNumber, request.ExpirationDate);

        await _repository.AddAsync(item, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return new CreateManufacturedInventoryItemResponse
        {
            Item = _mapper.Map<ManufacturedProductInventoryItemDto>(item)
        };
    }
}
```

- [ ] **Step 3: Create UpdateManufacturedInventoryItem files**

`UpdateManufacturedInventoryItemRequest.cs`:
```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufacturedInventoryItem;

public class UpdateManufacturedInventoryItemRequest : IRequest<UpdateManufacturedInventoryItemResponse>
{
    public int Id { get; set; }
    public decimal NewAmount { get; set; }
    public string? Note { get; set; }
}
```

`UpdateManufacturedInventoryItemResponse.cs`:
```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufacturedInventoryItem;

public class UpdateManufacturedInventoryItemResponse : BaseResponse
{
    public UpdateManufacturedInventoryItemResponse() : base() { }
    public UpdateManufacturedInventoryItemResponse(ErrorCodes code, Dictionary<string, string>? p = null) : base(code, p) { }
}
```

`UpdateManufacturedInventoryItemHandler.cs`:
```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufacturedInventoryItem;

public class UpdateManufacturedInventoryItemHandler
    : IRequestHandler<UpdateManufacturedInventoryItemRequest, UpdateManufacturedInventoryItemResponse>
{
    private readonly IManufacturedProductInventoryRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;

    public UpdateManufacturedInventoryItemHandler(
        IManufacturedProductInventoryRepository repository,
        ICurrentUserService currentUserService,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
    }

    public async Task<UpdateManufacturedInventoryItemResponse> Handle(
        UpdateManufacturedInventoryItemRequest request, CancellationToken cancellationToken)
    {
        var item = await _repository.GetByIdWithLogsAsync(request.Id, cancellationToken);
        if (item == null)
            return new UpdateManufacturedInventoryItemResponse(
                ErrorCodes.ManufacturedInventoryItemNotFound,
                new Dictionary<string, string> { { "id", request.Id.ToString() } });

        var user = _currentUserService.GetCurrentUser().Name;
        item.ManualAdjust(request.NewAmount, user, _timeProvider.GetUtcNow().UtcDateTime, request.Note);

        await _repository.SaveChangesAsync(cancellationToken);
        return new UpdateManufacturedInventoryItemResponse();
    }
}
```

- [ ] **Step 4: Create DeleteManufacturedInventoryItem files**

`DeleteManufacturedInventoryItemRequest.cs`:
```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.DeleteManufacturedInventoryItem;

public class DeleteManufacturedInventoryItemRequest : IRequest<DeleteManufacturedInventoryItemResponse>
{
    public int Id { get; set; }
    public string? Note { get; set; }
}
```

`DeleteManufacturedInventoryItemResponse.cs`:
```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.DeleteManufacturedInventoryItem;

public class DeleteManufacturedInventoryItemResponse : BaseResponse
{
    public DeleteManufacturedInventoryItemResponse() : base() { }
    public DeleteManufacturedInventoryItemResponse(ErrorCodes code, Dictionary<string, string>? p = null) : base(code, p) { }
}
```

`DeleteManufacturedInventoryItemHandler.cs`:
```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.DeleteManufacturedInventoryItem;

public class DeleteManufacturedInventoryItemHandler
    : IRequestHandler<DeleteManufacturedInventoryItemRequest, DeleteManufacturedInventoryItemResponse>
{
    private readonly IManufacturedProductInventoryRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;

    public DeleteManufacturedInventoryItemHandler(
        IManufacturedProductInventoryRepository repository,
        ICurrentUserService currentUserService,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
    }

    public async Task<DeleteManufacturedInventoryItemResponse> Handle(
        DeleteManufacturedInventoryItemRequest request, CancellationToken cancellationToken)
    {
        var item = await _repository.GetByIdWithLogsAsync(request.Id, cancellationToken);
        if (item == null)
            return new DeleteManufacturedInventoryItemResponse(
                ErrorCodes.ManufacturedInventoryItemNotFound,
                new Dictionary<string, string> { { "id", request.Id.ToString() } });

        var user = _currentUserService.GetCurrentUser().Name;
        item.ManualRemove(user, _timeProvider.GetUtcNow().UtcDateTime, request.Note);

        await _repository.SaveChangesAsync(cancellationToken);
        return new DeleteManufacturedInventoryItemResponse();
    }
}
```

- [ ] **Step 5: Run all inventory handler tests**

```bash
cd backend && dotnet test --filter "FullyQualifiedName~Inventory"
```

Expected: 9 tests pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/ \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/Inventory/
git commit -m "feat: add Create/Update/Delete manufactured inventory use cases"
```

---

## Task 8: API controller

**Files:**
- Create: `backend/src/Anela.Heblo.API/Controllers/ManufacturedProductInventoryController.cs`

- [ ] **Step 1: Create the controller**

```csharp
using Anela.Heblo.Application.Features.Manufacture.UseCases.CreateManufacturedInventoryItem;
using Anela.Heblo.Application.Features.Manufacture.UseCases.DeleteManufacturedInventoryItem;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufacturedInventory;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufacturedInventoryItem;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/manufactured-product-inventory")]
public class ManufacturedProductInventoryController : BaseApiController
{
    private readonly IMediator _mediator;

    public ManufacturedProductInventoryController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] string? search,
        [FromQuery] bool onlyWithStock = false,
        [FromQuery] int? manufactureOrderId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var response = await _mediator.Send(new GetManufacturedInventoryRequest
        {
            Search = search,
            OnlyWithStock = onlyWithStock,
            ManufactureOrderId = manufactureOrderId,
            Page = page,
            PageSize = pageSize
        });
        return HandleResponse(response);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateManufacturedInventoryItemRequest request)
    {
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateManufacturedInventoryItemRequest request)
    {
        request.Id = id;
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromQuery] string? note = null)
    {
        var response = await _mediator.Send(new DeleteManufacturedInventoryItemRequest { Id = id, Note = note });
        return HandleResponse(response);
    }
}
```

- [ ] **Step 2: Build**

```bash
cd backend && dotnet build 2>&1 | grep -E "^.*error" | head -10
```

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/ManufacturedProductInventoryController.cs
git commit -m "feat: add ManufacturedProductInventoryController"
```

---

## Task 9: Write-down on manufacture order completion

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderStatusHandlerTests.cs`

- [ ] **Step 1: Add failing test** — open the existing test file and append:

Find the test file at `backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderStatusHandlerTests.cs`. At the bottom of the class, add:

```csharp
[Fact]
public async Task Handle_TransitionToCompleted_WritesDownInventoryForEachProduct()
{
    // Read this test file to understand the existing mock setup pattern, then write:
    // Arrange: create an order with State = Planned, two ManufactureOrderProducts with ActualQuantity > 0
    // Mock _repository.GetOrderByIdAsync to return it
    // Mock _inventoryRepository.AddAsync and SaveChangesAsync
    // Act: send request with NewState = ManufactureOrderState.Completed
    // Assert: _inventoryRepository.Verify(r => r.AddAsync(...), Times.Exactly(2))
    //         _inventoryRepository.Verify(r => r.SaveChangesAsync(...), Times.Once)
}
```

**Important:** Before writing the full test, read the existing test file to match its mock setup pattern. The test class already has mocked dependencies — you need to add `Mock<IManufacturedProductInventoryRepository>` and inject it.

- [ ] **Step 2: Modify UpdateManufactureOrderStatusHandler.cs**

Add constructor parameter for `IManufacturedProductInventoryRepository`:
```csharp
private readonly IManufacturedProductInventoryRepository _inventoryRepository;
```

Add to constructor:
```csharp
IManufacturedProductInventoryRepository inventoryRepository
// add to parameter list and assignment
_inventoryRepository = inventoryRepository;
```

In `Handle`, after `await _repository.UpdateOrderAsync(order, cancellationToken);`, add:

```csharp
if (request.NewState == ManufactureOrderState.Completed && oldState != ManufactureOrderState.Completed)
{
    await WriteDownManufacturedProductsAsync(order, cancellationToken);
}
```

Add the private method:
```csharp
private async Task WriteDownManufacturedProductsAsync(ManufactureOrder order, CancellationToken cancellationToken)
{
    var userName = GetCurrentUserName();
    var now = _timeProvider.GetUtcNow().UtcDateTime;

    foreach (var product in order.Products.Where(p => p.ActualQuantity > 0))
    {
        var item = new ManufacturedProductInventoryItem(
            product.ProductCode,
            product.ProductName,
            product.ActualQuantity!.Value,
            userName,
            now,
            lotNumber: product.LotNumber,
            expirationDate: product.ExpirationDate,
            manufactureOrderId: order.Id);

        await _inventoryRepository.AddAsync(item, cancellationToken);
    }

    await _inventoryRepository.SaveChangesAsync(cancellationToken);
}
```

Add the required using:
```csharp
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
```

- [ ] **Step 3: Run tests**

```bash
cd backend && dotnet test --filter "FullyQualifiedName~UpdateManufactureOrderStatusHandler"
```

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/ \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/
git commit -m "feat: write down manufactured products inventory on order completion"
```

---

## Task 10: Inventory consumption in AddItemToBox + restore in RemoveItemFromBox

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/AddItemToBox/AddItemToBoxRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/AddItemToBox/AddItemToBoxHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/RemoveItemFromBox/RemoveItemFromBoxHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/TransportBoxItemDto.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/AddItemToBoxHandlerTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/RemoveItemFromBoxHandlerTests.cs`

- [ ] **Step 1: Update AddItemToBoxRequest.cs** — add optional inventory fields

After the `Amount` property add:
```csharp
public int? SourceInventoryId { get; set; }
public string? LotNumber { get; set; }
public DateOnly? ExpirationDate { get; set; }
```

- [ ] **Step 2: Update TransportBoxItemDto.cs** — add lot fields

Find `TransportBoxItemDto.cs` in `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/`. Add after existing properties:
```csharp
public string? LotNumber { get; set; }
public DateOnly? ExpirationDate { get; set; }
public int? SourceInventoryId { get; set; }
```

Update the AutoMapper profile (`TransportBoxMappingProfile.cs`) to map those fields if not auto-mapped.

- [ ] **Step 3: Write failing tests for AddItemToBox**

```csharp
using Anela.Heblo.Application.Features.Logistics.UseCases.AddItemToBox;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Domain.Features.Users;
using AutoMapper;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class AddItemToBoxHandlerTests
{
    private readonly Mock<ITransportBoxRepository> _boxRepoMock = new();
    private readonly Mock<IManufacturedProductInventoryRepository> _inventoryRepoMock = new();
    private readonly Mock<ICurrentUserService> _userMock = new();
    private readonly Mock<IMapper> _mapperMock = new();
    private readonly Mock<ILogger<AddItemToBoxHandler>> _loggerMock = new();

    public AddItemToBoxHandlerTests()
    {
        _userMock.Setup(u => u.GetCurrentUser()).Returns(new CurrentUser { Name = "user1" });
    }

    private TransportBox CreateOpenBox()
    {
        var box = new TransportBox();
        box.AssignBoxCodeIfAny("B001");
        // Transition to Opened state (reflection or use the Open method)
        // Use the state machine: box must be opened
        // For testing, use the public Open method:
        box.Open("B001", DateTime.UtcNow, "setup");
        return box;
    }

    [Fact]
    public async Task Handle_WithSourceInventoryId_ConsumesInventory()
    {
        var box = CreateOpenBox();
        _boxRepoMock.Setup(r => r.GetByIdWithDetailsAsync(1)).ReturnsAsync(box);

        var inventoryItem = new ManufacturedProductInventoryItem(
            "P-001", "Prod", 10m, "u", DateTime.UtcNow, lotNumber: "LOT-1");
        _inventoryRepoMock.Setup(r => r.GetByIdAsync(5, default)).ReturnsAsync(inventoryItem);

        var handler = new AddItemToBoxHandler(
            _boxRepoMock.Object, _inventoryRepoMock.Object,
            _userMock.Object, _loggerMock.Object, _mapperMock.Object, TimeProvider.System);

        var response = await handler.Handle(new AddItemToBoxRequest
        {
            BoxId = 1, ProductCode = "P-001", ProductName = "Prod",
            Amount = 3.0, SourceInventoryId = 5, LotNumber = "LOT-1"
        }, default);

        response.Success.Should().BeTrue();
        inventoryItem.Amount.Should().Be(7m);
        _inventoryRepoMock.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_WithSourceInventoryId_InsufficientStock_ReturnsError()
    {
        var box = CreateOpenBox();
        _boxRepoMock.Setup(r => r.GetByIdWithDetailsAsync(1)).ReturnsAsync(box);

        var inventoryItem = new ManufacturedProductInventoryItem(
            "P-001", "Prod", 2m, "u", DateTime.UtcNow);
        _inventoryRepoMock.Setup(r => r.GetByIdAsync(5, default)).ReturnsAsync(inventoryItem);

        var handler = new AddItemToBoxHandler(
            _boxRepoMock.Object, _inventoryRepoMock.Object,
            _userMock.Object, _loggerMock.Object, _mapperMock.Object, TimeProvider.System);

        var response = await handler.Handle(new AddItemToBoxRequest
        {
            BoxId = 1, ProductCode = "P-001", ProductName = "Prod",
            Amount = 5.0, SourceInventoryId = 5
        }, default);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ManufacturedInventoryInsufficientStock);
    }

    [Fact]
    public async Task Handle_WithoutSourceInventoryId_DoesNotTouchInventory()
    {
        var box = CreateOpenBox();
        _boxRepoMock.Setup(r => r.GetByIdWithDetailsAsync(1)).ReturnsAsync(box);

        var handler = new AddItemToBoxHandler(
            _boxRepoMock.Object, _inventoryRepoMock.Object,
            _userMock.Object, _loggerMock.Object, _mapperMock.Object, TimeProvider.System);

        var response = await handler.Handle(new AddItemToBoxRequest
        {
            BoxId = 1, ProductCode = "P-001", ProductName = "Prod", Amount = 3.0
        }, default);

        response.Success.Should().BeTrue();
        _inventoryRepoMock.Verify(r => r.GetByIdAsync(It.IsAny<int>(), default), Times.Never);
    }
}
```

- [ ] **Step 4: Update AddItemToBoxHandler.cs** — add inventory dependency and consumption logic

Add `IManufacturedProductInventoryRepository` to constructor parameters. Add to constructor body:
```csharp
private readonly IManufacturedProductInventoryRepository _inventoryRepository;
// in constructor: _inventoryRepository = inventoryRepository;
```

In `Handle`, after retrieving the box and before calling `box.AddItem(...)`, add:

```csharp
if (request.SourceInventoryId.HasValue)
{
    var inventoryItem = await _inventoryRepository.GetByIdAsync(
        request.SourceInventoryId.Value, cancellationToken);

    if (inventoryItem == null)
        return new AddItemToBoxResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.ManufacturedInventoryItemNotFound,
            Params = new Dictionary<string, string> { { "id", request.SourceInventoryId.Value.ToString() } }
        };

    try
    {
        inventoryItem.Consume((decimal)request.Amount, userName,
            _timeProvider.GetUtcNow().UtcDateTime, transportBoxId: request.BoxId);
    }
    catch (InvalidOperationException)
    {
        return new AddItemToBoxResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.ManufacturedInventoryInsufficientStock,
            Params = new Dictionary<string, string> { { "available", inventoryItem.Amount.ToString() } }
        };
    }

    await _inventoryRepository.SaveChangesAsync(cancellationToken);
}
```

Update the `box.AddItem(...)` call to pass lot fields:
```csharp
var addedItem = transportBox.AddItem(
    request.ProductCode,
    request.ProductName,
    request.Amount,
    _timeProvider.GetUtcNow().UtcDateTime,
    userName,
    lotNumber: request.LotNumber,
    expirationDate: request.ExpirationDate,
    sourceInventoryId: request.SourceInventoryId);
```

- [ ] **Step 5: Update RemoveItemFromBoxHandler.cs** — add inventory restore

Add `IManufacturedProductInventoryRepository` to the handler's constructor (same pattern as above).

In `Handle`, after `var removedItem = transportBox.DeleteItem(request.ItemId);` and before `SaveChangesAsync`, add:

```csharp
if (removedItem?.SourceInventoryId.HasValue == true)
{
    var inventoryItem = await _inventoryRepository.GetByIdAsync(
        removedItem.SourceInventoryId.Value, cancellationToken);

    if (inventoryItem != null)
    {
        inventoryItem.Restore((decimal)removedItem.Amount, userName,
            _timeProvider.GetUtcNow().UtcDateTime, transportBoxId: request.BoxId);
        await _inventoryRepository.SaveChangesAsync(cancellationToken);
    }
}
```

Add `TimeProvider _timeProvider` to the constructor (it wasn't there before — add it).

- [ ] **Step 6: Write RemoveItemFromBoxHandler tests** — create the test file with two tests:
  - `Handle_ItemWithSourceInventoryId_RestoresInventory`
  - `Handle_ItemWithoutSourceInventoryId_DoesNotTouchInventory`

  Follow the same pattern as AddItemToBoxHandlerTests above.

- [ ] **Step 7: Run Logistics tests**

```bash
cd backend && dotnet test --filter "FullyQualifiedName~AddItemToBoxHandlerTests|FullyQualifiedName~RemoveItemFromBoxHandlerTests"
```

Expected: all pass.

- [ ] **Step 8: Handle the Reset case** — read `ChangeTransportBoxStateHandler.cs` (located at `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`). Find where `box.Reset(...)` is called (or where the `Opened → New` transition is triggered). Before the transition, collect items with `SourceInventoryId`, then after the reset call, restore those items against the inventory repository. Add `IManufacturedProductInventoryRepository` to that handler's constructor.

- [ ] **Step 9: Build all**

```bash
cd backend && dotnet build && dotnet test
```

Expected: all tests pass.

- [ ] **Step 10: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/ \
        backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/
git commit -m "feat: consume inventory on AddItemToBox; restore on RemoveItemFromBox and Reset"
```

---

## Task 11: Frontend — hook and page

**Files:**
- Create: `frontend/src/api/hooks/useManufacturedProductInventory.ts`
- Create: `frontend/src/components/manufacture/pages/ManufacturedProductInventoryPage.tsx`
- Modify: `frontend/src/App.tsx`
- Modify: `frontend/src/components/Layout/Sidebar.tsx`

- [ ] **Step 1: Regenerate API client after running the backend build**

After `dotnet build` completes (Task 10), the TypeScript client auto-regenerates. The new `ManufacturedProductInventoryController` endpoints will appear in the generated client.

- [ ] **Step 2: Create useManufacturedProductInventory.ts**

```typescript
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "../client";

const QUERY_KEY = "manufactured-product-inventory";

export interface ManufacturedInventoryFilter {
  search?: string;
  onlyWithStock?: boolean;
  manufactureOrderId?: number;
  page?: number;
  pageSize?: number;
}

export function useManufacturedInventoryQuery(filter: ManufacturedInventoryFilter = {}) {
  return useQuery({
    queryKey: [QUERY_KEY, filter],
    queryFn: async () => {
      const params = new URLSearchParams();
      if (filter.search) params.set("search", filter.search);
      if (filter.onlyWithStock) params.set("onlyWithStock", "true");
      if (filter.manufactureOrderId) params.set("manufactureOrderId", String(filter.manufactureOrderId));
      if (filter.page) params.set("page", String(filter.page));
      if (filter.pageSize) params.set("pageSize", String(filter.pageSize));

      const url = `${apiClient.baseUrl}/api/manufactured-product-inventory?${params}`;
      const response = await apiClient.get(url);
      return response.data;
    },
  });
}

export function useUpdateManufacturedInventoryItem() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, newAmount, note }: { id: number; newAmount: number; note?: string }) => {
      const url = `${apiClient.baseUrl}/api/manufactured-product-inventory/${id}`;
      const response = await apiClient.put(url, { newAmount, note });
      return response.data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: [QUERY_KEY] }),
  });
}

export function useDeleteManufacturedInventoryItem() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, note }: { id: number; note?: string }) => {
      const params = note ? `?note=${encodeURIComponent(note)}` : "";
      const url = `${apiClient.baseUrl}/api/manufactured-product-inventory/${id}${params}`;
      const response = await apiClient.delete(url);
      return response.data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: [QUERY_KEY] }),
  });
}

export function useCreateManufacturedInventoryItem() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: {
      productCode: string;
      productName: string;
      amount: number;
      lotNumber?: string;
      expirationDate?: string;
      note?: string;
    }) => {
      const url = `${apiClient.baseUrl}/api/manufactured-product-inventory`;
      const response = await apiClient.post(url, data);
      return response.data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: [QUERY_KEY] }),
  });
}
```

**Note:** Check how other hooks call `apiClient` (GET/PUT/DELETE/POST) by looking at an existing hook like `usePackingMaterials.ts` or `useGiftPackageManufacturing.ts` and match the exact pattern used for HTTP calls.

- [ ] **Step 3: Create ManufacturedProductInventoryPage.tsx**

Create a page component at `frontend/src/components/manufacture/pages/ManufacturedProductInventoryPage.tsx` with:
- A search input wired to `useManufacturedInventoryQuery({ search, onlyWithStock: true })`
- A table with columns: Product Code, Product Name, Lot, Expiration, Amount, Actions
- Edit button → inline input to change amount → calls `useUpdateManufacturedInventoryItem`
- Delete button → confirmation → calls `useDeleteManufacturedInventoryItem`
- "Přidat ručně" button → modal form with fields (ProductCode, ProductName, Amount, LotNumber, ExpirationDate) → calls `useCreateManufacturedInventoryItem`
- Expandable row showing `item.log` entries (ChangeType, AmountDelta, User, Timestamp, Note)

Use `ManufactureInventoryList.tsx` at `frontend/src/components/pages/ManufactureInventoryList.tsx` as a style reference for the table layout.

- [ ] **Step 4: Add route to App.tsx**

Add import near other manufacture imports:
```tsx
import ManufacturedProductInventoryPage from "./components/manufacture/pages/ManufacturedProductInventoryPage";
```

Add route after `<Route path="/manufacturing/orders/:id" ...`:
```tsx
<Route path="/manufacturing/product-inventory" element={<ManufacturedProductInventoryPage />} />
```

- [ ] **Step 5: Add Sidebar entry**

In `Sidebar.tsx`, in the `vyroba` section items array, add after the `vyrobni-zakazky` entry:
```typescript
{
  id: "sklad-vyroba",
  name: "Sklad výroby",
  href: "/manufacturing/product-inventory",
},
```

- [ ] **Step 6: Run frontend build**

```bash
cd frontend && npm run build 2>&1 | tail -20
```

Expected: 0 errors.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/api/hooks/useManufacturedProductInventory.ts \
        frontend/src/components/manufacture/pages/ManufacturedProductInventoryPage.tsx \
        frontend/src/App.tsx \
        frontend/src/components/Layout/Sidebar.tsx
git commit -m "feat: add ManufacturedProductInventory page, hook, route, and sidebar link"
```

---

## Task 12: Frontend — Transport box Manufactured tab

**Files:**
- Modify: `frontend/src/components/transport/box-detail/TransportBoxItems.tsx`
- Modify: `frontend/src/api/hooks/useTransportBoxes.ts`

- [ ] **Step 1: Update useAddItemToBox mutation in useTransportBoxes.ts**

Find the `useAddItemToBox` mutation. Update the parameter type to include optional lot fields:
```typescript
interface AddItemParams {
  boxId: number;
  productCode: string;
  productName: string;
  amount: number;
  sourceInventoryId?: number;
  lotNumber?: string;
  expirationDate?: string; // ISO date string "YYYY-MM-DD"
}
```

The mutation body should pass these fields in the POST request body.

- [ ] **Step 2: Update TransportBoxItems.tsx** — add Manufactured tab

At the top of the component (inside the `isFormEditable("items")` block), replace the current single-picker add form with a tabbed interface.

Import the inventory hook and add state:
```tsx
import { useManufacturedInventoryQuery } from "../../../api/hooks/useManufacturedProductInventory";

// Inside component, add:
const [addTab, setAddTab] = useState<"manufactured" | "catalog">("manufactured");
const [inventorySearch, setInventorySearch] = useState("");
const [selectedInventoryItem, setSelectedInventoryItem] = useState<ManufacturedInventoryItemDto | null>(null);
const { data: inventoryData } = useManufacturedInventoryQuery({
  search: inventorySearch,
  onlyWithStock: true
});
```

Replace the add-item section with:
```tsx
{isFormEditable("items") && (
  <div className="bg-gray-50 p-4 mb-6 rounded-lg">
    {/* Tab selector */}
    <div className="flex border-b border-gray-200 mb-4">
      <button
        type="button"
        onClick={() => setAddTab("manufactured")}
        className={`px-4 py-2 text-sm font-medium ${addTab === "manufactured"
          ? "border-b-2 border-green-600 text-green-700"
          : "text-gray-500 hover:text-gray-700"}`}
      >
        Výroba
      </button>
      <button
        type="button"
        onClick={() => setAddTab("catalog")}
        className={`px-4 py-2 text-sm font-medium ${addTab === "catalog"
          ? "border-b-2 border-green-600 text-green-700"
          : "text-gray-500 hover:text-gray-700"}`}
      >
        Katalog
      </button>
    </div>

    {addTab === "manufactured" ? (
      /* Manufactured inventory picker */
      <div>
        <input
          type="text"
          placeholder="Hledat produkt..."
          value={inventorySearch}
          onChange={(e) => setInventorySearch(e.target.value)}
          className="w-full px-3 py-2 text-sm border border-gray-300 rounded-md mb-3 focus:outline-none focus:ring-2 focus:ring-green-500"
        />
        <div className="max-h-48 overflow-y-auto border border-gray-200 rounded mb-3">
          {inventoryData?.items?.length === 0 && (
            <p className="p-3 text-sm text-gray-500 text-center">
              Žádné zásoby z výroby
            </p>
          )}
          {inventoryData?.items?.map((inv) => (
            <button
              key={inv.id}
              type="button"
              onClick={() => setSelectedInventoryItem(inv)}
              className={`w-full text-left px-3 py-2 text-sm hover:bg-gray-50 border-b border-gray-100 last:border-0 ${
                selectedInventoryItem?.id === inv.id ? "bg-green-50" : ""
              }`}
            >
              <span className="font-medium">{inv.productName}</span>
              <span className="text-gray-400 ml-2 font-mono text-xs">{inv.productCode}</span>
              {inv.lotNumber && <span className="ml-2 text-xs text-gray-500">Šarže: {inv.lotNumber}</span>}
              {inv.expirationDate && <span className="ml-2 text-xs text-orange-600">Exp: {inv.expirationDate}</span>}
              <span className="float-right font-semibold text-green-700">{inv.amount} ks</span>
            </button>
          ))}
        </div>

        {selectedInventoryItem && (
          <div className="grid grid-cols-12 gap-3">
            <div className="col-span-8 text-sm text-gray-700 self-center">
              <strong>{selectedInventoryItem.productName}</strong>
              {selectedInventoryItem.lotNumber && ` • ${selectedInventoryItem.lotNumber}`}
              <span className="ml-2 text-green-700">Dostupné: {selectedInventoryItem.amount}</span>
            </div>
            <div className="col-span-2">
              <input
                type="number"
                value={quantityInput}
                onChange={(e) => setQuantityInput(e.target.value)}
                step="0.01" min="0.01"
                max={selectedInventoryItem.amount}
                className="w-full px-3 py-2 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-green-500"
              />
            </div>
            <div className="col-span-2">
              <button
                type="button"
                onClick={() => {
                  if (selectedInventoryItem && quantityInput && parseFloat(quantityInput) > 0) {
                    handleAddItem({
                      sourceInventoryId: selectedInventoryItem.id,
                      lotNumber: selectedInventoryItem.lotNumber ?? undefined,
                      expirationDate: selectedInventoryItem.expirationDate ?? undefined,
                    });
                    setSelectedInventoryItem(null);
                  }
                }}
                disabled={!quantityInput || parseFloat(quantityInput) <= 0}
                className="w-full px-3 py-2 text-sm font-medium text-white bg-green-600 rounded-md hover:bg-green-700 disabled:opacity-50"
              >
                Přidat
              </button>
            </div>
          </div>
        )}
      </div>
    ) : (
      /* Catalog tab — existing form, unchanged */
      <div className="grid grid-cols-1 md:grid-cols-12 gap-3">
        {/* existing catalog autocomplete + amount + add button — copy verbatim from current code */}
      </div>
    )}
  </div>
)}
```

**Important:** The `handleAddItem` prop/callback needs to be updated at the call site (`TransportBoxDetail.tsx`) to accept and forward the optional `sourceInventoryId`, `lotNumber`, `expirationDate` to the `useAddItemToBox` mutation.

- [ ] **Step 3: Update TransportBoxDetail.tsx handleAddItem** — find the existing `handleAddItem` function and update it to accept and pass through the new optional fields to the mutation.

- [ ] **Step 4: Frontend build**

```bash
cd frontend && npm run build 2>&1 | tail -30
```

Expected: 0 errors.

- [ ] **Step 5: Lint**

```bash
cd frontend && npm run lint 2>&1 | tail -20
```

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/transport/ \
        frontend/src/api/hooks/useTransportBoxes.ts
git commit -m "feat: add Manufactured tab to transport box add-item UI"
```

---

## Task 13: Final build, format, and full test run

- [ ] **Step 1: Backend format check**

```bash
cd backend && dotnet format --verify-no-changes 2>&1 | tail -20
```

If any formatting issues: `dotnet format`, then rebuild.

- [ ] **Step 2: Run all backend tests**

```bash
cd backend && dotnet test 2>&1 | tail -30
```

Expected: all pass.

- [ ] **Step 3: Apply migration to local DB**

```bash
cd backend/src/Anela.Heblo.Persistence && \
  dotnet ef database update \
  --startup-project ../Anela.Heblo.API
```

- [ ] **Step 4: Frontend build + lint**

```bash
cd frontend && npm run build && npm run lint
```

- [ ] **Step 5: Final commit**

```bash
git add -A
git commit -m "chore: final build, format, and test verification for manufactured-product-inventory"
```

---

## Verification

After implementation:

1. `dotnet build && dotnet format --verify-no-changes` — 0 errors/warnings
2. `dotnet test` — all pass
3. `npm run build && npm run lint` — 0 errors
4. **Smoke test:**
   1. Open a Planned manufacture order, transition → Completed
   2. Navigate to Výroba → Sklad výroby: expect rows per product with lot/expiration
   3. Open an Opened transport box → Add Item → "Výroba" tab → pick a row, enter 5 → Přidat → verify inventory decrements
   4. Remove that item → verify inventory restores
   5. On Sklad výroby, edit amount → verify log entry; delete row → verify zeroed with ManualRemoval log
   6. Revert manufacture order from Completed → inventory rows unchanged (no auto-reverse)
