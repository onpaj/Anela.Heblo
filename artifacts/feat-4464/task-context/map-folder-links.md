### task: map-folder-links
Add a `mapFolderLinks` helper and use it at both mapping sites.

Steps:
1. In `MarketingCalendarPage.tsx`, add a module-level pure helper:
   `mapFolderLinks(links?: Array<{ folderKey?: string; folderType?: string; label?: string }>) => Array<{ path: string; label?: string; folderType?: string }>`.
   It returns `(links ?? []).map(fl => ({ path: fl.folderKey ?? "", folderType: fl.folderType }))`. Add `label: fl.label` only if the API DTO in `frontend/src/api/generated/api-client.ts` has a label field. The `MarketingFolderLinkRequest` class has only `folderKey` and `folderType`, so check the response DTO before adding it.
2. Replace `folderLinks: a.folderLinks` at line ~135 (listActions mapping) with `folderLinks: mapFolderLinks(a.folderLinks)`.
3. Replace `folderLinks: a.folderLinks` at line ~207 (detailQuery effect) the same way.
4. Run `grep -n "folderLinks" frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` and confirm no raw pass-through remains.

Acceptance: list and detail actions expose `path` populated from `folderKey`. `npm run build` and `npm run lint` pass in `frontend/`.

