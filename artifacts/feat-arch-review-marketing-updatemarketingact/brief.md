## Module
Marketing

## Finding
`UpdateMarketingActionRequest.FolderLinks` is typed as `List<CreateMarketingActionRequest.CreateFolderLinkRequest>?`, directly referencing a nested class from a sibling contract:

`backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/UpdateMarketingActionRequest.cs:30`
```csharp
public List<CreateMarketingActionRequest.CreateFolderLinkRequest>? FolderLinks { get; set; }
```

The two request types are independent use-case contracts (Create and Update), but the Update DTO has an implicit compile-time dependency on the Create DTO's nested class.

## Why it matters
Any change to `CreateFolderLinkRequest` (e.g., adding a creation-only field) silently propagates to the Update contract. The coupling is invisible without reading both files — a future developer could break the Update contract while editing the Create one. This is an Interface Segregation violation: the Update contract carries a type it doesn't own.

## Suggested fix
Extract `CreateFolderLinkRequest` to a standalone class in the Contracts folder (e.g., `MarketingFolderLinkRequest.cs`) and reference it from both `CreateMarketingActionRequest` and `UpdateMarketingActionRequest`.

```csharp
// New file: Contracts/MarketingFolderLinkRequest.cs
public class MarketingFolderLinkRequest
{
    [Required][MaxLength(100)] public string FolderKey { get; set; } = null!;
    [Required] public MarketingFolderType FolderType { get; set; }
}
```

---
_Filed by daily arch-review routine on 2026-05-17._