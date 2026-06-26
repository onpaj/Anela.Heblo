# GetBankStatement by Id — Dedicated MediatR Handler Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the controller-level workaround in `BankStatementsController.GetBankStatement(int id)` with a purpose-built MediatR handler that calls `IBankStatementImportRepository.GetByIdAsync` directly, restoring the "thin controllers, business logic in handlers" rule and activating an existing-but-unused repository method.

**Architecture:** Add a Vertical Slice `Application/Features/Bank/UseCases/GetBankStatementById/` containing `GetBankStatementByIdRequest` and `GetBankStatementByIdHandler`. Handler returns `BankStatementImportDto?` (the existing list-item DTO — no new response type is introduced), mapped via the existing `BankMappingProfile` through `IMapper`. Controller becomes a thin dispatcher that converts `null → 404 + message body` or `value → 200 OK`. Repository interface and HTTP wire format are untouched.

**Tech Stack:** .NET 8, MediatR, AutoMapper, EF Core, xUnit + Moq + FluentAssertions, `WebApplicationFactory<>` for integration tests.

---

## Background You Need

Before writing code, read these so the changes fit the codebase:

- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs` — the controller you will change. `GetBankStatement(int id)` is at lines 141–169 and is the *only* thing in this file you will touch.
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListHandler.cs` — the handler pattern to mirror (ctor injection of `IBankStatementImportRepository`, `IMapper`, `ILogger<T>`; uses `_mapper.Map<List<BankStatementImportDto>>(items)`).
- `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs` — the single-item DTO. Already maps from `BankStatementImport` via `BankMappingProfile`. **Do not create a new response DTO.**
- `backend/src/Anela.Heblo.Application/Features/Bank/BankMappingProfile.cs` — already contains `CreateMap<BankStatementImport, BankStatementImportDto>()`. The new handler reuses it.
- `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs` — line 13: `Task<BankStatementImport?> GetByIdAsync(int id)`. **No `CancellationToken` parameter.** Do not change this interface.
- `backend/test/Anela.Heblo.Tests/Controllers/BankStatementsControllerTests.cs` — existing controller unit-test patterns (Moq `IMediator`, `Moq.Mock<ILogger<...>>`).
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportIntegrationTests.cs` — defines `BankStatementImportTestFactory : HebloWebApplicationFactory` at lines 306–end. We will reuse this factory for the integration tests (it already wires the WebApp + mocks, and tests share it via `IClassFixture<>`).
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryTests.cs` — pattern for seeding the in-memory DB in tests using `BankStatementImportRepository.AddAsync(...)`.

## Architectural Decisions (Locked, Do Not Re-litigate)

These come from the architecture review and override the original spec where the spec was wrong:

1. **No `GetBankStatementByIdResponse`.** Handler returns `BankStatementImportDto?`. The list-item DTO already *is* the single-item shape the existing endpoint serialises.
2. **No new mapper file.** Use injected `IMapper` against the existing `BankMappingProfile`.
3. **Preserve `404` body:** `NotFound(new { message = $"Bank statement import with ID {id} not found" })`. The original brief example dropped this and would be a wire-format change.
4. **Do not pass `CancellationToken` to the repository.** Interface has no CT parameter; FR-5 forbids interface changes. The handler still accepts a CT (MediatR contract) — it just doesn't forward it to `GetByIdAsync`.
5. **Keep `[HttpGet("{id}")]` exactly as is.** No `:int` constraint. Changing it alters routing/error behaviour for non-integer paths.
6. **Controller return type stays `ActionResult<BankStatementImportDto>`.**
7. **Drop the controller-level `try/catch (Exception)`** in `GetBankStatement(int id)` for consistency with `GetAccounts` (no top-level catch). Global ASP.NET error handling already covers unhandled exceptions.

## File Structure

| Path | Action | Responsibility |
|------|--------|----------------|
| `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementById/GetBankStatementByIdRequest.cs` | Create | MediatR request DTO. `class`, single `int Id`, `IRequest<BankStatementImportDto?>`. |
| `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementById/GetBankStatementByIdHandler.cs` | Create | Calls repo `GetByIdAsync(request.Id)`, maps via `IMapper`, returns DTO or `null`. |
| `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs` | Modify (lines 141–169 only) | Replace `GetBankStatement(int id)` body. Add `CancellationToken` param. Add `using` for the new slice. |
| `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementByIdHandlerTests.cs` | Create | Unit tests for the handler (Moq repo + AutoMapper instance). |
| `backend/test/Anela.Heblo.Tests/Controllers/BankStatementsControllerTests.cs` | Modify (append tests) | Controller unit tests asserting MediatR dispatch + 200/404 mapping with preserved message body. |
| `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportIntegrationTests.cs` | Modify (append tests) | Two end-to-end tests: existing id → 200 + DTO; missing id → 404 + `{message: "..."}` body. |

