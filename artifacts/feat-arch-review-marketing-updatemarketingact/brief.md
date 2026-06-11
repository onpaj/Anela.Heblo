## Module
Marketing

## Finding

`UpdateMarketingActionHandler` clears the entity's navigation collections directly (bypassing the domain entity's encapsulation) before re-adding items via the encapsulated domain methods.

File: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs`

```csharp
// Lines 95–110
action.ProductAssociations.Clear();          // reaches into EF collection directly
if (request.AssociatedProducts?.Any() == true)
    foreach (var product in request.AssociatedProducts.Distinct())
        action.AssociateWithProduct(product);   // adds via domain method

action.FolderLinks.Clear();                 // reaches into EF collection directly
if (request.FolderLinks?.Any() == true)
    foreach (var link in request.FolderLinks)
        action.LinkToFolder(link.FolderKey.Trim(), link.FolderType);  // adds via domain method
```

The `MarketingAction` entity provides `AssociateWithProduct` and `LinkToFolder` as encapsulated add methods with deduplication guards, but has no `ReplaceProducts` / `ClearProducts` / `ReplaceLinks` domain methods. The handler compensates by calling `.Clear()` on the raw `ICollection<>` navigation properties before re-adding — leaking persistence concerns into the Application layer.

## Why it matters

- **Broken encapsulation**: the entity controls *adding* associations but not *clearing* them. Any invariant the entity would want to enforce on removal (e.g. audit log, minimum-association guard) cannot be expressed.
- **EF coupling in Application layer**: `.Clear()` on an EF `virtual ICollection<>` works only because EF tracks the change via its change tracker. The Application-layer handler is now implicitly relying on EF behaviour — it would silently fail with a different persistence implementation.
- **SOLID — Single Responsibility**: mutation of collection state is a domain concern; the handler should not need to know how to replace the list.

## Suggested fix

Add replace methods to `MarketingAction` in the domain entity:

```csharp
public void ReplaceProductAssociations(IEnumerable<string> productCodes, DateTime utcNow)
{
    ProductAssociations.Clear();
    foreach (var code in productCodes.Select(c => c.Trim().ToUpperInvariant()).Distinct())
        ProductAssociations.Add(new MarketingActionProduct
        {
            MarketingActionId = Id,
            ProductCodePrefix = code,
            CreatedAt = utcNow,
        });
}

public void ReplaceFolderLinks(IEnumerable<(string key, MarketingFolderType type)> links)
{
    FolderLinks.Clear();
    foreach (var (key, type) in links)
        FolderLinks.Add(new MarketingActionFolderLink
        {
            MarketingActionId = Id,
            FolderKey = key.Trim(),
            FolderType = type,
            CreatedAt = utcNow,
        });
}
```

The handler then calls `action.ReplaceProductAssociations(...)` and `action.ReplaceFolderLinks(...)` — no direct collection mutation in Application layer.

---
_Filed by daily arch-review routine on 2026-06-07._