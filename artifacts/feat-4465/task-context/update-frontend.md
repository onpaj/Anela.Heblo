### task: update-frontend
**Files:** `frontend/src/api/generated/api-client.ts` (regenerated), `frontend/src/components/marketing/detail/MarketingActionModal.tsx`, `frontend/src/components/marketing/detail/__tests__/MarketingActionModal.test.tsx`.

- [ ] Regenerate the TS client per `docs/development/api-client-generation.md`; confirm enum is `Images|Texts|Videos|Graphics|Other`.
- [ ] In `MarketingActionModal.tsx` update `FOLDER_TYPE_OPTIONS` (labels unchanged), `resolveFolderType` fallback and new-row default to `Images`.
- [ ] In the modal test replace `MarketingFolderType.Campaign` with `Graphics`.
- [ ] Run `npm run build`, `npm run lint`, `npx jest src/components/marketing`; all pass.
- [ ] Commit: `refactor(marketing): use renamed MarketingFolderType in frontend`.

