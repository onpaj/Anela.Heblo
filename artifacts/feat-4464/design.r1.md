# Design: Map folderKey to path for Marketing folder links

## Component Design
Add in MarketingCalendarPage.tsx a pure helper:
`mapFolderLinks(links?: ApiFolderLink[]) => Array<{ path: string; label?: string; folderType?: string }>` returning `{ path: fl.folderKey ?? "", label: fl.label, folderType: fl.folderType }` per link (omit `label` if the generated DTO has none). Use it at the list mapping and the detail effect. The modal is unchanged.

## Data Schemas
API: `{ folderKey: string; folderType: string }`. View model: `{ path: string; label?: string; folderType?: string }`.
