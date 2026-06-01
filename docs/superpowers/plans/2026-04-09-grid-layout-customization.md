# Grid Layout Customization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users resize, reorder, and hide columns on the Řízení zásob výrobků and Analýza skladových zásob screens, with changes persisted per user in the database.

**Architecture:** A new `GridLayouts` vertical slice on the backend stores a JSON payload `{ columns: [{id, order, width, hidden}] }` in one row per `(UserId, GridKey)`. A generic `useGridLayout` hook on the frontend fetches this state, merges it with the in-code column definitions, and exposes mutators that debounce-save changes back. Two existing screens are refactored to drive their `<table>` markup from a `GridColumn<TRow>[]` array instead of hardcoded JSX headers.

**Tech Stack:** .NET 8 / EF Core / MediatR / ICurrentUserService (backend); React 18 / TypeScript / @dnd-kit/sortable / @headlessui/react (frontend); no new dependencies needed.

---

## File Map

### Backend — create
| File | Responsibility |
|---|---|
| `backend/src/Anela.Heblo.Domain/Features/GridLayouts/GridLayout.cs` | EF entity: Id, UserId, GridKey, LayoutJson, LastModified |
| `backend/src/Anela.Heblo.Domain/Features/GridLayouts/IGridLayoutRepository.cs` | Repository interface |
| `backend/src/Anela.Heblo.Application/Features/GridLayouts/Contracts/GridLayoutDto.cs` | Response DTO (class, not record) |
| `backend/src/Anela.Heblo.Application/Features/GridLayouts/Contracts/GridColumnStateDto.cs` | Column state DTO |
| `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutRequest.cs` | MediatR request |
| `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutResponse.cs` | MediatR response |
| `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs` | Handler |
| `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutRequest.cs` | MediatR request |
| `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutResponse.cs` | MediatR response |
| `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutHandler.cs` | Handler |
| `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/ResetGridLayout/ResetGridLayoutRequest.cs` | MediatR request |
| `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/ResetGridLayout/ResetGridLayoutResponse.cs` | MediatR response |
| `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/ResetGridLayout/ResetGridLayoutHandler.cs` | Handler |
| `backend/src/Anela.Heblo.Application/Features/GridLayouts/GridLayoutsModule.cs` | DI registrations for the slice |
| `backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutConfiguration.cs` | EF fluent config (table, keys, indexes) |
| `backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs` | EF implementation of IGridLayoutRepository |
| `backend/src/Anela.Heblo.API/Controllers/GridLayoutsController.cs` | REST: GET/PUT/DELETE /api/grid-layouts/{gridKey} |
| `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs` | Handler unit tests |
| `backend/test/Anela.Heblo.Tests/Features/GridLayouts/SaveGridLayoutHandlerTests.cs` | Handler unit tests |
| `backend/test/Anela.Heblo.Tests/Features/GridLayouts/ResetGridLayoutHandlerTests.cs` | Handler unit tests |

### Backend — modify
| File | Change |
|---|---|
| `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` | Add `DbSet<GridLayout> GridLayouts` |
| `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` | Register `GridLayoutRepository` |
| `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` | Call `services.AddGridLayoutsModule()` |

### Frontend — create
| File | Responsibility |
|---|---|
| `frontend/src/features/grid-layout/types.ts` | `GridColumn<TRow>`, `GridColumnState`, `GridLayoutPayload` |
| `frontend/src/features/grid-layout/useGridLayout.ts` | Hook: load, merge, mutate, debounce-save |
| `frontend/src/features/grid-layout/GridHeader.tsx` | `<thead>` with drag-to-reorder + resize handles |
| `frontend/src/features/grid-layout/ColumnChooser.tsx` | Popover checklist + Reset button |
| `frontend/src/features/grid-layout/index.ts` | Barrel export |
| `frontend/src/features/grid-layout/__tests__/useGridLayout.test.ts` | Hook merge/mutate/debounce tests |
| `frontend/src/features/grid-layout/__tests__/ColumnChooser.test.tsx` | Visibility toggle + reset tests |

### Frontend — modify
| File | Change |
|---|---|
| `frontend/src/components/pages/ManufacturingStockAnalysis.tsx` | Replace hardcoded `<th>` markup with column config array + hook |
| `frontend/src/components/pages/PurchaseStockAnalysis.tsx` | Same; remove `hidden md:table-cell` responsive classes |
| `frontend/src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx` | Update DOM assertions |
| `frontend/src/components/pages/__tests__/PurchaseStockAnalysis.test.tsx` | Update DOM assertions |

---

## Task 1: Domain entity and repository interface

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/GridLayouts/GridLayout.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/GridLayouts/IGridLayoutRepository.cs`

- [ ] **Step 1: Create the domain entity**

```csharp
// backend/src/Anela.Heblo.Domain/Features/GridLayouts/GridLayout.cs
namespace Anela.Heblo.Domain.Features.GridLayouts;

public class GridLayout
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string GridKey { get; set; } = string.Empty;
    public string LayoutJson { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
}
```

- [ ] **Step 2: Create the repository interface**

```csharp
// backend/src/Anela.Heblo.Domain/Features/GridLayouts/IGridLayoutRepository.cs
namespace Anela.Heblo.Domain.Features.GridLayouts;

