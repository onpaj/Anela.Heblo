## Module
Marketing

## Finding

Three domain methods on `MarketingAction` call `DateTime.UtcNow` directly, making them non-deterministic and hard to unit-test. This is inconsistent with `UpdateDetails()` (and the constructor) which both accept a `utcNow` parameter.

File: `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs`

| Method | Line | Problem |
|---|---|---|
| `AssociateWithProduct` | 108 | `CreatedAt = DateTime.UtcNow` |
| `LinkToFolder` | 124 | `CreatedAt = DateTime.UtcNow` |
| `SoftDelete` | 131–132 | `DeletedAt = DateTime.UtcNow`, `ModifiedAt = DateTime.UtcNow` |

By contrast, `UpdateDetails()` (line 159) receives `utcNow` as a parameter, which is the correct pattern. The constructor (line 86) also receives `utcNow`.

## Why it matters

- Unit tests for `AssociateWithProduct`, `LinkToFolder`, and `SoftDelete` cannot assert on the exact timestamp — they can only check that the value is "roughly now", which is fragile and timing-sensitive.
- The inconsistency means the same entity has two timestamp conventions. A developer adding a new domain method has no clear signal about which pattern to follow.
- The `SoftDelete` timestamps diverge from the handler's `now` value, creating a subtle one-millisecond discrepancy between `action.SoftDelete(...)` timestamps and the `utcNow` used by the handler for Outlook sync (`DeleteMarketingActionHandler`).

## Suggested fix

Add `DateTime utcNow` parameters to the three methods and update call sites in handlers:

```csharp
// AssociateWithProduct
public void AssociateWithProduct(string productCode, DateTime utcNow)
{
    ...
    ProductAssociations.Add(new MarketingActionProduct
    {
        MarketingActionId = Id,
        ProductCodePrefix = normalized,
        CreatedAt = utcNow,         // was DateTime.UtcNow
    });
}

// LinkToFolder
public void LinkToFolder(string folderKey, MarketingFolderType folderType, DateTime utcNow)
{
    ...
    FolderLinks.Add(new MarketingActionFolderLink
    {
        ...
        CreatedAt = utcNow,         // was DateTime.UtcNow
    });
}

// SoftDelete
public void SoftDelete(string userId, string username, DateTime utcNow)
{
    IsDeleted = true;
    DeletedAt = utcNow;             // was DateTime.UtcNow
    ModifiedAt = utcNow;            // was DateTime.UtcNow
    ...
}
```

Each handler already captures `var now = DateTime.UtcNow;` — pass that captured value through.

---
_Filed by daily arch-review routine on 2026-06-07._