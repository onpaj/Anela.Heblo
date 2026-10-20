---
process: module-catalog-documents
kind: module
module: catalog-documents
summary: Shows and uploads the regulatory files of a catalog item (material safety/technical sheets and lot certificates, product PIF) that live in the company SharePoint.
owns: []
verified_at: "5e993f9e2"
related:
  - flow-catalog-document-upload
---

# Catalog documents

## Purpose
Gives staff one place — the catalog item detail — to check and add the regulatory paperwork
that Anela must keep for cosmetics production:
- for each **raw material** (surovina): safety data sheet (Bezpečnostní list, MSDS), technical
  data sheet (Technický list, TDS) and lot certificates of analysis (Certifikát analýzy, COA);
- for each **product / semi-product**: the PIF folder (Product Information File required by the
  EU Cosmetics Regulation).

The files themselves stay in SharePoint (`/Výroba/PIF/…`); Heblo only finds the right folder,
lists it and uploads into it with a consistent file name. It answers "do we have the current
MSDS / the COA for this lot / the PIF for this product, and where is it?".

## Users & screens
Anyone with the `Products_Catalog` permission (Katalog) can see the tabs; uploading needs
`Products_Catalog` write access **and** SharePoint write rights of their own (uploads run under
the user's Microsoft 365 identity).

| Screen | Shown for | What it does |
|---|---|---|
| Catalog (`/catalog`) → item detail → tab **Dokumenty** | materials | lists files of the material folder; "Nahrát soubor" dialog (type, lot/šarže, name, or "Nahrát beze změny názvu") |
| Catalog → item detail → tab **PIF** | products, semi-products | lists files of the PIF folder; "Nahrát PIF" uploads with the original name |

When the folder is missing or ambiguous a banner tells the user which folder to create or fix in
SharePoint. No dashboard tiles, no MCP tools.

## Processes
- `flow-catalog-document-upload` — list and upload material / PIF documents in SharePoint via
  Microsoft Graph; on demand from the two catalog-detail tabs.

Plain read-only action without its own doc: `GET /api/catalog-documents/material-document-types`
returns the fixed type list (MSDS, TDS, COA). The module has no scheduled jobs.

## Data owned
None in Heblo — no tables, schemas, caches or blob containers. All files are owned by the
SharePoint document library configured in `CatalogDocuments:*:DriveId`
(`/Výroba/PIF/Suroviny` for materials, `/Výroba/PIF/Produkty` for PIF).

## External systems
- **Microsoft 365 SharePoint via Microsoft Graph v1.0** — read: list children of the base folder
  and of the item folder (app-only token); write: upload file (`…:/content` ≤ 4 MB, upload
  session in 10 MB chunks above that) with the signed-in user's delegated `Files.ReadWrite.All`
  token. Details in `flow-catalog-document-upload`.

## Dependencies
- Reads from: the **Catalog** module only indirectly — the frontend passes the item's product
  code and type (which decides which tab is shown); the backend does not query the catalog.
- Uses the shared Graph helpers in `Application/Common/Graph` (`GraphApiHelpers`) and
  Microsoft Identity Web `ITokenAcquisition` (same pattern as the meeting-tasks Planner
  integration).
- Read by: nothing else in Heblo.

## Known quirks
- In environments with `UseMockAuth` or `BypassJwtValidation`, or with an unset / placeholder
  drive id, a no-op storage is used and every tab reports "folder not found".
- The UI shows one generic "Nahrání selhalo" message for every upload failure; the precise
  31XX error code is lost (generated client throws on non-200).
- Error codes `CatalogDocumentFileMissing` (3105) and `CatalogDocumentGraphError` (3106) exist
  but are never returned.
- The document type list is hard-coded (`MaterialDocumentTypes`); adding a type needs a code
  change.
- More quirks (PIF prefix sharing, silent alphabetical choice, forbidden characters in lot) in
  `flow-catalog-document-upload`.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/CatalogDocumentsModule.cs` — DI, Graph vs no-op switch
- `backend/src/Anela.Heblo.API/Controllers/CatalogDocumentsController.cs` — the five endpoints
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Services/GraphCatalogDocumentsStorage.cs` — all Graph traffic
- `frontend/src/components/catalog/detail/CatalogDetailTabs.tsx` — which item types get which tab
- `frontend/src/api/hooks/useCatalogDocuments.ts` — FE hooks and caching
