# PackingMaterialDto Mapper Extraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace four duplicated `new PackingMaterialDto { ... }` object-initializer blocks (one per handler) with calls to a single shared `PackingMaterialMapper.ToDto(material, forecastedDays)` method, with zero behavior change.

**Architecture:** Add `internal static class PackingMaterialMapper` in a new `Mapping/` subfolder under `Features/PackingMaterials/`, mirroring the existing `Features/Journal/Mapping/JournalEntryMapper.cs` precedent. It takes the entity plus an already-computed nullable forecast and returns a fully populated `PackingMaterialDto`, calling the existing `PackingMaterialsTextHelper.ConsumptionTypeText` internally. Each of the four handlers keeps its own forecast-computation logic untouched and only swaps its DTO construction for a call to the mapper.

**Tech Stack:** .NET 8, xUnit, FluentAssertions (used in `GetPackingMaterialsListHandlerTests.cs`; plain `Xunit.Assert` used in `PackingMaterialCrudHandlerTests.cs` — this plan uses `Xunit.Assert` for the new mapper test to match the simpler, more common style of the two).

---

### task: add-packing-material-mapper

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Mapping/PackingMaterialMapper.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialMapperTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialMapperTests.cs`:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Mapping;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class PackingMaterialMapperTests
{
    private static PackingMaterial MakeMaterial(int id, string name, decimal consumptionRate, ConsumptionType consumptionType, decimal currentQuantity)
    {
        var material = new PackingMaterial(name, consumptionRate, consumptionType, currentQuantity);
        typeof(PackingMaterial)
            .GetProperty("Id")!
            .SetValue(material, id);
        return material;
    }

    [Fact]
    public void ToDto_MapsAllFields_WhenForecastedDaysIsProvided()
    {
        // Arrange
        var material = MakeMaterial(7, "Cardboard Box", 2.5m, ConsumptionType.PerOrder, 150m);

        // Act
        var dto = PackingMaterialMapper.ToDto(material, 12.3m);

        // Assert
        Assert.Equal(material.Id, dto.Id);
        Assert.Equal(material.Name, dto.Name);
        Assert.Equal(material.ConsumptionRate, dto.ConsumptionRate);
        Assert.Equal(material.ConsumptionType, dto.ConsumptionType);
        Assert.Equal("za zakázku", dto.ConsumptionTypeText);
        Assert.Equal(material.CurrentQuantity, dto.CurrentQuantity);
        Assert.Equal(12.3m, dto.ForecastedDays);
        Assert.Equal(material.CreatedAt, dto.CreatedAt);
        Assert.Equal(material.UpdatedAt, dto.UpdatedAt);
    }

    [Fact]
    public void ToDto_SetsForecastedDaysToNull_WhenForecastedDaysArgumentIsNull()
    {
        // Arrange
        var material = MakeMaterial(1, "Tape Roll", 1m, ConsumptionType.PerDay, 50m);

        // Act
        var dto = PackingMaterialMapper.ToDto(material, null);

        // Assert
        Assert.Null(dto.ForecastedDays);
    }

    [Theory]
    [InlineData(ConsumptionType.PerOrder, "za zakázku")]
    [InlineData(ConsumptionType.PerProduct, "za produkt")]
    [InlineData(ConsumptionType.PerDay, "za den")]
    public void ToDto_DerivesConsumptionTypeText_FromConsumptionType(ConsumptionType type, string expectedText)
    {
        // Arrange
        var material = MakeMaterial(1, "Material", 1m, type, 10m);

        // Act
        var dto = PackingMaterialMapper.ToDto(material, null);

        // Assert
        Assert.Equal(expectedText, dto.ConsumptionTypeText);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~PackingMaterialMapperTests"`
Expected: FAIL — build error, `PackingMaterialMapper` does not exist (`CS0246: The type or namespace name 'PackingMaterialMapper' could not be found`).

- [ ] **Step 3: Write minimal implementation**

Create `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Mapping/PackingMaterialMapper.cs`:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.PackingMaterials;

namespace Anela.Heblo.Application.Features.PackingMaterials.Mapping;

