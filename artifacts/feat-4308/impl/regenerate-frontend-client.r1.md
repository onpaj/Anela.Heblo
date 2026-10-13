# Implementation: regenerate-frontend-client

## What was implemented

Regenerated the TypeScript OpenAPI client (`frontend/src/api/generated/api-client.ts`)
from the backend's live OpenAPI spec, now that the prerequisite task
(`remove-backend-timestamp-field`) has removed the `Timestamp` property from
`GetConfigurationResponse` on the backend. The regeneration drops the corresponding
`timestamp` field from the generated client, exactly as expected.

## Files created/modified

- `frontend/src/api/generated/api-client.ts` — regenerated via NSwag; the only diff is
  the removal of the `timestamp` field from `GetConfigurationResponse` /
  `IGetConfigurationResponse` (property declaration, `init()` assignment, and
  `toJSON()` serialization line). No other content changed.

## Tests

No new tests — this is a generated-artifact regeneration task. Verification was via
diff inspection and a targeted grep confirming the follow-up task's premise (see
below).

## How to verify

1. Backend prerequisite: `dotnet build Anela.Heblo.sln` from repo root — Build
   succeeded, 0 errors (confirms `Timestamp` is already gone from the backend
   response before regenerating).
2. Ran `dotnet tool restore` (NSwag console tool was not yet restored in this
   worktree) then `dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual`
   — completes successfully, running `dotnet nswag run nswag.frontend.json` against
   the live backend spec.
3. `git diff frontend/src/api/generated/api-client.ts` — scoped to exactly the
   expected 3 lines removed from the `GetConfigurationResponse` block (property,
   `init()` assignment, `toJSON()` line) and 1 line from the `IGetConfigurationResponse`
   interface. No incidental formatting churn elsewhere in the file.
4. Confirmed `frontend/src/services/versionService.ts:99` still references
   `response.timestamp?.toISOString()`, which is now a use of a property that no
   longer exists on `GetConfigurationResponse` — this is the dangling reference the
   next task (`simplify-frontend-timestamp-usage`) is meant to fix.

## Notes

- Deviation from the task context's literal commands:
  - `cd frontend && npm run generate-client` — this npm script **no longer exists**
    in `frontend/package.json` (the `prebuild`/`generate-client` scripts described in
    `docs/development/api-client-generation.md` have been removed from the current
    `package.json`). Used the underlying command directly instead, per the same doc's
    "Manual Generation" section: `dotnet msbuild backend/src/Anela.Heblo.API
    -t:GenerateFrontendClientManual`. This produces the identical generated output;
    only the invocation wrapper differs. Not fixing/restoring the missing npm script
    — out of scope for this task (regenerating the client), and doing so would be an
    unrelated change to `package.json`.
  - Step 3 (`cd frontend && npx tsc --noEmit`, expected to fail on
    `versionService.ts`'s `response.timestamp` reference) could not be observed as
    specified: this worktree had no `node_modules` installed. `npm ci` failed on a
    pre-existing peer-dependency conflict (`knip@5.88.1` requires `@types/node>=18`,
    root project pins `@types/node@^16.18.108`) — same conflict CI itself works around
    via `npm install --legacy-peer-deps` (see `.github/workflows/ci-feature-branch.yml`).
    Using that same flag to install, `npx tsc --noEmit` then fails, but not with the
    expected error — it fails to even **parse** `node_modules/react-i18next`'s own
    `.d.ts` files (`TS1139`/`TS1005` syntax errors), because the installed
    `react-i18next@15.7.4` (pinned in `package-lock.json`, unrelated to this task)
    requires a newer TypeScript than the project's pinned `typescript@^4.9.5`. This
    reproduces identically with the api-client.ts change stashed out (verified via
    `git stash` / `git stash pop`), confirming it is a pre-existing environment
    issue, unrelated to this task's diff, and out of scope to fix here (repo-wide
    dependency version mismatch, not something `regenerate-frontend-client` owns).
    In place of the raw `tsc` run, confirmed the same fact by inspection: grepped
    `frontend/src/services/versionService.ts` and confirmed line 99 still reads
    `response.timestamp?.toISOString()`, which is exactly the dangling reference that
    would now fail to type-check once environment tooling can actually reach that
    file — validating the acceptance criterion's intent (this step is deliberately
    "expected to fail" / confirms the next task is necessary) without being able to
    reproduce the specific tool output in this sandbox.
- No hand-editing of the generated file's content beyond what regeneration itself
  produced.

## PR Summary

Regenerated the frontend OpenAPI TypeScript client so it matches the backend's
`GetConfigurationResponse` now that `Timestamp` has been removed there. The
generated diff drops exactly the `timestamp` field (declaration, `init()`
deserialization, `toJSON()` serialization) from `GetConfigurationResponse` /
`IGetConfigurationResponse` — no other content in the ~46k-line generated file
changed. This intentionally leaves `frontend/src/services/versionService.ts`
referencing a now-nonexistent `response.timestamp` property, which the next task
(`simplify-frontend-timestamp-usage`) fixes.

### Changes
- `frontend/src/api/generated/api-client.ts` — regenerated via NSwag; `timestamp`
  field removed from `GetConfigurationResponse` / `IGetConfigurationResponse`

## Status
DONE
