### task: verify-leaflet-generate-refactor

**Files:**
- None (verification only — no source changes in this task)

This task has no code changes of its own; it confirms the two prior tasks satisfy the spec's FR-3 acceptance criteria (`npm run build` and `npm run lint` succeed with no new warnings/errors; all Leaflet-related Jest tests pass) before considering the feature complete. No backend files were touched by this feature (no `.cs`/`.csproj` changes), so `dotnet build`/`dotnet format` are not applicable here.

- [ ] **Step 1: Run the frontend production build**

Run: `cd frontend && npm run build`

Expected: PASS — build completes with `Compiled successfully.` (or the pre-existing warning baseline, unchanged by this feature) and no new TypeScript or webpack errors. In particular, no "Cannot find module" or type errors referencing `useGenerateLeafletMutation`, `GenerateLeafletParams`, `GenerateLeafletRequest`, or `GenerateLeafletResponse`.

- [ ] **Step 2: Run ESLint**

Run: `cd frontend && npm run lint`

Expected: PASS — exit code `0`, no new lint errors/warnings reported for `frontend/src/api/hooks/useLeaflet.ts`, `frontend/src/api/hooks/__tests__/useLeaflet.test.ts`, `frontend/src/features/leaflet-generator/LeafletGenerateTab.tsx`, or `frontend/src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx`.

- [ ] **Step 3: Run all Leaflet-related Jest tests together**

Run: `cd frontend && CI=true npm test -- --watchAll=false src/api/hooks/__tests__/useLeaflet.test.ts src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx src/features/leaflet-generator/__tests__/LeafletGeneratorPage.test.tsx`

Expected: PASS — all test suites pass:
- `useLeaflet.test.ts`: 5 tests (3 `useSubmitLeafletFeedbackMutation` + 2 `useGenerateLeafletMutation`).
- `LeafletGenerateTab.test.tsx`: 2 tests.
- `LeafletGeneratorPage.test.tsx`: 8 tests (unaffected — it mocks `LeafletGenerateTab` entirely, so it exercises no code from this feature, but it imports the same module tree and must still pass).

- [ ] **Step 4: No commit for this task**

This task makes no file changes — it only runs verification commands against the commits made in `add-generate-leaflet-mutation-hook` and `refactor-leaflet-generate-tab-to-use-mutation-hook`. If any command in Steps 1-3 fails, fix the issue in the relevant prior task's files, re-commit there (do not amend), and re-run this task's steps from Step 1.
