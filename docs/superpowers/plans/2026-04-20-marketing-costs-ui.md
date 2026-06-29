# Marketing Costs UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Marketing > Náklady" page showing imported marketing transactions in a paginated grid with a detail modal.

**Architecture:** Vertical slice following existing patterns — MediatR handlers for list+detail queries, new controller, React Query hooks, list page + detail modal. Schema migration adds Description, Currency, RawData to the existing entity.

**Tech Stack:** .NET 8 / MediatR / EF Core (backend), React / React Query / Tailwind (frontend)

**Spec:** `docs/superpowers/specs/2026-04-20-marketing-costs-ui-design.md`

---

### Task 1: Extend Domain Entity with New Fields

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/ImportedMarketingTransaction.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionConfiguration.cs`

- [ ] **Step 1: Add properties to entity**

```csharp
// Add to ImportedMarketingTransaction.cs after ErrorMessage property:
public string? Description { get; set; }
public string? Currency { get; set; }
public string? RawData { get; set; }
```

- [ ] **Step 2: Add column configuration**

Add to `ImportedMarketingTransactionConfiguration.Configure()` after the `ErrorMessage` property config:

```csharp
builder.Property(e => e.Description)
    .HasColumnName("Description")
    .HasColumnType("character varying(500)")
    .HasMaxLength(500);

builder.Property(e => e.Currency)
    .HasColumnName("Currency")
    .HasColumnType("character varying(10)")
    .HasMaxLength(10);

builder.Property(e => e.RawData)
    .HasColumnName("RawData")
    .HasColumnType("text");
```

- [ ] **Step 3: Verify build**

Run: `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj && dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj`

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/ImportedMarketingTransaction.cs backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionConfiguration.cs
git commit -m "feat(marketing-costs): add Description, Currency, RawData fields to entity"
```

---

### Task 2: Update Import Service to Persist New Fields

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs`

- [ ] **Step 1: Update entity mapping in ImportAsync**

In `MarketingInvoiceImportService.cs`, update the `new ImportedMarketingTransaction` block inside the `foreach` loop (around line 52):

```csharp
toImport.Add(new ImportedMarketingTransaction
{
    TransactionId = transaction.TransactionId,
    Platform = _source.Platform,
    Amount = transaction.Amount,
    TransactionDate = transaction.TransactionDate,
    ImportedAt = DateTime.UtcNow,
    IsSynced = false,
    Description = transaction.Description,
    Currency = transaction.Currency,
    RawData = transaction.RawData,
});
```

- [ ] **Step 2: Verify existing tests still pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs`

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs
git commit -m "feat(marketing-costs): persist Description, Currency, RawData during import"
```

---

### Task 3: Add Repository Query Methods

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs`

- [ ] **Step 1: Add interface methods**

Add to `IImportedMarketingTransactionRepository.cs`:

```csharp
Task<ImportedMarketingTransaction?> GetByIdAsync(int id, CancellationToken ct);
Task<(List<ImportedMarketingTransaction> Items, int TotalCount)> GetPagedAsync(
    string? platform,
    DateTime? dateFrom,
    DateTime? dateTo,
    bool? isSynced,
    string? sortBy,
    bool sortDescending,
    int pageNumber,
    int pageSize,
    CancellationToken ct);
```

- [ ] **Step 2: Implement repository methods**

Add to `ImportedMarketingTransactionRepository.cs`:

```csharp
public async Task<ImportedMarketingTransaction?> GetByIdAsync(int id, CancellationToken ct)
{
    return await _context.Set<ImportedMarketingTransaction>()
        .FirstOrDefaultAsync(x => x.Id == id, ct);
}

public async Task<(List<ImportedMarketingTransaction> Items, int TotalCount)> GetPagedAsync(
    string? platform,
    DateTime? dateFrom,
    DateTime? dateTo,
    bool? isSynced,
    string? sortBy,
    bool sortDescending,
    int pageNumber,
    int pageSize,
    CancellationToken ct)
{
    var query = _context.Set<ImportedMarketingTransaction>().AsQueryable();

    if (!string.IsNullOrEmpty(platform))
        query = query.Where(x => x.Platform == platform);

    if (dateFrom.HasValue)
        query = query.Where(x => x.TransactionDate >= dateFrom.Value);

    if (dateTo.HasValue)
        query = query.Where(x => x.TransactionDate <= dateTo.Value);

    if (isSynced.HasValue)
        query = query.Where(x => x.IsSynced == isSynced.Value);

    var totalCount = await query.CountAsync(ct);

    query = sortBy?.ToLower() switch
    {
        "amount" => sortDescending ? query.OrderByDescending(x => x.Amount) : query.OrderBy(x => x.Amount),
        "transactiondate" => sortDescending ? query.OrderByDescending(x => x.TransactionDate) : query.OrderBy(x => x.TransactionDate),
        "importedat" => sortDescending ? query.OrderByDescending(x => x.ImportedAt) : query.OrderBy(x => x.ImportedAt),
        _ => query.OrderByDescending(x => x.TransactionDate)
    };

    var items = await query
        .Skip((pageNumber - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync(ct);

    return (items, totalCount);
}
```

