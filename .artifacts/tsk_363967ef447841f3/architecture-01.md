# Architecture review — Photobank hooks migration off `(apiClient as any)`

## Verdict

**Approved as designed.** plan-01.md / design-01.md are architecturally sound and every load-bearing
claim in them was independently re-verified against the current repository state (not just trusted
from the artifacts). No invariant in `docs/development/api-client-generation.md` or
`docs/architecture/development_guidelines.md` is violated by the proposed design. Proceed to
implementation as specified, with the two small tightenings noted under "Findings" below.

## What was checked, and against what

This is a pure client-side transport-layer refactor (two hook files + three one-line call-site
fixes), so the relevant invariants are narrow and concrete:

1. `docs/development/api-client-generation.md` — Enforcement Rules 1-3 (no relative URLs, use
   `getAuthenticatedApiClient()`, never `(apiClient as any)`) and the documented escape hatches
   (`getApiBaseUrl()`/`getAuthenticatedFetch()`, and the try/catch discriminate-on-status-or-envelope
   pattern for business outcomes modeled as non-2xx).
2. `docs/architecture/development_guidelines.md` — DTOs are classes not records (backend-only
   concern here, unaffected — no backend change in scope).
3. The precedent set by already-migrated hooks (`useBankStatements.ts`/`.test.ts`, cited as the
   re-export and test-mock pattern; `useArticles.ts`'s `useSubmitArticleFeedbackMutation`, cited as
   the canonical try/catch business-outcome pattern).

Verification method: read the actual generated client, the actual backend controller/base
controller, the actual current hook files, the actual consuming components, and the actual test
infra — not just the plan's prose description of them.

## Confirmed against source (no drift from plan/design claims)

