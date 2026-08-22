### task: retire-smartsuppclient

**Files:**
- Delete: `frontend/src/api/smartsuppClient.ts`

By this point, FR-1 through FR-6 have removed every caller of `smartsuppClient.ts`. This task deletes it and runs the full verification pass.

#### Step 1: Confirm no remaining references

```bash
cd frontend
grep -rn "smartsuppClient" src --include="*.ts" --include="*.tsx"
grep -rn "asInternal" src --include="*.ts" --include="*.tsx"
```

Expect both to return nothing. If anything shows up, it's a file this plan's earlier tasks missed — go back and finish routing it through the typed client or the `getApiBaseUrl()`/`getAuthenticatedFetch()` escape hatch before continuing.

#### Step 2: Delete the file

```bash
cd frontend
rm src/api/smartsuppClient.ts
```

#### Step 3: Full frontend verification

```bash
cd frontend
npm run build
npm run lint
CI=true npx react-scripts test src/api/hooks/__tests__ src/components/customer-support/smartsupp --watchAll=false
```

All three must be clean/green. `npm run build` succeeding here is the confirmation that deleting `smartsuppClient.ts` broke nothing (no dangling import survived).

#### Step 4: NFR-3 compile-time spot-check

This confirms the actual motivation for the whole migration: a backend DTO field rename now surfaces as a frontend compile error instead of a silent runtime `undefined`.

```bash
cd backend
grep -n "public bool Success" src/Anela.Heblo.Application/Shared/BaseResponse.cs
```

Temporarily rename the `Success` property on `BaseResponse` (e.g. to `SuccessX`) — every Smartsupp response DTO extends this class:

```bash
cd backend
sed -i 's/public bool Success/public bool SuccessX/' src/Anela.Heblo.Application/Shared/BaseResponse.cs
grep -rln "\.Success\b" src/Anela.Heblo.API/Controllers/BaseApiController.cs src/Anela.Heblo.Application/Shared/BaseResponse.cs
```

(Expect this to also require touching `BaseApiController.HandleResponse`'s `response.Success` read and `BaseResponse`'s own constructor/property references — fix those up locally too, just enough to get a clean backend build, since the point of this check is only to observe the generated client regenerate and the frontend break, not to ship a real rename.)

```bash
cd backend
dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Regenerate the TypeScript client per `docs/development/api-client-generation.md`'s documented command (find and run the project's NSwag regeneration script/command — check `frontend/package.json` for a `generate-api` or similar script, or `backend/src/Anela.Heblo.API/nswag-templates/` for the generation config referenced there).

```bash
cd frontend
npm run build
```

Expect `npm run build` to now fail with TypeScript compile errors in one or more of the five migrated hook files (any place reading `.success` on a Smartsupp response — `useSmartsupp.ts`'s `useCloseConversation`, `useGenerateDraftReply.ts`, `useSendMessage.ts` all read `data.success`). This is the desired outcome — it demonstrates NFR-3 holds.

Then revert everything from this step:

```bash
cd backend
git checkout -- src/Anela.Heblo.Application/Shared/BaseResponse.cs src/Anela.Heblo.API/Controllers/BaseApiController.cs
cd ../frontend
git checkout -- src/api/generated/api-client.ts
npm run build
```

Confirm the final `npm run build` is clean again (back to the state at the end of Step 3).

#### Step 5: Commit

```bash
cd frontend
git add -A src/api/smartsuppClient.ts
git commit -m "Delete smartsuppClient.ts now that every Smartsupp hook uses the generated typed client"
```

(`git add -A` here specifically to stage the deletion; if any other unrelated files show as modified from the spot-check in Step 4, `git status` first and stage only the deletion.)

#### Step 6: Final full-repo verification

```bash
cd frontend
npm run build
npm run lint
CI=true npx react-scripts test --watchAll=false
```

```bash
cd backend
dotnet build
dotnet format --verify-no-changes
```

All must pass — this is the final task in the plan, and CLAUDE.md's "Validation before completion" applies to the whole feature at this point (BE build/format, FE build/lint, all touched tests). No backend source was actually changed by this feature (Step 4's edits were reverted), so `dotnet build`/`dotnet format` should already be clean from the base branch, but running them here confirms the spot-check's revert was complete and nothing was left half-changed.
