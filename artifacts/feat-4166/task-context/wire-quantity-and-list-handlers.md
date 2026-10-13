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

