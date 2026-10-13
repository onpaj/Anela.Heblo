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