- [ ] **Step 3: Verify build**

Run: `dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj`

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs
git commit -m "feat(marketing-costs): add GetByIdAsync and GetPagedAsync repository methods"
```

---

### Task 4: Create MediatR List Query Handler

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/GetMarketingCostsList/GetMarketingCostsListRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/GetMarketingCostsList/GetMarketingCostsListResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/GetMarketingCostsList/GetMarketingCostsListHandler.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingCostListItemDto.cs`

- [ ] **Step 1: Create DTO**

Create `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingCostListItemDto.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MarketingInvoices.Contracts;

public class MarketingCostListItemDto
{
    public int Id { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public DateTime TransactionDate { get; set; }
    public DateTime ImportedAt { get; set; }
    public bool IsSynced { get; set; }
}
```

- [ ] **Step 2: Create request**

Create `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/GetMarketingCostsList/GetMarketingCostsListRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingInvoices.UseCases.GetMarketingCostsList;

public class GetMarketingCostsListRequest : IRequest<GetMarketingCostsListResponse>
{
    public string? Platform { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public bool? IsSynced { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = true;
}
```

- [ ] **Step 3: Create response**

Create `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/GetMarketingCostsList/GetMarketingCostsListResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingInvoices.UseCases.GetMarketingCostsList;

public class GetMarketingCostsListResponse : BaseResponse
{
    public List<MarketingCostListItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
```

- [ ] **Step 4: Create handler**

Create `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/GetMarketingCostsList/GetMarketingCostsListHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingInvoices.UseCases.GetMarketingCostsList;

public class GetMarketingCostsListHandler : IRequestHandler<GetMarketingCostsListRequest, GetMarketingCostsListResponse>
{
    private readonly IImportedMarketingTransactionRepository _repository;

    public GetMarketingCostsListHandler(IImportedMarketingTransactionRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetMarketingCostsListResponse> Handle(GetMarketingCostsListRequest request, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _repository.GetPagedAsync(
            request.Platform,
            request.DateFrom,
            request.DateTo,
            request.IsSynced,
            request.SortBy,
            request.SortDescending,
            request.PageNumber,
            request.PageSize,
            cancellationToken);

        var dtos = items.Select(x => new MarketingCostListItemDto
        {
            Id = x.Id,
            TransactionId = x.TransactionId,
            Platform = x.Platform,
            Amount = x.Amount,
            Currency = x.Currency,
            TransactionDate = x.TransactionDate,
            ImportedAt = x.ImportedAt,
            IsSynced = x.IsSynced,
        }).ToList();

        return new GetMarketingCostsListResponse
        {
            Items = dtos,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
        };
    }
}
```

- [ ] **Step 5: Verify build**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingCostListItemDto.cs backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/GetMarketingCostsList/
git commit -m "feat(marketing-costs): add GetMarketingCostsList MediatR handler"
```

---

### Task 5: Create MediatR Detail Query Handler

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/GetMarketingCostDetail/GetMarketingCostDetailRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/GetMarketingCostDetail/GetMarketingCostDetailResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/GetMarketingCostDetail/GetMarketingCostDetailHandler.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingCostDetailDto.cs`

- [ ] **Step 1: Create detail DTO**

Create `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingCostDetailDto.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MarketingInvoices.Contracts;

public class MarketingCostDetailDto
{
    public int Id { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public DateTime TransactionDate { get; set; }
    public DateTime ImportedAt { get; set; }
    public bool IsSynced { get; set; }
    public string? Description { get; set; }
    public string? ErrorMessage { get; set; }
    public string? RawData { get; set; }
}
```

- [ ] **Step 2: Create request**

Create `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/GetMarketingCostDetail/GetMarketingCostDetailRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingInvoices.UseCases.GetMarketingCostDetail;

public class GetMarketingCostDetailRequest : IRequest<GetMarketingCostDetailResponse>
{
    public int Id { get; set; }
}
```

- [ ] **Step 3: Create response**

Create `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/GetMarketingCostDetail/GetMarketingCostDetailResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingInvoices.UseCases.GetMarketingCostDetail;

public class GetMarketingCostDetailResponse : BaseResponse
{
    public MarketingCostDetailDto? Item { get; set; }
}
```

- [ ] **Step 4: Create handler**

