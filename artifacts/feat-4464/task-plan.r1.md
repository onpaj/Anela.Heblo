# Implementation Plan: Map folderKey to path for Marketing folder links

Goal: editing a marketing action must preserve its folder links by mapping API `folderKey` to the local `path` field in `MarketingCalendarPage.tsx`.

Scope: frontend only. Files: `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` and `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx`. The modal, backend and OpenAPI client are unchanged. `calendar/MobileAgendaView.tsx:72` is a display-only pass-through for calendar events and is not changed.

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

### task: regression-tests
Update and add tests in `pages/__tests__/MarketingCalendarPage.test.tsx`.

Steps:
1. Update the expectations at ~lines 481-500 (list mapping test) and ~525-538 (detail mapping test). Input fixtures keep `folderKey`. Expected output becomes `folderLinks: [{ path: "abc", folderType: "GoogleDrive" }]` and `[{ path: "xyz", folderType: "SharePoint" }]`. Include `label` only if the helper maps it.
2. Add a test that an action with no `folderLinks` maps to `folderLinks: []`.
3. Add a regression test covering the bug: the detail action with folder links is passed to the modal as `existingAction` with `path` populated, so that saving without touching the folder section preserves the links. Follow the existing mock-modal pattern. If the modal is mocked, assert that `existingAction.folderLinks[0].path` equals the API `folderKey`. If a real-modal render is feasible, also submit and assert the update request contains the original `folderKey`.
4. Run `cd frontend && npx jest src/components/marketing` (or the repo's test command) and confirm all pass.

Acceptance: tests fail against the old pass-through and pass with the fix.

### task: validate
Run final validation.

Steps:
1. `cd frontend && npm run build && npm run lint`.
2. Run the marketing test folder and confirm green.
3. Note in the PR description that `MobileAgendaView.tsx:72` has the same pass-through but only feeds calendar display and is intentionally unchanged.

Acceptance: build, lint and tests pass. No process-doc change is needed because there is no sync, calculation or data-feed change.
