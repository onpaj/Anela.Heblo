## Module
Journal

## Finding
`UpdateJournalEntryHandler` directly mutates navigation-property collections on the `JournalEntry` aggregate instead of going through domain methods:

```csharp
// backend/src/Anela.Heblo.Application/Features/Journal/UseCases/UpdateJournalEntry/UpdateJournalEntryHandler.cs
// Lines 62–78
entry.ProductAssociations.Clear();   // ← raw collection mutation
if (request.AssociatedProducts?.Any() == true)
{
    foreach (var productIdentifier in request.AssociatedProducts.Distinct())
        entry.AssociateWithProduct(productIdentifier);   // ← then domain method
}

entry.TagAssignments.Clear();         // ← raw collection mutation
if (request.TagIds?.Any() == true)
{
    foreach (var tagId in request.TagIds.Distinct())
        entry.AssignTag(tagId);                          // ← then domain method
}
```

The entity already provides `AssociateWithProduct` and `AssignTag` for the *add* path (used consistently in `CreateJournalEntryHandler`), but the *replace* path skips domain encapsulation entirely by calling `.Clear()` directly on the collections.

## Why it matters
- **Inconsistency between create and update paths**: create routes all mutations through domain methods; update partially bypasses them.
- **Domain invariants not enforced for removal**: `.Clear()` does not go through any domain logic; future invariants on removal (e.g. audit trail, validation) would need to be added in two places — the handler and the domain method.
- **EF change-tracking fragility**: clearing owned collections directly can cause unexpected behaviour under EF tracking if the collection is lazy-loaded or if the cascade delete behaviour changes.
- **Violates tell-don't-ask**: the handler asks for the collection and mutates it rather than telling the entity what its new state should be.

## Suggested fix
Add `ReplaceProductAssociations(IEnumerable<string> productIdentifiers)` and `ReplaceTagAssignments(IEnumerable<int> tagIds)` domain methods to `JournalEntry` (mirroring the existing guard logic in `AssociateWithProduct`/`AssignTag`), then call those from the handler instead of the raw `.Clear()` + loop pattern.

---
_Filed by daily arch-review routine on 2026-05-12._