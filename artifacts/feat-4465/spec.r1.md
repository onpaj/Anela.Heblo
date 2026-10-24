# Specification: Rename MarketingFolderType enum values to match media asset categories

## Summary
`MarketingFolderType` values (`General`, `Seasonal`, `ProductLine`, `Campaign`) describe organizational scope, but the UI uses them as media categories (Obrázky, Texty, Videa, Grafika). Rename the C# members to `Images`, `Texts`, `Videos`, `Graphics` (keeping `Other`) with unchanged integer values, and update every consumer so code is self-documenting.

## Background
Issue #4465 (arch-review, Marketing module). The DB column `MarketingActionFolderLinks.FolderType` is an `int` (no value conversion in `MarketingActionFolderLinkConfiguration`), so renaming members while keeping 0/1/2/3/99 needs no migration. The API however serializes the enum by **name**: `MarketingActionDto` uses `fl.FolderType.ToString()`, `MarketingFolderLinkRequest.FolderType` binds from the string name, and the generated TS enum is string-valued (`General = "General"`). The wire contract therefore changes with the rename and the frontend must change in the same PR.

## Functional Requirements

### FR-1: Rename domain enum members
In `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingFolderType.cs`: `General=0 -> Images`, `Seasonal=1 -> Texts`, `ProductLine=2 -> Videos`, `Campaign=3 -> Graphics`, `Other=99` unchanged.
**Acceptance criteria:**
- Integer values are identical to before.
- No remaining reference to the old member names in `backend/`.
- `dotnet build` and `dotnet format` pass.

### FR-2: Update backend tests
Replace old member names in the Marketing tests (Domain and Application test folders). `MarketingActionDtoTests` asserts `.ToString()` of the enum, which follows the rename automatically.
**Acceptance criteria:**
- All Marketing tests compile and pass.
- A test pins the integer values (`(int)Images==0, Texts==1, Videos==2, Graphics==3, Other==99`) to guard DB compatibility.

### FR-3: Update frontend
In `MarketingActionModal.tsx` update `FOLDER_TYPE_OPTIONS`, `resolveFolderType` default and the default new-link row to the new generated names (`Images`, `Texts`, `Videos`, `Graphics`, `Other`); labels (Obrázky, Texty, Videa, Grafika, Ostatní) unchanged. Update `MarketingActionModal.test.tsx` (`Campaign` -> `Graphics`). Regenerate the OpenAPI TS client (`frontend/src/api/generated/api-client.ts`, tracked in git, regenerated on build).
**Acceptance criteria:**
- `npm run build`, `npm run lint` and the Marketing jest tests pass.
- Each option maps label-to-value exactly as before (Obrázky=0 ... Grafika=3).

### FR-4: Update process doc
`docs/processes/feed-marketing-action-to-outlook.md` "Folder type labels do not match enum names" quirk is resolved; replace with the new mapping and bump `verified_at`; run `python3 scripts/process-docs/check.py index`.

## Non-Functional Requirements

### NFR-1: Performance
No impact.

### NFR-2: Security
No impact.

### NFR-3: Compatibility
No DB migration. Because stored data is int, existing rows keep their meaning. API wire names change; the only known client is the bundled frontend (deployed as one Docker image). MCP tools expose no folder type (verified by grep of `backend/src`).

## Data Model
`MarketingActionFolderLink.FolderType` (int column) unchanged.

## API / Interface Design
OpenAPI enum `MarketingFolderType` values change from `General|Seasonal|ProductLine|Campaign|Other` to `Images|Texts|Videos|Graphics|Other`. `MarketingActionFolderLinkDto.FolderType` (string) emits the new names.

## Dependencies
OpenAPI client regeneration (build step).

## Out of Scope
- Fixing the separate folder-link-loss bug (modal reads `fl.path`) noted in the process doc.
- Adding new folder types; renaming the type itself.
- Any DB migration.

## Open Questions
None.

## Status: COMPLETE
