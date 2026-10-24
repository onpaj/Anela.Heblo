## Review Result: CLEAN

### Summary
Pure relocation of `ExpeditionJobControlsBar`, `formatDateTime` and their tests from `components/pages/ExpeditionListArchive/` to `components/pages/Expedition/`, plus two path updates in `ExpeditionListArchivePage.tsx` and its test. Relative import depth is unchanged, no behaviour change, no remaining references to the old folder. Build, lint (touched files) and the affected jest suites (16 tests) pass.

### Blocking
- None

### Advisory
- `docs/architecture/module-map.md` does not list the new `components/pages/Expedition/` folder under the Logistics module's owned paths; consider adding it separately.