No changes to: `IBankStatementImportRepository.cs`, `BankStatementImportRepository.cs`, `BankMappingProfile.cs`, `BankStatementImportDto.cs`, `GetBankStatementListHandler.cs`, `GetBankStatementListRequest.cs`, `GetBankStatementListResponse.cs`.

---

## Task 1: Add MediatR request `GetBankStatementByIdRequest`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementById/GetBankStatementByIdRequest.cs`

- [ ] **Step 1: Create the request class**

Create the file with this exact content:

```csharp
using Anela.Heblo.Application.Features.Bank.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementById;

public class GetBankStatementByIdRequest : IRequest<BankStatementImportDto?>
{
    public int Id { get; set; }
}
```

Why a class (not record): project rule — DTOs and MediatR requests are classes to keep OpenAPI client generators happy.

- [ ] **Step 2: Verify it compiles**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build succeeds with zero warnings.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementById/GetBankStatementByIdRequest.cs
git commit -m "feat(bank): add GetBankStatementByIdRequest MediatR contract"
```

---

## Task 2: TDD — Write failing handler unit tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementByIdHandlerTests.cs`

The handler does not exist yet. These tests will not compile until Task 3 creates it. That's intentional — RED stage of TDD.

- [ ] **Step 1: Create the test file with three failing cases**

Create the file with this exact content:

```csharp
using Anela.Heblo.Application.Features.Bank;
using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementById;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Domain.Shared;
using AutoMapper;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank;

public class GetBankStatementByIdHandlerTests
{
    private readonly Mock<IBankStatementImportRepository> _repository;
    private readonly IMapper _mapper;
    private readonly Mock<ILogger<GetBankStatementByIdHandler>> _logger;
    private readonly GetBankStatementByIdHandler _handler;

    public GetBankStatementByIdHandlerTests()
    {
        _repository = new Mock<IBankStatementImportRepository>();
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<BankMappingProfile>());
        _mapper = mapperConfig.CreateMapper();
        _logger = new Mock<ILogger<GetBankStatementByIdHandler>>();
        _handler = new GetBankStatementByIdHandler(_repository.Object, _mapper, _logger.Object);
    }

    [Fact]
    public async Task Handle_WithExistingId_ReturnsMappedDto()
    {
        // Arrange
        var entity = new BankStatementImport("T12345", new DateTime(2026, 1, 15))
        {
            Account = "123456789",
            Currency = CurrencyCode.CZK,
            ItemCount = 7,
            ImportResult = "OK"
        };
        _repository
            .Setup(r => r.GetByIdAsync(42))
            .ReturnsAsync(entity);

        // Act
        var result = await _handler.Handle(new GetBankStatementByIdRequest { Id = 42 }, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("T12345", result!.TransferId);
        Assert.Equal("123456789", result.Account);
        Assert.Equal("CZK", result.Currency);
        Assert.Equal(7, result.ItemCount);
        Assert.Equal("OK", result.ImportResult);
        Assert.Null(result.ErrorType);
    }

    [Fact]
    public async Task Handle_WithMissingId_ReturnsNull()
    {
        // Arrange
        _repository
            .Setup(r => r.GetByIdAsync(99999))
            .ReturnsAsync((BankStatementImport?)null);

        // Act
        var result = await _handler.Handle(new GetBankStatementByIdRequest { Id = 99999 }, CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_CallsRepositoryGetByIdExactlyOnce_WithTheRequestId()
    {
        // Arrange
        _repository
            .Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((BankStatementImport?)null);

        // Act
        await _handler.Handle(new GetBankStatementByIdRequest { Id = 123 }, CancellationToken.None);

        // Assert
        _repository.Verify(r => r.GetByIdAsync(123), Times.Once);
        _repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_ProducesSameDtoAsListHandlerMapping_ForSameEntity()
    {
        // Arrange — guarantees no projection drift between list and by-id paths.
        var entity = new BankStatementImport("T-SAMENESS", new DateTime(2026, 2, 1))
        {
            Account = "987654321",
            Currency = CurrencyCode.EUR,
            ItemCount = 3,
            ImportResult = "PROCESSING_ERROR"
        };
        _repository.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(entity);

        // Act
        var fromHandler = await _handler.Handle(new GetBankStatementByIdRequest { Id = 7 }, CancellationToken.None);
        var fromListMapping = _mapper.Map<List<BankStatementImportDto>>(new[] { entity }).Single();

        // Assert
        Assert.NotNull(fromHandler);
        Assert.Equal(fromListMapping.Id, fromHandler!.Id);
        Assert.Equal(fromListMapping.TransferId, fromHandler.TransferId);
        Assert.Equal(fromListMapping.StatementDate, fromHandler.StatementDate);
        Assert.Equal(fromListMapping.ImportDate, fromHandler.ImportDate);
        Assert.Equal(fromListMapping.Account, fromHandler.Account);
        Assert.Equal(fromListMapping.Currency, fromHandler.Currency);
        Assert.Equal(fromListMapping.ItemCount, fromHandler.ItemCount);
        Assert.Equal(fromListMapping.ImportResult, fromHandler.ImportResult);
        Assert.Equal(fromListMapping.ErrorType, fromHandler.ErrorType);
    }
}
```

