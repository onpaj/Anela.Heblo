# Design: CatalogDocuments — Shared PIF short-code prefix derivation

## Component Design

### `PifFolderPrefixBuilder` (new)
- **Location:** `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Infrastructure/PifFolderPrefixBuilder.cs`
- **Responsibility:** Single source of truth for deriving the PIF SharePoint/OneDrive folder-matching prefix from a product code. Pure, stateless, static — no dependencies, no I/O, no DI registration.
- **Public contract:**
  - `public static string Build(string productCode)` — returns `{shortCode}__` where `shortCode` is the first 6 characters of `productCode`, or the whole string when it is shorter than 6 characters.
- **Consumers:** `UploadPifDocumentHandler`, `ListPifDocumentsHandler` — both call `PifFolderPrefixBuilder.Build(request.ProductCode)` in place of their current inline computation.

### `UploadPifDocumentHandler` (modified)
- **Responsibility:** Unchanged — resolves the target PIF folder, uploads the document, returns `UploadDocumentResponse`.
- **Change:** Lines computing `shortCode`/`prefix` inline are replaced by one call to `PifFolderPrefixBuilder.Build(request.ProductCode)`. No change to constructor, dependencies, or the rest of `Handle`'s logic (folder lookup, not-found error payload, upload call, logging).

### `ListPifDocumentsHandler` (modified)
- **Responsibility:** Unchanged — resolves the target PIF folder, lists its files, returns `ListCatalogDocumentsResponse`.
- **Change:** Same substitution as above. No change to constructor, dependencies, or the rest of `Handle`'s logic (folder lookup, not-found response shape, file listing, logging).

### Out of scope for this design
- `MaterialFilenameBuilder` and the Materials handlers are untouched — the two rules remain intentionally separate (see arch-review Decision 1).
- `ICatalogDocumentsStorage`, `FindFolderAsync`, `CatalogDocumentsOptions`, and all Graph/SharePoint integration are untouched.

## Data Schemas

No schema changes. No new or modified DTOs, request/response contracts, or persisted entities.

For traceability, the (unchanged) shapes that consume the derived `prefix` value:

- `UploadPifDocumentRequest` — unchanged; still carries `ProductCode` (string) used as `PifFolderPrefixBuilder.Build`'s input.
- `UploadDocumentResponse` — unchanged; on `FolderStatus.NotFound` still carries `["prefix"] = prefix` in its error detail dictionary — value is byte-for-byte identical to today's output for the same `ProductCode`.
- `ListPifDocumentsRequest` — unchanged; still carries `ProductCode` (string).
- `ListCatalogDocumentsResponse` — unchanged; `ExpectedPrefix` field still populated from the same derivation — value is byte-for-byte identical to today's output for the same `ProductCode`.

`PifFolderPrefixBuilder.Build` signature:

```csharp
public static string Build(string productCode)
```

| Input (`productCode`) | Output |
|---|---|
| `"ABC12345"` (length 8) | `"ABC123__"` |
| `"ABC123"` (length 6) | `"ABC123__"` |
| `"AB1"` (length 3) | `"AB1__"` |
| `""` (length 0) | `"__"` |
