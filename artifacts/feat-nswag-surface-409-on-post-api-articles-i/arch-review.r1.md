# Architecture Review: Surface HTTP 409 as a Typed Branch on `articles_SubmitFeedback`

## Skip Design: true

No UI work. Pure backend annotation + NSwag template customization + hook refactor + test rewrite. The consumer (`ArticleFeedbackSection.tsx`) is untouched.

## Architectural Fit Assessment

The feature aligns with the codebase's typed-client convention (verified in `docs/development/api-client-generation.md:217-233` and in the three sibling hooks in `useArticles.ts:130-216`, which already call typed methods). It reverses the helper-based escape hatch introduced in May 2026, restoring `useSubmitArticleFeedbackMutation` to the same pattern as `useListArticlesQuery`, `useGetArticleQuery`, and `useGenerateArticleMutation`.

The integration points are three:

1. **`ArticlesController.SubmitFeedback`** — gains `[ProducesResponseType]` attributes. The action body, MediatR dispatch, and `HandleResponse(...)` are not touched. Low-risk surface change to the OpenAPI document.
2. **`nswag.frontend.json` + new template directory under `backend/src/Anela.Heblo.API/`** — this is the architecturally invasive part. The `Fetch` template currently emits the same `if 200 ... else throwException` skeleton for every operation (verified at `frontend/src/api/generated/api-client.ts:583-599` and 16 other `process*` methods that look identical). A Liquid override applies globally; the safety of FR-2's "byte-for-byte identical for non-matching operations" claim rests entirely on how surgically the override is written.
3. **`useSubmitArticleFeedbackMutation`** — switches from raw `fetch` back to `apiClient.articles_SubmitFeedback(...)`. Aligns with the existing pattern. No `BaseResponse` discrimination plumbing exists yet in the hooks layer, but it's a single `errorCode` comparison — no new utility needed.

`getApiBaseUrl()` / `getAuthenticatedFetch()` stay as documented in `docs/development/api-client-generation.md:217-233`. The architectural shift is reframing them from "the canonical pattern for status-branching hooks" to "an escape hatch for endpoints not yet expressed through the generated client."

## Proposed Architecture

### Component Overview

```
                +-----------------------------+
                |  ArticlesController.cs      |
                |  [ProducesResponseType 200] |
                |  [ProducesResponseType 409] |
                |  (body change only)         |
                +--------------+--------------+
                               |
                               | aspnetcoretoopenapi
                               v
                +-----------------------------+
                |  OpenAPI document           |
                |  POST /api/Articles/{id}/   |
                |    feedback                 |
                |    responses: 200 + 409     |
                |    (both SubmitArticleFeed- |
                |     backResponse)           |
                +--------------+--------------+
                               |
                               | nswag run nswag.frontend.json
                               | + nswag-templates/Client.Class.Process.liquid override
                               v
                +-----------------------------+
                |  api-client.ts (generated)  |
                |  processArticles_Submit-    |
                |    Feedback:                |
                |    status 200 -> fromJS     |
                |    status 409 -> fromJS     |  <-- new typed branch
                |    else      -> throw       |
                +--------------+--------------+
                               |
                               | typed import
                               v
                +-----------------------------+
                |  useSubmitArticleFeedback-  |
                |    Mutation                 |
                |  branch on response.success |
                |    + response.errorCode     |
                +-----------------------------+
```

### Key Design Decisions

#### Decision 1: NSwag template fork vs. simpler alternatives

**Options considered:**

- **(A) Liquid template fork** (spec FR-2). Override `Client.Class.Process.liquid` (or whichever NSwag 14.1 `NSwag.CodeGeneration.TypeScript.Templates` file emits the per-status branches) so that any operation declaring a 4xx response whose body schema equals the 2xx response body schema generates a non-throwing branch with `fromJS(...)`.
- **(B) Catch `SwaggerException` in the hook.** Leave the generator unchanged. In the hook, `try { ... } catch (e) { if (e.status === 409) return SubmitArticleFeedbackResponse.fromJS(JSON.parse(e.response)); throw e; }`. FR-1 stays (409 in the OpenAPI doc, for human/tooling readers), but the typed branch lives at the call site.
- **(C) Post-build TypeScript codemod.** After `nswag run`, run a small Node/TS script that rewrites only the `processArticles_SubmitFeedback` body (driven by a registry of `{ method, status }` pairs co-located with the script). No Liquid fork.

