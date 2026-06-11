# Dedicated Lightweight Query Path for `GET /api/purchase-orders/{id}/history` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the wasteful aggregate load behind `GET /api/purchase-orders/{id}/history` with a dedicated repository method + MediatR use case that fetches only history rows.

**Architecture:** Add two focused methods (`ExistsAsync`, `GetHistoryAsync`) to `IPurchaseOrderRepository`. Add a new `GetPurchaseOrderHistory` use case (Request + Handler only — no bespoke Response class; the handler returns the shared `ListResponse<PurchaseOrderHistoryDto>` envelope). Switch the controller from `GetPurchaseOrderByIdRequest` to `GetPurchaseOrderHistoryRequest` and rely on the inherited `HandleResponse` helper to remove the manual remap. `GetPurchaseOrderByIdHandler` and `GetByIdWithDetailsAsync` are left untouched so the detail endpoint keeps loading lines and history.

**Tech Stack:** .NET 8 · ASP.NET Core MVC · MediatR · EF Core (Npgsql) · xUnit · FluentAssertions · Moq · Testcontainers (PostgreSQL) · EF Core InMemory provider (existing repository test pattern).

---

## File Structure

**New files:**

- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderHistory/GetPurchaseOrderHistoryRequest.cs`
  Single MediatR request record exposing the order ID; `TResponse = ListResponse<PurchaseOrderHistoryDto>`. Matches the existing `record` precedent set by `GetPurchaseOrderByIdRequest.cs`.

- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderHistory/GetPurchaseOrderHistoryHandler.cs`
  Thin handler. Depends on `ILogger<GetPurchaseOrderHistoryHandler>` and `IPurchaseOrderRepository`. Existence-checks via `ExistsAsync`, loads rows via `GetHistoryAsync`, projects to `PurchaseOrderHistoryDto`, returns `ListResponse<PurchaseOrderHistoryDto>` or `(ErrorCodes.PurchaseOrderNotFound, { Id })`.

- `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseOrderHistoryHandlerTests.cs`
  xUnit + Moq + FluentAssertions unit tests covering happy path, empty history, not-found, ordering, and verifying the handler never calls `GetByIdWithDetailsAsync`.

- `backend/test/Anela.Heblo.Tests/Features/Purchase/PurchaseOrderRepositoryHistoryTests.cs`
  Repository tests against the EF Core InMemory provider (matches existing `BankStatementImportRepositoryTests` pattern). Covers `ExistsAsync` semantics, `GetHistoryAsync` ordering/filtering, and the empty-list contract.

- `backend/test/Anela.Heblo.Tests/Features/Purchase/PurchaseOrderRepositoryHistorySqlShapeTests.cs`
  Testcontainers-backed PostgreSQL integration test (matches existing `PhotobankRepositoryGetTagsSqlShapeTests` pattern). Captures emitted SQL via a `DbCommandInterceptor` and asserts the `GetHistoryAsync` query touches only `PurchaseOrderHistory` — neither `PurchaseOrders` nor `PurchaseOrderLines` appears in the SQL.

**Modified files:**

- `backend/src/Anela.Heblo.Domain/Features/Purchase/IPurchaseOrderRepository.cs`
  Add two methods: `Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default)` and `Task<IReadOnlyList<PurchaseOrderHistory>> GetHistoryAsync(int orderId, CancellationToken cancellationToken = default)`.

- `backend/src/Anela.Heblo.Persistence/Purchase/PurchaseOrders/PurchaseOrderRepository.cs`
  Implement both new methods. `ExistsAsync` → `DbSet.AnyAsync(x => x.Id == id, ct)`. `GetHistoryAsync` → query `Context.PurchaseOrderHistory` with `AsNoTracking()`, filter by `PurchaseOrderId`, order by `ChangedAt` descending, materialize.

- `backend/src/Anela.Heblo.API/Controllers/PurchaseOrdersController.cs`
  Replace the body of `GetPurchaseOrderHistory` (lines 130–149) with a 3-line dispatch to `GetPurchaseOrderHistoryRequest`. Add the new `using` for `GetPurchaseOrderHistory`. Remove `GetPurchaseOrderById` usage from this action only.

**Touched-but-not-modified:**

- `backend/src/Anela.Heblo.Persistence/Persistence/PersistenceModule.cs` — already registers `IPurchaseOrderRepository` as scoped; no change needed.
- `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/PurchaseOrderHistoryDto.cs` — reused as-is.
- `backend/src/Anela.Heblo.Application/Shared/ListResponse.cs` — reused as-is; supports the `(ErrorCodes, params)` constructor needed for the not-found path.