public interface IGridLayoutRepository
{
    Task<GridLayout?> GetAsync(string userId, string gridKey, CancellationToken cancellationToken = default);
    Task UpsertAsync(string userId, string gridKey, string layoutJson, CancellationToken cancellationToken = default);
    Task DeleteAsync(string userId, string gridKey, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Build to verify no compilation errors**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/GridLayouts/
git commit -m "feat(grid-layout): add GridLayout domain entity and repository interface"
```

---

## Task 2: Persistence — EF config, repository, DbContext, migration

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs`

- [ ] **Step 1: Create EF configuration**

```csharp
// backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutConfiguration.cs
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.GridLayouts;

public class GridLayoutConfiguration : IEntityTypeConfiguration<GridLayout>
{
    public void Configure(EntityTypeBuilder<GridLayout> builder)
    {
        builder.ToTable("GridLayouts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.GridKey)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.LayoutJson)
            .IsRequired();

        builder.Property(x => x.LastModified)
            .AsUtcTimestamp()
            .IsRequired();

        builder.HasIndex(x => new { x.UserId, x.GridKey })
            .IsUnique();
    }
}
```

- [ ] **Step 2: Create repository implementation**

```csharp
// backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs
using Anela.Heblo.Domain.Features.GridLayouts;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.GridLayouts;

public class GridLayoutRepository : IGridLayoutRepository
{
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public GridLayoutRepository(ApplicationDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<GridLayout?> GetAsync(string userId, string gridKey, CancellationToken cancellationToken = default)
    {
        return await _context.GridLayouts
            .FirstOrDefaultAsync(x => x.UserId == userId && x.GridKey == gridKey, cancellationToken);
    }

    public async Task UpsertAsync(string userId, string gridKey, string layoutJson, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(userId, gridKey, cancellationToken);

        if (existing is not null)
        {
            existing.LayoutJson = layoutJson;
            existing.LastModified = _timeProvider.GetUtcNow().DateTime;
        }
        else
        {
            _context.GridLayouts.Add(new GridLayout
            {
                UserId = userId,
                GridKey = gridKey,
                LayoutJson = layoutJson,
                LastModified = _timeProvider.GetUtcNow().DateTime
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string userId, string gridKey, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(userId, gridKey, cancellationToken);
        if (existing is not null)
        {
            _context.GridLayouts.Remove(existing);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
```

- [ ] **Step 3: Register DbSet in ApplicationDbContext**

Open `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`. Find the existing `DbSet` block (around lines 62–63 where `UserDashboardSettings` and `UserDashboardTile` are registered). Add:

```csharp
public DbSet<GridLayout> GridLayouts => Set<GridLayout>();
```

Also add the using at the top if not already present:
```csharp
using Anela.Heblo.Domain.Features.GridLayouts;
```

- [ ] **Step 4: Register repository in PersistenceModule**

Open `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs`. Find where other repositories are registered (look for similar `services.AddScoped<I...Repository, ...Repository>()` calls). Add:

```csharp
services.AddScoped<IGridLayoutRepository, GridLayoutRepository>();
```

Add the using:
```csharp
using Anela.Heblo.Persistence.GridLayouts;
using Anela.Heblo.Domain.Features.GridLayouts;
```

- [ ] **Step 5: Build Persistence project to verify**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 6: Add EF migration**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet ef migrations add AddGridLayouts \
  --project src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj \
  --startup-project src/Anela.Heblo.API/Anela.Heblo.API.csproj \
  --context ApplicationDbContext
```
Expected: `Done. To undo this action, use 'ef migrations remove'`

Verify the migration file was created in `backend/src/Anela.Heblo.Persistence/Migrations/` and contains `CreateTable("GridLayouts", ...)` with columns UserId, GridKey, LayoutJson, LastModified and the unique index.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/GridLayouts/
git add backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs
git add backend/src/Anela.Heblo.Persistence/PersistenceModule.cs
git add backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat(grid-layout): add GridLayouts persistence layer and migration"
```

---

## Task 3: Application — contracts (DTOs)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/GridLayouts/Contracts/GridColumnStateDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/GridLayouts/Contracts/GridLayoutDto.cs`

> **Rule:** use classes with properties, not records (CLAUDE.md §3 — OpenAPI generator compatibility).

- [ ] **Step 1: Create GridColumnStateDto**

```csharp
// backend/src/Anela.Heblo.Application/Features/GridLayouts/Contracts/GridColumnStateDto.cs
using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.GridLayouts.Contracts;

public class GridColumnStateDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("hidden")]
    public bool Hidden { get; set; }
}
```

- [ ] **Step 2: Create GridLayoutDto**

```csharp
// backend/src/Anela.Heblo.Application/Features/GridLayouts/Contracts/GridLayoutDto.cs
using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.GridLayouts.Contracts;

public class GridLayoutDto
{
    [JsonPropertyName("gridKey")]
    public string GridKey { get; set; } = string.Empty;

    [JsonPropertyName("columns")]
    public List<GridColumnStateDto> Columns { get; set; } = new();

    [JsonPropertyName("lastModified")]
    public DateTime? LastModified { get; set; }
}
```

- [ ] **Step 3: Build Application project**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/GridLayouts/Contracts/
git commit -m "feat(grid-layout): add application layer DTOs"
```

---

## Task 4: GetGridLayout handler + tests

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs
using System.Text.Json;
using Anela.Heblo.Application.Features.GridLayouts.Contracts;
using Anela.Heblo.Application.Features.GridLayouts.UseCases.GetGridLayout;
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Domain.Features.Users;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.GridLayouts;

public class GetGridLayoutHandlerTests
{
    private readonly Mock<IGridLayoutRepository> _repositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();

    private GetGridLayoutHandler CreateHandler() =>
        new(_repositoryMock.Object, _currentUserMock.Object);

    [Fact]
    public async Task Handle_WhenNoSavedLayout_ReturnsNull()
    {
        _currentUserMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test", "test@test.com", true));
        _repositoryMock.Setup(x => x.GetAsync("user-1", "test-grid", default)).ReturnsAsync((GridLayout?)null);

        var handler = CreateHandler();
        var response = await handler.Handle(new GetGridLayoutRequest { GridKey = "test-grid" }, default);

        Assert.Null(response.Layout);
    }

    [Fact]
    public async Task Handle_WhenSavedLayoutExists_ReturnsDeserializedDto()
    {
        _currentUserMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test", "test@test.com", true));

        var payload = new { columns = new[] { new { id = "col1", order = 0, width = 120, hidden = false } } };
        var json = JsonSerializer.Serialize(payload);

        _repositoryMock.Setup(x => x.GetAsync("user-1", "test-grid", default)).ReturnsAsync(new GridLayout
        {
            UserId = "user-1",
            GridKey = "test-grid",
            LayoutJson = json,
            LastModified = DateTime.UtcNow
        });

        var handler = CreateHandler();
        var response = await handler.Handle(new GetGridLayoutRequest { GridKey = "test-grid" }, default);

        Assert.NotNull(response.Layout);
        Assert.Single(response.Layout!.Columns);
        Assert.Equal("col1", response.Layout.Columns[0].Id);
        Assert.Equal(120, response.Layout.Columns[0].Width);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~GetGridLayoutHandlerTests"
```
Expected: compilation error — `GetGridLayoutRequest`, `GetGridLayoutHandler` don't exist yet.

- [ ] **Step 3: Create request and response**

```csharp
// backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutRequest.cs
using Anela.Heblo.Application.Features.GridLayouts.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.GridLayouts.UseCases.GetGridLayout;

public class GetGridLayoutRequest : IRequest<GetGridLayoutResponse>
{
    public string GridKey { get; set; } = string.Empty;
}
```

```csharp
// backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutResponse.cs
using Anela.Heblo.Application.Features.GridLayouts.Contracts;

namespace Anela.Heblo.Application.Features.GridLayouts.UseCases.GetGridLayout;

public class GetGridLayoutResponse
{
    public GridLayoutDto? Layout { get; set; }
}
```

- [ ] **Step 4: Create handler**

```csharp
// backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs
using System.Text.Json;
using Anela.Heblo.Application.Features.GridLayouts.Contracts;
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.GridLayouts.UseCases.GetGridLayout;

public class GetGridLayoutHandler : IRequestHandler<GetGridLayoutRequest, GetGridLayoutResponse>
{
    private readonly IGridLayoutRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public GetGridLayoutHandler(IGridLayoutRepository repository, ICurrentUserService currentUserService)
    {
        _repository = repository;
        _currentUserService = currentUserService;
    }

    public async Task<GetGridLayoutResponse> Handle(GetGridLayoutRequest request, CancellationToken cancellationToken)
    {
        var user = _currentUserService.GetCurrentUser();
        var userId = user.Id ?? user.Email ?? "anonymous";

        var entity = await _repository.GetAsync(userId, request.GridKey, cancellationToken);

        if (entity is null)
        {
            return new GetGridLayoutResponse { Layout = null };
        }

        var dto = JsonSerializer.Deserialize<GridLayoutDto>(entity.LayoutJson) ?? new GridLayoutDto();
        dto.GridKey = entity.GridKey;
        dto.LastModified = entity.LastModified;

        return new GetGridLayoutResponse { Layout = dto };
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~GetGridLayoutHandlerTests"
```
Expected: 2 tests pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/
git add backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs
git commit -m "feat(grid-layout): add GetGridLayout handler with tests"
```

---

## Task 5: SaveGridLayout handler + tests

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/GridLayouts/SaveGridLayoutHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// backend/test/Anela.Heblo.Tests/Features/GridLayouts/SaveGridLayoutHandlerTests.cs
using System.Text.Json;
using Anela.Heblo.Application.Features.GridLayouts.Contracts;
using Anela.Heblo.Application.Features.GridLayouts.UseCases.SaveGridLayout;
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Domain.Features.Users;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.GridLayouts;

public class SaveGridLayoutHandlerTests
{
    private readonly Mock<IGridLayoutRepository> _repositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();

    private SaveGridLayoutHandler CreateHandler() =>
        new(_repositoryMock.Object, _currentUserMock.Object);

    [Fact]
    public async Task Handle_CallsUpsertWithSerializedColumns()
    {
        _currentUserMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test", "test@test.com", true));

        string? capturedJson = null;
        _repositoryMock
            .Setup(x => x.UpsertAsync("user-1", "test-grid", It.IsAny<string>(), default))
            .Callback<string, string, string, CancellationToken>((_, _, json, _) => capturedJson = json)
            .Returns(Task.CompletedTask);

        var request = new SaveGridLayoutRequest
        {
            GridKey = "test-grid",
            Columns = new List<GridColumnStateDto>
            {
                new() { Id = "col1", Order = 0, Width = 150, Hidden = false },
                new() { Id = "col2", Order = 1, Width = null, Hidden = true }
            }
        };

        var handler = CreateHandler();
        await handler.Handle(request, default);

        _repositoryMock.Verify(x => x.UpsertAsync("user-1", "test-grid", It.IsAny<string>(), default), Times.Once);
        Assert.NotNull(capturedJson);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(capturedJson!);
        Assert.True(parsed!.ContainsKey("columns"));
    }
}
```

- [ ] **Step 2: Run to verify compilation failure**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~SaveGridLayoutHandlerTests"
```
Expected: compilation error.

- [ ] **Step 3: Create request and response**

```csharp
// backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutRequest.cs
using Anela.Heblo.Application.Features.GridLayouts.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.GridLayouts.UseCases.SaveGridLayout;

public class SaveGridLayoutRequest : IRequest<SaveGridLayoutResponse>
{
    public string GridKey { get; set; } = string.Empty;
    public List<GridColumnStateDto> Columns { get; set; } = new();
}
```

```csharp
// backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutResponse.cs
namespace Anela.Heblo.Application.Features.GridLayouts.UseCases.SaveGridLayout;

public class SaveGridLayoutResponse { }
```

- [ ] **Step 4: Create handler**

```csharp
// backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/SaveGridLayoutHandler.cs
using System.Text.Json;
using Anela.Heblo.Application.Features.GridLayouts.Contracts;
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.GridLayouts.UseCases.SaveGridLayout;

public class SaveGridLayoutHandler : IRequestHandler<SaveGridLayoutRequest, SaveGridLayoutResponse>
{
    private readonly IGridLayoutRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public SaveGridLayoutHandler(IGridLayoutRepository repository, ICurrentUserService currentUserService)
    {
        _repository = repository;
        _currentUserService = currentUserService;
    }

    public async Task<SaveGridLayoutResponse> Handle(SaveGridLayoutRequest request, CancellationToken cancellationToken)
    {
        var user = _currentUserService.GetCurrentUser();
        var userId = user.Id ?? user.Email ?? "anonymous";

        var payload = new GridLayoutDto
        {
            GridKey = request.GridKey,
            Columns = request.Columns
        };

        var json = JsonSerializer.Serialize(payload);
        await _repository.UpsertAsync(userId, request.GridKey, json, cancellationToken);

        return new SaveGridLayoutResponse();
    }
}
```

- [ ] **Step 5: Run tests**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~SaveGridLayoutHandlerTests"
```
Expected: 1 test passes.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/SaveGridLayout/
git add backend/test/Anela.Heblo.Tests/Features/GridLayouts/SaveGridLayoutHandlerTests.cs
git commit -m "feat(grid-layout): add SaveGridLayout handler with tests"
```

---

## Task 6: ResetGridLayout handler + tests

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/ResetGridLayout/ResetGridLayoutRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/ResetGridLayout/ResetGridLayoutResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/ResetGridLayout/ResetGridLayoutHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/GridLayouts/ResetGridLayoutHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// backend/test/Anela.Heblo.Tests/Features/GridLayouts/ResetGridLayoutHandlerTests.cs
using Anela.Heblo.Application.Features.GridLayouts.UseCases.ResetGridLayout;
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Domain.Features.Users;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.GridLayouts;

public class ResetGridLayoutHandlerTests
{
    private readonly Mock<IGridLayoutRepository> _repositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();

    private ResetGridLayoutHandler CreateHandler() =>
        new(_repositoryMock.Object, _currentUserMock.Object);

    [Fact]
    public async Task Handle_CallsDeleteWithCorrectUserAndGrid()
    {
        _currentUserMock.Setup(x => x.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test", "test@test.com", true));
        _repositoryMock.Setup(x => x.DeleteAsync("user-1", "test-grid", default)).Returns(Task.CompletedTask);

        var handler = CreateHandler();
        await handler.Handle(new ResetGridLayoutRequest { GridKey = "test-grid" }, default);

        _repositoryMock.Verify(x => x.DeleteAsync("user-1", "test-grid", default), Times.Once);
    }
}
```

- [ ] **Step 2: Run to verify compilation failure**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~ResetGridLayoutHandlerTests"
```
Expected: compilation error.

- [ ] **Step 3: Create request, response, and handler**

```csharp
// backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/ResetGridLayout/ResetGridLayoutRequest.cs
using MediatR;

namespace Anela.Heblo.Application.Features.GridLayouts.UseCases.ResetGridLayout;

public class ResetGridLayoutRequest : IRequest<ResetGridLayoutResponse>
{
    public string GridKey { get; set; } = string.Empty;
}
```

```csharp
// backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/ResetGridLayout/ResetGridLayoutResponse.cs
namespace Anela.Heblo.Application.Features.GridLayouts.UseCases.ResetGridLayout;

public class ResetGridLayoutResponse { }
```

```csharp
// backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/ResetGridLayout/ResetGridLayoutHandler.cs
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.GridLayouts.UseCases.ResetGridLayout;

public class ResetGridLayoutHandler : IRequestHandler<ResetGridLayoutRequest, ResetGridLayoutResponse>
{
    private readonly IGridLayoutRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public ResetGridLayoutHandler(IGridLayoutRepository repository, ICurrentUserService currentUserService)
    {
        _repository = repository;
        _currentUserService = currentUserService;
    }

    public async Task<ResetGridLayoutResponse> Handle(ResetGridLayoutRequest request, CancellationToken cancellationToken)
    {
        var user = _currentUserService.GetCurrentUser();
        var userId = user.Id ?? user.Email ?? "anonymous";

        await _repository.DeleteAsync(userId, request.GridKey, cancellationToken);

        return new ResetGridLayoutResponse();
    }
}
```

- [ ] **Step 4: Run tests**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet test test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~ResetGridLayoutHandlerTests"
```
Expected: 1 test passes.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/ResetGridLayout/
git add backend/test/Anela.Heblo.Tests/Features/GridLayouts/ResetGridLayoutHandlerTests.cs
git commit -m "feat(grid-layout): add ResetGridLayout handler with tests"
```

---

## Task 7: Module registration + DI wiring

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/GridLayouts/GridLayoutsModule.cs`
- Modify: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Create application module**

Look at any existing `*Module.cs` file (e.g. `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs`) to follow the pattern. Typically it's an extension method on `IServiceCollection`. Create:

```csharp
// backend/src/Anela.Heblo.Application/Features/GridLayouts/GridLayoutsModule.cs
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.GridLayouts;

public static class GridLayoutsModule
{
    public static IServiceCollection AddGridLayoutsModule(this IServiceCollection services)
    {
        // MediatR handlers are auto-registered via AddMediatR assembly scanning.
        // Add any GridLayouts-specific services here if needed in the future.
        return services;
    }
}
```

- [ ] **Step 2: Register in ServiceCollectionExtensions**

Open `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`. Find where other feature modules are registered (search for `AddDashboardModule` or similar). Add:

```csharp
services.AddGridLayoutsModule();
```

Add the using:
```csharp
using Anela.Heblo.Application.Features.GridLayouts;
```

- [ ] **Step 3: Build entire solution**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build
```
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/GridLayouts/GridLayoutsModule.cs
git add backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat(grid-layout): register GridLayouts module in DI"
```

---

## Task 8: API Controller

**Files:**
- Create: `backend/src/Anela.Heblo.API/Controllers/GridLayoutsController.cs`

- [ ] **Step 1: Create the controller**

Model after `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs` but use `ICurrentUserService` via the base class / constructor injection instead of reading claims inline.

```csharp
// backend/src/Anela.Heblo.API/Controllers/GridLayoutsController.cs
using Anela.Heblo.Application.Features.GridLayouts.Contracts;
using Anela.Heblo.Application.Features.GridLayouts.UseCases.GetGridLayout;
using Anela.Heblo.Application.Features.GridLayouts.UseCases.ResetGridLayout;
using Anela.Heblo.Application.Features.GridLayouts.UseCases.SaveGridLayout;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GridLayoutsController : BaseApiController
{
    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUserService;

    public GridLayoutsController(IMediator mediator, ICurrentUserService currentUserService)
    {
        _mediator = mediator;
        _currentUserService = currentUserService;
    }

    [HttpGet("{gridKey}")]
    public async Task<ActionResult<GridLayoutDto?>> Get(string gridKey)
    {
        var request = new GetGridLayoutRequest { GridKey = gridKey };
        var response = await _mediator.Send(request);
        return Ok(response.Layout);
    }

    [HttpPut("{gridKey}")]
    public async Task<ActionResult> Save(string gridKey, [FromBody] SaveGridLayoutRequest body)
    {
        body.GridKey = gridKey;
        await _mediator.Send(body);
        return Ok();
    }

    [HttpDelete("{gridKey}")]
    public async Task<ActionResult> Reset(string gridKey)
    {
        var request = new ResetGridLayoutRequest { GridKey = gridKey };
        await _mediator.Send(request);
        return Ok();
    }
}
```

- [ ] **Step 2: Build and format**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build
dotnet format --verify-no-changes
```
Expected: `Build succeeded.` and no format violations. If format fails, run `dotnet format` (without `--verify-no-changes`) and inspect the changes.

- [ ] **Step 3: Run all backend tests**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet test test/Anela.Heblo.Tests/
```
Expected: all tests pass (including the 29 existing MCP tests + 4 new GridLayouts tests).

- [ ] **Step 4: Regenerate API client**

The NSwag postbuild step runs automatically on `dotnet build`. Verify the generated client was updated:

```bash
grep -n "gridLayouts\|GridLayouts" /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend/src/api/generated/api-client.ts | head -20
```
Expected: output shows new methods for the GridLayouts endpoints.

If the client is not regenerated automatically, check `docs/development/api-client-generation.md` for the manual generation command.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/GridLayoutsController.cs
git commit -m "feat(grid-layout): add GridLayoutsController with GET/PUT/DELETE endpoints"
```

---

## Task 9: Frontend types

**Files:**
- Create: `frontend/src/features/grid-layout/types.ts`

- [ ] **Step 1: Create types**

```typescript
// frontend/src/features/grid-layout/types.ts

export type ColumnAlign = 'left' | 'right' | 'center';

export interface GridColumn<TRow> {
  /** Stable key used in persisted state. Never change this once deployed. */
  id: string;
  header: React.ReactNode;
  defaultWidth?: number;   // px; undefined = no explicit width set
  minWidth?: number;       // px; default 60
  align?: ColumnAlign;
  /** Sort key passed to the page's onSort callback. Omit for unsortable columns. */
  sortBy?: string;
  /** If false, column cannot be toggled in the chooser and is always visible. Default: true */
  canHide?: boolean;
  /** If false, column cannot be dragged to a new position. Default: true */
  canReorder?: boolean;
  /** If false, no resize handle is rendered. Default: true */
  canResize?: boolean;
  renderCell: (row: TRow) => React.ReactNode;
  /** Extra Tailwind classes for the <th> */
  headerClassName?: string;
  /** Extra Tailwind classes for each <td> in this column */
  cellClassName?: string;
}

export interface GridColumnState {
  id: string;
  order: number;
  width?: number;    // px; undefined = use column defaultWidth
  hidden: boolean;
}

export interface GridLayoutPayload {
  columns: GridColumnState[];
}
```

- [ ] **Step 2: Commit**

```bash
git add frontend/src/features/grid-layout/types.ts
git commit -m "feat(grid-layout): add frontend GridColumn and GridColumnState types"
```

---

## Task 10: useGridLayout hook + tests

**Files:**
- Create: `frontend/src/features/grid-layout/useGridLayout.ts`
- Create: `frontend/src/features/grid-layout/__tests__/useGridLayout.test.ts`

- [ ] **Step 1: Write the failing tests**

```typescript
// frontend/src/features/grid-layout/__tests__/useGridLayout.test.ts
import { renderHook, act, waitFor } from '@testing-library/react';
import { useGridLayout } from '../useGridLayout';
import { GridColumn } from '../types';
import { getAuthenticatedApiClient } from '../../../api/apiClient';

jest.mock('../../../api/apiClient');

const mockFetch = jest.fn();
const mockBaseUrl = 'http://localhost:5001';

const mockColumns: GridColumn<{ id: string }>[] = [
  { id: 'name', header: 'Name', canHide: false, canReorder: false, renderCell: (r) => r.id },
  { id: 'stock', header: 'Stock', defaultWidth: 100, renderCell: (r) => r.id },
  { id: 'reserve', header: 'Reserve', defaultWidth: 80, renderCell: (r) => r.id },
];

beforeEach(() => {
  jest.clearAllMocks();
  (getAuthenticatedApiClient as jest.Mock).mockResolvedValue({
    baseUrl: mockBaseUrl,
    http: { fetch: mockFetch },
  });
});

describe('useGridLayout — merge behavior', () => {
  it('uses default order/visibility when no saved layout', async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, json: async () => null });
    const { result } = renderHook(() => useGridLayout('test-grid', mockColumns));
    await waitFor(() => expect(result.current.isLoaded).toBe(true));

    expect(result.current.orderedColumns.map((c) => c.id)).toEqual(['name', 'stock', 'reserve']);
    expect(result.current.columnState.every((c) => !c.hidden)).toBe(true);
  });