Create `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/GetMarketingCostDetail/GetMarketingCostDetailHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingInvoices.UseCases.GetMarketingCostDetail;

public class GetMarketingCostDetailHandler : IRequestHandler<GetMarketingCostDetailRequest, GetMarketingCostDetailResponse>
{
    private readonly IImportedMarketingTransactionRepository _repository;

    public GetMarketingCostDetailHandler(IImportedMarketingTransactionRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetMarketingCostDetailResponse> Handle(GetMarketingCostDetailRequest request, CancellationToken cancellationToken)
    {
        var entity = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (entity == null)
        {
            return new GetMarketingCostDetailResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.NotFound,
            };
        }

        return new GetMarketingCostDetailResponse
        {
            Item = new MarketingCostDetailDto
            {
                Id = entity.Id,
                TransactionId = entity.TransactionId,
                Platform = entity.Platform,
                Amount = entity.Amount,
                Currency = entity.Currency,
                TransactionDate = entity.TransactionDate,
                ImportedAt = entity.ImportedAt,
                IsSynced = entity.IsSynced,
                Description = entity.Description,
                ErrorMessage = entity.ErrorMessage,
                RawData = entity.RawData,
            }
        };
    }
}
```

- [ ] **Step 5: Verify build**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingCostDetailDto.cs backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/GetMarketingCostDetail/
git commit -m "feat(marketing-costs): add GetMarketingCostDetail MediatR handler"
```

---

### Task 6: Create API Controller

**Files:**
- Create: `backend/src/Anela.Heblo.API/Controllers/MarketingCostsController.cs`

- [ ] **Step 1: Create controller**

Create `backend/src/Anela.Heblo.API/Controllers/MarketingCostsController.cs`:

```csharp
using Anela.Heblo.API.Infrastructure;
using Anela.Heblo.Application.Features.MarketingInvoices.UseCases.GetMarketingCostDetail;
using Anela.Heblo.Application.Features.MarketingInvoices.UseCases.GetMarketingCostsList;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/marketing-costs")]
public class MarketingCostsController : BaseApiController
{
    private readonly IMediator _mediator;

    public MarketingCostsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<GetMarketingCostsListResponse>> GetList([FromQuery] GetMarketingCostsListRequest request)
    {
        var response = await _mediator.Send(request);
        return Ok(response);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<GetMarketingCostDetailResponse>> GetDetail(int id)
    {
        var request = new GetMarketingCostDetailRequest { Id = id };
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }
}
```

- [ ] **Step 2: Verify full backend build**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/MarketingCostsController.cs
git commit -m "feat(marketing-costs): add MarketingCostsController with list and detail endpoints"
```

---

### Task 7: Backend Unit Tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/GetMarketingCostsListHandlerTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/GetMarketingCostDetailHandlerTests.cs`

- [ ] **Step 1: Create list handler tests**

Create `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/GetMarketingCostsListHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingInvoices.UseCases.GetMarketingCostsList;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingInvoices;

public class GetMarketingCostsListHandlerTests
{
    private readonly Mock<IImportedMarketingTransactionRepository> _repositoryMock;
    private readonly GetMarketingCostsListHandler _handler;

    public GetMarketingCostsListHandlerTests()
    {
        _repositoryMock = new Mock<IImportedMarketingTransactionRepository>();
        _handler = new GetMarketingCostsListHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ReturnsPagedResults()
    {
        var items = new List<ImportedMarketingTransaction>
        {
            new()
            {
                Id = 1,
                TransactionId = "tx_001",
                Platform = "GoogleAds",
                Amount = 1250.00m,
                Currency = "CZK",
                TransactionDate = new DateTime(2026, 4, 15),
                ImportedAt = new DateTime(2026, 4, 16),
                IsSynced = true,
            }
        };

        _repositoryMock.Setup(r => r.GetPagedAsync(
            null, null, null, null, null, true, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((items, 1));

        var request = new GetMarketingCostsListRequest { PageNumber = 1, PageSize = 20 };
        var response = await _handler.Handle(request, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Single(response.Items);
        Assert.Equal("tx_001", response.Items[0].TransactionId);
        Assert.Equal(1, response.TotalCount);
        Assert.Equal(1, response.TotalPages);
    }

    [Fact]
    public async Task Handle_PassesFiltersToRepository()
    {
        _repositoryMock.Setup(r => r.GetPagedAsync(
            "MetaAds", It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), true, "amount", false, 2, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ImportedMarketingTransaction>(), 0));

        var request = new GetMarketingCostsListRequest
        {
            Platform = "MetaAds",
            IsSynced = true,
            SortBy = "amount",
            SortDescending = false,
            PageNumber = 2,
            PageSize = 10,
        };

        await _handler.Handle(request, CancellationToken.None);

        _repositoryMock.Verify(r => r.GetPagedAsync(
            "MetaAds", null, null, true, "amount", false, 2, 10, It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Create detail handler tests**

Create `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/GetMarketingCostDetailHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingInvoices.UseCases.GetMarketingCostDetail;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingInvoices;

public class GetMarketingCostDetailHandlerTests
{
    private readonly Mock<IImportedMarketingTransactionRepository> _repositoryMock;
    private readonly GetMarketingCostDetailHandler _handler;