---

## Conventions Used

- **DTOs vs records.** MediatR `IRequest` types are not serialised over OpenAPI; the existing precedent is `record` (see `GetPurchaseOrderByIdRequest`). The new `GetPurchaseOrderHistoryRequest` follows that.
- **Repository ordering.** The repository orders by `ChangedAt` descending in the query (server-side). The handler does **not** re-sort.
- **`AsNoTracking()`.** Read-only history queries skip the EF change tracker (matches `BankStatementImportRepository.cs` and `LeafletDocumentRepository.cs`).
- **Error envelope.** `ListResponse<T>` already implements `Success/ErrorCode/Params` via `BaseResponse`; the controller's inherited `HandleResponse<T>(response)` handles 404 mapping.
- **Project-rule deviation noted in spec:** `GetPurchaseOrderHistoryResponse` is **not** created. The handler's `TResponse` is `ListResponse<PurchaseOrderHistoryDto>` directly (per the architecture review's Specification Amendment #1).

---

## Task 1: Add `ExistsAsync` and `GetHistoryAsync` to `IPurchaseOrderRepository`

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Purchase/IPurchaseOrderRepository.cs`

- [ ] **Step 1: Add the two new method declarations to the interface**

Edit `backend/src/Anela.Heblo.Domain/Features/Purchase/IPurchaseOrderRepository.cs`. After the existing `GetByStatusAsync` declaration, add:

```csharp
    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PurchaseOrderHistory>> GetHistoryAsync(int orderId, CancellationToken cancellationToken = default);
```

The final file should read:

```csharp
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Purchase;

public interface IPurchaseOrderRepository : IRepository<PurchaseOrder, int>
{
    Task<(List<PurchaseOrder> Orders, int TotalCount)> GetPaginatedAsync(
        string? searchTerm,
        string? status,
        DateTime? fromDate,
        DateTime? toDate,
        bool? activeOrdersOnly,
        int pageNumber,
        int pageSize,
        string sortBy,
        bool sortDescending,
        CancellationToken cancellationToken = default);

    Task<PurchaseOrder?> GetByIdWithDetailsAsync(int id, CancellationToken cancellationToken = default);

    Task<bool> OrderNumberExistsAsync(string orderNumber, CancellationToken cancellationToken = default);

    Task<Dictionary<string, decimal>> GetOrderedQuantitiesAsync(CancellationToken cancellationToken = default);