internal static class PackingMaterialMapper
{
    public static PackingMaterialDto ToDto(PackingMaterial material, decimal? forecastedDays) => new()
    {
        Id = material.Id,
        Name = material.Name,
        ConsumptionRate = material.ConsumptionRate,
        ConsumptionType = material.ConsumptionType,
        ConsumptionTypeText = PackingMaterialsTextHelper.ConsumptionTypeText(material.ConsumptionType),
        CurrentQuantity = material.CurrentQuantity,
        ForecastedDays = forecastedDays,
        CreatedAt = material.CreatedAt,
        UpdatedAt = material.UpdatedAt
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~PackingMaterialMapperTests"`
Expected: PASS — 5 tests passed (1 + 1 + 3 theory cases).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/Mapping/PackingMaterialMapper.cs backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialMapperTests.cs
git commit -m "feat(packing-materials): add PackingMaterialMapper"
```

---

### task: wire-create-and-update-handlers

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/CreatePackingMaterial/CreatePackingMaterialHandler.cs:1-48`
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialHandler.cs:1-55`
- Test (existing, must keep passing unmodified): `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialCrudHandlerTests.cs`

This task has no new test — `PackingMaterialCrudHandlerTests.cs` (`UpdatePackingMaterial_UpdatesMaterialAndReturnsSuccess_WhenMaterialExists`) already asserts `response.Material.Id` / `.Name` on the updated DTO, so it is the regression guard. `CreatePackingMaterialHandler` currently has no dedicated handler test; this task does not add one (out of scope per spec — pure refactor, no new test infrastructure required beyond the mapper's own unit tests from the previous task).

- [ ] **Step 1: Run the existing regression test to confirm current baseline passes**

Run: `dotnet test --filter "FullyQualifiedName~PackingMaterialCrudHandlerTests"`
Expected: PASS (8 tests, all green, before this task's edits).

- [ ] **Step 2: Refactor CreatePackingMaterialHandler to use the mapper**

In `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/CreatePackingMaterial/CreatePackingMaterialHandler.cs`, add the mapper's namespace to the usings and replace the manual `PackingMaterialDto` construction (current lines 30-41) with a call to `PackingMaterialMapper.ToDto`:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Features.PackingMaterials.Mapping;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.CreatePackingMaterial;

public class CreatePackingMaterialHandler : IRequestHandler<CreatePackingMaterialRequest, CreatePackingMaterialResponse>
{
    private readonly IPackingMaterialRepository _repository;

    public CreatePackingMaterialHandler(IPackingMaterialRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreatePackingMaterialResponse> Handle(
        CreatePackingMaterialRequest request,
        CancellationToken cancellationToken)
    {
        var material = new PackingMaterial(
            request.Name,
            request.ConsumptionRate,
            request.ConsumptionType,
            request.CurrentQuantity);

        var createdMaterial = await _repository.AddAsync(material, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        var materialDto = PackingMaterialMapper.ToDto(createdMaterial, forecastedDays: null); // New material, no history

        return new CreatePackingMaterialResponse
        {
            Id = createdMaterial.Id,
            Material = materialDto
        };
    }
}
```

Note: `ConsumptionType` using directive is no longer referenced directly by this file after the change (the type still flows through `request.ConsumptionType`, whose type is inferred) — leave the `using Anela.Heblo.Domain.Features.PackingMaterials.Enums;` directive in place since `dotnet format`/build will flag it only if genuinely unused; verify in Step 5.

- [ ] **Step 3: Refactor UpdatePackingMaterialHandler to use the mapper**

In `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialHandler.cs`, add the mapper's namespace to the usings and replace the manual `PackingMaterialDto` construction (current lines 37-48) with a call to `PackingMaterialMapper.ToDto`:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Features.PackingMaterials.Mapping;
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

        var materialDto = PackingMaterialMapper.ToDto(material, forecastedDays: null);

        return new UpdatePackingMaterialResponse
        {
            Material = materialDto
        };
    }
}
```

- [ ] **Step 4: Run the regression test to verify it still passes**

Run: `dotnet test --filter "FullyQualifiedName~PackingMaterialCrudHandlerTests"`
Expected: PASS (8 tests, all green — same count and names as Step 1, no change in behavior).

- [ ] **Step 5: Build to confirm no unused-using or compile warnings introduced**

Run: `dotnet build`
Expected: Build succeeds, 0 errors. If `Anela.Heblo.Domain.Features.PackingMaterials.Enums` is flagged as an unused using in either file, remove that specific `using` line from that file only (do not touch other usings).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/CreatePackingMaterial/CreatePackingMaterialHandler.cs backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialHandler.cs
git commit -m "refactor(packing-materials): use PackingMaterialMapper in create/update handlers"
```

---

### task: wire-quantity-and-list-handlers

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs:1-67`
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListHandler.cs:1-76`
- Test (existing, must keep passing unmodified): `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialCrudHandlerTests.cs` (quantity-update cases)
- Test (existing, must keep passing unmodified): `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetPackingMaterialsListHandlerTests.cs`

`GetPackingMaterialsListHandlerTests.cs` already asserts `dto.ForecastedDays` values computed via the real `CalculateForecastedDays`/`Math.Round`/`decimal.MaxValue` guard path against an in-memory EF Core database — this is the strongest existing regression guard for forecast-value correctness through the mapper, since it exercises the exact computed-forecast branch this task must not alter.

- [ ] **Step 1: Run the existing regression tests to confirm current baseline passes**

