# Design: Frontend knip backlog triage

## Component Design
No UI. Units of change:
- **knip.json**: add `ignoreDependencies` (`@reportportal/agent-js-jest`, `@reportportal/agent-js-playwright`, `lint-staged`, `eslint-config-react-app`, plus any verified-config-only package), `rules`/`ignoreIssues` for `duplicates`, and `entry` additions only for files proven to be live. Keep `ignore: ["src/api/generated/**"]`.
- **Dead files**: delete `useManualCatalogRefresh.ts`, `useRecalculatePurchasePrice.ts`, `authConfig.ts`, `weatherIcons.tsx` (the `.ts` twin stays), `KnowledgeBaseAskTab.tsx`, `KnowledgeBaseSearchTab.tsx`, `CreateManufactureOrderModal.tsx`, `featureFlags.ts`, `useFeatureFlag.ts` after per-file verification and a check of `docs/development/feature-flags.md`.
- **package.json**: remove verified-unused `ajv`, `web-vitals`, `@types/react-router-dom`, `jest-junit`; add `jest-environment-jsdom` to devDependencies at a version matching the jest in use by react-scripts.
- **Exports**: per-file de-export/delete in `src/api/hooks/*` and `src/features/changelog/*`.
- **CI**: remove `continue-on-error: true` on the knip step when clean.

## Data Schemas
None.
