### task: relocate-update-packing-material-quantity

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityResponse.cs`
- Delete: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/UpdatePackingMaterialQuantityRequest.cs`
- Delete: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/UpdatePackingMaterialQuantityResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialCrudHandlerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialLogPersistenceTests.cs`

The controller already has `using Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdatePackingMaterialQuantity;` (it was already required for the handler type), so `PackingMaterialsController.cs` needs no new using for this task — no controller change here. `PackingMaterialsControllerNotFoundTests.cs` likewise already imports `UseCases.UpdatePackingMaterialQuantity` and keeps its `Contracts` using regardless (needed for `UpdateQuantityRequest`, per the previous task) — no change needed there either.

This is the last of the two use cases referenced by `PackingMaterialCrudHandlerTests.cs`'s `Contracts` using (the other being `UpdatePackingMaterial`, moved in the previous task) — after this task, that using becomes fully unused and is removed in Step 6 below.

- [ ] **Step 1: Create the new Request file**

Create `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdatePackingMaterialQuantity;

public class UpdatePackingMaterialQuantityRequest : IRequest<UpdatePackingMaterialQuantityResponse>
{
    public int Id { get; set; }
    public decimal NewQuantity { get; set; }
    public DateOnly Date { get; set; }
}
```

- [ ] **Step 2: Create the new Response file**

Create `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdatePackingMaterialQuantity;

public class UpdatePackingMaterialQuantityResponse : BaseResponse
{
    public PackingMaterialDto Material { get; set; } = null!;
    public string? Error { get; set; }
}
```

- [ ] **Step 3: Delete the two old Contracts files**

```bash
git rm backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/UpdatePackingMaterialQuantityRequest.cs
git rm backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/UpdatePackingMaterialQuantityResponse.cs
```

- [ ] **Step 4: Remove the now-unused `Contracts` using from the handler**

In `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs`, delete:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
```

(The handler body never names `PackingMaterialDto` explicitly — `PackingMaterialMapper.ToDto(...)` result is assigned to `var materialDto`. Remaining usings — `Mapping`, `Shared` (for `ErrorCodes`), `Domain.Features.PackingMaterials`, `Domain.Features.PackingMaterials.Enums`, `Domain.Features.Users`, `MediatR` — are unchanged.)

- [ ] **Step 5: Remove the now-unused `Contracts` using from `PackingMaterialLogPersistenceTests.cs`**

In `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialLogPersistenceTests.cs`, delete:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
```

(Verified: this file only uses `UpdatePackingMaterialQuantityRequest`, already covered by its existing `using ...UseCases.UpdatePackingMaterialQuantity;`, and never names `PackingMaterialDto` or any other `Contracts` type explicitly — `response.Material.CurrentQuantity` is accessed via inferred `var response`.)

- [ ] **Step 6: Remove the now-unused `Contracts` using from `PackingMaterialCrudHandlerTests.cs`**

In `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialCrudHandlerTests.cs`, delete:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
```

(Verified: this file references `UpdatePackingMaterialRequest`/`Response` — covered by its existing `using ...UseCases.UpdatePackingMaterial;`, added in the previous task — and `UpdatePackingMaterialQuantityRequest`/`Response`, covered by its existing `using ...UseCases.UpdatePackingMaterialQuantity;`. It never names `PackingMaterialDto` or any other `Contracts` type explicitly; `ErrorCodes` comes from its `using Anela.Heblo.Application.Shared;`. Do not run this step before `relocate-update-packing-material` has completed — until that task lands, this file's `UpdatePackingMaterialRequest` reference still resolves only through `Contracts`.)

- [ ] **Step 7: Build the solution**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Run the affected tests**

Run: `dotnet test --filter "FullyQualifiedName~PackingMaterialCrudHandlerTests|FullyQualifiedName~PackingMaterialLogPersistenceTests"`
Expected: All tests PASS.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityRequest.cs \
        backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityResponse.cs \
        backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialCrudHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialLogPersistenceTests.cs
git commit -m "refactor(packing-materials): co-locate UpdatePackingMaterialQuantity request/response with its handler"
```

---

