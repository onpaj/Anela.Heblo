# feature-flag-files r1 -- implementation summary

Decision: KEEP both files. docs/development/feature-flags.md names `frontend/src/features/feature-flags/featureFlags.ts` (Step 3 mirror) and `useFeatureFlag(FeatureFlagKeys...)` as the intended frontend API; backend test FeatureFlagRegistryFrontendMirrorTests reads featureFlags.ts. Added both to `frontend/knip.json` `entry`, so they are reserved public API awaiting first consumers. No docs change needed.

Verification: `knip` no longer reports unused files; build passes; lint 267 problems (baseline-equal); tests 422 suites / 3738 passed, 5 skipped.

## PR Summary
Resolved the last two knip unused-file findings by keeping the documented feature-flag frontend API and registering it as knip entries.

### Changes
- `frontend/knip.json` -- added featureFlags.ts and useFeatureFlag.ts to `entry`

## Status
DONE
