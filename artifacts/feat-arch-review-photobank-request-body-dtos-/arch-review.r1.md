I now have everything I need. The refactor is well-understood and grounded in the actual code.

```markdown
# Architecture Review: Relocate Photobank request-body DTOs to the Application project

## Skip Design: true

This is a backend-only structural refactor — relocating four C# class declarations between projects. No UI components, screens, or visual decisions are involved. The generated TypeScript client output is required to remain byte-for-byte identical, so even the frontend surface is untouched.

## Architectural Fit Assessment

The proposal does not introduce architecture — it **removes a deviation from existing architecture**. I verified the following against the codebase:

- `PhotobankController.cs:425–446` declares the four `…Body` classes as siblings of the controller inside the `Anela.Heblo.API.Controllers` namespace. They are referenced unqualified by the action signatures at lines 99, 129, 166, 190.
- The Photobank `Contracts/` folder already exists and already holds the module's other transport types: `IndexRootDto.cs`, `PhotoDto.cs`, `TagDto.cs` (which contains `TagDto` + `TagWithCountDto`), and `TagRuleDto.cs`. All use namespace `Anela.Heblo.Application.Features.Photobank.Contracts` with **block-scoped** namespace syntax and are declared as `class` with mutable `{ get; set; }` (or `init`) properties.
- The controller already imports many `Anela.Heblo.Application.Features.Photobank.*` namespaces (UseCases, Services), confirming the API→Application reference exists. The relocated types are reachable with one added `using`.

The four body classes are the lone exception to the documented rule (*"API project never defines or owns DTOs — it only uses them."*). The change makes the module 100% consistent with its own established convention. Integration points: the controller's four action signatures and the NSwag OpenAPI client generator.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.API (Controllers)                Anela.Heblo.Application (Features/Photobank/Contracts)
┌──────────────────────────────┐             ┌────────────────────────────────────────┐
│ PhotobankController           │             │ AddPhotoTagBody.cs        (new)         │
│  + using ...Photobank.Contracts│──uses──────▶│ CreateTagBody.cs          (new)         │
│  [FromBody] CreateTagBody      │             │ BulkAddPhotoTagBody.cs    (new)         │
│  [FromBody] AddPhotoTagBody    │             │ BulkAddPhotoTagByIdsBody.cs (new)       │
│  [FromBody] BulkAddPhotoTagBody│             │ (existing) PhotoDto, TagDto, TagRuleDto,│
│  [FromBody] BulkAddPhotoTagByIds│            │            IndexRootDto                 │
│  (class declarations DELETED)  │             └────────────────────────────────────────┘
└──────────────────────────────┘
            │
            │ maps onto (unchanged)
            ▼
   MediatR requests: CreateTagRequest, AddPhotoTagRequest,
   BulkAddPhotoTagRequest, BulkAddPhotoTagByIdsRequest
```

Dependency direction is preserved: API → Application. No new project references; no inversion.

### Key Design Decisions

#### Decision 1: One type per file vs. grouping in a single file
**Options considered:** (a) one file per class (`AddPhotoTagBody.cs`, etc.); (b) a single `PhotobankRequestBodies.cs` holding all four; (c) follow the local `TagDto.cs` precedent of grouping closely related types.
**Chosen approach:** One file per class, four files total — matching FR-1's acceptance criteria.
**Rationale:** The spec mandates four named files. While `TagDto.cs` groups `TagDto`+`TagWithCountDto`, those are a value/projection pair of one concept; the four bodies are independent request contracts for distinct endpoints. One-file-per-type is the dominant convention in the folder (`PhotoDto`, `IndexRootDto`, `TagRuleDto` are each standalone) and the global C# style rule ("keep files aligned with the primary type they define"). Follow it.

#### Decision 2: Namespace syntax — block-scoped vs. file-scoped
**Options considered:** file-scoped (`namespace X;`) or block-scoped (`namespace X { }`).
**Chosen approach:** Block-scoped, matching every existing file in the `Contracts/` folder.
**Rationale:** Consistency within the module trumps personal preference. All four sibling files (`TagDto.cs` etc.) use block-scoped. Match them exactly so the diff reads as a clean relocation. This is a surgical change — do not modernize syntax.

