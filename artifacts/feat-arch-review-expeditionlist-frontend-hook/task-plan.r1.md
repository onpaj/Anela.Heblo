Plan saved to `docs/superpowers/plans/2026-06-04-expeditionlist-frontend-hook-cleanup.md`.

## Summary

**Approach:** Pure call-site migration in 9 TDD-driven, bite-sized tasks. The plan follows the arch-review's recommendations over the spec's where they conflict:

1. **Task 1+2 — `useExpeditionDates`:** Failing happy-path test → typed `expeditionListArchive_GetDates(page, pageSize)` call → mapping `?? 0` / `?? []` defaults.
2. **Task 3 — `useExpeditionListsByDate`:** Date → ISO-string and `undefined` → `null` mapping inside `queryFn` (arch-review Decision 2 — local interface kept, generated DTO is structurally incompatible).
3. **Task 4 — `useReprintExpeditionList`:** Drops the local `ReprintExpeditionListRequest` interface (arch-review amendment A4), imports the generated class, calls `new ReprintExpeditionListRequest({...})`, preserves query invalidation.
4. **Task 5 — `useRunExpeditionListPrintFix`:** Typed `expeditionList_RunFix()` with `totalCount ?? 0` mapping.
5. **Task 6 — `getExpeditionListDownloadUrl`:** Uses `getApiBaseUrl()`, no client instantiation (arch-review amendment A5).
6. **Task 7 — `handleOpen` in `ExpeditionListArchivePage.tsx`:** Swaps `(apiClient as any).http.fetch` for `getAuthenticatedFetch()` (arch-review amendment A1 — sixth cast the spec missed).
7. **Task 8 — Guardrail:** Extends `MIGRATED_HOOKS` in `authenticated-api-usage.test.ts` (arch-review amendment A2 — reuses existing Jest gate instead of adding ESLint). Includes a sandbox-revert sanity check to prove the gate fires.
8. **Task 9 — Final validation:** Full lint + build + test suite + manual browser sanity check on the archive page.

**No backend, no OpenAPI regeneration, no new files except one test file.** Hook signatures preserved so `ExpeditionListArchivePage.tsx` and its tests need no consumer-side rewrite.