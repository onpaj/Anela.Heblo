### task: regenerate-frontend-client

**Files:**
- Modify (generated, do not hand-edit content beyond what regeneration produces): `frontend/src/api/generated/api-client.ts`

**Prerequisite:** `task: remove-backend-timestamp-field` must be complete and the backend must build successfully — the client is generated from the live backend OpenAPI spec, so the `Timestamp` field must already be gone from `GetConfigurationResponse` before regenerating, or this step is a no-op.

- [ ] **Step 1: Regenerate the TypeScript client from the updated backend**

Run: `cd frontend && npm run generate-client`

This runs `dotnet msbuild ../backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual` per `docs/development/api-client-generation.md`, which rebuilds the backend's OpenAPI spec and re-emits `frontend/src/api/generated/api-client.ts` via NSwag.

- [ ] **Step 2: Verify the generated diff is scoped to the `GetConfigurationResponse`/`IGetConfigurationResponse` block**

Run: `git diff frontend/src/api/generated/api-client.ts`
Expected: The diff removes exactly:
- `timestamp?: Date;` from `export interface IGetConfigurationResponse { ... }`
- `timestamp?: Date;` from `export class GetConfigurationResponse extends BaseResponse implements IGetConfigurationResponse { ... }`
- the line `this.timestamp = _data["timestamp"] ? new Date(_data["timestamp"].toString()) : <any>undefined;` from `GetConfigurationResponse.init()`
- any corresponding `timestamp` serialization line in `GetConfigurationResponse.toJSON()`, if present

If the diff contains unrelated changes (e.g. from an out-of-date NSwag toolchain producing incidental formatting churn across the whole file), do not hand-trim it — that would drift from the generator's actual output. Instead, verify the NSwag/toolchain version matches what CI uses (see `docs/development/api-client-generation.md`) before proceeding, since this file must always match what `npm run generate-client` produces byte-for-byte.

- [ ] **Step 3: Confirm the frontend still type-checks with the field gone (expected to fail here — this is deliberate)**

Run: `cd frontend && npx tsc --noEmit`
Expected: FAIL, with a TypeScript error in `frontend/src/services/versionService.ts` at the line referencing `response.timestamp` (property `timestamp` does not exist on type `GetConfigurationResponse`). This confirms the generated client actually dropped the field and that the next task's edit is necessary — do not treat this as a broken build to fix in this task; the fix is `task: simplify-frontend-timestamp-usage`.

- [ ] **Step 4: Commit the regenerated client**

```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "chore: regenerate OpenAPI client without Timestamp field"
```

---
