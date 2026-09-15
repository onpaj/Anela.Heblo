# PackingMaterials Contracts Co-location Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the four misplaced MediatR Request/Response pairs for `PackingMaterials` (`GetPackingMaterialsList`, `CreatePackingMaterial`, `UpdatePackingMaterial`, `UpdatePackingMaterialQuantity`) out of `Contracts/` and into their own `UseCases/{Name}/` folders, matching every sibling use case in the module, with zero behavior change.

**Architecture:** Pure structural refactor. For each of the four use cases: split (or move) the Request/Response classes into their own files inside `UseCases/{Name}/`, change their namespace from `...PackingMaterials.Contracts` to `...PackingMaterials.UseCases.{Name}`, delete the old `Contracts/` file(s), then fix every `using` directive that the move breaks — in the handler (which now needs no `Contracts` import), in `PackingMaterialsController.cs` (which needs the new `UseCases.{Name}` import added while *keeping* its existing `Contracts` import, which remains needed for `ConsumptionGroupBy`, `CreateAllocationRequestBody`, `UpdateAllocationRequestBody`, and `UpdateQuantityRequest` — genuinely-shared types that are not moving), and in the affected test files. No class is renamed, no member changes, no routes/JSON/DI changes.

**Tech Stack:** .NET 8, MediatR, xUnit, FluentAssertions/Moq (existing test stack) — no new dependencies.

**Important correction to the architecture review:** The arch-review's Decision 3 predicted the controller's `using Anela.Heblo.Application.Features.PackingMaterials.Contracts;` would become fully unused and should be deleted. This plan verified that prediction against the current controller source and it is **wrong**: the controller directly uses four other `Contracts` types that are explicitly out of scope for this refactor — `ConsumptionGroupBy` (in `GetDailyConsumptionBreakdown`'s `[FromQuery]` parameter), `CreateAllocationRequestBody`, `UpdateAllocationRequestBody`, and `UpdateQuantityRequest` (in `UpdatePackingMaterialQuantity`'s `[FromBody]` parameter). **The controller's `Contracts` using must be kept, not removed.** Only new `UseCases.{Name}` usings are added to it. Likewise, one test file (`PackingMaterialsControllerNotFoundTests.cs`) uses `UpdateQuantityRequest` directly and must keep its `Contracts` using for that reason, even though it also needs a new `UseCases.UpdatePackingMaterial` using added. Every other file's `Contracts` using was verified (by grep, per-file, in this plan) to become unused only when explicitly stated below — never removed by assumption.

---

## Reference: sibling pattern already followed elsewhere in this module

```csharp
// backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetAllocations/GetAllocationsRequest.cs
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetAllocations;

public class GetAllocationsRequest : IRequest<GetAllocationsResponse>
{
    public int PackingMaterialId { get; set; }
}
```

Every task below reproduces this same shape: one class per file, namespace `Anela.Heblo.Application.Features.PackingMaterials.UseCases.{Name}`.

---

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

### task: final-verification

**Files:** None created or modified — this task only runs whole-solution verification and, if any leftover reference or formatting issue surfaces, fixes it inline before committing.

- [ ] **Step 1: Confirm no file outside the known list still imports the old namespace for the 8 moved types**

Run, from the repo root:

```bash
grep -rn "PackingMaterials\.Contracts" backend/src backend/test \
  | grep -E "GetPackingMaterialsList(Request|Response)|CreatePackingMaterial(Request|Response)|UpdatePackingMaterial(Request|Response)|UpdatePackingMaterialQuantity(Request|Response)"
```

Expected: **no output**. If anything prints, it is a missed `using`/reference to the old namespace for one of the 8 moved types — fix that file's using directive to point at the correct `UseCases.{Name}` namespace before proceeding (do not skip this — a lingering import to a deleted namespace/type is a build break, and a lingering import to a type that still happens to compile because of a wildcard-like coincidence would defeat the purpose of this refactor).

- [ ] **Step 2: Confirm the `Contracts/` folder no longer contains the four moved files**

Run:

```bash
ls backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/
```

