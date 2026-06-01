Implementation complete. Here's a summary of what happened:

**Recon first**: The prior commit (`680ad577`) had already addressed most arch-review gaps — header merge order (auth wins), JSDoc expansion, auth-header-wins unit test, and docs update. Only Task 4 remained.

**What was implemented**: In `frontend/src/api/__tests__/authenticated-api-usage.test.ts`, the second test's `hasPlainFetch` logic was restructured:
- Replaced two implicit inline exclusions (`!content.includes("(apiClient as any).http.fetch")`) with a named `hasLegacyAsAnyFetch` variable — making the exemption explicit and documented
- Added `hasForbiddenCast` detection for `(apiClient as any)`, `as any).http`, `as any).baseUrl` patterns
- Gated: `hasForbiddenCast && !hasLegacyAsAnyFetch` — catches new hooks introducing forbidden casts, but preserves passing status for 32 legacy hooks (which all use `.http.fetch` AND `getAuthenticatedApiClient`)
- Third test's error message now links to `docs/development/api-client-generation.md`

**Concern**: Node.js/npm was not available in this environment, so test execution couldn't be verified. The logic was traced manually and reviewed by a spec-compliance subagent (PASS). Tests must be run by the developer before merging.