    public GetMarketingCostDetailHandlerTests()
    {
        _repositoryMock = new Mock<IImportedMarketingTransactionRepository>();
        _handler = new GetMarketingCostDetailHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ReturnsDetailWhenFound()
    {
        var entity = new ImportedMarketingTransaction
        {
            Id = 1,
            TransactionId = "tx_001",
            Platform = "GoogleAds",
            Amount = 1250.00m,
            Currency = "CZK",
            TransactionDate = new DateTime(2026, 4, 15),
            ImportedAt = new DateTime(2026, 4, 16),
            IsSynced = true,
            Description = "Brand campaign spend",
            RawData = "{\"budget\": \"123\"}",
        };

        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        var request = new GetMarketingCostDetailRequest { Id = 1 };
        var response = await _handler.Handle(request, CancellationToken.None);

        Assert.True(response.Success);
        Assert.NotNull(response.Item);
        Assert.Equal("tx_001", response.Item.TransactionId);
        Assert.Equal("Brand campaign spend", response.Item.Description);
        Assert.Equal("{\"budget\": \"123\"}", response.Item.RawData);
    }

    [Fact]
    public async Task Handle_ReturnsNotFoundWhenMissing()
    {
        _repositoryMock.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ImportedMarketingTransaction?)null);

        var request = new GetMarketingCostDetailRequest { Id = 999 };
        var response = await _handler.Handle(request, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.NotFound, response.ErrorCode);
        Assert.Null(response.Item);
    }
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~MarketingCosts"`

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/GetMarketingCostsListHandlerTests.cs backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/GetMarketingCostDetailHandlerTests.cs
git commit -m "test(marketing-costs): add unit tests for list and detail handlers"
```

---

### Task 8: Frontend API Hook

**Files:**
- Create: `frontend/src/api/hooks/useMarketingCosts.ts`

- [ ] **Step 1: Create the hook file**

Create `frontend/src/api/hooks/useMarketingCosts.ts`:

```typescript
import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";

export interface GetMarketingCostsRequest {
  platform?: string;
  dateFrom?: string;
  dateTo?: string;
  isSynced?: boolean | null;
  pageNumber?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
}

export interface MarketingCostListItemDto {
  id: number;
  transactionId: string;
  platform: string;
  amount: number;
  currency: string | null;
  transactionDate: string;
  importedAt: string;
  isSynced: boolean;
}

export interface GetMarketingCostsListResponse {
  items: MarketingCostListItemDto[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  success: boolean;
}

export interface MarketingCostDetailDto {
  id: number;
  transactionId: string;
  platform: string;
  amount: number;
  currency: string | null;
  transactionDate: string;
  importedAt: string;
  isSynced: boolean;
  description: string | null;
  errorMessage: string | null;
  rawData: string | null;
}

export interface GetMarketingCostDetailResponse {
  item: MarketingCostDetailDto | null;
  success: boolean;
}

const marketingCostsKeys = {
  all: ["marketing-costs"] as const,
  lists: () => [...marketingCostsKeys.all, "list"] as const,
  list: (filters: GetMarketingCostsRequest) =>
    [...marketingCostsKeys.lists(), filters] as const,
  details: () => [...marketingCostsKeys.all, "detail"] as const,
  detail: (id: number) => [...marketingCostsKeys.details(), id] as const,
};

export const useMarketingCostsQuery = (request: GetMarketingCostsRequest) => {
  return useQuery({
    queryKey: marketingCostsKeys.list(request),
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      const relativeUrl = `/api/marketing-costs`;
      const params = new URLSearchParams();

      if (request.platform) params.append("Platform", request.platform);
      if (request.dateFrom) params.append("DateFrom", request.dateFrom);
      if (request.dateTo) params.append("DateTo", request.dateTo);
      if (request.isSynced !== null && request.isSynced !== undefined)
        params.append("IsSynced", request.isSynced.toString());
      if (request.pageNumber)
        params.append("PageNumber", request.pageNumber.toString());
      if (request.pageSize)
        params.append("PageSize", request.pageSize.toString());
      if (request.sortBy) params.append("SortBy", request.sortBy);
      if (request.sortDescending !== undefined)
        params.append("SortDescending", request.sortDescending.toString());

      const queryString = params.toString();
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}${queryString ? `?${queryString}` : ""}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "GET",
        headers: { Accept: "application/json" },
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return response.json() as Promise<GetMarketingCostsListResponse>;
    },
    staleTime: 1000 * 60 * 5,
  });
};