#### Decision 3: Keep `class` (not `record`), keep mutable setters
**Chosen approach:** Verbatim move — `public class`, `{ get; set; }`, identical default initializers (`= null!`, `= string.Empty`, `= []`).
**Rationale:** Hard project rule (DTOs are classes for OpenAPI generator compatibility) and FR-3's byte-identical-client requirement. The model binder needs settable properties. Do not "improve" to records or `init`-only setters — that would risk the NSwag output and violate the rule.

## Implementation Guidance

### Directory / Module Structure

Create four files under `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/`:

```
Contracts/
  AddPhotoTagBody.cs           (new)
  CreateTagBody.cs             (new)
  BulkAddPhotoTagBody.cs       (new)
  BulkAddPhotoTagByIdsBody.cs  (new)
```

Delete lines 425–446 from `backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs` (the four `public class …Body` blocks), leaving the namespace's closing brace intact.

### Interfaces and Contracts

Each new file follows the established `Contracts/` template (block-scoped namespace, `class`, public auto-properties). Example:

```csharp
namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class BulkAddPhotoTagBody
    {
        public List<string>? Tags { get; set; }
        public string? Search { get; set; }
        public string TagName { get; set; } = null!;
    }
}
```

`List<>` resolves via the `Application` project's `ImplicitUsings` (verify it is enabled in `Anela.Heblo.Application.csproj`; if not, add `using System.Collections.Generic;`). The existing `Contracts` files don't include collection usings, which indicates `ImplicitUsings` is on for the project — but confirm before relying on it.

Controller change: add **one** import alongside the existing Photobank usings (alphabetical placement, after the `…Photobank.Contracts` would sort before `…Photobank.Services` at line 5):

```csharp
using Anela.Heblo.Application.Features.Photobank.Contracts;
```

The `[FromBody]` parameters at lines 99, 129, 166, 190 then resolve to the relocated types with **no signature change**.

### Data Flow

Unchanged. HTTP request body → ASP.NET model binder → `…Body` DTO (now in Application) → controller maps onto the corresponding MediatR `…Request` → handler. Only the *declaration location* of the DTO type moves; the runtime path is identical.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| NSwag/OpenAPI TS client output changes after relocation | LOW | NSwag derives TS interface names from the C# **type name**, not its CLR namespace. Names are unchanged. Validate per FR-3: regenerate and `git diff frontend/src/api/generated/api-client.ts` must be empty. |
| Type-name collision in `Contracts` namespace after import | LOW | Confirmed via grep: no other `CreateTagBody`/`AddPhotoTagBody`/`BulkAddPhotoTagBody`/`BulkAddPhotoTagByIdsBody` exists. The added `using` introduces no ambiguity. |
| `List<>` fails to compile if `ImplicitUsings` is off in Application project | LOW | Existing `Contracts` files use no collection usings, implying it's on. If `dotnet build` fails, add `using System.Collections.Generic;` to the two affected files. |
| Stale `using System.Collections.Generic;` left in controller becomes unused | LOW | After removing the body classes, the controller may no longer need its line-1 `using System.Collections.Generic;`. Let `dotnet format`/analyzer flag it; remove only if genuinely unused, otherwise leave untouched (surgical scope). |

## Specification Amendments

None required. The spec is complete, correct, and matches the verified code state (line numbers, property shapes, namespace, and the API→Application reference all confirmed). One clarification to fold into implementation, not a spec change: prefer relying on the project's `ImplicitUsings` for `List<>` (consistent with sibling files) rather than adding an explicit `using`, unless the build proves otherwise.

## Prerequisites

None. No migrations, config, or infrastructure changes. The API project already references the Application project. Validation gate after implementation:

1. `dotnet build` (backend compiles, client regenerates on build).
2. `dotnet format` (formatting/analyzer pass).
3. `git diff frontend/src/api/generated/api-client.ts` → **must be empty** (FR-3 proof).
4. Run any existing Photobank controller/integration tests touched by the four endpoints.
```