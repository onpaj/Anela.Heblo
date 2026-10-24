### task: trim-hook-exports

Reduce unused exports/types in `src/api/hooks/*` (FR-4).

Files: `frontend/src/api/hooks/*` entries listed by knip.

- [ ] From fresh knip output, list unused exports/types in `src/api/hooks/*`. Work in batches of about 10 files.
- [ ] For each symbol: if used locally, drop `export`; if unused entirely, delete it (and any import left unused). Keep `export` for anything another file or test imports.
- [ ] After each batch run `npx tsc --noEmit` (or `npm run build`) and knip delta.
- [ ] Commit per batch or once: `refactor(frontend): trim unused hook exports (#3931)`.

Verify: no hooks entries remain in knip exports/types sections; build and tests green.