export const useMarketingCostDetailQuery = (id: number | null) => {
  return useQuery({
    queryKey: marketingCostsKeys.detail(id!),
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/marketing-costs/${id}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "GET",
        headers: { Accept: "application/json" },
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      return response.json() as Promise<GetMarketingCostDetailResponse>;
    },
    enabled: id !== null,
    staleTime: 1000 * 60 * 5,
  });
};
```

- [ ] **Step 2: Verify frontend build**

Run: `cd frontend && npm run build`

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/hooks/useMarketingCosts.ts
git commit -m "feat(marketing-costs): add React Query hooks for list and detail endpoints"
```

---

### Task 9: Frontend List Page

**Files:**
- Create: `frontend/src/components/pages/MarketingCostsList.tsx`

- [ ] **Step 1: Create list page component**

Create `frontend/src/components/pages/MarketingCostsList.tsx`:

```typescript
import React, { useState } from "react";
import { ChevronUp, ChevronDown, AlertCircle, Loader2 } from "lucide-react";
import { useSearchParams } from "react-router-dom";
import {
  useMarketingCostsQuery,
  GetMarketingCostsRequest,
  MarketingCostListItemDto,
} from "../../api/hooks/useMarketingCosts";
import MarketingCostDetail from "./MarketingCostDetail";
import Pagination from "../common/Pagination";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";

const platformBadge: Record<string, string> = {
  GoogleAds: "bg-blue-100 text-blue-800",
  MetaAds: "bg-purple-100 text-purple-800",
};

const MarketingCostsList: React.FC = () => {
  const [searchParams, setSearchParams] = useSearchParams();

  // Filter input state
  const [platformInput, setPlatformInput] = useState(searchParams.get("platform") || "");
  const [dateFromInput, setDateFromInput] = useState(searchParams.get("dateFrom") || "");
  const [dateToInput, setDateToInput] = useState(searchParams.get("dateTo") || "");
  const [syncedInput, setSyncedInput] = useState(searchParams.get("isSynced") || "");

  // Applied filter state
  const [platformFilter, setPlatformFilter] = useState(searchParams.get("platform") || "");
  const [dateFromFilter, setDateFromFilter] = useState(searchParams.get("dateFrom") || "");
  const [dateToFilter, setDateToFilter] = useState(searchParams.get("dateTo") || "");
  const [syncedFilter, setSyncedFilter] = useState(searchParams.get("isSynced") || "");

  // Pagination
  const [pageNumber, setPageNumber] = useState(Number(searchParams.get("page")) || 1);
  const [pageSize, setPageSize] = useState(Number(searchParams.get("pageSize")) || 20);

  // Sorting
  const [sortBy, setSortBy] = useState(searchParams.get("sortBy") || "transactionDate");
  const [sortDescending, setSortDescending] = useState(
    searchParams.get("sortDesc") !== "false"
  );

  // Detail modal
  const [selectedItem, setSelectedItem] = useState<MarketingCostListItemDto | null>(null);
  const [isDetailOpen, setIsDetailOpen] = useState(false);

  const request: GetMarketingCostsRequest = {
    platform: platformFilter || undefined,
    dateFrom: dateFromFilter || undefined,
    dateTo: dateToFilter || undefined,
    isSynced: syncedFilter === "" ? null : syncedFilter === "true",
    pageNumber,
    pageSize,
    sortBy,
    sortDescending,
  };

  const { data, isLoading, error } = useMarketingCostsQuery(request);

  const applyFilters = () => {
    setPlatformFilter(platformInput);
    setDateFromFilter(dateFromInput);
    setDateToFilter(dateToInput);
    setSyncedFilter(syncedInput);
    setPageNumber(1);

    const params = new URLSearchParams();
    if (platformInput) params.set("platform", platformInput);
    if (dateFromInput) params.set("dateFrom", dateFromInput);
    if (dateToInput) params.set("dateTo", dateToInput);
    if (syncedInput) params.set("isSynced", syncedInput);
    params.set("page", "1");
    params.set("pageSize", pageSize.toString());
    params.set("sortBy", sortBy);
    params.set("sortDesc", sortDescending.toString());
    setSearchParams(params);
  };

  const handleSort = (column: string) => {
    if (sortBy === column) {
      setSortDescending(!sortDescending);
    } else {
      setSortBy(column);
      setSortDescending(true);
    }
    setPageNumber(1);
  };

  const handleRowClick = (item: MarketingCostListItemDto) => {
    setSelectedItem(item);
    setIsDetailOpen(true);
  };

  const handlePageChange = (page: number) => {
    setPageNumber(page);
  };

  const handlePageSizeChange = (size: number) => {
    setPageSize(size);
    setPageNumber(1);
  };

  const SortableHeader = ({ column, label }: { column: string; label: string }) => (
    <th
      className="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100 select-none"
      onClick={() => handleSort(column)}
    >
      <div className="flex items-center gap-1">
        {label}
        {sortBy === column && (
          sortDescending ? <ChevronDown className="w-3 h-3" /> : <ChevronUp className="w-3 h-3" />
        )}
      </div>
    </th>
  );

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <AlertCircle className="w-5 h-5 text-red-500 mr-2" />
        <span className="text-red-600">Chyba při načítání dat</span>
      </div>
    );
  }

  return (
    <div className="flex flex-col" style={{ height: PAGE_CONTAINER_HEIGHT }}>
      {/* Header */}
      <div className="px-4 py-3 border-b border-gray-200">
        <h1 className="text-lg font-semibold text-gray-900">Marketingové náklady</h1>
      </div>

      {/* Filters */}
      <div className="px-4 py-3 border-b border-gray-200 bg-gray-50 flex gap-3 items-end flex-wrap">
        <div className="flex flex-col gap-1">
          <label className="text-xs text-gray-500 uppercase">Platforma</label>
          <select
            className="px-2 py-1.5 border border-gray-300 rounded text-sm"
            value={platformInput}
            onChange={(e) => setPlatformInput(e.target.value)}
          >
            <option value="">Všechny</option>
            <option value="GoogleAds">Google Ads</option>
            <option value="MetaAds">Meta Ads</option>
          </select>
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs text-gray-500 uppercase">Od</label>
          <input
            type="date"
            className="px-2 py-1.5 border border-gray-300 rounded text-sm"
            value={dateFromInput}
            onChange={(e) => setDateFromInput(e.target.value)}
          />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs text-gray-500 uppercase">Do</label>
          <input
            type="date"
            className="px-2 py-1.5 border border-gray-300 rounded text-sm"
            value={dateToInput}
            onChange={(e) => setDateToInput(e.target.value)}
          />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs text-gray-500 uppercase">Sync</label>
          <select
            className="px-2 py-1.5 border border-gray-300 rounded text-sm"
            value={syncedInput}
            onChange={(e) => setSyncedInput(e.target.value)}
          >
            <option value="">Všechny</option>
            <option value="true">Synced</option>
            <option value="false">Not synced</option>
          </select>
        </div>
        <button
          className="px-3 py-1.5 bg-blue-600 text-white rounded text-sm hover:bg-blue-700"
          onClick={applyFilters}
        >
          Filtrovat
        </button>
      </div>

      {/* Table */}
      <div className="flex-1 overflow-auto">
        {isLoading ? (
          <div className="flex items-center justify-center h-64">
            <Loader2 className="w-5 h-5 animate-spin text-gray-400" />
          </div>
        ) : (
          <table className="w-full">
            <thead className="sticky top-0 z-10 bg-gray-50 border-b border-gray-200">
              <tr>
                <th className="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase">
                  Platforma
                </th>
                <th className="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase">
                  Transaction ID
                </th>
                <SortableHeader column="amount" label="Částka" />
                <th className="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase">
                  Měna
                </th>
                <SortableHeader column="transactionDate" label="Datum transakce" />
                <SortableHeader column="importedAt" label="Importováno" />
                <th className="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase">
                  Sync
                </th>
              </tr>
            </thead>
            <tbody>
              {data?.items.map((item) => (
                <tr
                  key={item.id}
                  className="border-b border-gray-100 hover:bg-gray-50 cursor-pointer"
                  onClick={() => handleRowClick(item)}
                >
                  <td className="px-3 py-2">
                    <span
                      className={`px-2 py-0.5 rounded text-xs font-medium ${platformBadge[item.platform] || "bg-gray-100 text-gray-800"}`}
                    >
                      {item.platform}
                    </span>
                  </td>
                  <td className="px-3 py-2 font-mono text-xs text-gray-700">
                    {item.transactionId}
                  </td>
                  <td className="px-3 py-2 font-medium">
                    {item.amount.toLocaleString("cs-CZ", { minimumFractionDigits: 2 })}
                  </td>
                  <td className="px-3 py-2 text-sm text-gray-600">
                    {item.currency || "—"}
                  </td>
                  <td className="px-3 py-2 text-sm">
                    {new Date(item.transactionDate).toLocaleDateString("cs-CZ")}
                  </td>
                  <td className="px-3 py-2 text-sm text-gray-500">
                    {new Date(item.importedAt).toLocaleString("cs-CZ")}
                  </td>
                  <td className="px-3 py-2">
                    {item.isSynced ? (
                      <span className="text-green-600">✓</span>
                    ) : (
                      <span className="text-red-600">✗</span>
                    )}
                  </td>
                </tr>
              ))}
              {data?.items.length === 0 && (
                <tr>
                  <td colSpan={7} className="px-3 py-8 text-center text-gray-500">
                    Žádné záznamy
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        )}
      </div>

      {/* Pagination */}
      {data && (
        <div className="border-t border-gray-200">
          <Pagination
            totalCount={data.totalCount}
            pageNumber={data.pageNumber}
            pageSize={data.pageSize}
            totalPages={data.totalPages}
            onPageChange={handlePageChange}
            onPageSizeChange={handlePageSizeChange}
            isFiltered={!!(platformFilter || dateFromFilter || dateToFilter || syncedFilter)}
          />
        </div>
      )}

      {/* Detail Modal */}
      <MarketingCostDetail
        item={selectedItem}
        isOpen={isDetailOpen}
        onClose={() => setIsDetailOpen(false)}
      />
    </div>
  );
};

export default MarketingCostsList;
```

