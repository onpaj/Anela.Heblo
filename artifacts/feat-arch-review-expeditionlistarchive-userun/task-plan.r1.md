Plan saved to `docs/superpowers/plans/2026-06-04-relocate-userun-expeditionlist-printfix.md`.

**Summary of the plan**

Five tasks, all frontend-only, fully verbatim relocation of `useRunExpeditionListPrintFix`:

1. **Create `useExpeditionList.ts`** — new sibling hook file with trimmed imports (`useMutation` + `getAuthenticatedApiClient` only; no `useQuery`/`useQueryClient`/`QUERY_KEYS` since the moved hook uses none of them).
2. **Remove the hook from `useExpeditionListArchive.ts`** — delete lines 130–152; verified `useMutation` stays imported (still used by `useReprintExpeditionList`).
3. **Redirect the page import** — split into two import lines so the page sources `useRunExpeditionListPrintFix` from the new module while the other five symbols stay on the archive import.
4. **Update Jest mocks in `ExpeditionListArchivePage.test.tsx`** — required by the arch-review amendment; without it the test's mock factory becomes orphaned. Move the one mock key and one `require(...)` destructure to the new module; `beforeEach` body is unchanged because the local binding name is the same.
5. **Validation gates** — tightened grep checks (defined location + 2 in the page + 3 in the test = exactly 6 matches, 0 in the old file), `npm run build`, `npm run lint`, full Jest suite, optional manual smoke check.

Each task uses bite-sized RED-style verify steps (e.g. expecting Task 2's intermediate `tsc` to report exactly the one error in the page, which Task 3 then fixes) with frequent commits, in line with the surgical-changes constraint from CLAUDE.md.