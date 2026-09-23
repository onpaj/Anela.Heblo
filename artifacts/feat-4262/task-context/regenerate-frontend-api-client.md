### task: regenerate-frontend-api-client

**Files:**
- Modify (generated, do not hand-edit): `frontend/src/api/generated/api-client.ts`

Depends on: `add-validation-exception-and-error-code` (needs `ErrorCodes.ShipmentValidationFailed` to exist in the OpenAPI spec the backend serves).

Per `docs/development/api-client-generation.md`, the TypeScript client is generated from the backend's OpenAPI spec via NSwag — it must be regenerated, not hand-edited, so the `ErrorCodes` union type in `api-client.ts` includes `"ShipmentValidationFailed"` and the `surface-validation-message-in-frontend` task's TypeScript compiles against it.

- [ ] **Step 1: Regenerate the client**

Run (from repository root):
```bash
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
```

- [ ] **Step 2: Verify the new error code is present**

Run: `grep -n "ShipmentValidationFailed" frontend/src/api/generated/api-client.ts`
Expected: at least one match, inside the generated `ErrorCodes` enum/union.

- [ ] **Step 3: Diff-review the regenerated file**

Run: `git diff --stat frontend/src/api/generated/api-client.ts`
Expected: a small, additive diff limited to the new `ErrorCodes` member (and whatever else NSwag's determinism produces — if the diff is much larger than expected, stop and investigate before proceeding rather than committing an unreviewed regeneration).

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "chore(api-client): regenerate for ShipmentValidationFailed error code"
```

---

