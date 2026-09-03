### task: final-validation

**Files:** none (verification only — no code changes in this task)

- [ ] **Step 1: Full backend build**

Run: `dotnet build backend/Anela.Heblo.sln` (adjust the solution path if `ls backend/*.sln`
showed a different name in the earlier task)
Expected: `Build succeeded.` with zero errors, zero new warnings compared to before this
change.

- [ ] **Step 2: Format check**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: no formatting diffs reported. If it reports diffs, run
`dotnet format backend/Anela.Heblo.sln` (without `--verify-no-changes`) to apply them, then
re-run the verify command, then `git add -u backend && git commit -m "chore(packing-materials): dotnet format"`
if it made changes.

- [ ] **Step 3: Full backend test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: all tests pass — not just the `PackingMaterials`-filtered subset from the
previous task, to catch any unexpected ripple effect elsewhere in the solution (there
should be none; `ConsumptionGroupBy` and every file this plan touches are private to the
PackingMaterials module).

- [ ] **Step 4: Confirm no leftover references to the removed validation**

Run: `grep -rn "ValidGroupByValues" backend/`
Expected: no matches (confirms the removed HashSet field and its usages are fully gone,
not just renamed or partially removed).

- [ ] **Step 5: Manual sanity check of the enum's accepted query values (optional but recommended)**

If a local dev instance is available (`docs/development/setup.md`), start the API and hit:
- `GET /api/packing-materials/consumption?date=<any-valid-yyyy-MM-dd>&groupBy=Product` → `200 OK`
- `GET /api/packing-materials/consumption?date=<any-valid-yyyy-MM-dd>` (no `groupBy`) → `200 OK`, grouped by `Material` (the default)
- `GET /api/packing-materials/consumption?date=<any-valid-yyyy-MM-dd>&groupBy=bogus` → `400 Bad Request` with the framework's default `ValidationProblemDetails` body (not the old `{ "error": "Invalid GroupBy value..." }` shape — this is the intentional, reviewed change from `arch-review.r1.md` Decision 3)

This step does not need to be automated as a new integration test — the unit-level
coverage from `update-groupby-tests` plus this manual check is sufficient per
`arch-review.r1.md`'s Risk table, which accepts the framework-default error shape without
requiring new integration test coverage.

- [ ] **Step 6: Note the OpenAPI/frontend client regeneration (no action required)**

Per `docs/development/api-client-generation.md`, the TypeScript client
(`frontend/src/api/generated/api-client.ts`) regenerates automatically on the next
`npm run build` / `npm start` in `frontend/`. No manual frontend edits are needed by this
plan — confirmed in `spec.r1.md`'s Out of Scope section that no frontend call site
currently exists for this endpoint.
