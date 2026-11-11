# Implementation Plan: Rename MarketingFolderType enum values (#4465)

### task: rename-enum-and-backend-tests
**Files:** `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingFolderType.cs`; Marketing tests under `backend/test/Anela.Heblo.Tests/Domain/Marketing/` and `.../Application/Marketing/`; new `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingFolderTypeTests.cs`.

- [ ] Rename members: `General->Images`, `Seasonal->Texts`, `ProductLine->Videos`, `Campaign->Graphics`; keep ints 0,1,2,3 and `Other=99`.
- [ ] Replace old names in all backend test files (`grep -rnE "MarketingFolderType\.(General|Seasonal|ProductLine|Campaign)" backend` must return nothing).
- [ ] Add `MarketingFolderTypeTests` asserting `(int)` of each member (0,1,2,3,99).
- [ ] Run `dotnet build`, `dotnet format`, `dotnet test --filter Marketing`; all pass.
- [ ] Commit: `refactor(marketing): rename MarketingFolderType members to media categories`.

### task: update-frontend
**Files:** `frontend/src/api/generated/api-client.ts` (regenerated), `frontend/src/components/marketing/detail/MarketingActionModal.tsx`, `frontend/src/components/marketing/detail/__tests__/MarketingActionModal.test.tsx`.

- [ ] Regenerate the TS client per `docs/development/api-client-generation.md`; confirm enum is `Images|Texts|Videos|Graphics|Other`.
- [ ] In `MarketingActionModal.tsx` update `FOLDER_TYPE_OPTIONS` (labels unchanged), `resolveFolderType` fallback and new-row default to `Images`.
- [ ] In the modal test replace `MarketingFolderType.Campaign` with `Graphics`.
- [ ] Run `npm run build`, `npm run lint`, `npx jest src/components/marketing`; all pass.
- [ ] Commit: `refactor(marketing): use renamed MarketingFolderType in frontend`.

### task: update-process-doc
**Files:** `docs/processes/feed-marketing-action-to-outlook.md`.

- [ ] Replace the "Folder type labels do not match enum names" quirk with the new mapping (Images=Obrázky, Texts=Texty, Videos=Videa, Graphics=Grafika, Other=Ostatní; ints 0-3, 99).
- [ ] Bump `verified_at`; run `python3 scripts/process-docs/check.py index`.
- [ ] Final check: `grep -rnE "MarketingFolderType\.(General|Seasonal|ProductLine|Campaign)" backend frontend/src` returns nothing; commit.
