### task: relocate-update-packing-material

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialResponse.cs`
- Delete: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/UpdatePackingMaterialRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialHandler.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsControllerNotFoundTests.cs`

`PackingMaterialCrudHandlerTests.cs` already imports `using Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdatePackingMaterial;` and uses `UpdatePackingMaterialRequest` through it — no import addition is needed there for this task. Its `Contracts` using is only removed later, in `relocate-update-packing-material-quantity` (Step 6 there), once both moves it depends on are done — do not touch it in this task.

- [ ] **Step 1: Create the new Request file**

Create `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialRequest.cs`:

```csharp
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdatePackingMaterial;

public class UpdatePackingMaterialRequest : IRequest<UpdatePackingMaterialResponse>
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public decimal ConsumptionRate { get; set; }
    public ConsumptionType ConsumptionType { get; set; }
}
```

- [ ] **Step 2: Create the new Response file**

Create `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdatePackingMaterial;

public class UpdatePackingMaterialResponse : BaseResponse
{
    public PackingMaterialDto Material { get; set; } = null!;
    public string? Error { get; set; }
}
```

- [ ] **Step 3: Delete the old combined Contracts file**

```bash
git rm backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/UpdatePackingMaterialRequest.cs
```

- [ ] **Step 4: Remove the now-unused `Contracts` using from the handler**

In `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialHandler.cs`, delete:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
```

(The handler body never names `PackingMaterialDto` explicitly — `PackingMaterialMapper.ToDto(...)` result is assigned to `var materialDto`. Remaining usings — `Mapping`, `Shared` (for `ErrorCodes`), `Domain.Features.PackingMaterials`, `Domain.Features.PackingMaterials.Enums`, `MediatR` — are unchanged.)

- [ ] **Step 5: Add the new using to the controller (keep the existing `Contracts` using)**

In `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs`, add (after `using ...UseCases.UpdateAllocation;`, before `using ...UseCases.UpdatePackingMaterialQuantity;`, to keep alphabetical grouping):

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdatePackingMaterial;
```

Do not remove the controller's top-level `Contracts` using.

- [ ] **Step 6: Add the missing using to `PackingMaterialsControllerNotFoundTests.cs`**

This file uses `UpdatePackingMaterialRequest`/`UpdatePackingMaterialResponse` (in `UpdatePackingMaterial_Returns404_WhenHandlerReturnsResourceNotFound`) but today resolves them only via its `Contracts` using — it has no `UseCases.UpdatePackingMaterial` import yet. Add this line to `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsControllerNotFoundTests.cs` (after `using ...UseCases.GetPackingMaterialLogs;`, before `using ...UseCases.UpdatePackingMaterialQuantity;`):

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdatePackingMaterial;
```

**Keep** this file's existing `using Anela.Heblo.Application.Features.PackingMaterials.Contracts;` — it is still required for `UpdateQuantityRequest`, constructed directly in `UpdatePackingMaterialQuantity_Returns404_WhenHandlerReturnsResourceNotFound` (`var body = new UpdateQuantityRequest { ... }`), which is a distinct, out-of-scope type.

- [ ] **Step 7: Build the solution**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Run the affected tests**

Run: `dotnet test --filter "FullyQualifiedName~PackingMaterialCrudHandlerTests|FullyQualifiedName~PackingMaterialsControllerNotFoundTests"`
Expected: All tests PASS.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialRequest.cs \
        backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialResponse.cs \
        backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialHandler.cs \
        backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsControllerNotFoundTests.cs
git commit -m "refactor(packing-materials): co-locate UpdatePackingMaterial request/response with its handler"
```

---

