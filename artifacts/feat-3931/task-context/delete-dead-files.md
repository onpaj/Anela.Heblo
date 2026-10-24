### task: delete-dead-files

Delete files with zero consumers after per-file verification (FR-1).

Files (delete, only after verification): `useManualCatalogRefresh.ts`, `useRecalculatePurchasePrice.ts`, `authConfig.ts`, `src/components/customer/cooling/weatherIcons.tsx`, `KnowledgeBaseAskTab.tsx`, `KnowledgeBaseSearchTab.tsx`, `CreateManufactureOrderModal.tsx`, plus any other file in the fresh baseline's "Unused files" section. Locate exact paths with `git ls-files | grep <name>`. Leave the feature-flag files to the next task.

- [ ] For each file, grep `src`, `test`, `docs`, `public` for the basename and path, including `import(`, `React.lazy`, `require(`, and string references. Also grep the matching test file (`*.test.ts(x)`).
- [ ] For `weatherIcons.tsx`: confirm `./weatherIcons` resolves to `.ts` (extension order in tsconfig/CRA: `.ts` before `.tsx`) and that no consumer needs JSX exports unique to the `.tsx` twin. Diff the two files' export lists first.
- [ ] `git rm` each verified-dead file and its own test/story/mocks, if any.
- [ ] If a file has a live consumer or is docs-stated API, do not delete; add it to `knip.json` `entry` and note it for the PR description.
- [ ] Run build, lint, tests, knip. Unused-files section must be empty (or only the justified kept entries).
- [ ] Commit: `chore(frontend): remove dead files flagged by knip (#3931)`.

Verify: knip "Unused files" empty; build/lint/tests green; no orphaned test.

