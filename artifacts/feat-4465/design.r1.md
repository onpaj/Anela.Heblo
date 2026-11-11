# Design: Rename MarketingFolderType enum values

## Component Design
- `MarketingFolderType` (Domain): `Images=0, Texts=1, Videos=2, Graphics=3, Other=99`.
- `MarketingActionModal` `FOLDER_TYPE_OPTIONS`: `[Images "Obrázky", Texts "Texty", Videos "Videa", Graphics "Grafika", Other "Ostatní"]`; `resolveFolderType` fallback and new-row default become `MarketingFolderType.Images`.
- No UI layout or label changes.

## Data Schemas
- DB: `MarketingActionFolderLinks.FolderType integer NOT NULL` unchanged.
- API: `MarketingFolderLinkRequest.folderType` and `MarketingActionFolderLinkDto.folderType` carry `"Images" | "Texts" | "Videos" | "Graphics" | "Other"`.
