## Context

`knip` was wired up in #3927 as a non-blocking frontend CI step (`.github/workflows/ci-feature-branch.yml`, config in `frontend/knip.json`). Per that issue's acceptance criteria, this issue tracks triaging the initial backlog the first report surfaced, so it can be burned down incrementally and the step eventually promoted to blocking.

Reproduce locally: `cd frontend && npm run knip`.

## Initial report (2026-08-15)

**Unused files (10)** — highest-confidence candidates for deletion, verify each has zero importers before removing:
- `src/api/hooks/useManualCatalogRefresh.ts`
- `src/api/hooks/useRecalculatePurchasePrice.ts`
- `src/api/hooks/useTransportBoxTransitions.ts` (already covered by #3889 — should drop off this list once that PR merges)
- `src/auth/authConfig.ts`
- `src/components/customer/cooling/weatherIcons.tsx`
- `src/components/knowledge-base/KnowledgeBaseAskTab.tsx`
- `src/components/knowledge-base/KnowledgeBaseSearchTab.tsx`
- `src/components/modals/CreateManufactureOrderModal.tsx`
- `src/features/feature-flags/featureFlags.ts`
- `src/hooks/useFeatureFlag.ts`

**Unused dependencies (2)** — safe to remove from `frontend/package.json` if confirmed unused:
- `ajv`
- `web-vitals`

**Unused devDependencies (5)**:
- `@reportportal/agent-js-jest`
- `@reportportal/agent-js-playwright`
- `@types/react-router-dom`
- `jest-junit`
- `lint-staged`

Note: some of these may be false positives if only referenced from config files knip doesn't parse (e.g. Jest/Husky config, `.eslintrc.json` `lint-staged` block) — verify before removing.

**Unlisted dependencies (1)** — used but not declared in `package.json` (relies on a transitive install today):
- `jest-environment-jsdom` (`src/api/hooks/__tests__/useManufactureBatch.test.ts`)

**Unresolved imports (1)**:
- `eslint-config-react-app` referenced from `.eslintrc.json` — likely resolves fine via `react-scripts`'s own dependency tree; knip just can't follow it statically. Worth a quick look, may just need an ignore rule.

**Unused exports (100) / unused exported types (98)** — the bulk of the backlog. Concentrated in `src/api/hooks/*` (DTOs and query-key helpers exported but only used internally within their own file) and `src/features/changelog/*` (barrel file re-exports nothing else imports). Full list is in the CI job's `knip-report` artifact on any feature-branch PR, or reproduce locally.

**Duplicate exports (15)** — mostly `ComponentName|default` pairs (a component exported both named and as default). This is a common intentional pattern in this codebase, not necessarily dead code — worth deciding whether to standardize on one export style per component and add an ignore rule, rather than treating each as a bug.

## Suggested approach

1. Start with the 10 unused files — each is either genuinely dead (delete) or missed by knip's entry-point detection (add to `frontend/knip.json`'s `ignore`/`entry`).
2. Then the unused/unlisted dependencies — quick wins in `package.json`.
3. Batch the `src/api/hooks/*` unused exports by file — many are DTO types generated alongside a hook that never got wired to a consumer.
4. Decide on the duplicate-export (`named|default`) pattern once, then either fix the offending files or add a knip ignore rule so it stops being reported as backlog noise.
5. Once the report is clean, promote the CI step from `continue-on-error: true` to blocking.

