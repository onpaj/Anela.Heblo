The plan file is saved. Per the pipeline note, I skip the execution handoff prompt — the harness will capture the file as the artifact.

**Summary:** Wrote `artifacts/feat-arch-review-article-usesubmitarticlefeed/task-plan.r2.md` covering the **remaining** work the arch-review flagged against the already-landed r1 helpers + hook refactor. The previous round (`impl/main.r2.md`) shipped `getApiBaseUrl()`, `getAuthenticatedFetch()`, the hook refactor, and the three branch tests — but three arch-review gaps remain open. The new plan covers them in 7 tasks:

1. **Task 1** — recon to confirm the gaps are still on disk and record line numbers.
2. **Tasks 2 + 3** — flip the inverted header merge at `client.ts:407` so auth wins over caller, plus two new tests pinning the contract (FR-2 acceptance criterion + arch-review Risk row 1).
3. **Task 4** — tighten `authenticated-api-usage.test.ts` to actively ban `(apiClient as any).http.fetch` / `.baseUrl` in hooks (FR-4 + arch-review Amendment 2 + Risk row 2), with a negative-control sanity check.
4. **Task 5** — rewrite the two `(apiClient as any)` "✅ CORRECT" blocks in `docs/development/api-client-generation.md` to teach the helpers, add an explicit "❌ AVOID" callout (arch-review Amendment 3 + Risk row 3).
5. **Task 6** — expand the `getAuthenticatedFetch` JSDoc to call out the 409-branching use case, the no-toast / no-401-redirect divergence, and the auth-wins precedence (arch-review Risk rows 4–6).
6. **Task 7** — final cross-file consistency check, tsc, jest, lint, build, optional PR.

Each task is independently revertable; only Task 2's one-line spread-order flip changes runtime behaviour, and only for a caller who was already broken (supplying its own `Authorization`).