# Review — TransportBox state-machine validation reasons collapse to generic ValidationError

## Verdict: request_changes

## What I checked

- Re-read `plan-01.md` / `design-01.md` / `architecture-01.md` / `development-01.md`.
- Read the full diff in `deed5b4d` (domain exceptions, `TransportBox.cs` throw sites, `ErrorCodes.cs`,
  all four handlers, `i18n.ts`, all touched test files).
- Rebuilt the backend (`dotnet build Anela.Heblo.sln`) — 0 errors.
- Ran `dotnet test --filter "FullyQualifiedName~Transport|~LocalizationCoverageTests|~Architecture"`
  — 273/273 passed, matching the claimed count.
- Ran `dotnet format Anela.Heblo.sln --verify-no-changes` — clean, no diff.
- Traced the frontend rendering path end-to-end: `errorHandler.ts:getErrorMessage` →
  `frontend/src/types/errors.ts` (`export { ErrorCodes }`, re-exporting from
  `frontend/src/api/generated/api-client.ts`) → the generated `ErrorCodes` string enum.

The backend-side implementation (domain exceptions, `ErrorCodes.cs` values, per-handler catch
scoping, i18n Czech strings, test coverage) is sound and matches the approved design — the
reachability analysis, catch-clause ordering, and the FR-5 test-coverage deviation are all
correctly reasoned and verified against the actual call graph.

## Blocking issue

**The frontend's generated `ErrorCodes` enum was never regenerated, so the fix does not reach the
operator.** `errorHandler.ts` gates the whole translation lookup on the *generated* enum, not on
`ErrorCodes.cs` or `i18n.ts` directly:

```ts
// frontend/src/utils/errorHandler.ts
const enumName =
  typeof errorCode === "string" && (errorCode as string) in ErrorCodes  // <- generated enum
    ? (errorCode as string)
    : undefined;

if (!enumName) {
  return `Nastala chyba (neznámý kód: ${errorCode})`;   // <- falls here for all 4 new codes
}
```

`ErrorCodes` here is re-exported from `frontend/src/api/generated/api-client.ts`
(`frontend/src/types/errors.ts:11`), which is a **committed, hand-regenerated file** — I confirmed
there is no `prebuild`/`generate-client` script in the current `frontend/package.json`, and neither
`Dockerfile` nor `.github/workflows/ci-*.yml` invoke the NSwag generation target. Production and CI
builds consume the file as checked into git.

I regenerated it locally (`dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual`)
to verify: before regeneration, `TransportBoxCodeRequired`/`TransportBoxCodeInvalidFormat`/
`TransportBoxEmpty`/`TransportBoxInvalidStateTransition` are **absent** from the generated
`ErrorCodes` enum (only `TransportBoxDuplicateActiveBoxFound = 1405` and earlier are present).
After regeneration, the four new members appear. I reverted the regenerated file afterward since
it wasn't committed as part of this change and pulls in a large amount of unrelated drift (the
generated client is already stale against other in-flight backend changes — e.g.
`ManufactureOrder_GetProtocolPdf`, `RemoveItemFromBox`'s `amount` param — that predate this task).

**Concrete failure scenario**: an operator dispatches an empty box. The handler now correctly
returns `ErrorCode = "TransportBoxEmpty"` with `Params["code"]`. But since `"TransportBoxEmpty"` is
not a key of the frontend's compiled `ErrorCodes` object, `getErrorMessage` never reaches the i18n
lookup and returns `"Nastala chyba (neznámý kód: TransportBoxEmpty)"` — a raw, untranslated code
string. This is not merely "the fix doesn't fully land"; for these four codes it's a step backward
in polish from today's `"Chyba validace"`, and it completely fails the task's stated goal (finding:
"the operator sees only 'Chyba validace' ... the specific, actionable reason ... never reaches the
screen" — after this change, a *different*, more opaque failure mode replaces it).

The design and architecture docs both asserted this wasn't needed ("no manual frontend enum edit
needed" — `design-01.md:209`; "OpenAPI-generated frontend enum (no manual edit)" —
`architecture-01.md:67`), on the premise that the client is "auto-generated on build." That premise
does not hold in this repo today: the generation step is a manual, out-of-band `dotnet msbuild`
target, not wired into `npm run build`, CI, or the Docker image. The dev step's verification ran
`npm run build`/`npm run lint` but never checked whether the new enum members actually appeared in
the generated client or exercised `getErrorMessage` with the new codes — it only verified the
backend handler's `ErrorCode`/`Params` shape and the i18n template text in isolation, not the two
wired together at runtime.

## What to fix

Regenerate and commit `frontend/src/api/generated/api-client.ts` via
`dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual` (per
`docs/development/api-client-generation.md`) so the four new `ErrorCodes` values are present, then
verify manually (not just via existing unit tests) that `getErrorMessage("TransportBoxEmpty", {code: "..."})`
resolves to the Czech template, not the "neznámý kód" fallback. If regenerating pulls in unrelated
API surface drift that's out of scope for this change, flag that separately, but the four
`ErrorCodes` enum members are a hard requirement for this fix to have any effect.
