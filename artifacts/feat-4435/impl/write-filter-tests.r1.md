# write-filter-tests r1

Created `frontend/src/components/manufacture/list/__tests__/ManufactureOrderFilters.test.tsx` (10 tests).
Covers: full-field mapping with Date objects, Enter-to-apply, all-null request, product code clear -> null,
state select mapping, manual-action select mapping, collapsed summary, tri-state toggle stale-closure
behaviour (null, true, false applied one click late; documented, issue 4435), "Aktivni filtry" summary, reset.

Results: 10/10 pass; ManufactureOrderFilters.tsx lines 97.43% (threshold 60%). `npm run build` OK.
ESLint clean for the new file (repo-wide lint has pre-existing errors in unrelated files).
Deviation: react-scripts test (not raw jest) is required; `npm ci --legacy-peer-deps` needed for install.
