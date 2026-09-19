### task: verify-full-build-and-format

**Files:**
- No new files. Verification-only task covering the whole solution plus the generated TypeScript client, per `docs/development/api-client-generation.md`.

- [ ] **Step 1: Full backend build**

Run: `cd backend && dotnet build`
Expected: `Build succeeded.` with 0 errors and no new warnings compared to the pre-change baseline.

- [ ] **Step 2: Format check**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: no formatting violations reported for the touched files. If it reports violations, run `dotnet format` (without `--verify-no-changes`) and re-stage/commit the formatting fix as part of the same task's changes.

- [ ] **Step 3: Full backend test run**

Run: `cd backend && dotnet test`
Expected: all tests pass (in particular the full `Anela.Heblo.Tests` suite, not just the Leaflet filter used in the previous task, to catch any missed reference elsewhere in the solution).

- [ ] **Step 4: Regenerate the OpenAPI/TypeScript client and confirm no diff**

Run the project's standard client-generation build step (see `docs/development/api-client-generation.md` — normally triggered by `npm run build` in `frontend/`, which pulls the OpenAPI spec from the built backend and regenerates `frontend/src/api/generated/api-client.ts`).

Run: `cd frontend && npm run build`
Expected: build succeeds. Then run `git diff --stat frontend/src/api/generated/api-client.ts` — expect **no changes** to the `LeafletDocumentSummary`-related generated types (the class name, namespace-independent OpenAPI schema name, and member shape are all unchanged, so regeneration should produce an empty diff for this type per spec FR-3). If the diff is non-empty, inspect it: an empty/no-op diff confirms the refactor is contract-safe; any non-empty diff around `LeafletDocumentSummary` is unexpected and must be investigated before proceeding, since spec FR-3 requires zero API-shape drift.

- [ ] **Step 5: Frontend lint (regression guard only — no frontend source was intentionally changed)**

Run: `cd frontend && npm run lint`
Expected: passes with the same baseline as before this change (this task does not intentionally modify any frontend source file; a clean lint run confirms nothing was inadvertently affected by the client regeneration in Step 4).

- [ ] **Step 6: Final commit (only if Step 2 or Step 4 produced file changes to stage)**

```bash
git add -A
git commit -m "chore(leaflet): dotnet format / client regen after Contracts move" || true
```

If Steps 2 and 4 produced no changes, there is nothing to commit for this task — the two prior task commits already contain the complete change.