Run: `dotnet test --filter "FullyQualifiedName~GetPackingMaterialsListHandlerTests|FullyQualifiedName~PackingMaterialCrudHandlerTests"`
Expected: PASS (all tests green, before this task's edits).

- [ ] **Step 2: Refactor UpdatePackingMaterialQuantityHandler to use the mapper**

In `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs`, add the mapper's namespace to the usings and replace the manual `PackingMaterialDto` construction (current lines 49-60) with a call to `PackingMaterialMapper.ToDto`, keeping the forecast computation exactly as-is:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Features.PackingMaterials.Mapping;
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

        var materialDto = PackingMaterialMapper.ToDto(material, displayForecast);

        return new UpdatePackingMaterialQuantityResponse
        {
            Material = materialDto
        };
    }
}
```

- [ ] **Step 3: Refactor GetPackingMaterialsListHandler to use the mapper**

In `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListHandler.cs`, add the mapper's namespace to the usings and replace the manual `PackingMaterialDto` construction inside the `.Select(...)` (current lines 53-64) with a call to `PackingMaterialMapper.ToDto`, keeping the per-item forecast computation and the `withForecast`/`withoutForecast`/`totalLogs` counters exactly as-is:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Features.PackingMaterials.Mapping;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetPackingMaterialsList;

public class GetPackingMaterialsListHandler : IRequestHandler<GetPackingMaterialsListRequest, GetPackingMaterialsListResponse>
{
    private readonly IPackingMaterialRepository _repository;
    private readonly ILogger<GetPackingMaterialsListHandler> _logger;

    public GetPackingMaterialsListHandler(
        IPackingMaterialRepository repository,
        ILogger<GetPackingMaterialsListHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<GetPackingMaterialsListResponse> Handle(
        GetPackingMaterialsListRequest request,
        CancellationToken cancellationToken)
    {
        var materials = (await _repository.GetAllAsync(cancellationToken)).ToList();
        var oneMonthAgo = DateTime.UtcNow.AddMonths(-1);

        var logsByMaterial = await _repository.GetRecentLogsForMaterialsAsync(
            materials.Select(m => m.Id),
            oneMonthAgo,
            cancellationToken);

        var withForecast = 0;
        var withoutForecast = 0;
        var totalLogs = 0;

        var materialDtos = materials.Select(material =>
        {
            var recentLogs = logsByMaterial.TryGetValue(material.Id, out var logs)
                ? logs.ToList()
                : new List<PackingMaterialLog>();
            totalLogs += recentLogs.Count;

            var forecastedDays = material.CalculateForecastedDays(recentLogs);
            var displayForecast = forecastedDays == decimal.MaxValue
                ? null
                : (decimal?)Math.Round(forecastedDays, 1);

            if (displayForecast.HasValue) withForecast++;
            else withoutForecast++;

            return PackingMaterialMapper.ToDto(material, displayForecast);
        }).ToList();

        _logger.LogDebug(
            "PackingMaterials list: materials={Count}, logsLoaded={LogCount}, withForecast={WithForecast}, withoutForecast={WithoutForecast}",
            materialDtos.Count, totalLogs, withForecast, withoutForecast);

        return new GetPackingMaterialsListResponse
        {
            Materials = materialDtos
        };
    }
}
```

- [ ] **Step 4: Run the regression tests to verify they still pass**

Run: `dotnet test --filter "FullyQualifiedName~GetPackingMaterialsListHandlerTests|FullyQualifiedName~PackingMaterialCrudHandlerTests"`
Expected: PASS — same test count and names as Step 1, all green, no behavior change (forecast values, especially the `10m` and zero-quantity cases in `GetPackingMaterialsListHandlerTests`, must be byte-identical).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListHandler.cs
git commit -m "refactor(packing-materials): use PackingMaterialMapper in quantity/list handlers"
```

---

### task: full-validation

**Files:** None created or modified — this task runs the project's full validation suite against everything the previous three tasks changed.

- [ ] **Step 1: Full backend build**

Run: `dotnet build`
Expected: Build succeeds, 0 errors, 0 new warnings.

- [ ] **Step 2: Full backend test suite**

Run: `dotnet test`
Expected: All tests pass, including the new `PackingMaterialMapperTests` (5 tests) and every pre-existing test under `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/` (`PackingMaterialCrudHandlerTests`, `GetPackingMaterialsListHandlerTests`, `PackingMaterialsControllerNotFoundTests`, `PackingMaterialLogPersistenceTests`, `PackingMaterialsListQueryCountTests`, `PackingMaterialRepositoryGetMaterialNamesByIdsAsyncTests`, `PackingMaterialRepositoryConsumptionHistoryTests`, `PackingMaterialRepositoryRecentLogsTests`). No test count should be lower than before this feature's changes; exactly 5 more than baseline (the new mapper tests).

- [ ] **Step 3: Format check**

Run: `dotnet format --verify-no-changes`
Expected: No formatting violations. If violations are reported: run `dotnet format` (not `--verify-no-changes`) once to auto-fix, review the diff to confirm it touches only files this feature changed, then re-run `dotnet format --verify-no-changes` to confirm clean. Commit any resulting fix separately in Step 5 below (per repository convention: prefer a new commit over amending a previous one).

- [ ] **Step 4: Confirm no `new PackingMaterialDto` object initializers remain outside the mapper**

Run: `grep -rn "new PackingMaterialDto" backend/src/`
Expected: Exactly one match, inside `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Mapping/PackingMaterialMapper.cs`. Zero matches in any of the four handler files. If any handler still contains `new PackingMaterialDto`, that handler's refactor step was missed or reverted — go back and fix it before proceeding.

- [ ] **Step 5: Final commit (only if Step 3 produced a format fix; otherwise skip — nothing to commit)**

```bash
git add -A
git commit -m "chore(packing-materials): apply dotnet format"
```
