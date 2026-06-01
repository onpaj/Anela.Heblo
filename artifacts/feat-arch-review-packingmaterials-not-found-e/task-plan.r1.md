# Consistent Not-Found Error Handling in PackingMaterials Module — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace exception-throwing not-found behavior in four PackingMaterials CRUD handlers with the existing structured-return pattern used by the allocation handlers, so `PUT /api/packing-materials/{id}`, `POST /api/packing-materials/{id}/quantity`, `DELETE /api/packing-materials/{id}`, and `GET /api/packing-materials/{id}/logs` return HTTP 404 instead of HTTP 500 for missing IDs.

**Architecture:** Each affected handler returns a response inheriting from `BaseResponse` (which provides `Success` and `ErrorCode`) plus a free-text `Error` string — exactly mirroring `GetAllocationsHandler` / `CreateAllocationHandler`. The controller inspects `response.Success` and `response.ErrorCode == ErrorCodes.ResourceNotFound` to return `NotFound(new { error = response.Error })`, matching the existing allocation endpoints in the same controller. `DeletePackingMaterialRequest` is promoted from `IRequest` (Unit) to `IRequest<DeletePackingMaterialResponse>` so it can carry the structured result.

**Tech Stack:** .NET 8, MediatR, ASP.NET Core MVC, xUnit + FluentAssertions + Moq, OpenAPI TypeScript client (auto-regenerated on `dotnet build`).

---

## File Map

**Modify (backend, application layer):**
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/UpdatePackingMaterialRequest.cs` — add `Error` to inline `UpdatePackingMaterialResponse` class.
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/UpdatePackingMaterialQuantityResponse.cs` — add `Error` property.
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialLogs/GetPackingMaterialLogsResponse.cs` — add `Error` property.
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialHandler.cs` — replace throw with structured return.
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs` — replace throw with structured return.
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialLogs/GetPackingMaterialLogsHandler.cs` — replace throw with structured return.
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/DeletePackingMaterial/DeletePackingMaterialRequest.cs` — change to `IRequest<DeletePackingMaterialResponse>`.
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/DeletePackingMaterial/DeletePackingMaterialHandler.cs` — change signature to `IRequestHandler<DeletePackingMaterialRequest, DeletePackingMaterialResponse>` and return structured response.

**Modify (backend, API layer):**
- `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs` — four endpoints (`UpdatePackingMaterial`, `UpdatePackingMaterialQuantity`, `DeletePackingMaterial`, `GetPackingMaterialLogs`) inspect response and map `ResourceNotFound` → 404.

**Create (backend):**
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/DeletePackingMaterial/DeletePackingMaterialResponse.cs` — new response DTO extending `BaseResponse` with `Error` property.

**Create (tests):**
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialCrudHandlerTests.cs` — handler tests for the four affected use cases.
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsControllerNotFoundTests.cs` — controller unit tests (Moq IMediator) verifying 404 on not-found responses.

**Frontend / generated artifacts (no source changes expected, only regeneration):**
- `frontend/src/api/generated/api-client.ts` — regenerates automatically on `dotnet build`. No frontend source edits expected per arch-review §FR-9.

---

## Task 1: Add structured-error scaffolding to response DTOs

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/UpdatePackingMaterialRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/UpdatePackingMaterialQuantityResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialLogs/GetPackingMaterialLogsResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/DeletePackingMaterial/DeletePackingMaterialResponse.cs`

This task is pure scaffolding (no behavior change). It compiles cleanly because `Error` is an additive optional property and `DeletePackingMaterialResponse` is a new type not yet referenced.

- [ ] **Step 1: Add `Error` property to `UpdatePackingMaterialResponse`**

Edit `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/UpdatePackingMaterialRequest.cs` so the inline response class becomes:

```csharp
public class UpdatePackingMaterialResponse : BaseResponse
{
    public PackingMaterialDto Material { get; set; } = null!;
    public string? Error { get; set; }
}
```

- [ ] **Step 2: Add `Error` property to `UpdatePackingMaterialQuantityResponse`**

Edit `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/UpdatePackingMaterialQuantityResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

public class UpdatePackingMaterialQuantityResponse : BaseResponse
{
    public PackingMaterialDto Material { get; set; } = null!;
    public string? Error { get; set; }
}
```

- [ ] **Step 3: Add `Error` property to `GetPackingMaterialLogsResponse`**

Edit `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialLogs/GetPackingMaterialLogsResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetPackingMaterialLogs;

public class GetPackingMaterialLogsResponse : BaseResponse
{
    public PackingMaterialDto Material { get; set; } = null!;
    public IEnumerable<PackingMaterialLogDto> Logs { get; set; } = new List<PackingMaterialLogDto>();
    public string? Error { get; set; }
}
```

- [ ] **Step 4: Create `DeletePackingMaterialResponse`**

Create `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/DeletePackingMaterial/DeletePackingMaterialResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.DeletePackingMaterial;

public class DeletePackingMaterialResponse : BaseResponse
{
    public string? Error { get; set; }
}
```

- [ ] **Step 5: Build to verify everything compiles**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds. The handlers and controller still reference the old contracts unchanged at this point; we have only added new optional members and one new type.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/UpdatePackingMaterialRequest.cs \
        backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/UpdatePackingMaterialQuantityResponse.cs \
        backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialLogs/GetPackingMaterialLogsResponse.cs \
        backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/DeletePackingMaterial/DeletePackingMaterialResponse.cs
