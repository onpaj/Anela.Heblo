## Module
Photobank

## Finding
Four request-body DTO classes are defined at the bottom of `PhotobankController.cs` inside the `Anela.Heblo.API` project:

- `AddPhotoTagBody` (line 425)
- `CreateTagBody` (line 429)
- `BulkAddPhotoTagBody` (line 434)
- `BulkAddPhotoTagByIdsBody` (line 441)

File: `backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs`, lines 425–447.

```csharp
public class AddPhotoTagBody
{
    public string TagName { get; set; } = null!;
}
// ... three more body classes follow
```

The project rule is explicit: *"API project never defines or owns DTOs — it only uses them."* All request/response DTOs must live in `Application/Features/<Module>/Contracts/`.

## Why it matters
- Violates the documented DTO ownership rule from `docs/architecture/development_guidelines.md`.
- The generated TypeScript client reflects this mis-placement: NSwag picks these types up from the API project and emits them in `api-client.ts` under names like `CreateTagBody` — if they were moved to the Application project they would still be generated correctly, but ownership would be correct and they could be shared/reused by validators without a project-level dependency inversion.
- Establishes a precedent that makes it harder to enforce the boundary for future contributors.

## Suggested fix
Move all four classes to `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/`:

```
Contracts/
  AddPhotoTagBody.cs
  CreateTagBody.cs
  BulkAddPhotoTagBody.cs
  BulkAddPhotoTagByIdsBody.cs
```

Add a `using Anela.Heblo.Application.Features.Photobank.Contracts;` to the controller. No logic changes required.

---
_Filed by daily arch-review routine on 2026-05-21._