  it('applies saved order from backend', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        columns: [
          { id: 'name', order: 0, hidden: false },
          { id: 'reserve', order: 1, hidden: false },
          { id: 'stock', order: 2, hidden: false },
        ],
      }),
    });

    const { result } = renderHook(() => useGridLayout('test-grid', mockColumns));
    await waitFor(() => expect(result.current.isLoaded).toBe(true));

    expect(result.current.orderedColumns.map((c) => c.id)).toEqual(['name', 'reserve', 'stock']);
  });

  it('appends new columns not in saved layout at the end', async () => {
    // Saved layout only knows about 'name' and 'stock' — 'reserve' was added later in code
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        columns: [
          { id: 'name', order: 0, hidden: false },
          { id: 'stock', order: 1, hidden: false },
        ],
      }),
    });

    const { result } = renderHook(() => useGridLayout('test-grid', mockColumns));
    await waitFor(() => expect(result.current.isLoaded).toBe(true));

    // 'reserve' should be appended at the end
    expect(result.current.orderedColumns[2].id).toBe('reserve');
  });

  it('forces pinned column visible even if saved as hidden', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        columns: [
          { id: 'name', order: 0, hidden: true },  // canHide: false — must stay visible
          { id: 'stock', order: 1, hidden: false },
          { id: 'reserve', order: 2, hidden: false },
        ],
      }),
    });

    const { result } = renderHook(() => useGridLayout('test-grid', mockColumns));
    await waitFor(() => expect(result.current.isLoaded).toBe(true));

    const nameState = result.current.columnState.find((c) => c.id === 'name');
    expect(nameState?.hidden).toBe(false);
  });
});

