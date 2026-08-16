## Module
Documents, File Storage & Printing (CatalogDocuments)

## Finding
`UploadPifDocumentHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/UploadPifDocument/UploadPifDocumentHandler.cs:30-33`) and `ListPifDocumentsHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/ListPifDocuments/ListPifDocumentsHandler.cs:29-32`) each independently implement the identical rule for deriving a PIF folder's matching prefix from a product code:

```csharp
var shortCode = request.ProductCode.Length >= 6
    ? request.ProductCode[..6]
    : request.ProductCode;
var prefix = $"{shortCode}__";
```

This is not trivial string interpolation (contrast the Materials flow's `{request.ProductCode}__`, which is duplicated too but carries no business rule) — it encodes *how many characters of a product code identify a PIF folder* on SharePoint/OneDrive, and it appears in two separate handler classes with no shared helper.

## Why it matters
The sibling Materials flow already factors its equivalent filename-construction rule into a shared `MaterialFilenameBuilder` (`backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Infrastructure/MaterialFilenameBuilder.cs`), so the PIF flow is the outlier within its own feature. If the truncation length or rule changes in one handler and not the other, `UploadPifDocument` and `ListPifDocuments` would silently disagree on which folder a given product's PIF documents belong to: an upload could succeed into one folder while the list view keeps searching a different one, with no error surfaced anywhere — the folder-not-found path wouldn't even trigger since both would still find *some* folder, just not the same one.

## Suggested direction
Factor the short-code/prefix derivation into a small shared helper (e.g. alongside or analogous to `MaterialFilenameBuilder`) and have both handlers call it, the same way the Materials flow already avoids duplicating its filename rule.
