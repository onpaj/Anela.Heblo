# Design: Journal `Title` field missing `[Required]` annotation in request DTOs

## Component Design
No new or restructured components. The two affected components are existing request DTOs in the Journal module's `Contracts/` folder:

- **`CreateJournalEntryRequest`** (`backend/src/Anela.Heblo.Application/Features/Journal/Contracts/CreateJournalEntryRequest.cs`) — MediatR request contract for creating a journal entry. Responsibility: describe the shape and validation constraints of the create payload, consumed by `CreateJournalEntryHandler` and by OpenAPI/TypeScript client generation. `Title` gains `[Required]` (in addition to its existing `[MaxLength(200)]`), matching `Content`'s existing `[Required] + [MaxLength(10000)]` pattern in the same class. No other property, constructor, or the paired `CreateJournalEntryResponse` class changes.
- **`UpdateJournalEntryRequest`** (`backend/src/Anela.Heblo.Application/Features/Journal/Contracts/UpdateJournalEntryRequest.cs`) — MediatR request contract for updating a journal entry. Same change to `Title`, same rationale, same scope boundary (`Id`, `Content`, `EntryDate`, `AssociatedProducts`, `TagIds`, and `UpdateJournalEntryResponse` all untouched).

No other component (handlers, domain entity, EF Core configuration, controller, frontend form) is part of this design — they already treat `Title` as required and require no change, per `spec.r1.md` and `arch-review.r1.md`.

## Data Schemas

### Request DTO shape (after fix)

`CreateJournalEntryRequest`:
```csharp
public class CreateJournalEntryRequest : IRequest<CreateJournalEntryResponse>
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = null!;

    [Required]
    [MaxLength(10000)]
    public string Content { get; set; } = null!;

    [Required]
    public DateTime EntryDate { get; set; }

    public List<string>? AssociatedProducts { get; set; }
    public List<int>? TagIds { get; set; }
}
```

`UpdateJournalEntryRequest`:
```csharp
public class UpdateJournalEntryRequest : IRequest<UpdateJournalEntryResponse>
{
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = null!;

    [Required]
    [MaxLength(10000)]
    public string Content { get; set; } = null!;

    [Required]
    public DateTime EntryDate { get; set; }

    public List<string>? AssociatedProducts { get; set; }
    public List<int>? TagIds { get; set; }
}
```

### OpenAPI schema effect
Both `CreateJournalEntryRequest` and `UpdateJournalEntryRequest` schemas in the generated OpenAPI spec gain `title` in their `required` array (alongside the already-required `content` and `entryDate`), where previously `title` was listed only as a property with a `maxLength` constraint and no required marker.

### Generated TypeScript client effect (build-time, not hand-edited)
In `frontend/src/api/generated/api-client.ts`, on next regeneration:
- `CreateJournalEntryRequest.title` / `ICreateJournalEntryRequest.title`: `title?: string` → `title!: string` (class) / `title: string` (interface) — matching the existing `content`/`content!: string` pattern in the same file.
- `UpdateJournalEntryRequest.title` / `IUpdateJournalEntryRequest.title`: same change.

No other generated type, method signature, or endpoint wrapper changes.

### Database schema
No change. `JournalEntryConfiguration.Title` already maps to a `NOT NULL`, max-length-200 column; the domain and persistence layers were already correct before this fix.
