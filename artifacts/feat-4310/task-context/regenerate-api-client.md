### task: regenerate-api-client


**Files:**
- Modify (generated, do not hand-edit content beyond what generation produces): `frontend/src/api/generated/api-client.ts`

**Depends on:** `fix-response-nullability` (the backend DTO must already be nullable before regenerating, so the OpenAPI schema reflects the fix).

- [ ] **Step 1: Regenerate the TypeScript client**

Run: `dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual`

Expected: succeeds, rewrites `frontend/src/api/generated/api-client.ts` in place. Confirm the `DownloadFromUrlResponse` interface (or class) now types `blobUrl`, `blobName`, `containerName` as optional/nullable (e.g. `blobUrl?: string | undefined;`) instead of a required `string`.

- [ ] **Step 2: Verify the frontend builds cleanly against the regenerated client**

Run: `cd frontend && npm run build`

Expected: PASS. This both re-runs client generation via the `prebuild` script (confirming Step 1's output is reproducible) and type-checks any code in `frontend/src/` that consumes `DownloadFromUrlResponse`'s fields — surfacing a compile error if any caller assumed non-null without a guard.

- [ ] **Step 3: Run frontend lint**

Run: `cd frontend && npm run lint`

Expected: PASS — no new lint errors from the regenerated client or any touched file.

- [ ] **Step 4: Commit the regenerated client**

```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "chore(filestorage): regenerate TS client for nullable DownloadFromUrlResponse fields

Reflects the backend DownloadFromUrlResponse.BlobUrl/BlobName/ContainerName
nullability fix in the generated OpenAPI client."
```

If `git status` shows no changes to `api-client.ts` after Step 1 (NSwag output can be byte-identical if the schema's effective nullability metadata was already inferred correctly), skip this commit — there is nothing to commit, and that is an acceptable outcome, not a failure.
