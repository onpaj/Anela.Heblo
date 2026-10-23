Regenerated TS client via dotnet build + GenerateFrontendClientManual; enum is now Images|Texts|Videos|Graphics|Other.
MarketingActionModal.tsx: FOLDER_TYPE_OPTIONS, resolveFolderType fallback and new-row default use Images (labels unchanged).
MarketingActionModal.test.tsx: Campaign -> Graphics.
Verification: `react-scripts test src/components/marketing` 42 suites / 462 tests pass. eslint on touched files reports only pre-existing testing-library errors on untouched lines. Full `npm run build` and `tsc` not run clean in this env (node_modules typings parse errors in react-i18next, pre-existing/env).
