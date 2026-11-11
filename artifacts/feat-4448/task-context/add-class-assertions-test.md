### task: add-class-assertions-test
Extend `frontend/src/features/leaflet-generator/__tests__/LeafletUploadTab.test.tsx` with assertions that the drop zone (default and dragOver) and a status label carry the expected `dark:` classes. No behaviour/testid changes.

Verify: `cd frontend && npx jest LeafletUploadTab --watchAll=false` (or repo's test command) passes.