- [ ] **Step 2: Verify frontend compiles (will fail until detail component exists — expected)**

Run: `cd frontend && npx tsc --noEmit 2>&1 | head -20`

Expected: Error about missing `MarketingCostDetail` — this is expected and fixed in Task 10.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/pages/MarketingCostsList.tsx
git commit -m "feat(marketing-costs): add MarketingCostsList page component"
```

---

### Task 10: Frontend Detail Modal

**Files:**
- Create: `frontend/src/components/pages/MarketingCostDetail.tsx`

- [ ] **Step 1: Create detail modal component**

Create `frontend/src/components/pages/MarketingCostDetail.tsx`:

```typescript
import React, { useEffect } from "react";
import { X, Loader2 } from "lucide-react";
import {
  MarketingCostListItemDto,
  useMarketingCostDetailQuery,
} from "../../api/hooks/useMarketingCosts";

interface MarketingCostDetailProps {
  item: MarketingCostListItemDto | null;
  isOpen: boolean;
  onClose: () => void;
}

const platformBadge: Record<string, string> = {
  GoogleAds: "bg-blue-100 text-blue-800",
  MetaAds: "bg-purple-100 text-purple-800",
};

const MarketingCostDetail: React.FC<MarketingCostDetailProps> = ({ item, isOpen, onClose }) => {
  const { data, isLoading } = useMarketingCostDetailQuery(isOpen && item ? item.id : null);

  useEffect(() => {
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    if (isOpen) {
      document.addEventListener("keydown", handleEscape);
    }
    return () => document.removeEventListener("keydown", handleEscape);
  }, [isOpen, onClose]);

  if (!isOpen || !item) return null;

  const detail = data?.item;

  const handleBackdropClick = (e: React.MouseEvent) => {
    if (e.target === e.currentTarget) onClose();
  };

  const formatJson = (raw: string | null | undefined): string => {
    if (!raw) return "";
    try {
      return JSON.stringify(JSON.parse(raw), null, 2);
    } catch {
      return raw;
    }
  };

  return (
    <div
      className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50"
      onClick={handleBackdropClick}
    >
      <div className="bg-white rounded-lg shadow-xl max-w-[700px] w-full max-h-[95vh] flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-4 border-b border-gray-200">
          <div className="flex items-center gap-3">
            <h2 className="text-lg font-semibold">Detail transakce</h2>
            <span
              className={`px-2 py-0.5 rounded text-xs font-medium ${platformBadge[item.platform] || "bg-gray-100 text-gray-800"}`}
            >
              {item.platform}
            </span>
          </div>
          <button
            onClick={onClose}
            className="text-gray-400 hover:text-gray-600"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Content */}
        <div className="overflow-y-auto p-5">
          {isLoading ? (
            <div className="flex items-center justify-center h-32">
              <Loader2 className="w-5 h-5 animate-spin text-gray-400" />
            </div>
          ) : (
            <>
              {/* Basic info grid */}
              <div className="grid grid-cols-2 gap-4 mb-6">
                <div>
                  <div className="text-xs uppercase text-gray-500 mb-1">Transaction ID</div>
                  <div className="font-mono text-sm">{item.transactionId}</div>
                </div>
                <div>
                  <div className="text-xs uppercase text-gray-500 mb-1">Platforma</div>
                  <div>{item.platform}</div>
                </div>
                <div>
                  <div className="text-xs uppercase text-gray-500 mb-1">Částka</div>
                  <div className="text-xl font-semibold">
                    {item.amount.toLocaleString("cs-CZ", { minimumFractionDigits: 2 })}{" "}
                    {item.currency || ""}
                  </div>
                </div>
                <div>
                  <div className="text-xs uppercase text-gray-500 mb-1">Datum transakce</div>
                  <div>{new Date(item.transactionDate).toLocaleDateString("cs-CZ")}</div>
                </div>
                <div>
                  <div className="text-xs uppercase text-gray-500 mb-1">Importováno</div>
                  <div>{new Date(item.importedAt).toLocaleString("cs-CZ")}</div>
                </div>
                <div>
                  <div className="text-xs uppercase text-gray-500 mb-1">Sync status</div>
                  <div>
                    {item.isSynced ? (
                      <span className="bg-green-100 text-green-800 px-2 py-0.5 rounded text-xs font-medium">
                        Synced
                      </span>
                    ) : (
                      <span className="bg-red-100 text-red-800 px-2 py-0.5 rounded text-xs font-medium">
                        Not synced
                      </span>
                    )}
                  </div>
                </div>
              </div>

              {/* Description */}
              {detail?.description && (
                <div className="mb-6">
                  <div className="text-xs uppercase text-gray-500 mb-1">Popis</div>
                  <div className="p-3 bg-gray-50 rounded text-sm">{detail.description}</div>
                </div>
              )}

              {/* Error message */}
              {detail?.errorMessage && (
                <div className="mb-6">
                  <div className="text-xs uppercase text-red-600 mb-1">Chyba</div>
                  <div className="p-3 bg-red-50 border border-red-200 rounded text-sm text-red-800">
                    {detail.errorMessage}
                  </div>
                </div>
              )}

              {/* Raw data */}
              {detail?.rawData && (
                <details className="border border-gray-200 rounded">
                  <summary className="px-4 py-2 cursor-pointer text-sm text-gray-500 select-none">
                    Surová data z API (JSON)
                  </summary>
                  <div className="p-4 bg-gray-900 rounded-b overflow-x-auto">
                    <pre className="text-gray-200 text-xs leading-relaxed whitespace-pre-wrap">
                      {formatJson(detail.rawData)}
                    </pre>
                  </div>
                </details>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  );
};

export default MarketingCostDetail;
```

- [ ] **Step 2: Verify frontend compiles**

Run: `cd frontend && npx tsc --noEmit`

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/pages/MarketingCostDetail.tsx
git commit -m "feat(marketing-costs): add MarketingCostDetail modal component"
```

---

### Task 11: Wire Up Navigation and Routing

**Files:**
- Modify: `frontend/src/components/Layout/Sidebar.tsx`
- Modify: `frontend/src/App.tsx`

- [ ] **Step 1: Add Marketing section to Sidebar**

In `frontend/src/components/Layout/Sidebar.tsx`:

1. Add `Megaphone` to the lucide-react import at the top.

2. In the `navigationSections` array, insert the following block immediately after the Finance section's closing `])` (after the `...hasRole("finance_reader")` spread, before the `{ id: "zakaznicke"` object):

```typescript
// Marketing section - only visible for marketing_reader role
...(hasRole("marketing_reader")
  ? [
      {
        id: "marketing",
        name: "Marketing",
        icon: Megaphone,
        type: "section" as const,
        items: [
          {
            id: "naklady",
            name: "Náklady",
            href: "/marketing/costs",
          },
        ],
      },
    ]
  : []),
```

- [ ] **Step 2: Add route to App.tsx**

In `frontend/src/App.tsx`:

1. Add import at the top with other page imports:

```typescript
import MarketingCostsList from "./components/pages/MarketingCostsList";
```

2. Add route inside the `<Routes>` block (near other finance/customer routes):

```typescript
<Route path="/marketing/costs" element={<MarketingCostsList />} />
```

- [ ] **Step 3: Verify frontend compiles**

Run: `cd frontend && npm run build`

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/Layout/Sidebar.tsx frontend/src/App.tsx
git commit -m "feat(marketing-costs): add Marketing menu group and /marketing/costs route"
```

---

### Task 12: SQL Migration Script

**Files:**
- Create: `backend/migrations/20260420_add_marketing_transaction_fields.sql`

- [ ] **Step 1: Create migration script**

Create `backend/migrations/20260420_add_marketing_transaction_fields.sql`:

```sql
-- Add Description, Currency, RawData columns to imported_marketing_transactions
-- These fields were previously available from source APIs but not persisted

ALTER TABLE dbo.imported_marketing_transactions
    ADD COLUMN "Description" character varying(500) NULL;

ALTER TABLE dbo.imported_marketing_transactions
    ADD COLUMN "Currency" character varying(10) NULL;

ALTER TABLE dbo.imported_marketing_transactions
    ADD COLUMN "RawData" text NULL;
```

- [ ] **Step 2: Commit**

```bash
git add backend/migrations/20260420_add_marketing_transaction_fields.sql
git commit -m "feat(marketing-costs): add SQL migration for Description, Currency, RawData columns"
```

---

### Task 13: Final Verification

- [ ] **Step 1: Full backend build**

Run: `dotnet build backend/Anela.Heblo.sln`

- [ ] **Step 2: Run all backend tests**

Run: `dotnet test backend/Anela.Heblo.sln`

- [ ] **Step 3: Full frontend build**

Run: `cd frontend && npm run build`

- [ ] **Step 4: Run frontend lint**

Run: `cd frontend && npm run lint`

- [ ] **Step 5: Format backend code**

Run: `dotnet format backend/Anela.Heblo.sln`

- [ ] **Step 6: Commit any formatting fixes**

```bash
git add -A
git commit -m "style(marketing-costs): apply dotnet format"
```
