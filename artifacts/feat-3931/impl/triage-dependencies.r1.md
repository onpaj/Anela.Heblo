# triage-dependencies r1 -- implementation summary

Removed (verified unused via grep of src, test, config, scripts, workflows): `ajv` (no direct import; still provided transitively by react-scripts, build verified), `web-vitals`, `@types/react-router-dom` (react-router-dom 7.18.3 ships its own types). Kept `jest-junit` (CI passes `--reporters=jest-junit` and JEST_JUNIT_* env) and added it to knip `ignoreDependencies`.

Added devDependency `jest-environment-jsdom@^27.5.1` (matches jest 27.5.1 from react-scripts). `knip.json` `ignoreDependencies`: @reportportal/agent-js-jest, @reportportal/agent-js-playwright, lint-staged, eslint-config-react-app, jest-junit, jest-environment-jsdom, jsdom (the latter two are referenced only via the `@jest-environment jsdom` docblock). `overrides` untouched.

Verification: knip dependency/devDependency/unlisted/unresolved sections empty; `rm -rf node_modules && npm ci --legacy-peer-deps` succeeds (plain `npm ci` fails at baseline with ERESOLVE, as noted in baseline-knip); build passes; lint 267 problems (baseline-equal); tests 422 suites / 3738 passed, 5 skipped.

## Status
DONE