**Chosen approach:** (A), as the spec dictates, with strict guardrails (see Risks and Mitigations and the implementation guidance below).

**Rationale:** (A) is the only approach that produces a fully typed return value (not via exception unwrap) AND surfaces the 409 contract at the type level for future similar endpoints (`LeafletFeedbackAlreadySubmitted = 2503`, `ArticleAlreadyGenerated = 2405` — see `ErrorCodes.cs:259,270`) without per-call-site boilerplate. (B) is simpler but bakes exception-as-control-flow back in (the very pattern the brief is trying to remove the *fragility* of, not just rename). (C) trades a Liquid fork for a Node script we'd own forever and that drifts when NSwag's emitted method shape changes — same maintenance class as (A) but worse, because Liquid changes are visible diffs while AST/regex codemods can silently mis-match.

**Caveat to document in the spec:** if the Liquid fork turns out to be more invasive than expected during implementation (e.g. the relevant template is monolithic and overriding it pulls in unrelated code), the implementer should escalate rather than ship a broad fork; (B) is then the safe fallback. The byte-for-byte equality acceptance criterion in FR-2 IS the trip-wire for this escalation.

#### Decision 2: Discriminator placement

**Options considered:** introduce a new `SubmitArticleFeedbackResult` union with a discriminator field; or read `response.success === false && response.errorCode === 2407` directly in the hook.

