# Implementation: add-generate-leaflet-mutation-hook

## What was implemented

Added `useGenerateLeafletMutation`, a React Query mutation hook that wraps the
generated OpenAPI client's `leaflet_Generate` method, following the
one-hook-per-operation convention already used by every other Leaflet hook in
`useLeaflet.ts`. Unlike the other hooks in the file (which call
`apiClient.http.fetch` against a manually constructed URL), this hook calls
the generated client method directly so that `leaflet_Generate`'s typed-throw
behavior (422 -> `GenerateLeafletResponse`, 400 -> `ProblemDetails`, other ->
`SwaggerException`) is preserved unchanged for callers instead of being
collapsed into a generic `Error`.

Followed strict TDD: wrote the failing test first (confirmed RED —
`TypeError: ... useGenerateLeafletMutation is not a function`), then added
the minimal implementation (confirmed GREEN — all 5 tests pass).

## Files created/modified

- `frontend/src/api/hooks/useLeaflet.ts` — added the `AudienceType`,
  `GenerateLeafletRequest`, `GenerateLeafletResponse`, `LeafletLength` import
  from the generated client; added the `GenerateLeafletParams` interface and
  the `useGenerateLeafletMutation` hook (inserted between
  `useUploadLeafletDocumentMutation` and `useSubmitLeafletFeedbackMutation`).
- `frontend/src/api/hooks/__tests__/useLeaflet.test.ts` — added imports for
  `useGenerateLeafletMutation`, `AudienceType`, `ErrorCodes`,
  `GenerateLeafletResponse`, `LeafletLength`; added a
  `describe("useGenerateLeafletMutation", ...)` block with two tests
  (success resolution, and 422-rejection passthrough).

## Tests

`frontend/src/api/hooks/__tests__/useLeaflet.test.ts`:
- `useSubmitLeafletFeedbackMutation` (3 pre-existing tests, unchanged) — cover
  the HTTP-409-as-success path, a non-409 error throw, and a normal
  success/JSON-body path.
- `useGenerateLeafletMutation` (2 new tests):
  - "resolves with the GenerateLeafletResponse returned by leaflet_Generate"
    — asserts the mocked `leaflet_Generate` is called once and its resolved
    value is returned unchanged by `mutateAsync`.
  - "propagates a rejected GenerateLeafletResponse (422) unchanged out of
    mutateAsync" — asserts a rejection from `leaflet_Generate` (simulating
    the generated client's 422 typed-throw) rejects `mutateAsync` with the
    same object, unwrapped.

Actual command and output (after `npm install --legacy-peer-deps`, since
`node_modules` did not yet exist in this worktree):

RED (before implementation):
```
✕ resolves with the GenerateLeafletResponse returned by leaflet_Generate (43 ms)
✕ propagates a rejected GenerateLeafletResponse (422) unchanged out of mutateAsync (14 ms)
TypeError: (0 , _useLeaflet.useGenerateLeafletMutation) is not a function
Tests: 2 failed, 3 passed, 5 total
```

GREEN (after implementation):
```
PASS src/api/hooks/__tests__/useLeaflet.test.ts
  useSubmitLeafletFeedbackMutation
    ✓ returns { success: false, alreadySubmitted: true } without throwing on HTTP 409 (101 ms)
    ✓ throws with the status code in the message on a non-ok, non-409 response (13 ms)
    ✓ returns the parsed JSON body on an ok response (2 ms)
  useGenerateLeafletMutation
    ✓ resolves with the GenerateLeafletResponse returned by leaflet_Generate (11 ms)
    ✓ propagates a rejected GenerateLeafletResponse (422) unchanged out of mutateAsync (2 ms)

Test Suites: 1 passed, 1 total
Tests:       5 passed, 5 total
```

## How to verify

```bash
cd frontend
CI=true npx react-scripts test --watchAll=false src/api/hooks/__tests__/useLeaflet.test.ts
CI=false npm run build
npx eslint src/api/hooks/useLeaflet.ts src/api/hooks/__tests__/useLeaflet.test.ts --ext .ts,.tsx
```

Build gate: `CI=false npm run build` completed with `Compiled successfully.`
(no new TypeScript errors; the generated-client exports `AudienceType`,
`LeafletLength`, `GenerateLeafletRequest`, `GenerateLeafletResponse`, and
`ErrorCodes.LeafletEmptyRetrieval`, and the `leaflet_Generate(request:
GenerateLeafletRequest): Promise<GenerateLeafletResponse>` signature all
match what the task context assumed, so no adaptation was needed).

Lint: `npx eslint` on the two changed files reported no errors.

## Notes

- **Deviation (environment only, no source impact):** `frontend/node_modules`
  did not exist in this worktree. A plain `npm install` failed on an
  `@types/node` peer-dependency conflict with `knip`. Resolved with `npm
  install --legacy-peer-deps`, matching how the main checkout's
  `node_modules` appears to have been installed (no `.npmrc` is checked in,
  so this must be done manually per-worktree). This is a local dev-dependency
  installation step, not a change to any tracked file.
- Verified every generated-client symbol the task context's prescribed code
  references (`AudienceType`, `LeafletLength`, `GenerateLeafletRequest`,
  `GenerateLeafletResponse`, `ErrorCodes.LeafletEmptyRetrieval`, and the
  `leaflet_Generate` method signature) against
  `frontend/src/api/generated/api-client.ts` before applying the edits — all
  matched exactly, so the prescribed code was applied verbatim with no
  adaptation.
- No subagents were dispatched (implementer/reviewer roles) — this session
  has no Task/Agent dispatch tool available, and the harness's own
  instruction confirmed the work should be done directly. Implementation
  followed the RED→GREEN TDD discipline manually instead.
- No CRITICAL/HIGH concerns identified in self-review: the change is a
  small, additive hook with no mutation of existing hooks' behavior, follows
  existing file conventions (JSDoc style, error-preservation pattern already
  used for other typed-throw cases in this codebase), and both new tests are
  meaningful (they'd fail without the implementation and pass with it).

## PR Summary

Adds `useGenerateLeafletMutation`, a React Query mutation hook wrapping the
generated OpenAPI client's `leaflet_Generate` call, to
`frontend/src/api/hooks/useLeaflet.ts`. This closes the gap where the
Generate-Leaflet mutation had no dedicated hook and callers would otherwise
have had to bypass the established one-hook-per-operation pattern used by
every other Leaflet operation in this file. The hook calls the generated
client method directly (not a raw `http.fetch`, unlike sibling hooks in this
file) specifically to preserve `leaflet_Generate`'s typed-throw behavior (422
-> `GenerateLeafletResponse`, 400 -> `ProblemDetails`, other ->
`SwaggerException`) unchanged for callers. Developed test-first: the two new
tests were confirmed failing before the implementation existed, then passing
after.

### Changes
- `frontend/src/api/hooks/useLeaflet.ts` — added `GenerateLeafletParams`
  interface and `useGenerateLeafletMutation` hook, plus the generated-client
  imports it needs.
- `frontend/src/api/hooks/__tests__/useLeaflet.test.ts` — added a
  `useGenerateLeafletMutation` test suite (success path, 422-rejection
  passthrough path) and the imports it needs.

## Status
DONE
