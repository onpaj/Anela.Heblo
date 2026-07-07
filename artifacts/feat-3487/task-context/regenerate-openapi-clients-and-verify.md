### task: regenerate-openapi-clients-and-verify


**Context:** `GetMarginReportRequest` no longer declares `IncludeDetailedBreakdown` (removed in the previous task). The frontend TypeScript client (`frontend/src/api/generated/api-client.ts`) and, if built in Debug, the backend C# client (`backend/src/Anela.Heblo.API.Client/Generated/AnelaHebloApiClient.cs`) are NSwag-generated derived artifacts that still contain `includeDetailedBreakdown`/`IncludeDetailedBreakdown` from the old contract. This task regenerates them per `docs/development/api-client-generation.md` and verifies the frontend still builds clean. Generated files must never be hand-edited — regenerate only.

**Step 1 — Regenerate the frontend TypeScript client**

From repo root:
```bash
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
```

If this msbuild target is unavailable in the environment, use the equivalent npm script instead:
```bash
cd frontend && npm run generate-client
```

**Step 2 — Verify the generated client no longer references the removed parameter**

```bash
grep -n "includeDetailedBreakdown\|IncludeDetailedBreakdown" frontend/src/api/generated/api-client.ts
```
Expected output: no matches (empty result).

Also confirm the `analytics_GetMarginReport` method signature dropped the parameter and did not silently reorder `maxProducts` incorrectly:
```bash
grep -n "analytics_GetMarginReport" frontend/src/api/generated/api-client.ts
```
Expected: method signature includes `startDate, endDate, productFilter, categoryFilter, maxProducts` (5 params, in that order) with no `includeDetailedBreakdown` present.

**Step 3 — Regenerate the backend C# client (Debug-mode PostBuild artifact)**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj -c Debug
```
This triggers the `GenerateApiClient` PostBuild target automatically in Debug configuration, regenerating `backend/src/Anela.Heblo.API.Client/Generated/AnelaHebloApiClient.cs`.

Verify:
```bash
grep -n "IncludeDetailedBreakdown" backend/src/Anela.Heblo.API.Client/Generated/AnelaHebloApiClient.cs
```
Expected output: no matches (empty result).

**Step 4 — Verify the frontend builds and lints clean**

```bash
cd frontend
npm run build
```
Expected: build succeeds with no TypeScript errors (no compile error referencing `includeDetailedBreakdown` or a shifted-argument type mismatch on `analytics_GetMarginReport`).

```bash
npm run lint
```
Expected: no new lint errors introduced by the regeneration.

**Step 5 — Confirm no hand-written frontend caller was affected**

```bash
grep -rn "analytics_GetMarginReport(" frontend/src --include="*.ts" --include="*.tsx" | grep -v "frontend/src/api/generated/api-client.ts"
```
Expected output: no matches — confirms (as established in the arch review) there is no hand-written caller of this generated method that could be affected by the parameter-list shift.

**Step 6 — Final full verification pass**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: build succeeds, all tests pass, 0 failures.

**Step 7 — Commit**

```bash
git add frontend/src/api/generated/api-client.ts backend/src/Anela.Heblo.API.Client/Generated/AnelaHebloApiClient.cs
git commit -m "Regenerate OpenAPI clients after removing IncludeDetailedBreakdown from GetMarginReport contract"
```

If the backend C# client generated file is gitignored (verify with `git status` — if it shows no changes, it's not tracked), skip staging it and commit only the frontend generated client file:
```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "Regenerate OpenAPI TypeScript client after removing IncludeDetailedBreakdown from GetMarginReport contract"
```
