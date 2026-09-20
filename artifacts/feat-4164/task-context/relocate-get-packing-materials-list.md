### task: relocate-get-packing-materials-list

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListResponse.cs`
- Delete: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/GetPackingMaterialsListRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListHandler.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetPackingMaterialsListHandlerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsListQueryCountTests.cs`

- [ ] **Step 1: Create the new Request file**

Create `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetPackingMaterialsList;

public class GetPackingMaterialsListRequest : IRequest<GetPackingMaterialsListResponse>
{
}
```

- [ ] **Step 2: Create the new Response file**

Create `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetPackingMaterialsList;

public class GetPackingMaterialsListResponse : BaseResponse
{
    public List<PackingMaterialDto> Materials { get; set; } = new();
}
```

- [ ] **Step 3: Delete the old combined Contracts file**

```bash
git rm backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/GetPackingMaterialsListRequest.cs
```

- [ ] **Step 4: Remove the now-unused `Contracts` using from the handler**

In `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListHandler.cs`, delete this line (the handler no longer needs it — `GetPackingMaterialsListRequest`/`Response` are now in its own namespace, and the handler body never names `PackingMaterialDto` or any other `Contracts` type explicitly, it only uses `PackingMaterialMapper.ToDto(...)` via `var`):

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
```

The file's remaining usings (`Mapping`, `Domain.Features.PackingMaterials`, `Domain.Features.PackingMaterials.Enums`, `MediatR`, `Microsoft.Extensions.Logging`) are unchanged.

- [ ] **Step 5: Add the new using to the controller (keep the existing `Contracts` using)**

In `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs`, the first line is `using Anela.Heblo.Application.Features.PackingMaterials.Contracts;` — **do not remove it** (still needed for `ConsumptionGroupBy`, `CreateAllocationRequestBody`, `UpdateAllocationRequestBody`, `UpdateQuantityRequest`). Add this new line alongside the other `UseCases.*` usings (between `using ...UseCases.GetPackingMaterialLogs;` and `using ...UseCases.ProcessDailyConsumption;`, to keep the existing alphabetical grouping — exact position doesn't matter functionally, `dotnet format` will normalize it):

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetPackingMaterialsList;
```

- [ ] **Step 6: Remove the now-unused `Contracts` using from `GetPackingMaterialsListHandlerTests.cs`**

In `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetPackingMaterialsListHandlerTests.cs`, delete:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
```

(Verified: this file only references `GetPackingMaterialsListRequest`, already covered by its existing `using ...UseCases.GetPackingMaterialsList;`, and never names `PackingMaterialDto` or any other `Contracts` type explicitly — `response.Materials.Single()` is accessed via inferred `var`.)

- [ ] **Step 7: Remove the now-unused `Contracts` using from `PackingMaterialsListQueryCountTests.cs`**

In `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsListQueryCountTests.cs`, delete:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
```

(Same verification as Step 6 — this file only uses `GetPackingMaterialsListRequest`, already covered by its existing `using ...UseCases.GetPackingMaterialsList;`.)

- [ ] **Step 8: Build the solution**

Run: `dotnet build`
Expected: Build succeeded, 0 errors. (If a missed reference surfaces, it will be a compile error naming the file — fix the `using` there before continuing; do not guess ahead of the compiler.)

- [ ] **Step 9: Run the affected tests**

Run: `dotnet test --filter "FullyQualifiedName~PackingMaterialsListQueryCountTests|FullyQualifiedName~GetPackingMaterialsListHandlerTests"`
Expected: All tests PASS (behavior unchanged — this is a namespace move, not a logic change).

- [ ] **Step 10: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListRequest.cs \
        backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListResponse.cs \
        backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListHandler.cs \
        backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetPackingMaterialsListHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsListQueryCountTests.cs
git commit -m "refactor(packing-materials): co-locate GetPackingMaterialsList request/response with its handler"
```

---

