### task: relocate-create-packing-material

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/CreatePackingMaterial/CreatePackingMaterialRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/CreatePackingMaterial/CreatePackingMaterialResponse.cs`
- Delete: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/CreatePackingMaterialRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/CreatePackingMaterial/CreatePackingMaterialHandler.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs`

No test file references `CreatePackingMaterialRequest`/`CreatePackingMaterialResponse` directly (verified by grepping `backend/test` for both names — zero matches), so no test file needs changes for this task.

- [ ] **Step 1: Create the new Request file**

Create `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/CreatePackingMaterial/CreatePackingMaterialRequest.cs`:

```csharp
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.CreatePackingMaterial;

public class CreatePackingMaterialRequest : IRequest<CreatePackingMaterialResponse>
{
    public string Name { get; set; } = null!;
    public decimal ConsumptionRate { get; set; }
    public ConsumptionType ConsumptionType { get; set; }
    public decimal CurrentQuantity { get; set; }
}
```

- [ ] **Step 2: Create the new Response file**

Create `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/CreatePackingMaterial/CreatePackingMaterialResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.CreatePackingMaterial;

public class CreatePackingMaterialResponse : BaseResponse
{
    public int Id { get; set; }
    public PackingMaterialDto Material { get; set; } = null!;
}
```

- [ ] **Step 3: Delete the old combined Contracts file**

```bash
git rm backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/CreatePackingMaterialRequest.cs
```

- [ ] **Step 4: Remove the now-unused `Contracts` using from the handler**

In `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/CreatePackingMaterial/CreatePackingMaterialHandler.cs`, delete:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
```

(The handler body never names `PackingMaterialDto` explicitly — `PackingMaterialMapper.ToDto(...)` result is assigned to `var materialDto`. Remaining usings — `Mapping`, `Domain.Features.PackingMaterials`, `Domain.Features.PackingMaterials.Enums`, `MediatR` — are unchanged.)

- [ ] **Step 5: Add the new using to the controller (keep the existing `Contracts` using)**

In `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs`, add (after `using ...UseCases.CreateAllocation;`, before `using ...UseCases.DeleteAllocation;`, to keep alphabetical grouping — `dotnet format` will normalize exact position):

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.CreatePackingMaterial;
```

Do not remove the controller's top-level `using Anela.Heblo.Application.Features.PackingMaterials.Contracts;` — it is still needed for `ConsumptionGroupBy`, `CreateAllocationRequestBody`, `UpdateAllocationRequestBody`, and `UpdateQuantityRequest`.

- [ ] **Step 6: Build the solution**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Run the full PackingMaterials test suite (no test targets this use case directly, so run the whole feature's tests as a regression check)**

Run: `dotnet test --filter "FullyQualifiedName~Features.PackingMaterials"`
Expected: All tests PASS.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/CreatePackingMaterial/CreatePackingMaterialRequest.cs \
        backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/CreatePackingMaterial/CreatePackingMaterialResponse.cs \
        backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/CreatePackingMaterial/CreatePackingMaterialHandler.cs \
        backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs
git commit -m "refactor(packing-materials): co-locate CreatePackingMaterial request/response with its handler"
```

---