- [ ] **Step 2: Run the tests — they MUST fail to compile (RED)**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: compile errors mentioning `GetBankStatementByIdHandler` is not defined. **Do not proceed if it compiles** — that means the handler somehow already exists, which is a discrepancy with the plan.

- [ ] **Step 3: Commit the failing test file**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementByIdHandlerTests.cs
git commit -m "test(bank): add failing GetBankStatementByIdHandler unit tests"
```

(Yes, we commit failing tests on a feature branch. They will go green in Task 3.)

---

## Task 3: Implement `GetBankStatementByIdHandler` (GREEN)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementById/GetBankStatementByIdHandler.cs`

- [ ] **Step 1: Create the handler**

Create the file with this exact content:

```csharp
using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Domain.Features.Bank;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementById;

public class GetBankStatementByIdHandler : IRequestHandler<GetBankStatementByIdRequest, BankStatementImportDto?>
{
    private readonly IBankStatementImportRepository _repository;
    private readonly IMapper _mapper;
    private readonly ILogger<GetBankStatementByIdHandler> _logger;

    public GetBankStatementByIdHandler(
        IBankStatementImportRepository repository,
        IMapper mapper,
        ILogger<GetBankStatementByIdHandler> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<BankStatementImportDto?> Handle(GetBankStatementByIdRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting bank statement with ID {Id}", request.Id);

        var entity = await _repository.GetByIdAsync(request.Id);

        if (entity is null)
        {
            _logger.LogInformation("Bank statement with ID {Id} not found", request.Id);
            return null;
        }

        return _mapper.Map<BankStatementImportDto>(entity);
    }
}
```

Notes:
- The `CancellationToken` is part of the MediatR contract and is accepted, but **not** forwarded to `_repository.GetByIdAsync` — the interface doesn't accept one (locked decision #4).
- MediatR registration is automatic via `AddMediatR(...RegisterServicesFromAssembly(...))` in `ApplicationModule.cs` — no DI wiring needed.

- [ ] **Step 2: Run the unit tests — they MUST all pass (GREEN)**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetBankStatementByIdHandlerTests"
```

Expected: 4 tests passed, 0 failed.

If a test fails, **read the failure**, fix the handler, re-run. Do not modify the tests to make them pass.

- [ ] **Step 3: Run `dotnet format` on the new files**

Run:
```bash
dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementById/
```

Expected: exits with no changes (or applies trivial whitespace fixes).

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementById/GetBankStatementByIdHandler.cs
git commit -m "feat(bank): implement GetBankStatementByIdHandler with IMapper + repository"
```

---