git commit -m "feat: scaffold structured error fields on PackingMaterials response DTOs"
```

---

## Task 2: UpdatePackingMaterial — TDD handler + controller 404 mapping

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialCrudHandlerTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsControllerNotFoundTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialHandler.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs` (`UpdatePackingMaterial` action)

- [ ] **Step 1: Write the failing handler tests**

Create `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialCrudHandlerTests.cs` with this content (we will append cases for the other three handlers in later tasks):

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdatePackingMaterial;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using System.Reflection;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class PackingMaterialCrudHandlerTests
{
    private static PackingMaterial MakeMaterial(int id, string name = "TestMaterial")
    {
        var material = new PackingMaterial(name, 1m, ConsumptionType.PerOrder, 100m);
        typeof(PackingMaterial)
            .GetProperty("Id")!
            .SetValue(material, id);
        return material;
    }

    private static MockPackingMaterialRepository BuildRepo(params PackingMaterial[] materials)
    {
        var repo = new MockPackingMaterialRepository();
        repo.SetMaterials(materials);
        return repo;
    }

    // ---- UpdatePackingMaterial ----

    [Fact]
    public async Task UpdatePackingMaterial_ReturnsNotFoundResponse_WhenMaterialDoesNotExist()
    {
        // Arrange
        var repo = BuildRepo();
        var handler = new UpdatePackingMaterialHandler(repo);

        // Act
        var response = await handler.Handle(new UpdatePackingMaterialRequest
        {
            Id = 99,
            Name = "Anything",
            ConsumptionRate = 1m,
            ConsumptionType = ConsumptionType.PerOrder
        }, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.ResourceNotFound, response.ErrorCode);
        Assert.NotNull(response.Error);
        Assert.Contains("99", response.Error!);
    }

    [Fact]
    public async Task UpdatePackingMaterial_UpdatesMaterialAndReturnsSuccess_WhenMaterialExists()
    {
        // Arrange
        var material = MakeMaterial(1, "Old");
        var repo = BuildRepo(material);
        var handler = new UpdatePackingMaterialHandler(repo);

        // Act
        var response = await handler.Handle(new UpdatePackingMaterialRequest
        {
            Id = 1,
            Name = "New",
            ConsumptionRate = 2m,
            ConsumptionType = ConsumptionType.PerProduct
        }, CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Null(response.ErrorCode);
        Assert.Null(response.Error);
        Assert.NotNull(response.Material);
        Assert.Equal(1, response.Material.Id);
        Assert.Equal("New", response.Material.Name);
        Assert.Equal(2m, response.Material.ConsumptionRate);
        Assert.Single(repo.UpdatedMaterials);
    }
}
```

- [ ] **Step 2: Run the new tests, expect the not-found case to fail with a thrown exception**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterialCrudHandlerTests"`
Expected:
- `UpdatePackingMaterial_ReturnsNotFoundResponse_WhenMaterialDoesNotExist` FAILS because the handler currently throws `ArgumentException`.
- `UpdatePackingMaterial_UpdatesMaterialAndReturnsSuccess_WhenMaterialExists` PASSES (current happy-path behavior is correct).

- [ ] **Step 3: Replace the throw in `UpdatePackingMaterialHandler` with a structured return**

Edit `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialHandler.cs`. Add `using Anela.Heblo.Application.Shared;` at the top, then replace the not-found branch:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdatePackingMaterial;

public class UpdatePackingMaterialHandler : IRequestHandler<UpdatePackingMaterialRequest, UpdatePackingMaterialResponse>
{
    private readonly IPackingMaterialRepository _repository;

    public UpdatePackingMaterialHandler(IPackingMaterialRepository repository)
    {
        _repository = repository;
    }

    public async Task<UpdatePackingMaterialResponse> Handle(
        UpdatePackingMaterialRequest request,
        CancellationToken cancellationToken)
    {
        var material = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (material == null)
        {
            return new UpdatePackingMaterialResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ResourceNotFound,
                Error = $"Packing material with ID {request.Id} not found."
            };
        }

        material.UpdateMaterial(request.Name, request.ConsumptionRate, request.ConsumptionType);
        await _repository.UpdateAsync(material, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        var materialDto = new PackingMaterialDto
        {
            Id = material.Id,
            Name = material.Name,
            ConsumptionRate = material.ConsumptionRate,
            ConsumptionType = material.ConsumptionType,
            ConsumptionTypeText = GetConsumptionTypeText(material.ConsumptionType),
            CurrentQuantity = material.CurrentQuantity,
            ForecastedDays = null,
            CreatedAt = material.CreatedAt,
            UpdatedAt = material.UpdatedAt
        };

        return new UpdatePackingMaterialResponse
        {
            Material = materialDto
        };
    }

    private static string GetConsumptionTypeText(ConsumptionType type) => type switch
    {
        ConsumptionType.PerOrder => "za zakázku",
        ConsumptionType.PerProduct => "za produkt",
        ConsumptionType.PerDay => "za den",
        _ => type.ToString()
    };
}
```

- [ ] **Step 4: Re-run the handler tests, expect both to pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterialCrudHandlerTests"`
Expected: both `UpdatePackingMaterial_*` tests PASS.

- [ ] **Step 5: Write the failing controller test for the 404 mapping**

Create `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsControllerNotFoundTests.cs`:

```csharp
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class PackingMaterialsControllerNotFoundTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly PackingMaterialsController _controller;

    public PackingMaterialsControllerNotFoundTests()
    {
        _controller = new PackingMaterialsController(_mediator.Object);
    }

    [Fact]
    public async Task UpdatePackingMaterial_Returns404_WhenHandlerReturnsResourceNotFound()
    {
        // Arrange
        var notFound = new UpdatePackingMaterialResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.ResourceNotFound,
            Error = "Packing material with ID 99 not found."
        };
        _mediator
            .Setup(m => m.Send(It.IsAny<UpdatePackingMaterialRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(notFound);

        var request = new UpdatePackingMaterialRequest
        {
            Id = 99,
            Name = "Anything",
            ConsumptionRate = 1m,
            ConsumptionType = ConsumptionType.PerOrder
        };

        // Act
        var result = await _controller.UpdatePackingMaterial(99, request, CancellationToken.None);

        // Assert
        var notFoundResult = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFoundResult.StatusCode.Should().Be(404);
    }
}
```

- [ ] **Step 6: Run the controller test, expect it to fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterialsControllerNotFoundTests.UpdatePackingMaterial_Returns404"`
Expected: FAIL. The current controller returns `Ok(response)` regardless of `response.Success`, so the assertion `BeOfType<NotFoundObjectResult>()` fails.

- [ ] **Step 7: Update the `UpdatePackingMaterial` controller action to map the structured response**

Edit `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs`, replace the `UpdatePackingMaterial` action body (lines around 58-76) with:

```csharp
[HttpPut("{id}")]
[ProducesResponseType(typeof(UpdatePackingMaterialResponse), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<ActionResult<UpdatePackingMaterialResponse>> UpdatePackingMaterial(
    int id,
    [FromBody] UpdatePackingMaterialRequest request,
    CancellationToken cancellationToken = default)
{
    if (id != request.Id)
    {
        return BadRequest("ID mismatch");
    }

    if (!ModelState.IsValid)
    {
        return BadRequest(ModelState);
    }

    var response = await _mediator.Send(request, cancellationToken);
    if (response.Success) return Ok(response);
    if (response.ErrorCode == ErrorCodes.ResourceNotFound) return NotFound(new { error = response.Error });
    return BadRequest(new { error = response.Error });
}
```

- [ ] **Step 8: Re-run the controller test, expect it to pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterialsControllerNotFoundTests.UpdatePackingMaterial_Returns404"`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialHandler.cs \
        backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialCrudHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsControllerNotFoundTests.cs
git commit -m "fix(packing-materials): return 404 for UpdatePackingMaterial when material is missing"
```

---

## Task 3: UpdatePackingMaterialQuantity — TDD handler + controller 404 mapping

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialCrudHandlerTests.cs` (append cases)
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsControllerNotFoundTests.cs` (append case)
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs` (`UpdatePackingMaterialQuantity` action)

The `UpdatePackingMaterialQuantityHandler` depends on `ICurrentUserService` — we need a stub for tests. The simplest approach is a tiny in-file stub; if the test base already has a mock for it elsewhere we can reuse, but the simplest self-contained approach is a local stub.

- [ ] **Step 1: Add a local stub `ICurrentUserService` for tests**

Append this stub to `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialCrudHandlerTests.cs` (above the existing `PackingMaterialCrudHandlerTests` class, inside the same file, in the same namespace):

```csharp
internal sealed class StubCurrentUserService : Anela.Heblo.Domain.Features.Users.ICurrentUserService
{
    private readonly Anela.Heblo.Domain.Features.Users.CurrentUser? _user;

    public StubCurrentUserService(Anela.Heblo.Domain.Features.Users.CurrentUser? user = null)
    {
        _user = user;
    }

    public Anela.Heblo.Domain.Features.Users.CurrentUser GetCurrentUser()
        => _user ?? new Anela.Heblo.Domain.Features.Users.CurrentUser("test-user-id", "Test User", "test@example.com", true);
}
```

> **Note for executor:** if the `CurrentUser` constructor signature differs in this codebase, adapt the construction above to match the existing type. Discover by reading `backend/src/Anela.Heblo.Domain/Features/Users/CurrentUser.cs` (or wherever the type is defined) and use the existing happy-path constructor. The point of the stub is only that `GetCurrentUser()` returns a non-null value with an `Id`; nothing else is asserted.

- [ ] **Step 2: Append failing handler tests for `UpdatePackingMaterialQuantityHandler`**

Append these `[Fact]` methods inside the `PackingMaterialCrudHandlerTests` class (after the `UpdatePackingMaterial_*` tests). Add the needed `using` at the top of the file if missing: `using Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdatePackingMaterialQuantity;`.

```csharp
// ---- UpdatePackingMaterialQuantity ----

[Fact]
public async Task UpdatePackingMaterialQuantity_ReturnsNotFoundResponse_WhenMaterialDoesNotExist()
{
    // Arrange
    var repo = BuildRepo();
    var handler = new UpdatePackingMaterialQuantityHandler(repo, new StubCurrentUserService());

    // Act
    var response = await handler.Handle(new UpdatePackingMaterialQuantityRequest
    {
        Id = 99,
        NewQuantity = 5m,
        Date = DateOnly.FromDateTime(DateTime.UtcNow)
    }, CancellationToken.None);

    // Assert
    Assert.False(response.Success);
    Assert.Equal(ErrorCodes.ResourceNotFound, response.ErrorCode);
    Assert.NotNull(response.Error);
    Assert.Contains("99", response.Error!);
}

[Fact]
public async Task UpdatePackingMaterialQuantity_UpdatesAndReturnsSuccess_WhenMaterialExists()
{
    // Arrange
    var material = MakeMaterial(1);
    var repo = BuildRepo(material);
    var handler = new UpdatePackingMaterialQuantityHandler(repo, new StubCurrentUserService());

    // Act
    var response = await handler.Handle(new UpdatePackingMaterialQuantityRequest
    {
        Id = 1,
        NewQuantity = 42m,
        Date = DateOnly.FromDateTime(DateTime.UtcNow)
    }, CancellationToken.None);

    // Assert
    Assert.True(response.Success);
    Assert.Null(response.ErrorCode);
    Assert.Null(response.Error);
    Assert.NotNull(response.Material);
    Assert.Equal(1, response.Material.Id);
    Assert.Single(repo.UpdatedMaterials);
}
```

- [ ] **Step 3: Run the new tests, expect not-found to fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterialCrudHandlerTests.UpdatePackingMaterialQuantity"`
Expected: `UpdatePackingMaterialQuantity_ReturnsNotFoundResponse_WhenMaterialDoesNotExist` FAILS (handler throws). Happy-path PASSES.

- [ ] **Step 4: Replace the throw in `UpdatePackingMaterialQuantityHandler`**

Edit `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs`. Add `using Anela.Heblo.Application.Shared;` at the top, then replace the not-found branch:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdatePackingMaterialQuantity;

public class UpdatePackingMaterialQuantityHandler : IRequestHandler<UpdatePackingMaterialQuantityRequest, UpdatePackingMaterialQuantityResponse>
{
    private readonly IPackingMaterialRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public UpdatePackingMaterialQuantityHandler(
        IPackingMaterialRepository repository,
        ICurrentUserService currentUserService)
    {
        _repository = repository;
        _currentUserService = currentUserService;
    }

    public async Task<UpdatePackingMaterialQuantityResponse> Handle(
        UpdatePackingMaterialQuantityRequest request,
        CancellationToken cancellationToken)
    {
        var material = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (material == null)
        {
            return new UpdatePackingMaterialQuantityResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ResourceNotFound,
                Error = $"Packing material with ID {request.Id} not found."
            };
        }

        var currentUser = _currentUserService.GetCurrentUser();
        material.UpdateQuantity(request.NewQuantity, request.Date, LogEntryType.Manual, currentUser?.Id);

        await _repository.UpdateAsync(material, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        var oneMonthAgo = DateTime.UtcNow.AddMonths(-1);
        var recentLogs = await _repository.GetRecentLogsAsync(material.Id, oneMonthAgo, cancellationToken);
        var forecastedDays = material.CalculateForecastedDays(recentLogs.ToList());
        var displayForecast = forecastedDays == decimal.MaxValue ? null : (decimal?)Math.Round(forecastedDays, 1);

        var materialDto = new PackingMaterialDto
        {
            Id = material.Id,
            Name = material.Name,
            ConsumptionRate = material.ConsumptionRate,
            ConsumptionType = material.ConsumptionType,
            ConsumptionTypeText = GetConsumptionTypeText(material.ConsumptionType),
            CurrentQuantity = material.CurrentQuantity,
            ForecastedDays = displayForecast,
            CreatedAt = material.CreatedAt,
            UpdatedAt = material.UpdatedAt
        };

        return new UpdatePackingMaterialQuantityResponse
        {
            Material = materialDto
        };
    }

    private static string GetConsumptionTypeText(ConsumptionType type) => type switch
    {
        ConsumptionType.PerOrder => "za zakázku",
        ConsumptionType.PerProduct => "za produkt",
        ConsumptionType.PerDay => "za den",
        _ => type.ToString()
    };
}
```

- [ ] **Step 5: Re-run handler tests, expect both to pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterialCrudHandlerTests.UpdatePackingMaterialQuantity"`
Expected: PASS.

- [ ] **Step 6: Append failing controller test**

Append this `[Fact]` to `PackingMaterialsControllerNotFoundTests` (add `using Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdatePackingMaterialQuantity;` at the top if needed):

```csharp
[Fact]
public async Task UpdatePackingMaterialQuantity_Returns404_WhenHandlerReturnsResourceNotFound()
{
    // Arrange
    var notFound = new UpdatePackingMaterialQuantityResponse
    {
        Success = false,
        ErrorCode = ErrorCodes.ResourceNotFound,
        Error = "Packing material with ID 99 not found."
    };
    _mediator
        .Setup(m => m.Send(It.IsAny<UpdatePackingMaterialQuantityRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(notFound);

    var body = new UpdateQuantityRequest
    {
        NewQuantity = 5m,
        Date = DateOnly.FromDateTime(DateTime.UtcNow)
    };

    // Act
    var result = await _controller.UpdatePackingMaterialQuantity(99, body, CancellationToken.None);

    // Assert
    var notFoundResult = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
    notFoundResult.StatusCode.Should().Be(404);
}
```

- [ ] **Step 7: Run the controller test, expect it to fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterialsControllerNotFoundTests.UpdatePackingMaterialQuantity_Returns404"`
Expected: FAIL — controller currently returns `Ok(response)` regardless of `response.Success`.

- [ ] **Step 8: Update the `UpdatePackingMaterialQuantity` controller action**

Edit `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs`, replace the `UpdatePackingMaterialQuantity` action with:

```csharp
[HttpPost("{id}/quantity")]
[ProducesResponseType(typeof(UpdatePackingMaterialQuantityResponse), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<ActionResult<UpdatePackingMaterialQuantityResponse>> UpdatePackingMaterialQuantity(
    int id,
    [FromBody] UpdateQuantityRequest request,
    CancellationToken cancellationToken = default)
{
    if (!ModelState.IsValid)
    {
        return BadRequest(ModelState);
    }

    var quantityRequest = new UpdatePackingMaterialQuantityRequest
    {
        Id = id,
        NewQuantity = request.NewQuantity,
        Date = request.Date
    };

    var response = await _mediator.Send(quantityRequest, cancellationToken);
    if (response.Success) return Ok(response);
    if (response.ErrorCode == ErrorCodes.ResourceNotFound) return NotFound(new { error = response.Error });
    return BadRequest(new { error = response.Error });
}
```

- [ ] **Step 9: Re-run controller test, expect PASS**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterialsControllerNotFoundTests.UpdatePackingMaterialQuantity_Returns404"`
Expected: PASS.

- [ ] **Step 10: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs \
        backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialCrudHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsControllerNotFoundTests.cs
git commit -m "fix(packing-materials): return 404 for UpdatePackingMaterialQuantity when material is missing"
```

---

## Task 4: GetPackingMaterialLogs — TDD handler + controller 404 mapping

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialCrudHandlerTests.cs` (append)
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsControllerNotFoundTests.cs` (append)
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialLogs/GetPackingMaterialLogsHandler.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs` (`GetPackingMaterialLogs` action)

- [ ] **Step 1: Append failing handler tests**

Add `using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetPackingMaterialLogs;` at the top of `PackingMaterialCrudHandlerTests.cs` if missing, then append inside the test class:

```csharp
// ---- GetPackingMaterialLogs ----

[Fact]
public async Task GetPackingMaterialLogs_ReturnsNotFoundResponse_WhenMaterialDoesNotExist()
{
    // Arrange
    var repo = BuildRepo();
    var handler = new GetPackingMaterialLogsHandler(repo);

    // Act
    var response = await handler.Handle(new GetPackingMaterialLogsRequest
    {
        PackingMaterialId = 99,
        Days = 30
    }, CancellationToken.None);

    // Assert
    Assert.False(response.Success);
    Assert.Equal(ErrorCodes.ResourceNotFound, response.ErrorCode);
    Assert.NotNull(response.Error);
    Assert.Contains("99", response.Error!);
}

[Fact]
public async Task GetPackingMaterialLogs_ReturnsSuccess_WhenMaterialExists()
{
    // Arrange
    var material = MakeMaterial(1);
    var repo = BuildRepo(material);
    var handler = new GetPackingMaterialLogsHandler(repo);

    // Act
    var response = await handler.Handle(new GetPackingMaterialLogsRequest
    {
        PackingMaterialId = 1,
        Days = 30
    }, CancellationToken.None);

    // Assert
    Assert.True(response.Success);
    Assert.Null(response.ErrorCode);
    Assert.Null(response.Error);
    Assert.NotNull(response.Material);
    Assert.Equal(1, response.Material.Id);
    Assert.Empty(response.Logs);
}
```

- [ ] **Step 2: Run the new tests, expect not-found to fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterialCrudHandlerTests.GetPackingMaterialLogs"`
Expected: not-found case FAILS (handler throws `InvalidOperationException`). Happy-path PASSES.

- [ ] **Step 3: Replace the throw in `GetPackingMaterialLogsHandler`**

Edit `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialLogs/GetPackingMaterialLogsHandler.cs`. Add `using Anela.Heblo.Application.Shared;` and replace the not-found branch:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetPackingMaterialLogs;

public class GetPackingMaterialLogsHandler : IRequestHandler<GetPackingMaterialLogsRequest, GetPackingMaterialLogsResponse>
{
    private readonly IPackingMaterialRepository _repository;

    public GetPackingMaterialLogsHandler(IPackingMaterialRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetPackingMaterialLogsResponse> Handle(
        GetPackingMaterialLogsRequest request,
        CancellationToken cancellationToken)
    {
        var material = await _repository.GetByIdWithLogsAsync(request.PackingMaterialId, cancellationToken);
        if (material == null)
        {
            return new GetPackingMaterialLogsResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ResourceNotFound,
                Error = $"Packing material with ID {request.PackingMaterialId} not found."
            };
        }

        var fromDate = DateTime.UtcNow.AddDays(-request.Days);
        var recentLogs = await _repository.GetRecentLogsAsync(request.PackingMaterialId, fromDate, cancellationToken);

        var materialDto = new PackingMaterialDto
        {
            Id = material.Id,
            Name = material.Name,
            ConsumptionRate = material.ConsumptionRate,
            ConsumptionType = material.ConsumptionType,
            ConsumptionTypeText = GetConsumptionTypeText(material.ConsumptionType),
            CurrentQuantity = material.CurrentQuantity,
            CreatedAt = material.CreatedAt,
            UpdatedAt = material.UpdatedAt
        };

        var logDtos = recentLogs.Select(log => new PackingMaterialLogDto
        {
            Id = log.Id,
            PackingMaterialId = log.PackingMaterialId,
            Date = log.Date,
            OldQuantity = log.OldQuantity,
            NewQuantity = log.NewQuantity,
            ChangeAmount = log.ChangeAmount,
            LogType = log.LogType,
            LogTypeText = GetLogTypeText(log.LogType),
            UserId = log.UserId,
            CreatedAt = log.CreatedAt
        }).OrderByDescending(log => log.Date).ThenByDescending(log => log.CreatedAt).ToList();

        return new GetPackingMaterialLogsResponse
        {
            Material = materialDto,
            Logs = logDtos
        };
    }

    private static string GetConsumptionTypeText(ConsumptionType type) => type switch
    {
        ConsumptionType.PerOrder => "za zakázku",
        ConsumptionType.PerProduct => "za produkt",
        ConsumptionType.PerDay => "za den",
        _ => type.ToString()
    };

    private static string GetLogTypeText(LogEntryType type) => type switch
    {
        LogEntryType.Manual => "Ruční",
        LogEntryType.AutomaticConsumption => "Automatická spotřeba",
        _ => type.ToString()
    };
}
```

- [ ] **Step 4: Re-run handler tests, expect both to pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterialCrudHandlerTests.GetPackingMaterialLogs"`
Expected: PASS.

- [ ] **Step 5: Append failing controller test**

Append this `[Fact]` to `PackingMaterialsControllerNotFoundTests` (add `using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetPackingMaterialLogs;` at the top if missing):

```csharp
[Fact]
public async Task GetPackingMaterialLogs_Returns404_WhenHandlerReturnsResourceNotFound()
{
    // Arrange
    var notFound = new GetPackingMaterialLogsResponse
    {
        Success = false,
        ErrorCode = ErrorCodes.ResourceNotFound,
        Error = "Packing material with ID 99 not found."
    };
    _mediator
        .Setup(m => m.Send(It.IsAny<GetPackingMaterialLogsRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(notFound);

    // Act
    var result = await _controller.GetPackingMaterialLogs(99, 60, CancellationToken.None);

    // Assert
    var notFoundResult = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
    notFoundResult.StatusCode.Should().Be(404);
}
```

- [ ] **Step 6: Run controller test, expect fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterialsControllerNotFoundTests.GetPackingMaterialLogs_Returns404"`
Expected: FAIL.

- [ ] **Step 7: Update the `GetPackingMaterialLogs` controller action**

Edit `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs`, replace the `GetPackingMaterialLogs` action with:

```csharp
[HttpGet("{id}/logs")]
[ProducesResponseType(typeof(GetPackingMaterialLogsResponse), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<ActionResult<GetPackingMaterialLogsResponse>> GetPackingMaterialLogs(
    int id,
    [FromQuery] int days = 60,
    CancellationToken cancellationToken = default)
{
    var request = new GetPackingMaterialLogsRequest
    {
        PackingMaterialId = id,
        Days = days
    };

    var response = await _mediator.Send(request, cancellationToken);
    if (response.Success) return Ok(response);
    if (response.ErrorCode == ErrorCodes.ResourceNotFound) return NotFound(new { error = response.Error });
    return BadRequest(new { error = response.Error });
}
```

- [ ] **Step 8: Re-run controller test, expect PASS**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterialsControllerNotFoundTests.GetPackingMaterialLogs_Returns404"`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialLogs/GetPackingMaterialLogsHandler.cs \
        backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialCrudHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsControllerNotFoundTests.cs
git commit -m "fix(packing-materials): return 404 for GetPackingMaterialLogs when material is missing"
```

---

## Task 5: DeletePackingMaterial — contract change + TDD handler + controller 204/404 mapping

This task is larger than the previous three because `DeletePackingMaterialRequest` must be promoted from `IRequest` (Unit) to `IRequest<DeletePackingMaterialResponse>`, the handler signature changes, and the controller action moves from awaiting `Unit` to mapping a typed response.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/DeletePackingMaterial/DeletePackingMaterialRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/DeletePackingMaterial/DeletePackingMaterialHandler.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs` (`DeletePackingMaterial` action)
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialCrudHandlerTests.cs` (append)
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsControllerNotFoundTests.cs` (append)

- [ ] **Step 1: Append failing handler tests**

Add `using Anela.Heblo.Application.Features.PackingMaterials.UseCases.DeletePackingMaterial;` at the top of `PackingMaterialCrudHandlerTests.cs` if missing, then append inside the test class:

```csharp
// ---- DeletePackingMaterial ----

[Fact]
public async Task DeletePackingMaterial_ReturnsNotFoundResponse_WhenMaterialDoesNotExist()
{
    // Arrange
    var repo = BuildRepo();
    var handler = new DeletePackingMaterialHandler(repo);

    // Act
    var response = await handler.Handle(new DeletePackingMaterialRequest { Id = 99 }, CancellationToken.None);

    // Assert
    Assert.False(response.Success);
    Assert.Equal(ErrorCodes.ResourceNotFound, response.ErrorCode);
    Assert.NotNull(response.Error);
    Assert.Contains("99", response.Error!);
    Assert.Single(repo.Materials);
}

[Fact]
public async Task DeletePackingMaterial_DeletesAndReturnsSuccess_WhenMaterialExists()
{
    // Arrange
    var material = MakeMaterial(1);
    var repo = BuildRepo(material);
    var handler = new DeletePackingMaterialHandler(repo);

    // Act
    var response = await handler.Handle(new DeletePackingMaterialRequest { Id = 1 }, CancellationToken.None);

    // Assert
    Assert.True(response.Success);
    Assert.Null(response.ErrorCode);
    Assert.Null(response.Error);
    Assert.Empty(repo.Materials);
}
```

- [ ] **Step 2: Run, expect compile errors (return type changed)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterialCrudHandlerTests.DeletePackingMaterial"`
Expected: COMPILE ERROR. The new tests treat `handler.Handle(...)` as returning a `DeletePackingMaterialResponse`, but the current handler returns `Task` (Unit). The tests cannot even compile until the contract is changed in Step 3.

- [ ] **Step 3: Change `DeletePackingMaterialRequest` to typed request**

Edit `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/DeletePackingMaterial/DeletePackingMaterialRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.DeletePackingMaterial;

public class DeletePackingMaterialRequest : IRequest<DeletePackingMaterialResponse>
{
    public int Id { get; set; }
}
```

- [ ] **Step 4: Change `DeletePackingMaterialHandler` signature and body**

Edit `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/DeletePackingMaterial/DeletePackingMaterialHandler.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.PackingMaterials;
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.DeletePackingMaterial;

public class DeletePackingMaterialHandler : IRequestHandler<DeletePackingMaterialRequest, DeletePackingMaterialResponse>
{
    private readonly IPackingMaterialRepository _repository;

    public DeletePackingMaterialHandler(IPackingMaterialRepository repository)
    {
        _repository = repository;
    }

    public async Task<DeletePackingMaterialResponse> Handle(DeletePackingMaterialRequest request, CancellationToken cancellationToken)
    {
        var packingMaterial = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (packingMaterial == null)
        {
            return new DeletePackingMaterialResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ResourceNotFound,
                Error = $"Packing material with ID {request.Id} not found."
            };
        }

        await _repository.DeleteAsync(packingMaterial, cancellationToken);
        return new DeletePackingMaterialResponse();
    }
}
```

- [ ] **Step 5: Update the `DeletePackingMaterial` controller action**

Edit `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs`, replace the `DeletePackingMaterial` action with:

```csharp
[HttpDelete("{id}")]
[ProducesResponseType(StatusCodes.Status204NoContent)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<ActionResult> DeletePackingMaterial(
    int id,
    CancellationToken cancellationToken = default)
{
    var request = new DeletePackingMaterialRequest { Id = id };
    var response = await _mediator.Send(request, cancellationToken);
    if (response.Success) return NoContent();
    if (response.ErrorCode == ErrorCodes.ResourceNotFound) return NotFound(new { error = response.Error });
    return BadRequest(new { error = response.Error });
}
```

- [ ] **Step 6: Build to confirm everything compiles**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds.

- [ ] **Step 7: Re-run handler tests, expect both DeletePackingMaterial cases to pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterialCrudHandlerTests.DeletePackingMaterial"`
Expected: PASS.

- [ ] **Step 8: Append failing controller test**

Append these `[Fact]` methods to `PackingMaterialsControllerNotFoundTests` (add `using Anela.Heblo.Application.Features.PackingMaterials.UseCases.DeletePackingMaterial;` at the top if missing):

```csharp
[Fact]
public async Task DeletePackingMaterial_Returns404_WhenHandlerReturnsResourceNotFound()
{
    // Arrange
    var notFound = new DeletePackingMaterialResponse
    {
        Success = false,
        ErrorCode = ErrorCodes.ResourceNotFound,
        Error = "Packing material with ID 99 not found."
    };
    _mediator
        .Setup(m => m.Send(It.IsAny<DeletePackingMaterialRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(notFound);

    // Act
    var result = await _controller.DeletePackingMaterial(99, CancellationToken.None);

    // Assert
    var notFoundResult = result.Should().BeOfType<NotFoundObjectResult>().Subject;
    notFoundResult.StatusCode.Should().Be(404);
}

[Fact]
public async Task DeletePackingMaterial_Returns204_WhenHandlerReturnsSuccess()
{
    // Arrange
    var ok = new DeletePackingMaterialResponse();
    _mediator
        .Setup(m => m.Send(It.IsAny<DeletePackingMaterialRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(ok);

    // Act
    var result = await _controller.DeletePackingMaterial(1, CancellationToken.None);

    // Assert
    result.Should().BeOfType<NoContentResult>();
}
```

- [ ] **Step 9: Run the controller tests, expect PASS**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterialsControllerNotFoundTests.DeletePackingMaterial"`
Expected: PASS (Step 5 already updated the controller to inspect the typed response).

- [ ] **Step 10: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/DeletePackingMaterial/DeletePackingMaterialRequest.cs \
        backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/DeletePackingMaterial/DeletePackingMaterialHandler.cs \
        backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialCrudHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsControllerNotFoundTests.cs
git commit -m "fix(packing-materials): return 404 for DeletePackingMaterial when material is missing"
```

---

## Task 6: Backend-wide validation

This task confirms nothing else regressed (the PackingMaterials module shares the `PackingMaterialsController` with the allocation endpoints, and the global build must remain clean).

**Files:** none changed; verification only. If `dotnet format` reports formatting drift in the files this PR touched, accept those edits in a follow-up commit.

- [ ] **Step 1: Run `dotnet build` on the whole solution**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: zero errors, zero warnings introduced by this PR.

- [ ] **Step 2: Run the full PackingMaterials test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.PackingMaterials"`
Expected: all PackingMaterials tests pass, including the existing `AllocationHandlerTests`, `ConsumptionCalculationServiceTests`, and `GetDailyConsumptionBreakdownHandlerTests`.

- [ ] **Step 3: Run the full backend test suite**

Run: `dotnet test backend/Anela.Heblo.sln`
Expected: total test count is at least equal to the count before this PR plus the new tests added in Tasks 2–5; zero failures.

- [ ] **Step 4: Run `dotnet format` on the modified projects**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes` (or `dotnet format backend/Anela.Heblo.sln` if drift is found, then re-run with `--verify-no-changes`)
Expected: no formatting violations remain.

- [ ] **Step 5: Commit any formatting fixes (only if Step 4 reformatted files)**

```bash
git add -A
git commit -m "chore: apply dotnet format"
```

Skip this step entirely if `dotnet format --verify-no-changes` reported nothing.

---

## Task 7: Frontend regeneration + verification + audit notes

The OpenAPI TypeScript client is regenerated automatically by `dotnet build`. We verify the frontend still builds, lints, and that the frontend audit (FR-9) finds no required source changes.

**Files:** no frontend source edits expected. Audit findings are recorded in the eventual PR description, not in any source file.

- [ ] **Step 1: Ensure backend builds (so the TS client regenerates)**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: succeeds. The build target regenerates `frontend/src/api/generated/api-client.ts`. The regenerated client will gain a new TypeScript type for `DeletePackingMaterialResponse` and additional optional fields on the three existing types — purely additive.

- [ ] **Step 2: Frontend build**

Run: `npm --prefix frontend run build`
Expected: succeeds with no new TS errors. The hand-rolled `frontend/src/api/hooks/usePackingMaterials.ts` does not consume the new fields and is unaffected.

- [ ] **Step 3: Frontend lint**

Run: `npm --prefix frontend run lint`
Expected: succeeds with no new lint errors.

- [ ] **Step 4: Audit frontend call sites for the four affected endpoints**

Use Grep to find every call site that targets the four affected endpoints. Run from the worktree root:

```
Grep pattern: packing-materials/\$\{|packing-materials\?|/quantity|/logs
Glob:        frontend/src/**/*.ts*
```

Specifically inspect `frontend/src/api/hooks/usePackingMaterials.ts`. Verify that:
- It currently throws a generic `Error` for any non-OK HTTP status. (Confirmed by arch-review §FR-9.)
- No call site distinguishes HTTP 500 from HTTP 404 by status code.
- No call site reads `response.body.Error` for a special UX on not-found.

The expected outcome is **zero source-code changes**. Record this confirmation as a one-line note for the eventual PR description, e.g.:

> *Frontend audit: only call site is `frontend/src/api/hooks/usePackingMaterials.ts`, which throws a generic `Error` on any non-OK status. No 500-vs-404 differentiation. No source changes required. (FR-9.)*

If the audit unexpectedly finds a call site that branches on 500, **stop and flag it** — that case is beyond the spec's scope and needs a separate discussion before proceeding.

- [ ] **Step 5: Commit (only if Step 1 produced regenerated client edits to commit)**

If the regenerated `frontend/src/api/generated/api-client.ts` has uncommitted diffs from the build, stage and commit them:

```bash
git status --short frontend/src/api/generated
# if changes are listed:
git add frontend/src/api/generated/api-client.ts
git commit -m "chore: regenerate openapi client for PackingMaterials response DTOs"
```

If the file is gitignored or otherwise not tracked (some projects exclude generated artifacts), skip this step.

---

## Self-Review (executed before declaring the plan complete)

**Spec coverage:**
- FR-1 (UpdatePackingMaterial structured not-found) → Task 2.
- FR-2 (UpdatePackingMaterialQuantity structured not-found) → Task 3.
- FR-3 (DeletePackingMaterial structured not-found) → Task 5.
- FR-4 (GetPackingMaterialLogs structured not-found) → Task 4.
- FR-5 (response DTOs carry `Success`/`ErrorCode`/`Error`) → Task 1 (scaffolding) + arch-review override (`Error` per response, not on `BaseResponse`).
- FR-6 (controllers map ResourceNotFound to 404) → Tasks 2, 3, 4, 5 (controller edits + tests). Envelope is `NotFound(new { error = response.Error })` per arch-review override.
- FR-7 (consistent message template) → All four handlers emit `"Packing material with ID {id} not found."` matching `GetAllocationsHandler.cs:32`.
- FR-8 (tests for changed behavior) → Tasks 2–5 add both not-found and happy-path tests per handler, plus controller-level 404 mapping tests.
- FR-9 (frontend audit) → Task 7 Step 4 documents the audit result with no source changes.
- NFR-1 (performance) → no concern; structured returns replace throws, marginally faster on the not-found path.
- NFR-2 (security) → unchanged; messages echo only the caller-supplied ID.
- NFR-3 (backwards compat) → 404 replaces 500 on not-found path; happy path unchanged. Stated intentionally in this plan and in the spec.
- NFR-4 (testability) → handler not-found path tested without throws.
- NFR-5 (observability) → no logger injected (matches the spec NFR-5 amendment in arch-review).

**Placeholder scan:** none — every step has either concrete code or a concrete command + expected outcome. The one judgment call (`CurrentUser` constructor signature in Task 3 Step 1) is explicitly framed as "read the existing type and match it"; no other step contains "TBD" / "as appropriate" / "etc."

**Type consistency:** the four response DTOs all gain the same `string? Error { get; set; }` member. The four handler not-found return paths all emit the identical message template `"Packing material with ID {id} not found."`. The four controller actions all use the identical mapping `if (response.Success) return Ok/NoContent; if (response.ErrorCode == ErrorCodes.ResourceNotFound) return NotFound(new { error = response.Error }); return BadRequest(new { error = response.Error });`. `DeletePackingMaterialResponse` is referenced consistently between Task 1 (creation) and Task 5 (use).