- **Every generated method the plan maps to exists with the claimed signature.** Grepped
  `frontend/src/api/generated/api-client.ts` for all 15 `photobank_*` methods
  (`GetPhotos`, `GetTags`, `CreateTag`, `DeleteTag`, `AddPhotoTag`, `RemovePhotoTag`,
  `BulkAddPhotoTag`, `BulkAddPhotoTagByIds`, `RetagPhotos`, `GetRoots`, `AddRoot`, `DeleteRoot`,
  `GetRules`, `AddRule`, `DeleteRule`, `ReapplyRules`) — all present, parameter order and body-class
  shape match FR-1/FR-3's table exactly (e.g. `photobank_GetPhotos(tags, search, useRegex,
  withoutTags, page, pageSize)` positional order matches `GetPhotosParams` field order).
- **`throwException` really does `throw result` when `result` is non-null**
  (`generated/api-client.ts:43198-43203`), confirming the plan's core mechanism claim: the 400 branch
  of `processPhotobank_BulkAddPhotoTag` builds `result400 = ProblemDetails.fromJS(resultData400)`
  and throws it directly, not a wrapping `SwaggerException`.
- **`ProblemDetails.init()` really does copy every raw JSON property before overwriting its five
  declared fields** (`generated/api-client.ts:13799-13811`, `for (var property in _data) this[property]
  = _data[property]` followed by the five named assignments), and the class carries a `[key: string]:
  any` index signature (`:13788`). This is the load-bearing fact behind FR-2/design's translation
  boundary — verified directly, not assumed.
- **The backend 400 body genuinely is the `BulkAddPhotoTagResponse` DTO, not a `ProblemDetails`.**
  `PhotobankController.cs:154-168` annotates `BulkAddPhotoTag` with
  `[ProducesResponseType(typeof(BulkAddPhotoTagResponse), StatusCodes.Status200OK)]` +
  `[ProducesResponseType(StatusCodes.Status400BadRequest)]` (the 400 annotation carries no type —
  confirming the plan's "Open Questions" observation that this isn't yet a typed non-throwing NSwag
  branch) and calls `HandleResponse(response)`. `BaseApiController.HandleResponse`
  (`BaseApiController.cs:28-59`) does `BadRequest(response)` for the `BadRequest` status-code branch —
  `response` is the actual `BulkAddPhotoTagResponse` MediatR result, serialized as-is. So
  `success`/`errorCode`/`params`/`addedCount`/etc. really do arrive in the 400 JSON body and really do
  survive onto the thrown `ProblemDetails` instance at runtime. FR-2's design is not "hopeful" — it's
  a direct, traced consequence of how NSwag's fetch template and this codebase's `BaseResponse`
  serialization actually behave together.
- **BaseResponse's `errorCode`/`params` fields exist exactly as the design assumes**
  (`generated/api-client.ts:13228-13254`) — `errorCode?: ErrorCodes` is a plain number at runtime,
  matching the design's `typeof err.errorCode === "number"` discriminator.
- **`getApiBaseUrl()` and `getAuthenticatedFetch()` both exist in `client.ts`** (`:178`, `:428`) —
  the documented escape hatch the plan correctly declines to use here, since FR-2's approach keeps
  the typed call and only branches in the catch clause.
- **`mockAuthenticatedApiClient`/`createQueryClientWrapper` exist in `testUtils.ts`** and
  `useBankStatements.test.ts` already uses exactly the pattern FR-6/design's Test module boundary
  section describes (`jest.mock("../../client")` + per-method `jest.fn()` mock object +
  `mockAuthenticatedApiClient(mockClient)`) — confirmed by reading both files, not inferred.
- **`useBankStatements.ts` really does re-export generated DTOs via `export type { ... } from
  "../generated/api-client"`** — confirmed at line 14, validating FR-4/design's precedent claim.
- **`useArticles.ts`'s `useSubmitArticleFeedbackMutation`** (`:213-244`) is a real, already-shipped
  try/catch business-outcome hook, discriminating on `err.status` rather than the response envelope —
  a different discriminator than FR-2 uses, but the doc explicitly sanctions both ("discriminate on
  the exception status **or** the existing `BaseResponse.success` + `errorCode` envelope"), so FR-2's
  choice of the envelope-based discriminator is a documented, not improvised, alternative.
- **All three FR-5 call sites and both "no change needed" `Date`-constructor call sites match line
  numbers and code exactly**: `PhotoGrid.tsx:111`, `PhotoList.tsx:119,146`, `PhotoDrawer.tsx:92,105`,
  `IndexRootsTab.tsx:84-85` (guarded with a truthiness check before `new Date(...)`, which continues
  to work correctly against a `Date | undefined` value). `PhotoThumbnail.tsx`'s `modifiedAt: string`
  prop (`:8`) is confirmed untouched by the plan, consistent with FR-5's "convert at call sites, don't
  widen the shared prop" decision.
- **`BulkTagDialog.tsx`'s actual field reads** (`result.success`, `result.tagName`,
  `result.addedCount`, `result.alreadyTaggedCount`, `result.errorCode === 2606`,
  `result.params?.Count`/`result.params?.Limit`) match `BulkAddPhotoTagResult`'s shape field-for-field
  — the translation boundary in FR-2/design fully covers this consumer's contract with zero component
  changes, as claimed.
- **Generated `AddRootBody.displayName?: string | undefined`** vs. hook-local
  `AddIndexRootInput.displayName: string | null` — confirmed the exact type mismatch FR-3 calls out,
  correctly scoped to a one-line normalization inside the hook rather than touching
  `IndexRootsTab.tsx`.

## Findings (non-blocking, worth folding into implementation)

1. **Generated `photobank_UpdateRule(id, body)` and `photobank_ReapplyRule(id)` exist but are
   correctly left out of scope.** Grep confirms these two generated methods
   (`generated/api-client.ts:10557`, `:10704`) have no current hand-rolled hook counterpart in either
   file — there is no `useUpdateTagRule` or per-rule reapply hook today. The plan's method table
   doesn't reference them, which is correct: this task migrates existing hooks, it doesn't add new
   ones. No action needed, just confirming the plan didn't silently drop functionality — it didn't.
2. **FR-2's catch-block type guard should assert on `err` being non-null before touching
   `.success`.** The design's sketch (`err && err.success === false && ...`) already includes a
   truthiness guard on `err`, which is correct and necessary — a network-level `TypeError: Failed to
   fetch` (not a `SwaggerException`/`ProblemDetails`) would otherwise crash the guard itself on
   property access. Confirm the dev step keeps that `err &&` guard verbatim; it's easy to drop by
   accident when translating the design's prose into code, and dropping it turns a clean rethrow into
   a `TypeError` inside the `catch` block itself, which is a worse failure mode than what's being
   fixed.
3. **The 403 branch's empty response body is the correct silent-rethrow path, but only because
   `ProblemDetails.fromJS(null)` produces an object with no `success` property at all** (rather than
   `success: undefined` colliding with the `=== false` check in a way that could accidentally match).
   Traced this precisely: `BaseApiController.HandleResponse`'s `Forbidden => Forbid()` branch returns
   no body, so `_responseText === ""` → `resultData403 = null` → `ProblemDetails.fromJS(null)` still
   runs `init({})` (guarded by `typeof data === 'object' ? data : {}`), meaning `for (var property in
   {})` copies nothing — the thrown object has no own `success` property, so `err.success === false`
   is `false` (not throwing on `undefined`), and the catch correctly falls through to `throw e`. This
   is exactly the behavior FR-2/design assume ("Rethrow anything that isn't a recognizable
   `{success:false, errorCode:number}` shape ... 403"); worth a one-line comment in the implementation
   pointing at this trace so a future reader doesn't need to re-derive it.

## Risks already correctly identified and appropriately scoped by the plan

- **The FR-2 mechanism relies on an implementation detail of generated code** (that `ProblemDetails`
  preserves unknown JSON properties via its index signature) **rather than a typed contract.** The
  plan already flags this in "Open Questions" as an accepted, documented risk with a concrete
  follow-up (annotate the 400 response with `[ProducesResponseType(typeof(BulkAddPhotoTagResponse),
  StatusCodes.Status400BadRequest)]` once the NSwag template override is active). This assessment
  concurs: it is the correct call for *this* task's scope — the alternative (falling back to
  `getApiBaseUrl()`/`getAuthenticatedFetch()` for this one endpoint) would reintroduce exactly the
  private-field-adjacent fragility this task exists to remove, in exchange for avoiding a *different*,
  arguably smaller fragility. Keep it flagged as a follow-up issue; do not block this task on it.
- **No backend change is in scope**, so this task cannot regress backend behavior — confirmed the
  plan/design never touch `PhotobankController.cs` or any handler.

## Implementation guidance (unchanged from plan/design, restated for the dev step)

- Follow plan-01.md's FR-1 through FR-6 and design-01.md's method-mapping tables as written — both
  are verified accurate against the current generated client, controller, and consuming components.
- Preserve the `err &&` truthiness guard and the `typeof err.errorCode === "number"` check verbatim
  in FR-2's catch block (Finding 2).
- No new abstraction, module, or shared helper is warranted — the generated `ApiClient` class is
  already the correct typed-HTTP-boundary component; these hooks remain thin adapters, matching
  `useBankStatements.ts`'s existing role for the Bank module.

## Prerequisites before implementation begins

None. No open question blocks starting FR-1 through FR-6; the one open question (typed 400 branch)
is explicitly deferred to a follow-up issue by the plan and this review concurs.
