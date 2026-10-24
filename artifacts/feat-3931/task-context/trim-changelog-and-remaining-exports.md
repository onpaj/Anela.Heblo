### task: trim-changelog-and-remaining-exports

Reduce remaining unused exports/types (FR-4).

Files: `frontend/src/features/changelog/*` and any other non-generated files left in knip's export/types sections.

- [ ] Remove dead barrel re-exports in `src/features/changelog` that nothing imports; de-export or delete symbols per the same rule as above.
- [ ] Re-run knip. For any residual that is genuinely required (public API consumed outside knip's view), add a narrow `ignoreIssues`/`ignoreExportsUsedInFile` entry in `knip.json` with the reason noted for the PR description. Do not add blanket ignores.
- [ ] Commit: `refactor(frontend): trim unused changelog and remaining exports (#3931)`.

Verify: knip exports and types sections empty or only explicitly allow-listed.

