# Design: Remove dead legacy API-client scaffolding in `frontend/src/services`

## Applicability

No UI/UX section: this change has no user-facing surface. It deletes unreferenced source files; no screen, component tree, or interaction changes as a result. The "component design" and "data schema" sections below are correspondingly thin, since this is subtractive-only work — there is nothing new to design, only a defined removal boundary to lock down so implementation doesn't have to make judgment calls.

## Component design (removal boundary)

Three independent, non-overlapping units to remove. Each is verified (per `plan-01.md`) to have zero importers, so removal order between them doesn't matter — they can be deleted in a single change.

| Unit | Path(s) | Why it's safe to remove as one unit |
|---|---|---|
| Legacy client | `frontend/src/services/api.ts` | Self-contained module (`ApiClient` class + `apiClient` singleton). No other file imports from it. |
| Orphaned generated-client placeholder | `frontend/src/services/generated/api-client.ts`, `frontend/src/services/generated/.gitkeep` | Self-contained placeholder (`AnelaHebloApiClient` + unused `WeatherForecast` interface). Deleting both files empties the directory, which then drops from git tracking automatically — no explicit `rmdir` step needed. |
| Unused test component | `frontend/src/components/test/ApiTestComponent.tsx` | Only reference to the symbol `ApiTestComponent` is its own declaration/export; `TestApp.tsx` (sole sibling in `components/test/`) does not import it. |

Explicit non-boundary (must remain untouched, and this is the one place a mistake is plausible since the names look related):
- `frontend/src/services/versionService.ts` and `frontend/src/services/__tests__/versionService.test.ts` — already import from the canonical `../api/client`, unrelated to the two deleted seams.
- `frontend/src/api/**` — the canonical client/generated-client/hooks seam; nothing here changes.
- `frontend/src/components/test/TestApp.tsx` — remains as the only file in `components/test/`; must still compile and its own (unrelated) usages must not reference `ApiTestComponent`.

No interfaces are introduced, modified, or need stubbing — deletion only, no call sites to update.

## Data schemas

N/A. No DB schema, request/response shape, or event payload is touched by this change.

## Verification design

Since there's no behavior to design, the "design" that matters here is the verification contract implementation must satisfy, carried over from the plan's acceptance criteria and made concrete as commands:

1. Structural removal check — confirms all three units are gone and nothing else in the tree references them:
   - `git status` shows the three paths (plus the now-empty `services/generated/` dir) as deleted.
   - `git grep -n "services/api['\"]" -- frontend/src frontend/test` → no output.
   - `git grep -n "services/generated" -- frontend/src frontend/test` → no output.
   - `git grep -n "AnelaHebloApiClient"` → no output.
   - `git grep -n "ApiClient" -- frontend/src frontend/test` → only matches inside the canonical `frontend/src/api/**` seam (e.g. `api/client.ts`, generated client, hooks) survive.
   - `git grep -n "ApiTestComponent"` → no output.
2. Build/lint gate (per repo standard, `CLAUDE.md` → Validation before completion): `npm run build` and `npm run lint` in `frontend/` must pass cleanly — this is what catches any TS project-reference or barrel-export surprise the grep pass might miss.
3. Test gate: run the frontend test suite; `versionService.test.ts` must be unaffected and pass, and no other test may implicitly depend on the removed files (e.g. via Jest manual mocks or `__mocks__` shadowing — none currently exist for these paths, but the full suite run is the check).

No new tests are needed — there is no remaining behavior to cover, only the negative-space checks above.

## Risks / edge cases considered

- **Empty-directory git semantics**: `services/generated/` contains only `.gitkeep` + `api-client.ts`; removing both is sufficient, git does not track empty directories, no separate action required.
- **Import-by-substring false positives**: the grep boundary above searches for the exact re-exported symbol names (`ApiClient`, `AnelaHebloApiClient`, `ApiTestComponent`) in addition to path strings, so a rename-only import (e.g. `import { ApiClient as X }`) is still caught.
- **Dynamic/string-based imports**: already ruled out during the plan step (grep for `require(`, template-literal path construction); no additional design mitigation needed since none exist.
