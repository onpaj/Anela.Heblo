---
process: flow-catalog-document-upload
kind: workflow
module: catalog-documents
summary: Lists and uploads regulatory files (material MSDS/TDS/COA, product PIF) in the SharePoint folders that belong to a catalog item, finding the folder by a code prefix and naming material files by a fixed scheme.
owns:
  - backend/src/Anela.Heblo.Application/Features/CatalogDocuments/**
  - backend/src/Anela.Heblo.API/Controllers/CatalogDocumentsController.cs
  - frontend/src/api/hooks/useCatalogDocuments.ts
  - frontend/src/components/catalog/detail/tabs/MaterialDocumentsTab.tsx
  - frontend/src/components/catalog/detail/tabs/PifDocumentsTab.tsx
  - frontend/src/components/catalog/detail/tabs/shared/**
verified_at: "5e993f9e2"
related: []
---

# Catalog document upload (materials + PIF)

## Purpose
Anela must keep regulatory paperwork for everything it makes: for each raw material
(surovina) its safety data sheet, technical data sheet and lot certificates of analysis, and for
each cosmetic product its PIF (Product Information File, required by the EU Cosmetics
Regulation). These files live in the company SharePoint, not in Heblo. This workflow lets staff
see and add them straight from the catalog item detail instead of browsing SharePoint:

- **Dokumenty** tab — on materials (`ProductType.Material`) only: lists the material's folder
  and uploads into it (button "Nahrát soubor").
- **PIF** tab — on products and semi-products (`Product`, `SemiProduct`) only: lists the
  product's PIF folder and uploads into it (button "Nahrát PIF").

Heblo stores nothing: no table, no cache, no blob. SharePoint is the single source of truth; the
tabs are a live window onto it. Clicking a file opens it in SharePoint (`webUrl`).

## Trigger
On demand only — no Hangfire job, no BackgroundRefresh task.
- Opening a tab: `GET /api/catalog-documents/materials/{productCode}` or
  `GET /api/catalog-documents/pif/{productCode}` (needs `Products_Catalog` read). The frontend
  caches the answer for 30 s (react-query `staleTime`); the refresh icon ("Obnovit") refetches.
- Upload dialog: `POST /api/catalog-documents/materials/{productCode}` (multipart: `file`,
  `documentTypeCode`, `lot`, `commonName`, `uploadAsIs`) or
  `POST /api/catalog-documents/pif/{productCode}` (multipart: `file`). Needs `Products_Catalog`
  **write**; request limit 50 MB. On success the list is invalidated and reloaded.
- Type picklist: `GET /api/catalog-documents/material-document-types` (cached 5 min in the FE).

The upload button appears only when the folder was found (`FolderStatus = Found`).

## Data flow
Source and target are both Microsoft 365 SharePoint, through Microsoft Graph v1.0
(`https://graph.microsoft.com/v1.0`). Repo config points both areas at the same document
library (same `DriveId`), in different base folders:

| Area | Base folder (`BasePath`) | Folder found by prefix | Multiple matches |
|---|---|---|---|
| Materials | `/Výroba/PIF/Suroviny` | `{productCode}__` (full code) | error — "Nalezeno více složek…" |
| PIF | `/Výroba/PIF/Produkty` | first 6 characters of the code + `__` | allowed — first alphabetically is used |

**List**
1. Find folder: `GET /drives/{DriveId}/root:/{BasePath}:/children`, following every
   `@odata.nextLink` page. Keep children that are folders whose name starts with the prefix
   (case-insensitive). 404 on the base path → NotFound.
2. List files: `GET /drives/{DriveId}/items/{folderId}/children` (all pages); keep only
   files (subfolders are ignored, not recursed). Return name, `webUrl`, size in bytes,
   `lastModifiedDateTime`.
3. Response always carries `FolderStatus`, `ExpectedPrefix`, `BasePath`, so the tab can tell
   the user exactly which folder to create ("Složka pro {prefix} nebyla nalezena pod {basePath}.
   Vytvořte ji v SharePointu a obnovte stránku.").

**Upload**
1. Validate (materials, structured mode only — see Logic).
2. Find the folder exactly as in List step 1.
3. Upload into it:
   - ≤ 4 MB: `PUT /drives/{DriveId}/items/{folderId}:/{filename}:/content?@microsoft.graph.conflictBehavior=rename`.
   - \> 4 MB: `POST …/items/{folderId}:/{filename}:/createUploadSession` (conflictBehavior
     `rename`), then `PUT` to the session URL in 10 MB chunks with `Content-Range`.
4. Return the filename SharePoint actually stored (may differ after a rename) as
   `UploadedFilename`.

**Identity.** Reads use an **app-only** token (`/.default`), so every user sees the same list
regardless of their own SharePoint rights. Uploads use the **signed-in user's delegated** token
(on-behalf-of, scope `Files.ReadWrite.All`): the file is created as that person, and SharePoint
permissions decide whether they may write.

## Logic & formulas
- **PIF folder prefix** (`PifFolderPrefixBuilder`): `productCode[..6] + "__"`, or the whole
  code + `__` when shorter than 6. Product variants that share the first 6 characters (e.g.
  sizes of one product) therefore share one PIF folder. Example: `AKL001030` → `AKL001__`.
- **Material folder prefix**: the full product code + `__` (e.g. `SUR0123__`), exactly one
  folder expected.
- **Material document types** (hard-coded in `MaterialDocumentTypes`, not configurable):

  | Code | Label (UI) | Lot (šarže) required |
  |---|---|---|
  | `MSDS` | Bezpečnostní list | no |
  | `TDS` | Technický list | no |
  | `COA` | Certifikát analýzy | yes |

- **Material file name** (`MaterialFilenameBuilder`): `{TYPE}__{lot}__{commonName}{ext}`, with
  `commonName` trimmed and `ext` taken from the original file. An empty lot keeps both
  separators: `MSDS____Shea butter.pdf`; with a lot: `COA__2024-001__Shea butter.pdf`.
  `commonName` defaults to the original file name without extension (both in the dialog and in
  the controller when the field is blank).
- **"Nahrát beze změny názvu"** (`uploadAsIs = true`): skips type/lot validation and the naming
  scheme; the original file name is used.
- **PIF upload** never renames — always the original file name, no type or lot.
- **Name collision**: SharePoint keeps both files and gives the new one a numbered suffix
  (`conflictBehavior=rename`); nothing is overwritten. The stored name is returned.
- **Errors** (`ErrorCodes`, 31XX):

  | Code | HTTP | When |
  |---|---|---|
  | `CatalogDocumentInvalidTypeCode` 3101 | 400 | structured material upload with unknown type |
  | `CatalogDocumentLotRequired` 3102 | 400 | `COA` without a lot |
  | `CatalogDocumentFolderNotFound` 3103 | 404 | no folder matches the prefix |
  | `CatalogDocumentFolderMultipleMatches` 3104 | 409 | more than one material folder matches |

  An empty file returns plain 400 "File is required." Any Graph failure throws
  `GraphApiException` (status + first 300 chars of the body) and is not mapped to a module error
  code, so it surfaces as a generic server error. A missing delegated consent becomes
  `InvalidOperationException` "Microsoft 365 consent required for scope …".

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `CatalogDocuments:Materials:DriveId` | `b!ZHop5l9-…NqkCI3cHeQYkMtyqK0FkG` | SharePoint document library (Graph drive id) for material folders |
| `CatalogDocuments:Materials:BasePath` | `/Výroba/PIF/Suroviny` | parent folder holding one `{code}__…` folder per material |
| `CatalogDocuments:PIF:DriveId` | same drive as Materials | library for PIF folders |
| `CatalogDocuments:PIF:BasePath` | `/Výroba/PIF/Produkty` | parent folder holding one `{6-char code}__…` folder per product line |
| `UseMockAuth` | `false` | `true` → no-op storage (see quirks) |
| `BypassJwtValidation` | `false` | `true` → no-op storage |

All four `CatalogDocuments` keys are `[Required]` and validated on start. The real Graph
storage is registered only when both drive ids are set, neither contains the placeholder text
`secrets.json`, and neither mock-auth switch is on; otherwise `NoOpCatalogDocumentsStorage`.

## Runtime facts
None.

## Known quirks
- **Local/mock environments always say "folder not found".** With mock auth or bypassed JWT the
  no-op storage returns NotFound for every list and upload, so the tabs show the "Vytvořte ji v
  SharePointu" banner and no upload button even when the folder exists.
- **Upload errors all look the same in the UI.** The generated TS client throws on any non-200,
  so the dialog shows only "Nahrání selhalo. Zkuste to znovu." — the specific reason (lot
  missing, folder not found, Graph/consent error) is not shown to the user.
- **Silent folder choice for PIF.** When several PIF folders share the 6-character prefix the
  alphabetically first is used for both listing and upload, logged only at Information level.
  A file can land in a sibling product's folder if folders are named inconsistently.
- **Folder naming is the contract.** Folders are matched only by "name starts with
  `{prefix}`" (case-insensitive) directly under the base path. A folder named without the
  double underscore, nested one level deeper, or renamed in SharePoint is invisible to Heblo
  until it follows the convention again.
- **Only direct files are shown.** Files in subfolders of the product/material folder are not
  listed and cannot be uploaded to.
- **Lot is not sanitised.** The lot and common name go into the file name as typed; characters
  SharePoint forbids in names (e.g. `/`, `:`, `*`, `?`) make Graph reject the upload with a
  generic error.
- **Unused error codes.** `CatalogDocumentFileMissing` (3105) and `CatalogDocumentGraphError`
  (3106) are defined and translated in `i18n.ts` but never returned; empty files get a plain 400
  and Graph errors are not mapped.
- **Reads and writes use different identities.** A user may see a folder (app-only read) yet
  fail to upload into it because their own SharePoint permission or the tenant's admin consent
  for `Files.ReadWrite.All` is missing.

## Code entry points
- `backend/src/Anela.Heblo.API/Controllers/CatalogDocumentsController.cs` — endpoints, permissions, 50 MB limit, `commonName` default
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/UploadMaterialDocument/UploadMaterialDocumentHandler.cs` — validation, naming, folder lookup
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/UploadPifDocument/UploadPifDocumentHandler.cs` — PIF upload
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/UseCases/List*Documents/` — listing
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Services/GraphCatalogDocumentsStorage.cs` — Graph calls, paging, chunked upload, token types
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/Infrastructure/` — prefix/name builders, type list, options
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/CatalogDocumentsModule.cs` — Graph vs no-op registration
- `frontend/src/components/catalog/detail/tabs/MaterialDocumentsTab.tsx`, `PifDocumentsTab.tsx`, `shared/*UploadDialog.tsx` — UI
