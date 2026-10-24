### task: feature-flag-files

Resolve `featureFlags.ts` and `useFeatureFlag.ts` (FR-1, docs-gated).

Files: `featureFlags.ts` (locate with `git ls-files | grep -i featureFlags`; path-level check, basename also matches `useFeatureFlagsAdmin.ts`/`client.ts`), `useFeatureFlag.ts`, possibly `docs/development/feature-flags.md`.

- [ ] Read `docs/development/feature-flags.md`. If it names these files as the intended frontend API, keep them and add them to `knip.json` `entry` (record why); otherwise delete.
- [ ] If deleting, grep for path-level and basename imports (distinguish from `useFeatureFlagsAdmin`), remove their tests, and update the doc if it references them.
- [ ] Run build, lint, tests, knip.
- [ ] Commit: `chore(frontend): resolve unused feature-flag files (#3931)`.

Verify: knip no longer lists either file; doc consistent with code.