    Task<IEnumerable<PurchaseOrder>> GetByStatusAsync(PurchaseOrderStatus status, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PurchaseOrderHistory>> GetHistoryAsync(int orderId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Verify the solution builds**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: **build fails** in `PurchaseOrderRepository.cs` with errors about the two new interface methods not being implemented. This is the RED step for Task 2; do not fix anything here.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Purchase/IPurchaseOrderRepository.cs
git commit -m "feat(purchase): declare ExistsAsync and GetHistoryAsync on IPurchaseOrderRepository"
```

---

## Task 2: Implement `ExistsAsync` in `PurchaseOrderRepository` (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Purchase/PurchaseOrderRepositoryHistoryTests.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Purchase/PurchaseOrders/PurchaseOrderRepository.cs`

- [ ] **Step 1: Write the failing test file with two `ExistsAsync` tests**

Create `backend/test/Anela.Heblo.Tests/Features/Purchase/PurchaseOrderRepositoryHistoryTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Purchase.PurchaseOrders;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase;

public sealed class PurchaseOrderRepositoryHistoryTests : IDisposable
{
    private const long ValidSupplierId = 1;
    private const string ValidSupplierName = "Test Supplier";

    private readonly ApplicationDbContext _context;
    private readonly PurchaseOrderRepository _repository;

    public PurchaseOrderRepositoryHistoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"PurchaseOrderRepoHistoryTests_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new PurchaseOrderRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrue_WhenOrderExists()
    {
        // Arrange
        var order = new PurchaseOrder(
            "PO-2026-EXIST",
            ValidSupplierId,
            ValidSupplierName,
            DateTime.UtcNow,
            null,
            null,
            "notes",
            "system");

        _context.PurchaseOrders.Add(order);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.ExistsAsync(order.Id, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenOrderMissing()
    {
        // Act
        var result = await _repository.ExistsAsync(999_999, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run the new tests — confirm they fail to compile**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PurchaseOrderRepositoryHistoryTests" --no-restore`
Expected: build error — `IPurchaseOrderRepository` does not contain a definition for `ExistsAsync` (from Task 1) AND `PurchaseOrderRepository` does not implement the interface.

- [ ] **Step 3: Implement `ExistsAsync` in the repository**

In `backend/src/Anela.Heblo.Persistence/Purchase/PurchaseOrders/PurchaseOrderRepository.cs`, append the following method inside the class (after `GetByStatusAsync`):

```csharp
    public async Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await DbSet.AsNoTracking().AnyAsync(x => x.Id == id, cancellationToken);
    }
```

- [ ] **Step 4: Run the tests — confirm `ExistsAsync` tests pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PurchaseOrderRepositoryHistoryTests.ExistsAsync"`
Expected: 2 passing tests. The file does not yet compile fully if `GetHistoryAsync` is still missing — that's expected; Task 3 fixes it.

If the solution-level build still fails because `GetHistoryAsync` is unimplemented, this is fine for now (do not commit yet — Task 3 finishes the implementation).

> **Note:** Do not run `dotnet format` or commit here. Task 3 completes the repository surface; commits land at Task 3's end.

---

## Task 3: Implement `GetHistoryAsync` in `PurchaseOrderRepository` (TDD)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Purchase/PurchaseOrderRepositoryHistoryTests.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Purchase/PurchaseOrders/PurchaseOrderRepository.cs`

- [ ] **Step 1: Add `GetHistoryAsync` tests to the existing test file**

In `backend/test/Anela.Heblo.Tests/Features/Purchase/PurchaseOrderRepositoryHistoryTests.cs`, add these three test methods inside the class (above `Dispose`):

```csharp
    [Fact]
    public async Task GetHistoryAsync_ReturnsEmpty_WhenOrderHasNoHistory()
    {
        // Arrange
        var order = new PurchaseOrder(
            "PO-2026-EMPTY",
            ValidSupplierId,
            ValidSupplierName,
            DateTime.UtcNow,
            null,
            null,
            null,
            "system");

        _context.PurchaseOrders.Add(order);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetHistoryAsync(order.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsRowsForOrder_OrderedByChangedAtDescending()
    {
        // Arrange
        var order = new PurchaseOrder(
            "PO-2026-WITHHISTORY",
            ValidSupplierId,
            ValidSupplierName,
            DateTime.UtcNow,
            null,
            null,
            null,
            "system");

        _context.PurchaseOrders.Add(order);
        await _context.SaveChangesAsync();

        // Insert three rows; persist twice so ChangedAt timestamps differ.
        _context.PurchaseOrderHistory.Add(new PurchaseOrderHistory(order.Id, "Created", null, "Draft", "user-1"));
        await _context.SaveChangesAsync();

        await Task.Delay(5);
        _context.PurchaseOrderHistory.Add(new PurchaseOrderHistory(order.Id, "StatusChanged", "Draft", "InTransit", "user-2"));
        await _context.SaveChangesAsync();

        await Task.Delay(5);
        _context.PurchaseOrderHistory.Add(new PurchaseOrderHistory(order.Id, "InvoiceAcquired", "false", "true", "user-3"));
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetHistoryAsync(order.Id, CancellationToken.None);

        // Assert
        result.Should().HaveCount(3);
        result.Select(h => h.Action).Should().ContainInOrder("InvoiceAcquired", "StatusChanged", "Created");
        result.Should().BeInDescendingOrder(h => h.ChangedAt);
    }

    [Fact]
    public async Task GetHistoryAsync_DoesNotReturnRowsForOtherOrders()
    {
        // Arrange
        var orderA = new PurchaseOrder("PO-A", ValidSupplierId, ValidSupplierName, DateTime.UtcNow, null, null, null, "system");
        var orderB = new PurchaseOrder("PO-B", ValidSupplierId, ValidSupplierName, DateTime.UtcNow, null, null, null, "system");
        _context.PurchaseOrders.AddRange(orderA, orderB);
        await _context.SaveChangesAsync();

        _context.PurchaseOrderHistory.Add(new PurchaseOrderHistory(orderA.Id, "Created", null, "Draft", "user-1"));
        _context.PurchaseOrderHistory.Add(new PurchaseOrderHistory(orderB.Id, "Created", null, "Draft", "user-2"));
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetHistoryAsync(orderA.Id, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result.Single().ChangedBy.Should().Be("user-1");
    }
```

- [ ] **Step 2: Run the three new tests — confirm they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PurchaseOrderRepositoryHistoryTests.GetHistoryAsync"`
Expected: build still fails (interface method `GetHistoryAsync` is declared but not implemented in `PurchaseOrderRepository`).

- [ ] **Step 3: Implement `GetHistoryAsync` in the repository**

In `backend/src/Anela.Heblo.Persistence/Purchase/PurchaseOrders/PurchaseOrderRepository.cs`, append the following method inside the class (after `ExistsAsync`):

```csharp
    public async Task<IReadOnlyList<PurchaseOrderHistory>> GetHistoryAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await _context.PurchaseOrderHistory
            .AsNoTracking()
            .Where(h => h.PurchaseOrderId == orderId)
            .OrderByDescending(h => h.ChangedAt)
            .ToListAsync(cancellationToken);
    }
```

> **Note on `_context`:** The class inherits from `BaseRepository<PurchaseOrder, int>`. Check whether the base class exposes the `ApplicationDbContext` as `_context`, `Context`, or via a protected property. If the field/property name differs, use the correct identifier. As a fallback, you can cast: `((ApplicationDbContext)Context).PurchaseOrderHistory`. The expression must resolve to `DbSet<PurchaseOrderHistory>`.
>
> **Verification command for the base class:** `grep -n "protected" backend/src/Anela.Heblo.Persistence/Repositories/BaseRepository.cs` (or search for `class BaseRepository` and inspect the constructor / fields it exposes).

- [ ] **Step 4: Run all repository tests — confirm they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PurchaseOrderRepositoryHistoryTests"`
Expected: 5 passing tests (2 `ExistsAsync` + 3 `GetHistoryAsync`).

- [ ] **Step 5: Format the changed files**

Run: `dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.Persistence/Purchase/PurchaseOrders/PurchaseOrderRepository.cs backend/test/Anela.Heblo.Tests/Features/Purchase/PurchaseOrderRepositoryHistoryTests.cs`
Expected: no formatting errors reported.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Purchase/PurchaseOrders/PurchaseOrderRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/Purchase/PurchaseOrderRepositoryHistoryTests.cs
git commit -m "feat(purchase): implement ExistsAsync and GetHistoryAsync on PurchaseOrderRepository"
```

---

## Task 4: SQL-shape integration test (verify no JOIN to `PurchaseOrders` / `PurchaseOrderLines`)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Purchase/PurchaseOrderRepositoryHistorySqlShapeTests.cs`

This task uses Testcontainers + PostgreSQL (matches the existing pattern in `PhotobankRepositoryGetTagsSqlShapeTests.cs`). It catches the failure mode the architecture review flagged as "Medium" risk: EF Core silently joining `PurchaseOrders` due to a shadow FK / nav misconfiguration.

- [ ] **Step 1: Create the SQL-shape test file**

Create `backend/test/Anela.Heblo.Tests/Features/Purchase/PurchaseOrderRepositoryHistorySqlShapeTests.cs`:

```csharp
using System.Data.Common;
using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Purchase.PurchaseOrders;
using DotNet.Testcontainers.Configurations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase;

[Trait("Category", "Integration")]
public class PurchaseOrderRepositoryHistorySqlShapeTests : IAsyncLifetime
{
    static PurchaseOrderRepositoryHistorySqlShapeTests()
    {
        // Required on macOS with Podman; matches PhotobankRepositoryGetTagsSqlShapeTests.
        TestcontainersSettings.ResourceReaperEnabled = false;
    }

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .Build();

    private readonly CapturingCommandInterceptor _interceptor = new();
    private ApplicationDbContext _context = null!;
    private PurchaseOrderRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Minimal schema — only the PurchaseOrderHistory table the query touches.
        // No FKs / no PurchaseOrders / no PurchaseOrderLines, so any unintended JOIN would fail at execution time.
        await using (var conn = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE public."PurchaseOrderHistory" (
                    "Id"              serial      NOT NULL PRIMARY KEY,
                    "PurchaseOrderId" integer     NOT NULL,
                    "Action"          varchar(50) NOT NULL,
                    "OldValue"        varchar(2000),
                    "NewValue"        varchar(2000),
                    "ChangedBy"       varchar(200) NOT NULL,
                    "ChangedAt"       timestamp   NOT NULL
                );
                CREATE INDEX "IX_PurchaseOrderHistory_PurchaseOrderId" ON public."PurchaseOrderHistory" ("PurchaseOrderId");
                CREATE INDEX "IX_PurchaseOrderHistory_ChangedAt"      ON public."PurchaseOrderHistory" ("ChangedAt");

                INSERT INTO public."PurchaseOrderHistory"
                    ("PurchaseOrderId","Action","OldValue","NewValue","ChangedBy","ChangedAt") VALUES
                    (42, 'Created', NULL, 'Draft', 'user-1', now() - interval '2 minute'),
                    (42, 'StatusChanged', 'Draft', 'InTransit', 'user-2', now() - interval '1 minute'),
                    (43, 'Created', NULL, 'Draft', 'user-3', now());
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .AddInterceptors(_interceptor)
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new PurchaseOrderRepository(_context);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task GetHistoryAsync_EmitsSqlThatTouchesOnlyHistoryTable()
    {
        _interceptor.Reset();

        var rows = await _repository.GetHistoryAsync(42, CancellationToken.None);

        rows.Should().HaveCount(2);

        var sql = _interceptor.Commands.Should().ContainSingle().Subject;
        sql.Should().Contain("PurchaseOrderHistory", "the query must read from the history table");
        sql.Should().NotContainEquivalentOf("\"PurchaseOrders\"", "GetHistoryAsync must not JOIN to PurchaseOrders");
        sql.Should().NotContainEquivalentOf("\"PurchaseOrderLines\"", "GetHistoryAsync must not JOIN to PurchaseOrderLines");
    }

    private sealed class CapturingCommandInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = new();

        public void Reset() => Commands.Clear();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
```

- [ ] **Step 2: Run the SQL-shape test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PurchaseOrderRepositoryHistorySqlShapeTests"`
Expected: 1 passing test. (Requires Docker / Podman locally — same as other `*SqlShapeTests`. If the container runtime is unavailable, the test will skip/fail with an environment error; in that case, document the skip in the PR and rely on the InMemory tests as the primary safety net.)

- [ ] **Step 3: Format**

Run: `dotnet format backend/Anela.Heblo.sln --include backend/test/Anela.Heblo.Tests/Features/Purchase/PurchaseOrderRepositoryHistorySqlShapeTests.cs`
Expected: no formatting errors.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Purchase/PurchaseOrderRepositoryHistorySqlShapeTests.cs
git commit -m "test(purchase): assert GetHistoryAsync SQL touches only PurchaseOrderHistory"
```

---

## Task 5: Create `GetPurchaseOrderHistoryRequest` (MediatR contract)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderHistory/GetPurchaseOrderHistoryRequest.cs`

- [ ] **Step 1: Create the request file**

Create `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderHistory/GetPurchaseOrderHistoryRequest.cs`:

```csharp
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseOrderHistory;

public record GetPurchaseOrderHistoryRequest(int Id) : IRequest<ListResponse<PurchaseOrderHistoryDto>>;
```

- [ ] **Step 2: Verify the project builds**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderHistory/GetPurchaseOrderHistoryRequest.cs
git commit -m "feat(purchase): add GetPurchaseOrderHistoryRequest MediatR contract"
```

---

## Task 6: Implement `GetPurchaseOrderHistoryHandler` (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseOrderHistoryHandlerTests.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderHistory/GetPurchaseOrderHistoryHandler.cs`

- [ ] **Step 1: Write the failing handler test file**

Create `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseOrderHistoryHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseOrderHistory;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Purchase;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase;

public sealed class GetPurchaseOrderHistoryHandlerTests
{
    private readonly Mock<ILogger<GetPurchaseOrderHistoryHandler>> _loggerMock = new();
    private readonly Mock<IPurchaseOrderRepository> _repositoryMock = new();
    private readonly GetPurchaseOrderHistoryHandler _handler;

    public GetPurchaseOrderHistoryHandlerTests()
    {
        _handler = new GetPurchaseOrderHistoryHandler(_loggerMock.Object, _repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ReturnsPurchaseOrderNotFound_WhenOrderDoesNotExist()
    {
        // Arrange
        const int missingId = 42;
        _repositoryMock
            .Setup(r => r.ExistsAsync(missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _handler.Handle(new GetPurchaseOrderHistoryRequest(missingId), CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.PurchaseOrderNotFound);
        response.Params.Should().ContainKey("Id").WhoseValue.Should().Be(missingId.ToString());

        _repositoryMock.Verify(r => r.GetHistoryAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsEmptyList_WhenOrderHasNoHistory()
    {
        // Arrange
        const int orderId = 7;
        _repositoryMock.Setup(r => r.ExistsAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repositoryMock.Setup(r => r.GetHistoryAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseOrderHistory>());

        // Act
        var response = await _handler.Handle(new GetPurchaseOrderHistoryRequest(orderId), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Items.Should().BeEmpty();
        response.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_ReturnsMappedHistory_InRepositoryOrder()
    {
        // Arrange
        const int orderId = 11;
        var newer = new PurchaseOrderHistory(orderId, "StatusChanged", "Draft", "InTransit", "user-2");
        var older = new PurchaseOrderHistory(orderId, "Created", null, "Draft", "user-1");
        // Repository pre-orders newest-first; handler must preserve that order.
        var repoOutput = new List<PurchaseOrderHistory> { newer, older };

        _repositoryMock.Setup(r => r.ExistsAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repositoryMock.Setup(r => r.GetHistoryAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(repoOutput);

        // Act
        var response = await _handler.Handle(new GetPurchaseOrderHistoryRequest(orderId), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Items.Should().HaveCount(2);
        response.Items[0].Action.Should().Be("StatusChanged");
        response.Items[0].OldValue.Should().Be("Draft");
        response.Items[0].NewValue.Should().Be("InTransit");
        response.Items[0].ChangedBy.Should().Be("user-2");
        response.Items[1].Action.Should().Be("Created");
        response.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_DoesNotCallGetByIdWithDetailsAsync()
    {
        // Arrange — the whole point of this refactor: the handler must never load the aggregate.
        const int orderId = 99;
        _repositoryMock.Setup(r => r.ExistsAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repositoryMock.Setup(r => r.GetHistoryAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseOrderHistory>());

        // Act
        await _handler.Handle(new GetPurchaseOrderHistoryRequest(orderId), CancellationToken.None);

        // Assert
        _repositoryMock.Verify(
            r => r.GetByIdWithDetailsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the history handler must never load lines / supplier / catalog data");
    }
}
```

- [ ] **Step 2: Run the new tests — confirm they fail to compile**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetPurchaseOrderHistoryHandlerTests"`
Expected: compile error — `GetPurchaseOrderHistoryHandler` does not exist.

- [ ] **Step 3: Create the handler**

Create `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderHistory/GetPurchaseOrderHistoryHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Purchase;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseOrderHistory;

public class GetPurchaseOrderHistoryHandler : IRequestHandler<GetPurchaseOrderHistoryRequest, ListResponse<PurchaseOrderHistoryDto>>
{
    private readonly ILogger<GetPurchaseOrderHistoryHandler> _logger;
    private readonly IPurchaseOrderRepository _repository;

    public GetPurchaseOrderHistoryHandler(
        ILogger<GetPurchaseOrderHistoryHandler> logger,
        IPurchaseOrderRepository repository)
    {
        _logger = logger;
        _repository = repository;
    }

    public async Task<ListResponse<PurchaseOrderHistoryDto>> Handle(GetPurchaseOrderHistoryRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting purchase order history for ID {Id}", request.Id);

        var exists = await _repository.ExistsAsync(request.Id, cancellationToken);
        if (!exists)
        {
            _logger.LogWarning("Purchase order not found for ID {Id}", request.Id);
            return new ListResponse<PurchaseOrderHistoryDto>(
                ErrorCodes.PurchaseOrderNotFound,
                new Dictionary<string, string> { { "Id", request.Id.ToString() } });
        }

        var history = await _repository.GetHistoryAsync(request.Id, cancellationToken);

        var items = history
            .Select(h => new PurchaseOrderHistoryDto
            {
                Id = h.Id,
                Action = h.Action,
                OldValue = h.OldValue,
                NewValue = h.NewValue,
                ChangedAt = h.ChangedAt,
                ChangedBy = h.ChangedBy,
            })
            .ToList();

        _logger.LogInformation("Returning {Count} history entries for purchase order {Id}", items.Count, request.Id);

        return new ListResponse<PurchaseOrderHistoryDto>
        {
            Items = items,
            TotalCount = items.Count,
        };
    }
}
```

- [ ] **Step 4: Run the handler tests — confirm they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetPurchaseOrderHistoryHandlerTests"`
Expected: 4 passing tests.

- [ ] **Step 5: Format**

Run: `dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderHistory/GetPurchaseOrderHistoryHandler.cs backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseOrderHistoryHandlerTests.cs`
Expected: no formatting errors.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderHistory/GetPurchaseOrderHistoryHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseOrderHistoryHandlerTests.cs
git commit -m "feat(purchase): add GetPurchaseOrderHistoryHandler thin use case"
```

---

## Task 7: Wire the controller action to the new use case

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/PurchaseOrdersController.cs`

- [ ] **Step 1: Add the using directive for the new use case**

Edit `backend/src/Anela.Heblo.API/Controllers/PurchaseOrdersController.cs`. Add this `using` (alphabetically, between the existing `GetPurchaseOrderById` and `GetPurchaseOrders` usings):

```csharp
using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseOrderHistory;
```

The using block should now contain (order matters for `dotnet format`):

```csharp
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Features.Purchase.UseCases.CreatePurchaseOrder;
using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseOrderById;
using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseOrderHistory;
using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseOrders;
using Anela.Heblo.Application.Features.Purchase.UseCases.RecalculatePurchasePrice;
using Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;
using Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrderInvoiceAcquired;
using Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrderStatus;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.API.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
```

- [ ] **Step 2: Replace the body of `GetPurchaseOrderHistory` (current lines 130–149)**

Replace this current block:

```csharp
    [HttpGet("{id:int}/history")]
    public async Task<ActionResult<ListResponse<PurchaseOrderHistoryDto>>> GetPurchaseOrderHistory(
        [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        var request = new GetPurchaseOrderByIdRequest(id);
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.Success)
        {
            return HandleResponse<ListResponse<PurchaseOrderHistoryDto>>(new ListResponse<PurchaseOrderHistoryDto>(response.ErrorCode!.Value, response.Params));
        }

        var listResponse = new ListResponse<PurchaseOrderHistoryDto>
        {
            Items = response.History,
            TotalCount = response.History.Count
        };
        return Ok(listResponse);
    }
```

with:

```csharp
    [HttpGet("{id:int}/history")]
    public async Task<ActionResult<ListResponse<PurchaseOrderHistoryDto>>> GetPurchaseOrderHistory(
        [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetPurchaseOrderHistoryRequest(id), cancellationToken);
        return HandleResponse(response);
    }
```

- [ ] **Step 3: Build the API project**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: build succeeds with no warnings about unused usings (the `GetPurchaseOrderById` import is still used by the `GetPurchaseOrderById` action above).

- [ ] **Step 4: Format**

Run: `dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.API/Controllers/PurchaseOrdersController.cs`
Expected: no formatting errors.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/PurchaseOrdersController.cs
git commit -m "feat(purchase): switch /history endpoint to dedicated GetPurchaseOrderHistory use case"
```

---

## Task 8: Verify existing controller tests still pass (regression sweep)

**Files:**
- None modified.

There are two existing test files that cover the controller surface: `Controllers/PurchaseOrdersControllerTests.cs` and `PurchaseOrdersControllerErrorTests.cs`. The HTTP contract is unchanged, so all assertions about route, response envelope (`ListResponse<PurchaseOrderHistoryDto>`), 200/404 status codes, and `ErrorCodes.PurchaseOrderNotFound` must continue to pass with no test edits.

- [ ] **Step 1: Inspect existing controller tests for any direct mocking of `GetPurchaseOrderByIdRequest` on the history route**

Run: `grep -n "GetPurchaseOrderByIdRequest\|/history" backend/test/Anela.Heblo.Tests/Controllers/PurchaseOrdersControllerTests.cs backend/test/Anela.Heblo.Tests/PurchaseOrdersControllerErrorTests.cs`

If any test seeds a Mediator/handler mock for `GetPurchaseOrderByIdRequest` and then exercises the `/history` route, that test must be updated to seed `GetPurchaseOrderHistoryRequest` instead. **Read those tests** before changing anything; many likely call the action method directly and only depend on the underlying repository — in which case no test changes are needed.

- [ ] **Step 2: Run all Purchase + controller tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Purchase|FullyQualifiedName~PurchaseOrdersController"`
Expected: all tests green. If a single existing test fails because it mocked `GetPurchaseOrderByIdRequest` to drive the history endpoint, update that mock to use `GetPurchaseOrderHistoryRequest` and re-run. Do **not** weaken assertions about the response shape.

- [ ] **Step 3: If any existing test was updated, commit the surgical change**

```bash
git status
# If files under backend/test changed:
git add backend/test/...   # use specific paths from `git status`
git commit -m "test(purchase): update controller tests for GetPurchaseOrderHistory use case"
```

If nothing changed, skip this step (nothing to commit).

---

## Task 9: Full backend validation

**Files:**
- None modified.

- [ ] **Step 1: Full solution build**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: 0 errors, 0 new warnings.

- [ ] **Step 2: Full test run (unit + integration)**

Run: `dotnet test backend/Anela.Heblo.sln`
Expected: all tests pass. Note that `*SqlShapeTests` tests need Docker/Podman; if the local environment lacks a container runtime, exclude them with `--filter "Category!=Integration"` and call this out in the PR description.

- [ ] **Step 3: Full format**

Run: `dotnet format backend/Anela.Heblo.sln`
Expected: no changes needed (or only whitespace fixes — apply if any).

- [ ] **Step 4: Verify the OpenAPI shape is unchanged (NFR-3)**

The TypeScript client is auto-generated from the API's OpenAPI document on frontend build. The endpoint's response type (`ListResponse<PurchaseOrderHistoryDto>`) and route are unchanged, so the generated client must not diff.

Run:
```bash
cd frontend && npm run build
```
Expected: build succeeds. If `frontend/src/api-client/` files show any modifications under `git status` afterwards, **stop** — the OpenAPI surface has shifted unexpectedly. Inspect the diff and fix the controller / response type until the generated client is byte-identical to `main`.

- [ ] **Step 5: Commit any formatting fixes (if any)**

```bash
git status
# Only commit if dotnet format produced changes:
git add -p   # review changes interactively
git commit -m "chore(purchase): dotnet format after history endpoint refactor"
```

If `git status` is clean, skip this step.

---

## Self-Review Checklist (run before declaring complete)

Tick each box; fix anything that fails.

- [ ] **Spec FR-1** — `IPurchaseOrderRepository.GetHistoryAsync` declared (Task 1) and implemented in `PurchaseOrderRepository` (Task 3) querying only `PurchaseOrderHistory`, ordered by `ChangedAt` desc, returns empty list for unknown orders, passes the `CancellationToken`, uses `AsNoTracking()`. SQL shape verified in Task 4.
- [ ] **Spec FR-2** — Order existence check uses `IPurchaseOrderRepository.ExistsAsync` (Task 1/Task 2), not raw `DbSet`. Empty-history existing-order returns HTTP 200 + `Items=[]` + `TotalCount=0` (handler test in Task 6). Missing order returns `ErrorCodes.PurchaseOrderNotFound` with `Params["Id"]` populated (handler test in Task 6).
- [ ] **Spec FR-3** — Use case folder + Request created (Task 5), Handler created (Task 6). Handler depends only on `ILogger` + `IPurchaseOrderRepository`. Spec Amendment #1 honoured: no bespoke Response class — `TResponse = ListResponse<PurchaseOrderHistoryDto>`.
- [ ] **Spec FR-4** — Controller `GetPurchaseOrderHistory` dispatches `GetPurchaseOrderHistoryRequest` and uses `HandleResponse(response)` (Task 7).
- [ ] **Spec FR-5** — `GetPurchaseOrderByIdHandler` and `GetByIdWithDetailsAsync` are not modified. Verified by Task 8 controller regression sweep and Task 9 full test run.
- [ ] **NFR-1 Performance** — Handler test in Task 6 verifies `GetByIdWithDetailsAsync` is never called. SQL-shape test in Task 4 verifies no JOIN to `PurchaseOrders` or `PurchaseOrderLines`. Worst-case two statements: existence + history.
- [ ] **NFR-2 Security** — Handler logs only IDs/counts, never `OldValue`/`NewValue`. Authorization is unchanged (controller still has `[Authorize]` from the class attribute).
- [ ] **NFR-3 Backwards compatibility** — Frontend `npm run build` shows no generated-client diff (Task 9 Step 4).
- [ ] **NFR-4 Observability** — Handler logs info on entry, warning on not-found, info on completion (matches `GetPurchaseOrderByIdHandler` conventions).
- [ ] **No placeholders** — Every step contains the actual code or command. No TODO/TBD/"appropriate error handling".
- [ ] **Type consistency** — `GetPurchaseOrderHistoryRequest(int Id)`, `ListResponse<PurchaseOrderHistoryDto>`, `ExistsAsync(int, CT)`, `GetHistoryAsync(int, CT)`, `PurchaseOrderHistoryDto` — names match across every task.

---