describe('useGridLayout — mutators', () => {
  beforeEach(() => {
    jest.useFakeTimers();
    mockFetch.mockResolvedValue({ ok: true, json: async () => null });
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('toggleColumnVisibility hides a column immediately', async () => {
    const { result } = renderHook(() => useGridLayout('test-grid', mockColumns));
    await waitFor(() => expect(result.current.isLoaded).toBe(true));

    act(() => { result.current.toggleColumnVisibility('stock'); });

    const stockState = result.current.columnState.find((c) => c.id === 'stock');
    expect(stockState?.hidden).toBe(true);
  });

  it('toggleColumnVisibility does nothing for canHide:false columns', async () => {
    const { result } = renderHook(() => useGridLayout('test-grid', mockColumns));
    await waitFor(() => expect(result.current.isLoaded).toBe(true));

    act(() => { result.current.toggleColumnVisibility('name'); });

    const nameState = result.current.columnState.find((c) => c.id === 'name');
    expect(nameState?.hidden).toBe(false);
  });

  it('debounces save — does not call PUT immediately', async () => {
    const { result } = renderHook(() => useGridLayout('test-grid', mockColumns));
    await waitFor(() => expect(result.current.isLoaded).toBe(true));

    const callsBefore = mockFetch.mock.calls.length;
    act(() => { result.current.toggleColumnVisibility('stock'); });
    // No PUT yet
    expect(mockFetch.mock.calls.length).toBe(callsBefore);
  });

  it('calls PUT after debounce delay', async () => {
    const { result } = renderHook(() => useGridLayout('test-grid', mockColumns));
    await waitFor(() => expect(result.current.isLoaded).toBe(true));

    mockFetch.mockResolvedValue({ ok: true });
    act(() => { result.current.toggleColumnVisibility('stock'); });

    act(() => { jest.advanceTimersByTime(600); });
    await waitFor(() => {
      const putCalls = mockFetch.mock.calls.filter((c) =>
        c[0]?.includes('grid-layouts') && c[1]?.method === 'PUT'
      );
      expect(putCalls.length).toBeGreaterThan(0);
    });
  });
});
```

- [ ] **Step 2: Run to verify failure**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npm test -- --watchAll=false --testPathPattern="useGridLayout"
```
Expected: compilation/import error — `useGridLayout` doesn't exist.

- [ ] **Step 3: Create the hook**

```typescript
// frontend/src/features/grid-layout/useGridLayout.ts
import { useCallback, useEffect, useRef, useState } from 'react';
import { getAuthenticatedApiClient } from '../../api/apiClient';
import { GridColumn, GridColumnState, GridLayoutPayload } from './types';

const DEBOUNCE_MS = 500;

function buildDefaultState<TRow>(columns: GridColumn<TRow>[]): GridColumnState[] {
  return columns.map((col, index) => ({
    id: col.id,
    order: index,
    width: col.defaultWidth,
    hidden: false,
  }));
}

function mergeStates<TRow>(
  columns: GridColumn<TRow>[],
  saved: GridColumnState[]
): GridColumnState[] {
  const savedMap = new Map(saved.map((s) => [s.id, s]));
  const knownIds = new Set(columns.map((c) => c.id));

  // Start from columns defined in code — apply saved overrides
  const merged: GridColumnState[] = columns.map((col, fallbackOrder) => {
    const s = savedMap.get(col.id);
    const hidden = col.canHide === false ? false : (s?.hidden ?? false);
    return {
      id: col.id,
      order: s?.order ?? fallbackOrder,
      width: s?.width ?? col.defaultWidth,
      hidden,
    };
  });

  // Append saved entries for columns removed from code (they're silently dropped — we just skip them)
  // New columns not in saved layout: already handled above via fallback order

  merged.sort((a, b) => a.order - b.order);

  // Re-number order sequentially (gaps can appear after merging)
  return merged.map((s, i) => ({ ...s, order: i }));
}

export function useGridLayout<TRow>(gridKey: string, columns: GridColumn<TRow>[]) {
  const [columnState, setColumnState] = useState<GridColumnState[]>([]);
  const [isLoaded, setIsLoaded] = useState(false);
  const debounceTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const columnsRef = useRef(columns);

  useEffect(() => {
    columnsRef.current = columns;
  }, [columns]);

  // Load from backend on mount
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const apiClient = await getAuthenticatedApiClient();
        const baseUrl: string = (apiClient as any).baseUrl;
        const fullUrl = `${baseUrl}/api/grid-layouts/${encodeURIComponent(gridKey)}`;
        const response = await (apiClient as any).http.fetch(fullUrl, { method: 'GET' });
        if (cancelled) return;

        let payload: GridLayoutPayload | null = null;
        if (response.ok) {
          payload = await response.json();
        }

        const state = payload?.columns
          ? mergeStates(columnsRef.current, payload.columns)
          : buildDefaultState(columnsRef.current);

        setColumnState(state);
      } catch {
        // fallback to defaults on network error
        setColumnState(buildDefaultState(columnsRef.current));
      } finally {
        if (!cancelled) setIsLoaded(true);
      }
    })();
    return () => { cancelled = true; };
  }, [gridKey]); // eslint-disable-line react-hooks/exhaustive-deps

  const scheduleSave = useCallback((nextState: GridColumnState[]) => {
    if (debounceTimer.current) clearTimeout(debounceTimer.current);
    debounceTimer.current = setTimeout(async () => {
      try {
        const apiClient = await getAuthenticatedApiClient();
        const baseUrl: string = (apiClient as any).baseUrl;
        const fullUrl = `${baseUrl}/api/grid-layouts/${encodeURIComponent(gridKey)}`;
        await (apiClient as any).http.fetch(fullUrl, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ columns: nextState }),
        });
      } catch {
        // silently ignore save errors — user's visual state is already updated
      }
    }, DEBOUNCE_MS);
  }, [gridKey]);

  const toggleColumnVisibility = useCallback((id: string) => {
    const col = columnsRef.current.find((c) => c.id === id);
    if (col?.canHide === false) return;

    setColumnState((prev) => {
      const next = prev.map((s) => s.id === id ? { ...s, hidden: !s.hidden } : s);
      scheduleSave(next);
      return next;
    });
  }, [scheduleSave]);

  const setColumnWidth = useCallback((id: string, width: number) => {
    const col = columnsRef.current.find((c) => c.id === id);
    if (col?.canResize === false) return;

    setColumnState((prev) => {
      const next = prev.map((s) => s.id === id ? { ...s, width } : s);
      scheduleSave(next);
      return next;
    });
  }, [scheduleSave]);

  const setColumnOrder = useCallback((newOrderIds: string[]) => {
    setColumnState((prev) => {
      const next = newOrderIds.map((id, index) => {
        const existing = prev.find((s) => s.id === id) ?? { id, hidden: false };
        return { ...existing, order: index };
      });
      scheduleSave(next);
      return next;
    });
  }, [scheduleSave]);

  const resetLayout = useCallback(async () => {
    if (debounceTimer.current) clearTimeout(debounceTimer.current);
    try {
      const apiClient = await getAuthenticatedApiClient();
      const baseUrl: string = (apiClient as any).baseUrl;
      const fullUrl = `${baseUrl}/api/grid-layouts/${encodeURIComponent(gridKey)}`;
      await (apiClient as any).http.fetch(fullUrl, { method: 'DELETE' });
    } catch {
      // ignore
    }
    setColumnState(buildDefaultState(columnsRef.current));
  }, [gridKey]);

  // Derived: visible + ordered columns
  const orderedColumns = columnState
    .filter((s) => !s.hidden)
    .sort((a, b) => a.order - b.order)
    .map((s) => columnsRef.current.find((c) => c.id === s.id)!)
    .filter(Boolean);

  return {
    orderedColumns,
    columnState,
    setColumnOrder,
    setColumnWidth,
    toggleColumnVisibility,
    resetLayout,
    isLoaded,
  };
}
```

- [ ] **Step 4: Run tests**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npm test -- --watchAll=false --testPathPattern="useGridLayout"
```
Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/features/grid-layout/useGridLayout.ts
git add frontend/src/features/grid-layout/__tests__/useGridLayout.test.ts
git commit -m "feat(grid-layout): add useGridLayout hook with merge, mutate, and debounce-save"
```

---

## Task 11: GridHeader component

**Files:**
- Create: `frontend/src/features/grid-layout/GridHeader.tsx`

> This component has complex visual behavior (drag, resize, sort indicators). No automated test here — covered by page-level tests in Tasks 14–15.

- [ ] **Step 1: Create the component**

```tsx
// frontend/src/features/grid-layout/GridHeader.tsx
import React, { useRef } from 'react';
import {
  DndContext,
  DragEndEvent,
  PointerSensor,
  useSensor,
  useSensors,
} from '@dnd-kit/core';
import {
  SortableContext,
  horizontalListSortingStrategy,
  useSortable,
  arrayMove,
} from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { ChevronDown, ChevronUp, GripVertical } from 'lucide-react';
import { GridColumn, GridColumnState } from './types';

interface SortableHeaderCellProps<TRow> {
  column: GridColumn<TRow>;
  state: GridColumnState;
  sortBy?: string;
  sortDescending?: boolean;
  activeSortKey?: string;
  onSort?: (sortKey: string) => void;
  onResizeEnd?: (id: string, newWidth: number) => void;
}

function SortableHeaderCell<TRow>({
  column,
  state,
  activeSortKey,
  sortDescending,
  onSort,
  onResizeEnd,
}: SortableHeaderCellProps<TRow>) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } =
    useSortable({ id: column.id, disabled: column.canReorder === false });

  const resizeStartX = useRef<number | null>(null);
  const resizeStartWidth = useRef<number>(state.width ?? column.defaultWidth ?? 100);

  const isActive = column.sortBy !== undefined && activeSortKey === column.sortBy;
  const isAscending = isActive && !sortDescending;
  const isDescending = isActive && sortDescending;

  const width = state.width ?? column.defaultWidth;
  const minWidth = column.minWidth ?? 60;

  const style: React.CSSProperties = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.5 : undefined,
    width: width ? `${width}px` : undefined,
    minWidth: `${minWidth}px`,
    position: 'relative',
  };

  const alignClass =
    column.align === 'right'
      ? 'text-right'
      : column.align === 'center'
      ? 'text-center'
      : 'text-left';

  const handleMouseDownResize = (e: React.MouseEvent) => {
    e.preventDefault();
    e.stopPropagation();
    resizeStartX.current = e.clientX;
    resizeStartWidth.current = state.width ?? column.defaultWidth ?? 100;

    const onMouseMove = (ev: MouseEvent) => {
      if (resizeStartX.current === null) return;
      const dx = ev.clientX - resizeStartX.current;
      const newWidth = Math.max(minWidth, resizeStartWidth.current + dx);
      onResizeEnd?.(column.id, newWidth);
    };
    const onMouseUp = () => {
      resizeStartX.current = null;
      window.removeEventListener('mousemove', onMouseMove);
      window.removeEventListener('mouseup', onMouseUp);
    };
    window.addEventListener('mousemove', onMouseMove);
    window.addEventListener('mouseup', onMouseUp);
  };

  return (
    <th
      ref={setNodeRef}
      style={style}
      scope="col"
      className={`px-4 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider select-none ${alignClass} ${column.headerClassName ?? ''}`}
      onClick={() => column.sortBy && onSort?.(column.sortBy)}
    >
      <div className={`flex items-center gap-1 ${column.align === 'right' ? 'justify-end' : ''}`}>
        {column.canReorder !== false && (
          <span
            {...attributes}
            {...listeners}
            className="text-gray-300 hover:text-gray-500 cursor-grab active:cursor-grabbing flex-shrink-0"
            onClick={(e) => e.stopPropagation()}
          >
            <GripVertical className="h-3 w-3" />
          </span>
        )}
        <span className={column.sortBy ? 'cursor-pointer hover:text-gray-700' : ''}>
          {column.header}
        </span>
        {column.sortBy && (
          <div className="flex flex-col flex-shrink-0">
            <ChevronUp className={`h-3 w-3 ${isAscending ? 'text-indigo-600' : 'text-gray-300'}`} />
            <ChevronDown className={`h-3 w-3 -mt-1 ${isDescending ? 'text-indigo-600' : 'text-gray-300'}`} />
          </div>
        )}
      </div>
      {column.canResize !== false && (
        <div
          className="absolute right-0 top-0 h-full w-1.5 cursor-col-resize hover:bg-indigo-200"
          onMouseDown={handleMouseDownResize}
          onClick={(e) => e.stopPropagation()}
        />
      )}
    </th>
  );
}

interface GridHeaderProps<TRow> {
  columns: GridColumn<TRow>[];
  columnState: GridColumnState[];
  activeSortKey?: string;
  sortDescending?: boolean;
  onSort?: (sortKey: string) => void;
  onReorder?: (newOrderIds: string[]) => void;
  onResizeEnd?: (id: string, newWidth: number) => void;
}

export function GridHeader<TRow>({
  columns,
  columnState,
  activeSortKey,
  sortDescending,
  onSort,
  onReorder,
  onResizeEnd,
}: GridHeaderProps<TRow>) {
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 5 } }));

  const visibleIds = columns.map((c) => c.id);

  const handleDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;
    if (!over || active.id === over.id) return;

    const oldIndex = visibleIds.indexOf(active.id as string);
    const newIndex = visibleIds.indexOf(over.id as string);
    const newOrder = arrayMove(visibleIds, oldIndex, newIndex);
    onReorder?.(newOrder);
  };

  return (
    <DndContext sensors={sensors} onDragEnd={handleDragEnd}>
      <SortableContext items={visibleIds} strategy={horizontalListSortingStrategy}>
        <thead className="bg-gray-50 sticky top-0 z-10">
          <tr>
            {columns.map((col) => {
              const state = columnState.find((s) => s.id === col.id) ?? {
                id: col.id,
                order: 0,
                hidden: false,
                width: col.defaultWidth,
              };
              return (
                <SortableHeaderCell
                  key={col.id}
                  column={col}
                  state={state}
                  activeSortKey={activeSortKey}
                  sortDescending={sortDescending}
                  onSort={onSort}
                  onResizeEnd={onResizeEnd}
                />
              );
            })}
          </tr>
        </thead>
      </SortableContext>
    </DndContext>
  );
}
```

- [ ] **Step 2: Build frontend to check TypeScript**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npm run build 2>&1 | tail -30
```
Expected: build succeeds (TypeScript errors would appear here).

- [ ] **Step 3: Commit**

```bash
git add frontend/src/features/grid-layout/GridHeader.tsx
git commit -m "feat(grid-layout): add GridHeader with drag-to-reorder and column resize"
```

---

## Task 12: ColumnChooser component + tests

**Files:**
- Create: `frontend/src/features/grid-layout/ColumnChooser.tsx`
- Create: `frontend/src/features/grid-layout/__tests__/ColumnChooser.test.tsx`

- [ ] **Step 1: Write failing tests**

```tsx
// frontend/src/features/grid-layout/__tests__/ColumnChooser.test.tsx
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { ColumnChooser } from '../ColumnChooser';
import { GridColumn, GridColumnState } from '../types';

const mockColumns: GridColumn<{ id: string }>[] = [
  { id: 'name', header: 'Produkt', canHide: false, canReorder: false, renderCell: (r) => r.id },
  { id: 'stock', header: 'Skladem', renderCell: (r) => r.id },
  { id: 'reserve', header: 'Rezerva', renderCell: (r) => r.id },
];

const defaultState: GridColumnState[] = [
  { id: 'name', order: 0, hidden: false },
  { id: 'stock', order: 1, hidden: false },
  { id: 'reserve', order: 2, hidden: true },
];

function renderChooser(
  onToggle = jest.fn(),
  onReset = jest.fn()
) {
  render(
    <ColumnChooser
      columns={mockColumns}
      columnState={defaultState}
      onToggle={onToggle}
      onReset={onReset}
    />
  );
}

test('opens popover and shows column list on trigger click', () => {
  renderChooser();
  fireEvent.click(screen.getByRole('button', { name: /sloupce/i }));
  expect(screen.getByText('Skladem')).toBeInTheDocument();
  expect(screen.getByText('Rezerva')).toBeInTheDocument();
});

test('does not show canHide:false columns in the list', () => {
  renderChooser();
  fireEvent.click(screen.getByRole('button', { name: /sloupce/i }));
  expect(screen.queryByText('Produkt')).not.toBeInTheDocument();
});

test('calls onToggle when a column checkbox is clicked', () => {
  const onToggle = jest.fn();
  renderChooser(onToggle);
  fireEvent.click(screen.getByRole('button', { name: /sloupce/i }));
  fireEvent.click(screen.getByLabelText('Skladem'));
  expect(onToggle).toHaveBeenCalledWith('stock');
});

test('hidden columns have unchecked checkbox', () => {
  renderChooser();
  fireEvent.click(screen.getByRole('button', { name: /sloupce/i }));
  const reservaCheckbox = screen.getByLabelText('Rezerva') as HTMLInputElement;
  expect(reservaCheckbox.checked).toBe(false);
});

test('calls onReset when reset button clicked', () => {
  const onReset = jest.fn();
  renderChooser(jest.fn(), onReset);
  fireEvent.click(screen.getByRole('button', { name: /sloupce/i }));
  fireEvent.click(screen.getByRole('button', { name: /reset/i }));
  expect(onReset).toHaveBeenCalledTimes(1);
});
```

- [ ] **Step 2: Run to verify failure**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npm test -- --watchAll=false --testPathPattern="ColumnChooser"
```
Expected: compilation error — `ColumnChooser` doesn't exist.

- [ ] **Step 3: Create the component**

```tsx
// frontend/src/features/grid-layout/ColumnChooser.tsx
import React, { useState } from 'react';
import { Settings2 } from 'lucide-react';
import { GridColumn, GridColumnState } from './types';

interface ColumnChooserProps<TRow> {
  columns: GridColumn<TRow>[];
  columnState: GridColumnState[];
  onToggle: (id: string) => void;
  onReset: () => void;
}

export function ColumnChooser<TRow>({ columns, columnState, onToggle, onReset }: ColumnChooserProps<TRow>) {
  const [open, setOpen] = useState(false);

  const hiddableColumns = columns.filter((c) => c.canHide !== false);

  const isHidden = (id: string) =>
    columnState.find((s) => s.id === id)?.hidden ?? false;

  return (
    <div className="relative">
      <button
        type="button"
        className="flex items-center gap-1 px-3 py-2 text-sm text-gray-600 border border-gray-300 rounded-md hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-indigo-500"
        onClick={() => setOpen((v) => !v)}
        aria-label="Sloupce"
      >
        <Settings2 className="h-4 w-4" />
        <span>Sloupce</span>
      </button>

      {open && (
        <>
          {/* Backdrop */}
          <div
            className="fixed inset-0 z-20"
            onClick={() => setOpen(false)}
          />
          <div className="absolute right-0 z-30 mt-1 w-52 bg-white border border-gray-200 rounded-md shadow-lg">
            <div className="p-3 space-y-2 max-h-72 overflow-y-auto">
              {hiddableColumns.map((col) => {
                const id = `col-chooser-${col.id}`;
                return (
                  <label key={col.id} htmlFor={id} className="flex items-center gap-2 cursor-pointer text-sm text-gray-700 hover:text-gray-900">
                    <input
                      id={id}
                      type="checkbox"
                      className="h-4 w-4 text-indigo-600 rounded border-gray-300 focus:ring-indigo-500"
                      checked={!isHidden(col.id)}
                      onChange={() => onToggle(col.id)}
                      aria-label={typeof col.header === 'string' ? col.header : col.id}
                    />
                    {col.header}
                  </label>
                );
              })}
            </div>
            <div className="border-t border-gray-100 p-2">
              <button
                type="button"
                className="w-full text-sm text-gray-500 hover:text-gray-700 px-2 py-1 rounded hover:bg-gray-50"
                onClick={() => { onReset(); setOpen(false); }}
              >
                Reset rozvržení
              </button>
            </div>
          </div>
        </>
      )}
    </div>
  );
}
```

- [ ] **Step 4: Run tests**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npm test -- --watchAll=false --testPathPattern="ColumnChooser"
```
Expected: all 5 tests pass.

- [ ] **Step 5: Create barrel export**

```typescript
// frontend/src/features/grid-layout/index.ts
export { useGridLayout } from './useGridLayout';
export { GridHeader } from './GridHeader';
export { ColumnChooser } from './ColumnChooser';
export type { GridColumn, GridColumnState, GridLayoutPayload, ColumnAlign } from './types';
```

- [ ] **Step 6: Commit**

```bash
git add frontend/src/features/grid-layout/ColumnChooser.tsx
git add frontend/src/features/grid-layout/__tests__/ColumnChooser.test.tsx
git add frontend/src/features/grid-layout/index.ts
git commit -m "feat(grid-layout): add ColumnChooser popover and barrel export"
```

---

## Task 13: Refactor ManufacturingStockAnalysis

**Files:**
- Modify: `frontend/src/components/pages/ManufacturingStockAnalysis.tsx`
- Modify: `frontend/src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx`

> This file is 1,661 lines. The change is surgical: (1) add a `columns` const at module scope, (2) replace the `<thead>` block (lines ~1218–1314) with `<GridHeader>`, (3) replace the `<tr>` body map (lines ~1316–1542) to iterate `orderedColumns`, (4) add the hook call, (5) add `<ColumnChooser>` to the controls toolbar. Delete the local `SortableHeader` component (lines 263–294).

- [ ] **Step 1: Run existing tests to establish baseline**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npm test -- --watchAll=false --testPathPattern="ManufacturingStockAnalysis"
```
Note how many tests pass.

- [ ] **Step 2: Add column definitions above the component (at top of file, after imports)**

Add the import near the top:
```typescript
import { GridColumn, GridHeader, ColumnChooser, useGridLayout } from '../../features/grid-layout';
import { ManufacturingStockSortBy } from '../../api/generated/api-client';
```

Add before the component function:
```typescript
type StockItem = /* use whatever type the page already uses — look for the type of elements in tableData */ any;

const manufacturingStockColumns: GridColumn<StockItem>[] = [
  {
    id: 'product',
    header: 'Produkt',
    canHide: false,
    canReorder: false,
    minWidth: 200,
    defaultWidth: 276,
    align: 'left',
    renderCell: (item) => (
      /* Move the existing <td> content for the product column here exactly as-is */
      null // placeholder — fill in during refactor
    ),
  },
  {
    id: 'indicator',
    header: '',
    canHide: false,
    canReorder: false,
    minWidth: 60,
    defaultWidth: 60,
    align: 'center',
    canResize: false,
    renderCell: (item) => null, // placeholder
  },
  { id: 'currentStock',        header: 'Sklad',      sortBy: ManufacturingStockSortBy.CurrentStock,        align: 'right', minWidth: 60, defaultWidth: 144, renderCell: (item) => null },
  { id: 'reserve',             header: 'Rezerv',     sortBy: ManufacturingStockSortBy.Reserve,             align: 'right', minWidth: 60, defaultWidth: 120, renderCell: (item) => null },
  { id: 'quarantine',          header: 'Karant.',    sortBy: ManufacturingStockSortBy.Quarantine,          align: 'right', minWidth: 60, defaultWidth: 120, renderCell: (item) => null },
  { id: 'planned',             header: 'Plán',       sortBy: ManufacturingStockSortBy.Planned,             align: 'right', minWidth: 60, defaultWidth: 120, renderCell: (item) => null },
  { id: 'salesInPeriod',       header: 'Prodeje',    sortBy: ManufacturingStockSortBy.SalesInPeriod,       align: 'right', minWidth: 60, defaultWidth: 144, renderCell: (item) => null },
  { id: 'dailySales',          header: 'Prod/den',   sortBy: ManufacturingStockSortBy.DailySales,          align: 'right', minWidth: 100, defaultWidth: 144, renderCell: (item) => null },
  { id: 'stockDaysAvailable',  header: 'NS',         sortBy: ManufacturingStockSortBy.StockDaysAvailable,  align: 'right', minWidth: 90,  defaultWidth: 120, renderCell: (item) => null },
  { id: 'overstockPercentage', header: 'NS %',       sortBy: ManufacturingStockSortBy.OverstockPercentage, align: 'right', minWidth: 90,  defaultWidth: 120, renderCell: (item) => null },
  { id: 'minimumStock',        header: 'Min',        sortBy: ManufacturingStockSortBy.MinimumStock,        align: 'right', minWidth: 90,  defaultWidth: 120, renderCell: (item) => null },
  { id: 'optimalDaysSetup',    header: 'Nastavení',  sortBy: ManufacturingStockSortBy.OptimalDaysSetup,    align: 'right', minWidth: 90,  defaultWidth: 120, renderCell: (item) => null },
  { id: 'batchSize',           header: 'ks/šarže',   sortBy: ManufacturingStockSortBy.BatchSize,           align: 'right', minWidth: 80,  defaultWidth: 96,  renderCell: (item) => null },
];
```

> **Important:** The `renderCell` functions need the actual `<td>` content from the existing row map. Replace the `null` placeholders by cutting the relevant JSX from the `tableData.map(...)` block and pasting it into each `renderCell`. The `item` parameter gives you access to the row data. You will need access to page-level state (e.g. `expandedRows`, `loadingSubgrids`) inside `renderCell` — move the `columns` const *inside* the component function so it closes over those values.

- [ ] **Step 3: Add hook call inside the component function**

Inside `ManufacturingStockAnalysis` component, after existing `useState` declarations, add:

```typescript
const { orderedColumns, columnState, setColumnOrder, setColumnWidth, toggleColumnVisibility, resetLayout, isLoaded } =
  useGridLayout('manufacturing-stock-analysis', manufacturingStockColumns);
```

- [ ] **Step 4: Replace `<thead>` block with `<GridHeader>`**

Find lines ~1218–1314 (the `<thead>...</thead>` block with the hardcoded `SortableHeader` calls) and replace the entire `<thead>` element with:

```tsx
<GridHeader
  columns={orderedColumns}
  columnState={columnState}
  activeSortKey={filters.sortBy}
  sortDescending={filters.sortDescending}
  onSort={handleSort}
  onReorder={setColumnOrder}
  onResizeEnd={setColumnWidth}
/>
```

- [ ] **Step 5: Replace the row body map to iterate orderedColumns**

Find the `<tbody>` block (lines ~1315–1542). The inner `<tr>` currently lists all `<td>` cells explicitly. Replace it so cells are driven by `orderedColumns`:

```tsx
<tbody className="bg-white divide-y divide-gray-200">
  {tableData.map((item) => {
    const hasSubItems = shouldShowExpandButton(item);
    const isExpanded = item.productFamily && expandedRows.has(item.productFamily);
    const isLoading = !!(item.productFamily && loadingSubgrids.has(item.productFamily));

    return (
      <React.Fragment key={item.code}>
        <tr
          className={`${getRowColorClass(item.severity)} cursor-pointer transition-colors duration-150`}
          onClick={(e) => handleRowClick(item, e)}
          title="Klikněte pro zobrazení detailu produktu"
        >
          {orderedColumns.map((col) => {
            const state = columnState.find((s) => s.id === col.id);
            const width = state?.width ?? col.defaultWidth;
            return (
              <td
                key={col.id}
                className={`px-4 py-3 whitespace-nowrap ${col.cellClassName ?? ''}`}
                style={width ? { width: `${width}px`, minWidth: `${col.minWidth ?? 60}px` } : undefined}
              >
                {col.renderCell(item)}
              </td>
            );
          })}
        </tr>
        {/* Keep existing subgrid expansion rows exactly as they are */}
      </React.Fragment>
    );
  })}
</tbody>
```

> The subgrid expansion rows beneath each main row should remain unchanged.

- [ ] **Step 6: Add ColumnChooser to the toolbar**

Find the controls/filter toolbar area (search for `isControlsCollapsed` in the JSX — around line 1100–1190). Add `<ColumnChooser>` next to the existing buttons:

```tsx
<ColumnChooser
  columns={manufacturingStockColumns}
  columnState={columnState}
  onToggle={toggleColumnVisibility}
  onReset={resetLayout}
/>
```

- [ ] **Step 7: Delete the local SortableHeader component**

Remove lines 263–294 (the local `SortableHeader` function component). It is now replaced by `GridHeader`.

Also remove unused imports: `ChevronUp`, `ChevronDown` if they are no longer used elsewhere in the file (check before removing).

- [ ] **Step 8: Run frontend build to check TypeScript**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npm run build 2>&1 | grep -E "error|Error|TS" | head -30
```
Fix any TypeScript errors. Common ones: missing `item` type annotation, incorrect `renderCell` return type (must be `React.ReactNode`).

- [ ] **Step 9: Run frontend tests**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npm test -- --watchAll=false --testPathPattern="ManufacturingStockAnalysis"
```
Fix any broken assertions (typically tests that look for specific `<th>` text like `"Sklad"` — they will now come from `GridHeader` instead of hardcoded JSX). Update assertions to match the new rendering.

- [ ] **Step 10: Lint**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npm run lint
```
Fix any lint errors.

- [ ] **Step 11: Commit**

```bash
git add frontend/src/components/pages/ManufacturingStockAnalysis.tsx
git add frontend/src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx
git commit -m "feat(grid-layout): refactor ManufacturingStockAnalysis to config-driven columns"
```

---

## Task 14: Refactor PurchaseStockAnalysis

**Files:**
- Modify: `frontend/src/components/pages/PurchaseStockAnalysis.tsx`
- Modify: `frontend/src/components/pages/__tests__/PurchaseStockAnalysis.test.tsx`

> Same pattern as Task 13. This file is 1,192 lines. Notable difference: current columns use responsive Tailwind `hidden md:table-cell` / `hidden lg:table-cell` classes — **remove all of these**. Column visibility is now user-controlled.

- [ ] **Step 1: Run existing tests to establish baseline**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npm test -- --watchAll=false --testPathPattern="PurchaseStockAnalysis"
```
Note passing count.

- [ ] **Step 2: Add column definitions inside the component function (closed over state)**

Add import at the top:
```typescript
import { GridColumn, GridHeader, ColumnChooser, useGridLayout } from '../../features/grid-layout';
import { StockAnalysisSortBy } from '../../api/generated/api-client';
```

Inside the component function (needs closure over row-click handlers, severity classes, etc.):

```typescript
const purchaseStockColumns: GridColumn</* type of items in tableData */>[] = [
  {
    id: 'product',
    header: 'Produkt',
    canHide: false,
    canReorder: false,
    align: 'left',
    minWidth: 160,
    defaultWidth: 160,
    renderCell: (item) => (
      /* existing product <td> content */
      null
    ),
  },
  {
    id: 'indicator',
    header: '',
    canHide: false,
    canReorder: false,
    minWidth: 60,
    defaultWidth: 60,
    canResize: false,
    renderCell: (item) => null,
  },
  { id: 'availableStock', header: 'Skladem',       sortBy: StockAnalysisSortBy.AvailableStock,  align: 'right', minWidth: 80,  defaultWidth: 100, renderCell: (item) => null },
  { id: 'minOpt',         header: 'Min/Opt',                                                     align: 'right', minWidth: 80,  defaultWidth: 100, renderCell: (item) => null },
  { id: 'consumption',    header: 'Spotřeba',      sortBy: StockAnalysisSortBy.Consumption,     align: 'right', minWidth: 80,  defaultWidth: 100, renderCell: (item) => null },
  { id: 'stockEfficiency',header: 'NS',            sortBy: StockAnalysisSortBy.StockEfficiency, align: 'right', minWidth: 80,  defaultWidth: 80,  renderCell: (item) => null },
  { id: 'moq',            header: 'MOQ',                                                         align: 'right', minWidth: 80,  defaultWidth: 80,  renderCell: (item) => null },
  { id: 'days',           header: 'Dny',                                                         align: 'right', minWidth: 60,  defaultWidth: 80,  renderCell: (item) => null },
  { id: 'supplier',       header: 'Dodavatel',                                                   align: 'left',  minWidth: 100, defaultWidth: 140, renderCell: (item) => null },
  { id: 'lastPurchase',   header: 'Poslední nákup', sortBy: StockAnalysisSortBy.LastPurchaseDate, align: 'left', minWidth: 100, defaultWidth: 224, renderCell: (item) => null },
];
```

> Fill in each `renderCell` by cutting the corresponding `<td>` JSX from the existing `tableData.map(...)` row rendering block.

- [ ] **Step 3: Add hook call**

```typescript
const { orderedColumns, columnState, setColumnOrder, setColumnWidth, toggleColumnVisibility, resetLayout, isLoaded } =
  useGridLayout('purchase-stock-analysis', purchaseStockColumns);
```

- [ ] **Step 4: Replace `<thead>` block**

Find lines ~856–907 and replace the `<thead>...</thead>` with:

```tsx
<GridHeader
  columns={orderedColumns}
  columnState={columnState}
  activeSortKey={filters.sortBy}
  sortDescending={filters.sortDescending}
  onSort={handleSort}
  onReorder={setColumnOrder}
  onResizeEnd={setColumnWidth}
/>
```

- [ ] **Step 5: Replace row body to iterate orderedColumns**

```tsx
<tbody className="bg-white divide-y divide-gray-200">
  {tableData.map((item) => (
    <tr
      key={item.productCode}
      className={`${getRowColorClass(item.severity)} hover:bg-gray-50 cursor-pointer transition-colors duration-150`}
      onClick={() => handleRowClick(item)}
      title="Klikněte pro zobrazení detailu produktu"
    >
      {orderedColumns.map((col) => {
        const state = columnState.find((s) => s.id === col.id);
        const width = state?.width ?? col.defaultWidth;
        return (
          <td
            key={col.id}
            className={`px-4 py-3 whitespace-nowrap ${col.cellClassName ?? ''}`}
            style={width ? { width: `${width}px`, minWidth: `${col.minWidth ?? 60}px` } : undefined}
          >
            {col.renderCell(item)}
          </td>
        );
      })}
    </tr>
  ))}
</tbody>
```

- [ ] **Step 6: Add ColumnChooser to toolbar**

Find the filter/controls toolbar in the JSX. Add:
```tsx
<ColumnChooser
  columns={purchaseStockColumns}
  columnState={columnState}
  onToggle={toggleColumnVisibility}
  onReset={resetLayout}
/>
```

- [ ] **Step 7: Delete local SortableHeader and remove responsive Tailwind classes**

- Remove the local `SortableHeader` function (lines ~291–318).
- Search the file for any remaining `hidden md:table-cell`, `hidden lg:table-cell`, `hidden xl:table-cell` Tailwind classes. Remove them — column visibility is now user-controlled.

- [ ] **Step 8: Run build**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npm run build 2>&1 | grep -E "error|Error|TS" | head -30
```
Fix any TypeScript errors.

- [ ] **Step 9: Run tests**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npm test -- --watchAll=false --testPathPattern="PurchaseStockAnalysis"
```
Fix any broken assertions.

- [ ] **Step 10: Lint**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npm run lint
```

- [ ] **Step 11: Commit**

```bash
git add frontend/src/components/pages/PurchaseStockAnalysis.tsx
git add frontend/src/components/pages/__tests__/PurchaseStockAnalysis.test.tsx
git commit -m "feat(grid-layout): refactor PurchaseStockAnalysis to config-driven columns"
```

---

## Task 15: Final verification

- [ ] **Step 1: Run all frontend tests**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npm test -- --watchAll=false
```
Expected: all tests pass.

- [ ] **Step 2: Run all backend tests**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet test test/Anela.Heblo.Tests/
```
Expected: all tests pass.

- [ ] **Step 3: Backend format check**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet format --verify-no-changes
```
Expected: exits with 0. If not, run `dotnet format` and commit the result.

- [ ] **Step 4: Run backend + frontend locally and smoke-test**

Start backend (`docs/development/setup.md`), then:
```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npm start
```
Open http://localhost:3000, navigate to Řízení zásob výrobků:

1. Drag a column header to a new position → refresh → order persists.
2. Drag a column edge to resize → refresh → width persists.
3. Open ColumnChooser → hide "Rezerv" → refresh → still hidden.
4. Open ColumnChooser → Reset rozvržení → all columns visible, default widths.
5. Verify "Produkt" column has no drag handle and no chooser checkbox.
6. Repeat steps 1–5 on Analýza skladových zásob.
7. Verify both grids are independent (changing one grid doesn't affect the other).

- [ ] **Step 5: Check network requests**

In browser DevTools → Network:
- `GET /api/grid-layouts/manufacturing-stock-analysis` on page load.
- `PUT /api/grid-layouts/manufacturing-stock-analysis` after changes (with ~500ms delay).
- Verify no 401 / 404 errors.

- [ ] **Step 6: Final commit**

```bash
git add -A
git commit -m "feat(grid-layout): complete column customization feature — width, order, visibility"
```
