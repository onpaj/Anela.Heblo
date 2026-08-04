# Architecture Assessment — StatusBar: replace `as any` config fetch with `useConfigurationQuery`

## Verdict
Approve the plan/design as specified. This is a same-file, same-behavior refactor with no new architectural surface — the "architecture" here is entirely about not letting the fix silently change behavior or reach for a bigger cleanup than the finding calls for. One correction to the design's stated rationale (below) and one out-of-scope observation worth recording for the backlog; neither blocks implementation.

## Alignment with existing patterns
Verified directly against source, not assumed:

- **`api/hooks/*.ts` is the established convention** for wrapping `apiClient.<x>_<Method>()` in `useQuery`. `useConfiguration.ts` already follows it correctly (confirmed: `configuration_GetConfiguration()` exists in the generated client at `frontend/src/api/generated/api-client.ts:2578` and returns `Promise<GetConfigurationResponse>` with `version?`, `environment?`, `useMockAuth?`, `timestamp?` — all optional, matching the design's fallback logic). At least five other hooks in `api/hooks/` follow the same `useQuery`-wrapping-generated-method shape, so consuming `useConfigurationQuery` from `StatusBar` is the codebase's normal pattern, not a new one.
- **`getAuthenticatedApiClient` is actually synchronous** (`frontend/src/api/client.ts:276`, returns `ApiClient` not `Promise<ApiClient>`), yet both `useConfiguration.ts` and `useHealth.ts` `await` it. This is harmless (`await` on a non-promise resolves immediately) and is an existing codebase convention — do not "fix" it as part of this change; it's out of scope and touches a shared file.
- **CSS/rendering/prop shape is untouched** — plan and design correctly scope the change to the fetch mechanism and `appInfo` derivation only, leaving the JSX return block (lines 96–248 of current file) alone.

## Correction to the design doc
Design (`design-01.md:97`) states the change means "a non-200 response now surfaces as a thrown/rejected promise ... rather than the old code's `if (response.ok)` check." This is correct, but the design undersells one behavioral difference worth the implementer's attention: the generated client's `configuration_GetConfiguration()` throws via NSwag's `throwException` helper on non-2xx, which TanStack Query surfaces as `isError: true` with `retry: 1` — meaning **on a real backend error, the component now issues two requests (initial + one retry) before settling**, versus one attempt today. This is very unlikely to be user-visible (health-check hooks already retry once, same pattern), but flag it in the PR description so it isn't mistaken for a regression during review.

## Out-of-scope finding worth recording (not to action now)
`frontend/src/api/hooks/useHealth.ts:19-37` (`fetchHealthStatus`, backing `useLiveHealthCheck`/`useReadyHealthCheck`, both consumed by this same `StatusBar` component) uses the **identical `as any` / `.baseUrl` / `.http.fetch` pattern** this finding is fixing. Confirmed this is not a candidate for the same fix: `/health/live` and `/health/ready` are ASP.NET Core Health Checks middleware endpoints, not under `/api/`, and are not present in the generated OpenAPI client (`grep` of `api-client.ts` shows no `health` route besides an unrelated `diagnostics_Health()` file-download endpoint). So there is no typed method to switch to — the raw fetch is the correct approach there today. Worth a backlog note that if health endpoints are ever exposed via a typed contract, `useHealth.ts` would become the third instance of this pattern-family; not a blocker for this task, and do not touch it here.

## Implementation guidance
The plan (`plan-01.md`) and design (`design-01.md`) are implementation-ready as written. Concretely, in `frontend/src/components/StatusBar.tsx`:

1. Replace the `useState`/`useEffect`/manual-fetch block (lines 19–24 and 39–90) with:
   ```ts
   const { data: configData, isLoading } = useConfigurationQuery();

   if (isLoading) {
     return null;
   }

   const config = getRuntimeConfig();

   const appInfo = {
     version: configData?.version || process.env.REACT_APP_VERSION || "0.1.0",
     environment:
       configData?.environment ?? (config.useMockAuth ? "Development" : "Production"),
     apiUrl: config.apiUrl,
     mockAuth: configData?.useMockAuth ?? config.useMockAuth,
   };
   ```
2. Update imports: add `useConfigurationQuery` from `../api/hooks/useConfiguration`; drop `getAuthenticatedApiClient`; drop `useState`/`useEffect` from the `react` import (confirm neither is used elsewhere in the file before removing — a quick grep of the file after editing is sufficient, no need for a broader search).
3. Leave everything from `if (!appInfo) return null;` (current line 92) onward untouched — it already only reads the four `appInfo` fields, whose shape is unchanged.
4. Preserve the `||` vs `??` split exactly as specified in the design (`version` uses `||` to treat `""` as absent; `environment`/`mockAuth` use `??` so an explicit `false`/non-empty value from the backend is never overwritten by fallback logic). This is a correctness requirement — get it backwards and a real non-mock, non-default environment could silently mis-render.
5. Drop the `console.warn` calls per the design's resolution of Open Question 2 — matches the no-logging convention already used by the sibling `useLiveHealthCheck`/`useReadyHealthCheck` hooks in this same component.

No changes to `useConfiguration.ts`, the generated client, or any backend code.

## Risks and mitigations
- **Risk: double-request-before-error changes retry timing under a broken backend.** Mitigation: none needed functionally (fallback values are identical either way); just note it in the PR description per above so a reviewer doesn't flag it as unexpected.
- **Risk: removing `useState`/`useEffect` import when still needed elsewhere in the file.** Mitigation: verify with a targeted read of the final file (not a broader search) before removing the import — the file has no other `useState`/`useEffect` usage today, so this should be a non-issue, but confirm post-edit.
- **Risk: no existing test coverage for `StatusBar`** (confirmed — no matches for `StatusBar` under `frontend/test/` or any `*.test.*` file in `frontend/src/`). A regression in the fallback path (e.g., `??` accidentally written as `||`) would not be caught by CI. Mitigation: this is a call for the implementer/reviewer, not a hard blocker — given the component is a footer-only, low-risk display element and the change is small and mechanical, a unit test is a nice-to-have, not a prerequisite. If added, it should cover: (a) success path renders backend `version`/`environment`/`useMockAuth`, (b) error/loading-settled path renders local fallback, (c) `useMockAuth: false` from the backend is respected and not overridden by the local fallback (the `??` correctness case).
- **Risk: scope creep into `useHealth.ts`.** Mitigation: explicitly out of scope per above — do not touch it in this PR.

## Prerequisites before implementation
None. `useConfigurationQuery` and `GetConfigurationResponse` already exist, are already correct, and require no changes. This can proceed straight to implementation.