## Task 4: Refactor the controller to dispatch the new request

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs` (lines 1–9 add a using; lines 141–169 replace method body)

- [ ] **Step 1: Update the using directives**

Open `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs`. The current line 1–8 looks like:

```csharp
using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankAccounts;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;
using Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
```

Add the new slice's using. Do **not** remove `GetBankStatementList` — the list endpoint `GetBankStatements` still uses it. The final block must be:

```csharp
using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankAccounts;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementById;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList;
using Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
```

- [ ] **Step 2: Replace the body of `GetBankStatement(int id)`**

The current method (lines 141–169) is:

```csharp
[HttpGet("{id}")]
public async Task<ActionResult<BankStatementImportDto>> GetBankStatement(int id)
{
    try
    {
        _logger.LogInformation("Getting bank statement with ID {Id}", id);

        var request = new GetBankStatementListRequest
        {
            Id = id,
            Take = 1
        };

        var response = await _mediator.Send(request);
        var statement = response.Items.FirstOrDefault();

        if (statement == null)
        {
            return NotFound(new { message = $"Bank statement import with ID {id} not found" });
        }

        return Ok(statement);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error occurred while retrieving bank statement {Id}", id);
        return StatusCode(500, new { message = "An error occurred while retrieving bank statement" });
    }
}
```

Replace it with:

```csharp
[HttpGet("{id}")]
public async Task<ActionResult<BankStatementImportDto>> GetBankStatement(int id, CancellationToken cancellationToken)
{
    var response = await _mediator.Send(new GetBankStatementByIdRequest { Id = id }, cancellationToken);

    return response is null
        ? NotFound(new { message = $"Bank statement import with ID {id} not found" })
        : Ok(response);
}
```

What changed and why:
- Route attribute `[HttpGet("{id}")]` — **unchanged** (no `:int`, locked decision #5).
- Return type `ActionResult<BankStatementImportDto>` — **unchanged** (locked decision #6).
- Added `CancellationToken cancellationToken` parameter (good practice; flows through to MediatR even though the inner repo call drops it — locked decision #4).
- `try/catch (Exception)` removed (locked decision #7 — consistent with `GetAccounts` above; global error handling covers it).
- 404 body preserved as `new { message = ... }` (locked decision #3).
- No more `GetBankStatementListRequest`, `Take = 1`, or `FirstOrDefault()`.

- [ ] **Step 3: Verify the build is clean**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: build succeeds with zero new warnings.

- [ ] **Step 4: Run `dotnet format` on the controller**

Run:
```bash
dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs
```

Expected: no changes (or trivial whitespace).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs
git commit -m "refactor(bank): dispatch GET /api/bank-statements/{id} to dedicated handler"
```

---

## Task 5: Add controller-level unit tests for the new dispatch + 404 mapping

**Files:**
- Modify (append): `backend/test/Anela.Heblo.Tests/Controllers/BankStatementsControllerTests.cs`

- [ ] **Step 1: Update the using directives at the top of the file**

The current usings at the top of `BankStatementsControllerTests.cs` are:

```csharp
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankAccounts;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
```

Add the new slice's namespace. Final usings:

```csharp
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankAccounts;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementById;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
```

- [ ] **Step 2: Append the new test cases inside the class**

Add these three tests inside `BankStatementsControllerTests` (just before the final closing brace of the class):

```csharp
[Fact]
public async Task GetBankStatement_WithExistingId_DispatchesByIdRequestAndReturnsOk()
{
    // Arrange
    var dto = new BankStatementImportDto
    {
        Id = 42,
        TransferId = "T-EXISTS",
        StatementDate = new DateTime(2026, 1, 15),
        ImportDate = new DateTime(2026, 1, 16),
        Account = "123456789",
        Currency = "CZK",
        ItemCount = 5,
        ImportResult = "OK"
    };
    _mockMediator
        .Setup(m => m.Send(It.Is<GetBankStatementByIdRequest>(r => r.Id == 42), It.IsAny<CancellationToken>()))
        .ReturnsAsync(dto);

    // Act
    var actionResult = await _controller.GetBankStatement(42, CancellationToken.None);

    // Assert
    var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
    var payload = Assert.IsType<BankStatementImportDto>(ok.Value);
    Assert.Equal(42, payload.Id);
    Assert.Equal("T-EXISTS", payload.TransferId);

    _mockMediator.Verify(
        m => m.Send(It.Is<GetBankStatementByIdRequest>(r => r.Id == 42), It.IsAny<CancellationToken>()),
        Times.Once);
}

[Fact]
public async Task GetBankStatement_WithMissingId_ReturnsNotFoundWithMessageBody()
{
    // Arrange
    _mockMediator
        .Setup(m => m.Send(It.IsAny<GetBankStatementByIdRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((BankStatementImportDto?)null);

    // Act
    var actionResult = await _controller.GetBankStatement(99999, CancellationToken.None);

    // Assert
    var notFound = Assert.IsType<NotFoundObjectResult>(actionResult.Result);
    Assert.NotNull(notFound.Value);

    // Wire-format guarantee: the 404 body must keep the { message = "..." } shape.
    var messageProp = notFound.Value!.GetType().GetProperty("message");
    Assert.NotNull(messageProp);
    var messageValue = messageProp!.GetValue(notFound.Value) as string;
    Assert.Equal("Bank statement import with ID 99999 not found", messageValue);
}

[Fact]
public async Task GetBankStatement_DoesNotDispatchListRequest()
{
    // Arrange — guarantees the old list-handler workaround is gone.
    _mockMediator
        .Setup(m => m.Send(It.IsAny<GetBankStatementByIdRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((BankStatementImportDto?)null);

    // Act
    await _controller.GetBankStatement(7, CancellationToken.None);

    // Assert — only ByIdRequest was sent; no ListRequest was constructed.
    _mockMediator.Verify(
        m => m.Send(It.IsAny<GetBankStatementByIdRequest>(), It.IsAny<CancellationToken>()),
        Times.Once);
    _mockMediator.Verify(
        m => m.Send(It.IsAny<Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList.GetBankStatementListRequest>(), It.IsAny<CancellationToken>()),
        Times.Never);
}
```

