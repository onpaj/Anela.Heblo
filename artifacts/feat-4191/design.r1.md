# Design: Align CreateMarketingActionHandler with UpdateMarketingActionHandler's bulk-assignment APIs

## Component Design

### `CreateMarketingActionHandler` (modified)
- **Responsibility:** unchanged — handle `CreateMarketingActionRequest`, construct a new `MarketingAction`, set its initial product associations and folder links, optionally sync to Outlook, persist, and return `CreateMarketingActionResponse`.
- **Change:** the block that currently populates `action.ProductAssociations` and `action.FolderLinks` via per-item loops is replaced with two calls to the aggregate's existing bulk-replace methods:

  ```csharp
  action.ReplaceProductAssociations(request.AssociatedProducts, now);
  action.ReplaceFolderLinks(
      request.FolderLinks?.Select(l => (l.FolderKey, l.FolderType)),
      now);
  ```

  These two lines occupy the same position in `Handle` that the current `if (request.AssociatedProducts?.Any() == true) foreach (...)` / `if (request.FolderLinks?.Any() == true) foreach (...)` block occupies today (immediately after `action` is constructed, before the Outlook sync block). No other line in `Handle` changes.
- **New using requirement:** `System.Linq` for `.Select` on `request.FolderLinks` — `UpdateMarketingActionHandler.cs` already has this `using`; `CreateMarketingActionHandler.cs`'s current `using` block does not include `System.Linq` explicitly (it relies on `.Any()`/`.Distinct()` already resolving, so it must already be present via an existing `using` or implicit global usings — confirm at implementation time whether an explicit `using System.Linq;` needs adding).

### `MarketingAction` (domain aggregate — unchanged)
- `ReplaceProductAssociations(IEnumerable<string>? productCodes, DateTime utcNow)` and `ReplaceFolderLinks(IEnumerable<(string folderKey, MarketingFolderType folderType)>? links, DateTime utcNow)` are consumed as-is; no signature, behavior, or doc-comment changes. These already contain all normalization (trim, invariant-uppercase for product codes; trim for folder keys), dedup (case-normalized for products; composite `(folderKey, folderType)` for folder links), and validation (`ArgumentException` on a null/empty/whitespace entry) logic — the handler does not duplicate any of it.

### `UpdateMarketingActionHandler` — out of scope, unchanged
Already calls `ReplaceProductAssociations` / `ReplaceFolderLinks`; not touched by this change. Included here only as the existing reference implementation the Create handler is being aligned to.

## Data Schemas
No schema changes — this is a behavior-preserving-except-for-two-documented-fixes refactor of write logic, not a data model change.

- **Request shape (unchanged):** `CreateMarketingActionRequest` — `AssociatedProducts: IEnumerable<string>?`, `FolderLinks: IEnumerable<MarketingFolderLinkRequest>?` where `MarketingFolderLinkRequest` has `FolderKey: string`, `FolderType: MarketingFolderType`.
- **Response shape (unchanged):** `CreateMarketingActionResponse` — `Id`, `CreatedAt`, plus the existing `ErrorCode`/error-dictionary shape inherited from the base response type.
- **Persisted shape (unchanged columns, changed population rule only):**
  - `MarketingActionProduct.ProductCodePrefix` — now always populated via `ReplaceProductAssociations`'s trim+invariant-uppercase+dedup, for both Create and Update (previously Create's dedup was case-sensitive at the pre-check but still normalized per-row by `AssociateWithProduct`; net persisted rows for typical inputs are unchanged, except the documented case-variant-duplicate fix in spec FR-1).
  - `MarketingActionFolderLink.FolderKey` / `FolderType` — now always populated via `ReplaceFolderLinks`'s trim+composite-key dedup, for both Create and Update (previously Create deduped by `FolderKey` alone; the documented same-key-different-type behavior change in spec FR-2 applies).

No new tables, columns, indexes, migrations, or API endpoints are introduced.
