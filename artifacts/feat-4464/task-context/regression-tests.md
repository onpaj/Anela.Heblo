### task: regression-tests
Update and add tests in `pages/__tests__/MarketingCalendarPage.test.tsx`.

Steps:
1. Update the expectations at ~lines 481-500 (list mapping test) and ~525-538 (detail mapping test). Input fixtures keep `folderKey`. Expected output becomes `folderLinks: [{ path: "abc", folderType: "GoogleDrive" }]` and `[{ path: "xyz", folderType: "SharePoint" }]`. Include `label` only if the helper maps it.
2. Add a test that an action with no `folderLinks` maps to `folderLinks: []`.
3. Add a regression test covering the bug: the detail action with folder links is passed to the modal as `existingAction` with `path` populated, so that saving without touching the folder section preserves the links. Follow the existing mock-modal pattern. If the modal is mocked, assert that `existingAction.folderLinks[0].path` equals the API `folderKey`. If a real-modal render is feasible, also submit and assert the update request contains the original `folderKey`.
4. Run `cd frontend && npx jest src/components/marketing` (or the repo's test command) and confirm all pass.

Acceptance: tests fail against the old pass-through and pass with the fix.