- [ ] **Step 3: Run the controller tests — all should pass**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~BankStatementsControllerTests"
```

Expected: all tests pass (existing + 3 new = 6 tests).

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Controllers/BankStatementsControllerTests.cs
git commit -m "test(bank): cover controller dispatch + 404 message body for GetBankStatement"
```

---

## Task 6: Add integration tests for the end-to-end HTTP behavior

**Files:**
- Modify (append): `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportIntegrationTests.cs`

The existing `BankStatementImportTestFactory` (defined at the bottom of that same file) already wires the WebApp with mocked `IBankClient` / `IBankStatementImportService` while keeping the real EF Core in-memory `ApplicationDbContext` and MediatR pipeline. We seed data via `ApplicationDbContext` directly and assert end-to-end through `HttpClient`.

- [ ] **Step 1: Append two integration tests**

Add these two tests inside the existing `BankStatementImportIntegrationTests` class (before the final closing brace, but **before** the trailing `BankStatementImportTestFactory` class definition):

```csharp
[Fact]
public async Task GetBankStatement_WithExistingId_Returns200WithDtoBody()
{
    // Arrange — seed a bank statement directly through the DbContext.
    int seededId;
    using (var scope = _factory.Services.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<Anela.Heblo.Persistence.ApplicationDbContext>();
        var entity = new Anela.Heblo.Domain.Features.Bank.BankStatementImport("T-INT-GET", new DateTime(2026, 3, 1))
        {
            Account = "ComgateCZK",
            Currency = Anela.Heblo.Domain.Shared.CurrencyCode.CZK,
            ItemCount = 4,
            ImportResult = "OK"
        };
        context.BankStatements.Add(entity);
        await context.SaveChangesAsync();
        seededId = entity.Id;
    }

    // Act
    var response = await _client.GetAsync($"/api/bank-statements/{seededId}");

    // Assert
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var body = await response.Content.ReadAsStringAsync();
    var dto = JsonSerializer.Deserialize<Anela.Heblo.Application.Features.Bank.Contracts.BankStatementImportDto>(
        body,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    Assert.NotNull(dto);
    Assert.Equal(seededId, dto!.Id);
    Assert.Equal("T-INT-GET", dto.TransferId);
    Assert.Equal("ComgateCZK", dto.Account);
    Assert.Equal("CZK", dto.Currency);
    Assert.Equal(4, dto.ItemCount);
    Assert.Equal("OK", dto.ImportResult);
}

[Fact]
public async Task GetBankStatement_WithMissingId_Returns404WithMessageBody()
{
    // Arrange — pick an id that cannot exist.
    const int missingId = 987654321;

    // Act
    var response = await _client.GetAsync($"/api/bank-statements/{missingId}");

    // Assert
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

    var body = await response.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(body);
    Assert.True(doc.RootElement.TryGetProperty("message", out var messageProp),
        "404 response must contain a 'message' field to preserve wire format");
    Assert.Equal($"Bank statement import with ID {missingId} not found", messageProp.GetString());
}
```

Note on imports: `using System.Net;`, `using System.Text.Json;`, and `using Microsoft.Extensions.DependencyInjection;` are already at the top of this file from the existing tests; no new usings required if you fully qualify the few new types as shown above. (Optionally add `using Anela.Heblo.Application.Features.Bank.Contracts;` and `using Anela.Heblo.Domain.Features.Bank;` at the top and drop the fully qualified names — purely cosmetic.)