**Chosen approach:** the latter (the spec's choice).

**Rationale:** `BaseResponse.success` + `errorCode` is the existing project-wide discriminator (already consumed by `extractErrorMessage` in `client.ts:226-237`). Introducing a parallel typed union just for this hook fragments the convention. The runtime check is one line; the type-level signal is the shared `SubmitArticleFeedbackResponse`.

#### Decision 3: Template scope — limit to "4xx with body schema equal to 2xx body schema"

**Options considered:** broaden the template to emit a typed branch for **any** explicitly-declared 4xx; or restrict to the exact "body schema matches 2xx" predicate.

**Chosen approach:** restrict to the schema-equality predicate.

**Rationale:** Many controllers in this codebase will eventually annotate 404/422/403 paths with `[ProducesResponseType]`. Those paths return error envelopes that may or may not share the 2xx DTO shape. Without the equality predicate, every annotated 4xx becomes a typed non-throwing branch — silently converting genuine error paths into success paths in the React Query layer. The predicate keeps the typed-branch behavior opt-in by virtue of the *backend's* choice to return the same DTO on the failure path. This is the existing design intent on `SubmitFeedback` (and `LeafletController.SubmitFeedback`, `KnowledgeBaseController.SubmitFeedback`), and the spec's out-of-scope list correctly excludes 404/403/422.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.API/
├── nswag.frontend.json                  # add "templateDirectory": "nswag-templates"
├── nswag-templates/
│   ├── README.md                        # explain override rationale + verification steps
│   └── Client.Class.Process.liquid      # minimum override; copy from NSwag 14.1 + diff
└── Controllers/
    └── ArticlesController.cs            # add 2 [ProducesResponseType] above SubmitFeedback

frontend/src/api/
├── client.ts                            # JSDoc tweak only on getAuthenticatedFetch
├── hooks/
│   └── useArticles.ts                   # remove getApiBaseUrl/getAuthenticatedFetch imports; rewrite mutationFn
├── hooks/__tests__/
│   └── useArticles.test.ts              # rewrite the third describe block; leave first two untouched
└── generated/
    └── api-client.ts                    # regenerated; verify diff scope per FR-2

docs/development/
└── api-client-generation.md             # update lines 217-233 (canonical-example reference)
```

### Interfaces and Contracts

**Backend — unchanged code, new metadata.** `SubmitFeedback` action signature, MediatR dispatch, and `HandleResponse(...)` call are not modified. The action gains exactly two `[ProducesResponseType]` attributes (200, 409), both typed `SubmitArticleFeedbackResponse`. No 404/403/422 annotations in this change (per the spec's out-of-scope list and Decision 3 above).

**NSwag template — the contract for the override.** The Liquid override MUST:

- Live at `backend/src/Anela.Heblo.API/nswag-templates/` and be referenced by *relative path* from `nswag.frontend.json` (`"templateDirectory": "nswag-templates"`). NSwag.MSBuild resolves it from the API project working directory (`<Exec WorkingDirectory>` in the `.csproj`).
- Override exactly one Liquid file (the one that emits `processX(response)` method bodies). Identify the file by running `nswag run` with `--templateDirectory` pointing at a clone of the NSwag 14.1 default templates and diffing.
- Be functionally a no-op for any operation that does not declare a 4xx response whose body schema equals the 2xx body schema. That is: for the vast majority of operations, the emitted method body MUST be byte-for-byte identical to the unmodified output.
- Read the predicate "4xx response with body schema equal to 2xx body schema" from the OpenAPI document model NSwag passes to the template (Liquid has access to the operation's `Responses` collection). Avoid encoding the list of qualifying methods in the template — that would re-create the codemod problem in a different form.

**Frontend hook — same signature, different body.**

```ts
export interface SubmitArticleFeedbackResult {
  alreadySubmitted?: true;
  precisionScore?: number | null;
  styleScore?: number | null;
  feedbackComment?: string | null;
}

export const useSubmitArticleFeedbackMutation:
  (articleId: string) => UseMutationResult<SubmitArticleFeedbackResult, Error, SubmitArticleFeedbackPayload>;
```

unchanged. Internally:

```ts
const client = getAuthenticatedApiClient();
const request = new SubmitArticleFeedbackRequest({ articleId, precisionScore, styleScore, comment });
const response = await client.articles_SubmitFeedback(articleId, request);

if (response.success === false && response.errorCode === ErrorCodes.ArticleFeedbackAlreadySubmitted) {
  return { alreadySubmitted: true };
}
return {
  precisionScore: response.precisionScore ?? null,
  styleScore: response.styleScore ?? null,
  feedbackComment: response.feedbackComment ?? null,
};
```

No `as any`, no `try/catch` (let `SwaggerException` propagate for non-2xx-non-409).

### Data Flow

**Success (200):**
React component → `mutate(payload)` → `articles_SubmitFeedback` → `http.fetch` (via `getAuthenticatedApiClient`'s `authenticatedHttp`) → backend MediatR → `HandleResponse` (sets 200) → response body parsed by `processArticles_SubmitFeedback` → `SubmitArticleFeedbackResponse` instance with `success: true` → hook returns `{ precisionScore, styleScore, feedbackComment }` → React Query `onSuccess` invalidates `articleKeys.detail(articleId)`.

**Already submitted (409):**
React component → ... → backend MediatR returns failure → `HandleResponse` (uses `[HttpStatusCode(Conflict)]` on `ErrorCodes.ArticleFeedbackAlreadySubmitted` to set 409) → response body parsed by **new** `status === 409` branch in `processArticles_SubmitFeedback` → `SubmitArticleFeedbackResponse` instance with `success: false, errorCode: 2407` → hook returns `{ alreadySubmitted: true }` → React Query `onSuccess` invalidates → consumer reads stored scores from the refetched detail.

**Toast suppression on 409.** The `authenticatedHttp.fetch` wrapper in `client.ts:281-356` already reads `response.ok` to decide whether to fire a toast. `response.ok` is `false` for 409, so today's behavior would fire an "Upozornění" toast with the structured error message. Verify in QA: with FR-1 in place, does the 409 still trigger a toast, and is that desirable? The spec asserts "no observable behavior change" (NFR-1) but the current raw-fetch path goes through `getAuthenticatedFetch()`, which does NOT trigger toasts. The new path goes through `getAuthenticatedApiClient()`, which DOES. **This is a behavior regression unless mitigated.** See Specification Amendments below.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| 409 → toast regression: switching from `getAuthenticatedFetch()` (no toast) to `getAuthenticatedApiClient()` (toast on `!response.ok`) makes the 409 response fire a global "Upozornění" toast. | **HIGH** | Treat the toast suppression as a first-class requirement. Either (a) extend `getAuthenticatedApiClient`'s `authenticatedHttp.fetch` to skip the toast when `response.status === 409` AND the parsed body is a `BaseResponse` with `errorCode` decorated `HttpStatusCode.Conflict` (project-wide; benefits future endpoints), or (b) pass `showErrorToasts: false` to a dedicated client instance used by this hook only. Option (a) is closer to "no observable behavior change" globally; option (b) is the minimal-blast-radius fix. Spec must pick one before implementation. |
| NSwag Liquid template override is fragile across NSwag upgrades (14.1 → 14.x → 15.x). | MEDIUM | Pin `NSwag.MSBuild` (already at 14.1.0 in `Anela.Heblo.API.csproj:81`). Add a paragraph to `nswag-templates/README.md` noting which NSwag version the override was forked from and the diff command to re-verify on upgrade. Consider adding a CI check that runs `nswag run` twice and asserts the second run produces no diff (FR-2's idempotency criterion). |
| Liquid override changes more methods than intended; byte-for-byte equality breaks silently. | MEDIUM | Take a snapshot of `frontend/src/api/generated/api-client.ts` before the template change. After regeneration, `git diff` MUST show changes only to `processArticles_SubmitFeedback`, `articles_SubmitFeedback` return type, and (if NSwag emits per-status return types) the method signature. If any other `process*` method changes, the predicate is too broad — narrow it before merging. |
| Liquid template predicate ("schema equal to 2xx") incorrectly matches future operations the team doesn't want to convert to typed-non-throwing branches. | MEDIUM | Document the predicate in `nswag-templates/README.md` with a worked example. Add a regression check: after merging, every time a new `[ProducesResponseType(...)]` with a 4xx code is added to any controller, the PR author must explicitly verify the generated `api-client.ts` diff and either accept or work around the template behavior (e.g. by using a wrapper DTO that breaks the schema-equality test). |
| 409 response with empty/null body: the generated `fromJS(null)` for the new branch may produce an instance whose `success` is `undefined`, breaking the hook's discriminator. | LOW | Verify `SubmitArticleFeedbackResponse.fromJS(null)` returns an instance where the discriminator fields are `undefined` (existing `BaseResponse` design — check `api-client.ts` for the class). The hook MUST treat `success === undefined` defensively in the 409 path. Alternatively, the controller MUST always populate the body on 409 (it already does via `HandleResponse(result)` returning the result DTO; verify). |
| Sibling endpoints (`KnowledgeBaseController.SubmitFeedback`, `LeafletController.SubmitFeedback`) will inherit the typed-branch behavior the moment someone adds `[ProducesResponseType(409)]` to them — and their consumer hooks today still use `getAuthenticatedApiClient()` expecting throws on 409. | LOW | Out-of-scope per spec, but call it out in `nswag-templates/README.md` as a known consequence. The pattern is opt-in via the backend annotation, so this only manifests when someone makes the change — at which point they MUST also update the consumer hook. |
| Documentation drift: `docs/development/api-client-generation.md` lines 217-233 cite `useSubmitArticleFeedbackMutation` as the canonical example for the `getApiBaseUrl()` / `getAuthenticatedFetch()` pattern. After this change, that example is no longer accurate. | LOW | Update the doc as part of this PR (see Specification Amendments). |

## Specification Amendments

1. **Add a requirement to address the 409 → toast regression (HIGH severity).** The spec's NFR-1 claims no observable behavior change, but switching from `getAuthenticatedFetch` (toast-free) to `getAuthenticatedApiClient` (toast on `!response.ok`) will surface a toast on the 409 path. Add an explicit FR (e.g. FR-7) selecting one of:
   - **Recommended:** Extend `authenticatedHttp.fetch` in `client.ts:281-356` to skip the toast when the response is a `BaseResponse` whose `errorCode` is decorated `[HttpStatusCode(Conflict)]` AND the 409 body schema equals the 2xx body schema. This is global and benefits future 409-typed-branch endpoints. Constrains: must inspect the cloned response body, which the wrapper already does for `extractErrorMessage`.
   - Alternative: have the hook obtain a no-toast client via `getAuthenticatedApiClient(showErrorToasts: false)` (the parameter exists already at line 276), and accept that any genuine HTTP error (500, etc.) on this endpoint won't surface a toast either. Document the trade-off.

2. **Add an acceptance criterion to FR-5 for `docs/development/api-client-generation.md`.** Lines 217-233 reference `useSubmitArticleFeedbackMutation` as the canonical example for the status-code-branching pattern. After the refactor, that example is stale. Rewrite the section to reframe the helpers as a forward-looking escape hatch (e.g. "for endpoints with status-code branching not yet expressed through the generated client — e.g. HTTP 412 precondition-failed responses") and remove the `useSubmitArticleFeedbackMutation` code sample. Substitute a hypothetical example or simply describe the API without a concrete call site.

3. **Tighten FR-2's "byte-for-byte identical" criterion.** Add: "the verification command is `git diff frontend/src/api/generated/api-client.ts` immediately after regeneration on a tree where only the `nswag-templates/` files and `nswag.frontend.json` have changed; the diff MUST be limited to (a) `articles_SubmitFeedback` return type, (b) `processArticles_SubmitFeedback` method body, and (c) any imports those changes require. Any change to another method is a blocker."

4. **Clarify the predicate in FR-2 / template implementation.** State explicitly: "the predicate is *schema equality to the 2xx response body*, not *any 4xx with a body*. This protects future operations that annotate 404/422/403 with different error envelopes from being silently converted to non-throwing typed branches." (This codifies Decision 3 above.)

5. **Add an escape hatch to FR-2.** If the implementer determines during execution that the Liquid template override cannot be made surgical enough (i.e. the FR-2 byte-equality criterion is unachievable with NSwag 14.1's template structure), the implementer MUST escalate rather than ship a broad override. The fallback is to revert to the helper-based raw-fetch implementation OR switch to a hook-level `try/catch SwaggerException` approach. Both preserve FR-1 (409 in the OpenAPI doc) while abandoning FR-2.

## Prerequisites

- **None blocking.** All required infrastructure exists:
  - `NSwag.MSBuild 14.1.0` and `NSwag.AspNetCore 14.1.0` are referenced in `Anela.Heblo.API.csproj:37,81`.
  - `nswag.frontend.json` has a `templateDirectory: null` field at line 76, ready to be set to `"nswag-templates"`.
  - The `GenerateFrontendClientManual` MSBuild target at `Anela.Heblo.API.csproj:92-99` runs `dotnet nswag run nswag.frontend.json` from the API project directory — relative paths to `nswag-templates/` resolve correctly.
  - The `ErrorCodes.ArticleFeedbackAlreadySubmitted = 2407` enum value at `ErrorCodes.cs:262-263` is already decorated `[HttpStatusCode(HttpStatusCode.Conflict)]` and is exported through the generated TypeScript `ErrorCodes` enum.
  - `BaseResponse` (the discriminator carrier) is the project-wide convention.
- **Decision required before coding starts:** which of the two toast-regression mitigations in Specification Amendment #1 to ship. This is a one-line architectural choice; do not start the hook refactor without it nailed down.