Expected: contains only genuinely shared types — `ConsumptionDetailDto.cs`, `ConsumptionGroupBy.cs`, `ConsumptionGroupDto.cs`, `CreateAllocationRequestBody.cs`, `IInvoiceConsumptionSource.cs`, `InvoiceConsumptionHeader.cs`, `MaterialConsumptionHistoryItemDto.cs`, `PackingMaterialAllocationDto.cs`, `PackingMaterialDto.cs`, `PackingMaterialLogDto.cs`, `PackingMaterialsTextHelper.cs`, `UpdateAllocationRequestBody.cs`, `UpdateQuantityRequest.cs` — and none of `GetPackingMaterialsListRequest.cs`, `CreatePackingMaterialRequest.cs`, `UpdatePackingMaterialRequest.cs`, `UpdatePackingMaterialQuantityRequest.cs`, `UpdatePackingMaterialQuantityResponse.cs`.

- [ ] **Step 3: Full solution build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors, 0 new warnings compared to the pre-refactor baseline.

- [ ] **Step 4: Full solution test run**

Run: `dotnet test`
Expected: All tests PASS, including every test in `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/` (this exercises `MediatR`'s assembly-scan handler registration end-to-end for the four moved request/response pairs — confirming the architecture review's claim that namespace changes don't affect scan-based DI registration).

- [ ] **Step 5: Format check**

Run: `dotnet format --verify-no-changes`
Expected: No formatting violations. If it reports any (e.g. using-directive ordering), run `dotnet format` (without `--verify-no-changes`) to apply the fixes, review the diff to confirm it only touches formatting (no logic changes), then continue.

- [ ] **Step 6: Confirm the generated OpenAPI/TypeScript client is unaffected**

Run:

```bash
git status --porcelain frontend/src
```

first to confirm a clean baseline, then:

```bash
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
git status --porcelain frontend/src
git diff frontend/src
```

Expected: no diff. The four endpoints' routes, HTTP verbs, and request/response JSON shapes are unchanged, and the generator is driven by controller action signatures and DTO shapes, not by internal C# namespaces — per NFR-2 and the design doc's Data Schemas section. If a diff appears, treat it as a signal to investigate (do not commit it blindly) — it would mean some other assumption in this plan was wrong.

- [ ] **Step 7: Commit any formatting fixes from Step 5 (only if `dotnet format` changed anything)**

```bash
git add -u
git commit -m "chore(packing-materials): apply dotnet format after use-case relocation" --allow-empty-message -m "No-op if dotnet format made no changes"
```

If Step 5 reported no changes, skip this step — there is nothing to commit.

---

## Self-Review (performed while writing this plan)

**Spec coverage:**
- FR-1 (`GetPackingMaterialsList`) → `relocate-get-packing-materials-list`.
- FR-2 (`CreatePackingMaterial`) → `relocate-create-packing-material`.
- FR-3 (`UpdatePackingMaterial`) → `relocate-update-packing-material`.
- FR-4 (`UpdatePackingMaterialQuantity`) → `relocate-update-packing-material-quantity`.
- FR-5 (update every reference) → covered incrementally in each task's controller/test steps, plus verified exhaustively in `final-verification` Step 1 (a fresh grep, not a re-assertion of the plan's own predictions).
- NFR-1 (no behavior change) → every relocation task runs the pre-existing tests for that use case unmodified in assertions, only `using` edits.
- NFR-2 (build/format compliance, MediatR assembly-scan safety) → `final-verification` Steps 3–6.
- Architecture review's Specification Amendments (split combined files; controller using additions) → both applied (Steps 1–2 of `relocate-get-packing-materials-list` and `relocate-create-packing-material` split the combined files; every task's Step 5-equivalent adds the controller using).
- Out-of-scope items (`UpdateQuantityRequest`, other shared `Contracts` types, other modules, behavior/API changes, renames) → untouched by every task above; `UpdateQuantityRequest` is explicitly called out wherever it interacts with a `Contracts` using-removal decision (Step 6 of `relocate-update-packing-material`, Step 6 of `final-verification`'s Step 2 file list).

**Placeholder scan:** No "TBD"/"add error handling"/"similar to Task N" phrasing — every step shows the literal file content or the literal command to run.

**Type consistency:** Class names (`GetPackingMaterialsListRequest`/`Response`, `CreatePackingMaterialRequest`/`Response`, `UpdatePackingMaterialRequest`/`Response`, `UpdatePackingMaterialQuantityRequest`/`Response`), their members, and their namespaces are identical across every task that mentions them and match the actual current source read from the repository during planning (not reconstructed from memory) — including the one place the architecture review's own prediction (controller's `Contracts` using becoming fully unused) was checked against the real controller source and found incorrect, which this plan corrects rather than propagates.
