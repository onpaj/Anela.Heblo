### task: regenerate-openapi-client

**Files:**
- Modify (generated, do not hand-edit): `frontend/src/api/generated/api-client.ts`

- [ ] **Step 1: Regenerate the TypeScript client from the updated backend contracts**

Run (from repository root):
```bash
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
```
Expected: Command exits 0 and `frontend/src/api/generated/api-client.ts` is rewritten.

- [ ] **Step 2: Confirm the duplicate type is gone from the generated client**

Run: `grep -n "SearchJournalEntryDto" frontend/src/api/generated/api-client.ts`
Expected: No output (no matches) — the generator no longer emits a `SearchJournalEntryDto` interface/class.

- [ ] **Step 3: Confirm the search response now types entries as `JournalEntryDto[]`**

Run: `grep -n "entries" frontend/src/api/generated/api-client.ts | grep -i journalentrydto`
Expected: At least one match showing `SearchJournalEntriesResponse`'s `entries` property (or its generated equivalent) typed with `JournalEntryDto`.

- [ ] **Step 4: Commit the regenerated client**

```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "chore(journal): regenerate OpenAPI client after SearchJournalEntryDto removal"
```

(This will intentionally fail to build against the frontend files still referencing the old type until the next task updates them — that is expected at this point in the plan.)

---

