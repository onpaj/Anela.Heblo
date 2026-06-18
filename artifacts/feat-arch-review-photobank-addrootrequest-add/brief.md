## Module
Photobank

## Finding
Three MediatR request types are used directly as `[FromBody]` HTTP body types in `PhotobankController`, bypassing the module's own established `contracts/` pattern:

| Controller action | `[FromBody]` type used | Where it lives |
|---|---|---|
| `AddRoot` (line 233) | `AddRootRequest` | `UseCases/AddRoot/AddRootRequest.cs` |
| `AddRule` (line 281) | `AddRuleRequest` | `UseCases/AddRule/AddRuleRequest.cs` |
| `RetagPhotos` (line 203) | `RetagPhotosRequest` | `UseCases/RetagPhotos/RetagPhotosRequest.cs` |

The same module already applies the correct pattern for four other endpoints, where a thin `contracts/*Body.cs` type is mapped to the MediatR request in the controller:
- `AddPhotoTagBody` → `AddPhotoTagRequest`
- `BulkAddPhotoTagBody` → `BulkAddPhotoTagRequest`
- `CreateTagBody` → `CreateTagRequest`
- `BulkAddPhotoTagByIdsBody` → `BulkAddPhotoTagByIdsRequest`

## Why it matters
`development_guidelines.md` states: *"DTO objects for API (Request, Response) live in `contracts/` of the specific module."* When the MediatR request type is also the HTTP body type, the API contract is coupled to the application contract:
- Adding an internal server-side field to `AddRootRequest` (e.g. `CreatedByUserId` populated in the handler) immediately exposes it in the OpenAPI schema and the auto-generated TypeScript client — a potential spoofing hole if someone forgets to strip the body value.
- Changing what the handler needs changes the visible API contract, and vice versa.
- The three requests all live in `UseCases/` subdirectories, the wrong location per guidelines for API-facing DTOs.

## Suggested fix
For each affected endpoint, add a slim body DTO to `contracts/`:

```csharp
// contracts/AddRootBody.cs
public class AddRootBody
{
    public string SharePointPath { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string DriveId { get; set; } = null!;
}

// contracts/AddRuleBody.cs
public class AddRuleBody
{
    public string PathPattern { get; set; } = null!;
    public string TagName { get; set; } = null!;
    public int SortOrder { get; set; }
}

// contracts/RetagPhotosBody.cs
public class RetagPhotosBody
{
    public int[] PhotoIds { get; set; } = Array.Empty<int>();
    public bool ClearExistingAiTags { get; set; }
}
```

Then update the controller actions to map `Body` → `Request`, matching the existing pattern in the same file.

---
_Filed by daily arch-review routine on 2026-06-14._