- [ ] **Step 2: Run the integration tests**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~BankStatementImportIntegrationTests.GetBankStatement"
```

Expected: both new tests pass. If the existing-id test fails because the seeded entity isn't found, the most likely cause is that the test factory is using a transient/replaced DbContext — re-read `BankStatementImportTestFactory` and `HebloWebApplicationFactory` and adjust the seeding scope.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportIntegrationTests.cs
git commit -m "test(bank): integration tests for GET /api/bank-statements/{id} 200 and 404"
```

---

## Task 7: Full validation sweep

Final checks per project rules (CLAUDE.md "Validation before completion").

- [ ] **Step 1: Backend full build**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: build succeeds, zero new warnings.

- [ ] **Step 2: Backend formatting**

Run:
```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: exits with code 0. If it reports drift, run without `--verify-no-changes`, then re-commit the formatting fixes as `chore: dotnet format`.

- [ ] **Step 3: Full test suite (Bank slice + Controller tests)**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Bank"
```

Expected: all Bank-feature tests + all `BankStatementsControllerTests` pass. No regressions.

- [ ] **Step 4: Confirm repository / interface untouched**

Run:
```bash
git diff main -- backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs
```

Expected: empty output. If anything appears, revert those files — they are out of scope (FR-5).

- [ ] **Step 5: Confirm OpenAPI / TypeScript client surface for this endpoint is unchanged**

The TypeScript client is auto-generated. The response type for `GET /api/bank-statements/{id}` was `BankStatementImportDto` before and remains `BankStatementImportDto` after (locked decision #1).

Run:
```bash
cd frontend && npm run build
```

Expected: build succeeds. If the generated client surface changed in a way that breaks the build, the most likely cause is the controller's return-type signature drifted — check it still reads `ActionResult<BankStatementImportDto>`.

- [ ] **Step 6: Frontend lint (sanity check; no FE code is intentionally being changed)**

Run:
```bash
cd frontend && npm run lint
```

Expected: passes. No new lint errors.

- [ ] **Step 7: Final commit (if formatter or generated client produced any drift)**

If any tracked file was modified by the formatter or the OpenAPI client regen, commit it:

```bash
git status
# If anything is unstaged:
git add -A
git commit -m "chore: apply formatter and regenerated openapi client"
```

If `git status` is clean, skip this step.

---

## Spec Coverage Map

Cross-checking every requirement in `spec.r1.md` (with arch-review amendments applied) against the tasks above:

| Requirement | Status | Task(s) |
|-------------|--------|---------|
| FR-1: `GetBankStatementByIdRequest` class, `IRequest<...>`, `int Id` | Covered | Task 1 |
| FR-2: New response type | **Removed by arch review** — reuses existing `BankStatementImportDto` | Task 1 (request signature), Task 3 (handler return) |
| FR-3: Handler calling `GetByIdAsync`, mapping to DTO, `null` for not-found, no `CancellationToken` passed to repo | Covered | Task 2 (tests), Task 3 (impl) |
| FR-4: Thin controller dispatcher, preserved 404 body, keep `[HttpGet("{id}")]`, keep `ActionResult<BankStatementImportDto>`, drop top-level catch | Covered | Task 4 |
| FR-5: Repository interface + impl unchanged | Verified | Task 7 Step 4 |
| FR-6: No new mapper file — use existing `BankMappingProfile` via `IMapper` | Covered (removed by arch review) | Task 3 (handler uses `_mapper.Map<BankStatementImportDto>(entity)`) |
| FR-7: Handler unit tests (found, missing, repo-called-once) + integration tests (200 + 404 with message body) + projection-equivalence test | Covered | Tasks 2, 5, 6 |
| NFR-1: Performance — single keyed lookup, no extra round-trips | Covered by design | Task 3 (single `GetByIdAsync` call, no list + filter + FirstOrDefault) |
| NFR-2: Security — unchanged authorization, same route binding | Covered | Task 4 (no auth attrs changed; route literal unchanged) |
| NFR-3: HTTP contract + OpenAPI surface unchanged | Verified | Task 4 (route + return type), Task 5 (404 body shape test), Task 6 (integration 200/404 body assertions), Task 7 Step 5 (`npm run build`) |
| NFR-4: Conforms to development guidelines, clean `dotnet build` + `dotnet format` | Verified | Task 7 Steps 1–2 |

No spec requirement is unmapped.
