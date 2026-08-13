# Architecture Review: Route Smartsupp frontend hooks through the generated typed API client

## Skip Design: true

No new or changed UI components, screens, layouts, or visual design decisions. This is a pure internal-plumbing/typing migration behind five existing hooks with unchanged exported signatures; NFR-2 explicitly requires byte-identical caching, optimistic-update, and user-facing (Czech error message) behavior, and "Any UI/UX behavior change" is explicitly Out of Scope in the spec. Confirmed against the actual component tree (`ConversationDetail.tsx`, `MessageBubble.tsx`, `ConversationListItem.tsx`, `ContactDetailsPanel.tsx`, `ShoptetCustomerCard.tsx`, `ConversationList.tsx`) — these consume the hooks' return values but are not touched by the spec's FRs beyond the mechanical type-signature fallout described below.

## Architectural Fit Assessment

This is a textbook instance of an already-established, repeatedly-executed pattern in this codebase: retire a hand-rolled fetch escape hatch in favor of the NSwag-generated `{feature}_*` client methods, per `docs/development/api-client-generation.md`. It is not a new architectural pattern — it is the *n*-th application of one, and the codebase already contains two directly-applicable precedents that should be followed literally rather than reinvented:

1. **`frontend/src/api/hooks/useArticles.ts`** (`useSubmitArticleFeedbackMutation`) — the canonical `try/catch`-on-typed-exception pattern for a 409 business-outcome status, referenced by both the doc and the spec. Verified: it calls `client.articles_SubmitFeedback(...)` inside `try/catch`, checks `err.status === 409`, returns `{ alreadySubmitted: true }` on that branch, rethrows otherwise. This is a structural match for FR-5 (`useSubmitDraftReplyFeedback.ts`) and should be copied near-verbatim.
2. **`frontend/src/api/hooks/useKnowledgeBase.ts`** (`useGetFeedbackListQuery` / `toLocalFeedbackListResponse`) — an *exact* precedent for FR-6 that the spec does not cite (it only cites `usePackingMaterials`/#3221 and PR #3857). The KnowledgeBase feedback-list endpoint returns the **same generated `RagFeedbackLogSummary`/`RagFeedbackStatsDto` classes** as `smartsupp_GetDraftReplyFeedbackList` (both back the same `RagInteractionLogs` table per the comment in `ragFeedbackTypes.ts`), and `useKnowledgeBase.ts` already contains a `toLocalFeedbackListResponse()` mapping function that converts the generated `Date`/`undefined`-shaped DTO into the hand-maintained `ragFeedbackTypes.ts` shape (`string` dates, `null` for absent fields) that `RagFeedbackDetailExtra.tsx` and other consumers assume. FR-6 must extract/reuse this mapper rather than write a parallel one — see Decision 2 below.

The other four call sites (`useSmartsupp.ts`'s four exported hooks, `useGenerateDraftReply.ts`, `useSendMessage.ts`) map cleanly onto the plain `getAuthenticatedApiClient()` pattern demonstrated by `usePackingMaterials.ts` (thin `queryFn`/`mutationFn` wrapping a single generated method call, no manual URL building, no manual JSON parsing).

**Confirmed backend readiness**: every `smartsupp_*` generated method the spec requires already exists in `frontend/src/api/generated/api-client.ts` (verified at the actual current line numbers, not the stale ones in the original finding) — no backend change is required to unblock this work, and the `SmartsuppController.cs` `ProducesResponseType` annotations match what the spec describes.

**One factual correction to the spec**: FR-5's rationale states the controller "already dual-annotates 200/409 with the same DTO." Reading `SmartsuppController.cs:124-129` directly: `SubmitDraftReplyFeedback` is annotated `[ProducesResponseType(typeof(SubmitDraftReplyFeedbackResponse), StatusCodes.Status200OK)]` + `[ProducesResponseType(StatusCodes.Status409Conflict)]` — the 409 annotation is **untyped**, not dual-annotated with the same DTO (unlike the `useSubmitArticleFeedbackMutation` example in the doc, whose backend action genuinely is dual-annotated). This does not change the implementation: the `try/catch`-on-`err.status` pattern is agnostic to whether the 409 is typed, since it never touches the exception's payload, only `.status`. Flagged under Specification Amendments — no code impact.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│  frontend/src/api/hooks/useSmartsupp.ts                              │
│  useSmartsuppConversations / useSmartsuppConversation /              │
│  useSmartsuppShoptetInfo / useSmartsuppVisitorInfo /                 │
│  useCloseConversation / usePresenceHeartbeat / otherActiveViewers    │
│                                                                        │
│    ├─ getAuthenticatedApiClient() ──► smartsupp_GetConversations     │
│    ├─ getAuthenticatedApiClient() ──► smartsupp_GetConversation      │
│    ├─ getAuthenticatedApiClient() ──► smartsupp_CloseConversation    │
│    ├─ getAuthenticatedApiClient() ──► smartsupp_RecordPresence       │
│    ├─ getApiBaseUrl()+getAuthenticatedFetch() ──► DELETE .../presence│
│    │    (keepalive escape hatch — see Decision 3)                    │
│    └─ try/catch on typed call ──► smartsupp_GetShoptetInfo /         │
│         smartsupp_GetVisitorInfo (404 → null; see Decision 1)        │
└───────────────────────────┬────────────────────────────────────────┘
                             │ re-exports ConversationDto, MessageDto,
                             │ GetConversationResponse, SMARTSUPP_QUERY_KEYS
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│  frontend/src/components/customer-support/smartsupp/hooks/           │
│                                                                        │
│  useGenerateDraftReply.ts   ──► smartsupp_GenerateDraftReply          │
│  useSendMessage.ts          ──► smartsupp_SendMessage                 │
│                                  (imports MessageDto/                 │
│                                   GetConversationResponse from         │
│                                   useSmartsupp.ts, per current layout) │
│  useSubmitDraftReplyFeedback.ts ──► smartsupp_SubmitDraftReplyFeedback│
│                                  (try/catch on 409, Decision 2 pattern)│
│  useSmartsuppDraftReplyFeedbackListQuery.ts                           │
│      ──► smartsupp_GetDraftReplyFeedbackList                          │
│      ──► toLocalFeedbackListResponse()-style mapper (Decision 2)      │
│      ──► ragFeedbackTypes.ts (RagFeedbackLogSummary/Stats, unchanged) │
└─────────────────────────────────────────────────────────────────────┘

DELETED:
  frontend/src/api/smartsuppClient.ts (asInternal / getClientAndBaseUrl /
                                        apiGet / apiPost / apiDelete)
```

All six generated-client entry points funnel through the same `ApiClient` instance obtained via `getAuthenticatedApiClient()` (already how every other migrated feature — `useArticles.ts`, `useKnowledgeBase.ts`, `usePackingMaterials.ts` — talks to the backend). The one exception is `usePresenceHeartbeat`'s unmount `DELETE`, which cannot go through the generated client at all (see Decision 3) and must use `getApiBaseUrl()` + `getAuthenticatedFetch()` — the documented escape hatch, used for exactly the reason the doc names it for: a transport option the generated method's public surface doesn't expose.

### Key Design Decisions

#### Decision 1: 404-as-null for Shoptet/visitor info — escape hatch, not typed try/catch

**Options considered:**
- (a) `getApiBaseUrl()` + `getAuthenticatedFetch()`, keeping the current manual `response.status === 404 → null` check (spec's FR-2 path (a)).
- (b) Call `smartsupp_GetShoptetInfo`/`smartsupp_GetVisitorInfo` directly, `try/catch`, return `null` when `err.status === 404` (spec's FR-2 path (b)).

**Chosen approach:** (a), the escape hatch — do **not** additionally annotate the controller with a typed 404.

**Rationale:** The nswag-templates README (`backend/src/Anela.Heblo.API/nswag-templates/README.md`) confirms the (currently unwired) template-override predicate is hardcoded to fire **only for HTTP 409**, deliberately excluding 404 (to avoid colliding with unrelated same-shaped 404s elsewhere, e.g. `FeatureFlagsController`). Annotating `GetShoptetInfo`/`GetVisitorInfo` with a typed 404 therefore buys nothing today — the generated client would still throw via `ProblemDetails.fromJS(...)` regardless, since the override that would change that behavior is scoped to 409 only and isn't active. Path (b)'s `try/catch` would work functionally, but it's strictly more code than path (a) for zero benefit (no typed exception payload to gain from), and it's a backend annotation change with no corresponding template payoff — the kind of speculative change `docs/api-client-generation.md`'s "escape hatch" callout exists to avoid. Use path (a) for both hooks; do not touch `SmartsuppController.cs`'s `ProducesResponseType` set for these two actions.

#### Decision 2: FR-6's feedback list reuses the `useKnowledgeBase.ts` mapping-layer pattern, not a fresh one

**Options considered:**
- Type `useSmartsuppDraftReplyFeedbackListQuery`'s `queryFn` return directly as the generated `GetDraftReplyFeedbackListResponse` and let TypeScript widen `ragFeedbackTypes.ts` consumers to accept `Date`/`undefined`.
- Write a new local mapping function inside `useSmartsuppDraftReplyFeedbackListQuery.ts` that converts the generated `RagFeedbackLogSummary`/`RagFeedbackStatsDto` classes into the `ragFeedbackTypes.ts` shape.
- Extract `useKnowledgeBase.ts`'s existing `toLocalFeedbackChunk`/`toLocalFeedbackListResponse` functions into a shared module and call them from both hooks.

**Chosen approach:** Extract the existing mapper into a shared location (e.g. `frontend/src/components/feedback/ragFeedbackMapping.ts`, next to `ragFeedbackTypes.ts`) and have both `useKnowledgeBase.ts` and `useSmartsuppDraftReplyFeedbackListQuery.ts` call it, passing each generated response's own `logs`/`stats` fields.

**Rationale:** `smartsupp_GetDraftReplyFeedbackList` and `knowledgeBase_GetFeedbackList` return the **identical generated classes** (`RagFeedbackLogSummary`, `RagFeedbackStatsDto`) — confirmed by reading both generated response classes' field lists in `api-client.ts`. `ragFeedbackTypes.ts`'s own header comment ("Shared shape ... returned by both the KnowledgeBase and Smartsupp draft-reply feedback-list endpoints") already documents this as one shared contract. Not reusing the mapper means two independently-maintained copies of the same `Date→string`/`undefined→null` conversion logic for the same generated types — exactly the duplication this whole migration exists to eliminate. This is also lower implementation risk than writing a fresh mapper: the existing one is already correct and tested indirectly via KnowledgeBase's usage.
Field-by-field parity check performed (NFR-1): `GetDraftReplyFeedbackListResponse.{logs, totalCount, pageNumber, pageSize, totalPages, stats}` line up 1:1 with the hand-declared `DraftReplyFeedbackListResponse`'s fields; `RagFeedbackLogSummary`'s generated fields line up 1:1 with `ragFeedbackTypes.ts`'s `RagFeedbackLogSummary` interface (both already enumerated field-by-field in `useKnowledgeBase.ts`'s existing mapper). No incompatibility found — the "flag as open question if incompatible" branch in FR-6's acceptance criteria does not trigger.

#### Decision 3: `usePresenceHeartbeat`'s unmount `DELETE` stays on the escape hatch permanently, not the typed client

**Options considered:**
- Call `smartsupp_RemovePresence(id)` (typed) for both the interval heartbeat's implicit "leave" and the unmount leave.
- Keep the unmount `DELETE` on `getApiBaseUrl()` + `getAuthenticatedFetch()` (with `keepalive: true`), typed client for everything else in the hook.

**Chosen approach:** the latter — resolves the spec's Open Question definitively rather than leaving it as a "check at implementation time" item.

**Rationale:** Read `smartsupp_RemovePresence` in the generated client directly (`api-client.ts:12613-12630`): its `options_: RequestInit` is built inline inside the method with a hardcoded `{ method: "DELETE", headers: {...} }` — there is no parameter on the public method signature through which a caller can inject `keepalive`. This is not a "template not wired yet" gap like Decision 1/2's 404/409 cases — it is a structural property of every NSwag Fetch-template-generated method (each method builds and owns its own `RequestInit`), so no future template change will add a keepalive passthrough. `getAuthenticatedFetch()` returns `(input, init) => fetch(...)` and merges `init` directly into the underlying `fetch` call, so `keepalive: true` passes through unmodified — confirmed by reading `client.ts:432-444`. This is exactly the case `docs/development/api-client-generation.md`'s escape-hatch section names as legitimate ("an endpoint's business outcome cannot yet be expressed through the generated client") — generalized here to "a transport option," which is the same category of gap. `smartsupp_RecordPresence` (the periodic heartbeat, no `keepalive` need) should still go through the typed client — only the unmount leave needs the escape hatch. Do not migrate the whole hook off the typed client to keep this one call consistent; that would lose NFR-3's compile-time checking for the heartbeat's own response shape for no reason.

#### Decision 4: `Date` vs `string` — commit to the generated `Date` type end-to-end for FR-1 through FR-5; keep the mapping-layer `string` boundary only where FR-6 already requires one

**Options considered:**
- Re-wrap every generated `Date` field back into an ISO string at each hook boundary (mirrors current hand-declared shapes exactly, minimizes visible diff in consuming components).
- Use the generated classes' `Date`-typed fields as-is everywhere, and fix the (small, enumerable) set of consumers whose local type signatures assume `string`.

**Chosen approach:** the latter, for `ConversationDto`/`MessageDto`/`ConversationSummaryDto`/`ConversationPresenceDto`/`ShoptetContactInfoDto`/`VisitorInfoDto` and their response wrappers. FR-6's `ragFeedbackTypes.ts` boundary is the one deliberate exception (Decision 2) — keep it string-typed because it's an existing shared contract with other consumers already assuming strings.

**Rationale:** Every generated Smartsupp DTO's date-ish field (`ConversationDto.lastMessageAt/createdAt/updatedAt`, `MessageDto.createdAt/deliveredAt`, `ConversationSummaryDto.lastMessageAt`, `ConversationPresenceDto.enteredAt`, `ShoptetContactInfoDto.cartUpdatedAt`, `ShoptetOrderSnapshotDto.orderDate`) is typed `Date`, not `string` — this is NSwag's standard ISO-8601-string-to-`Date` conversion per `docs/development/api-client-generation.md`'s own "Date handling" note, and it is **not** a hand-declared-vs-generated drift bug, it is the generator's designed behavior. Re-wrapping every one of these back to a string at the hook boundary (mirroring FR-6's approach) would require writing and maintaining a parallel mapper for `ConversationDto`/`MessageDto` — a much larger, more actively-used type than the feedback-list DTOs — purely to avoid touching five downstream files, and it would silently defeat NFR-3 (a backend field rename would again compile cleanly against a hand-maintained mapper, reintroducing exactly the class of bug this migration is fixing). Concretely verified consumers that need a one-line type-signature change, found via direct grep — not hypothetical:
- `frontend/src/components/customer-support/smartsupp/ConversationListItem.tsx:19` — `function formatRelativeTime(dateStr?: string | null)` called with `conversation.lastMessageAt ?? conversation.updatedAt`.
- `frontend/src/components/customer-support/smartsupp/MessageBubble.tsx:11` — `function formatTime(dateStr: string)` called with `message.createdAt`.
- `frontend/src/components/customer-support/smartsupp/ContactDetailsPanel.tsx:37-38`, `ConversationDetail.tsx:49,176`, `ConversationList.tsx:50-51`, `ShoptetCustomerCard.tsx:53,77` — all currently do `new Date(fieldValue)` before formatting/comparison, which is *idempotent* whether `fieldValue` is a `Date` or a `string` (the `Date` constructor accepts and clones a `Date` instance), so these call sites need no change; only the two formatter functions with an explicit `string`/`string | null` parameter type need widening to `Date | string` (or `Date`).
This is a small, mechanically-discoverable (by `tsc`) set of fixes, which is itself evidence the compile-time-safety goal (NFR-3) is working as intended.

## Implementation Guidance

### Directory / Module Structure

No new files beyond one extraction:

- `frontend/src/api/hooks/useSmartsupp.ts` — rewritten in place; keeps its current location and all seven exports (`useSmartsuppConversations`, `useSmartsuppConversation`, `useSmartsuppShoptetInfo`, `useSmartsuppVisitorInfo`, `useCloseConversation`, `usePresenceHeartbeat`, `otherActiveViewers`, plus `SMARTSUPP_QUERY_KEYS`). Re-export `ConversationDto`, `MessageDto`, `GetConversationResponse` (and any other generated type still consumed by sibling hooks/components) from `../generated/api-client` here, exactly as `usePackingMaterials.ts` re-exports its generated types — this keeps `useSendMessage.ts`'s existing import path (`from "../../../../api/hooks/useSmartsupp"`) working unchanged.
- `frontend/src/components/customer-support/smartsupp/hooks/{useGenerateDraftReply,useSendMessage,useSubmitDraftReplyFeedback,useSmartsuppDraftReplyFeedbackListQuery}.ts` — rewritten in place, same exported function names/shapes.
- **New**: `frontend/src/components/feedback/ragFeedbackMapping.ts` — extracted from `useKnowledgeBase.ts`'s existing `toLocalFeedbackChunk`/`toLocalFeedbackListResponse` functions (Decision 2), taking the generated `GeneratedGetFeedbackListResponse`-shaped input generically (both `knowledgeBase_GetFeedbackList` and `smartsupp_GetDraftReplyFeedbackList` return structurally-identical generated types, so a single mapper signature typed against the shared generated `RagFeedbackLogSummary`/`RagFeedbackStatsDto` classes covers both). Update `useKnowledgeBase.ts` to import from here instead of defining its own copy.
- `frontend/src/api/smartsuppClient.ts` — **deleted**. Confirmed via repo-wide search that its only importers are the five files this spec touches; nothing else references it.

### Interfaces and Contracts

- Every hook keeps its current exported function name, parameter list, and return shape as observed by its consumers — this is a call-site-transparent refactor. Only the *types* backing those return shapes change (hand-declared interface → generated class/interface), per Decision 4.
- `useCloseConversation`'s `mutationFn` must handle **two** distinct failure channels, not one: a typed 200 response with `success: false` + `errorCode` (still fully expressible via the generated `CloseConversationResponse extends BaseResponse`, whose `errorCode` field is typed `ErrorCodes | undefined` — confirmed `SmartsuppCloseConversationUnavailable` and `SmartsuppConversationNotFound` both exist as members of the generated `ErrorCodes` enum, so `CLOSE_ERROR_MESSAGES`'s keys type-check against it), **and** a thrown exception for the untyped `503`/`404` `ProducesResponseType`s on the same action — wrap the typed call in `try/catch`, check `data.success` on the resolved value, and separately catch-and-map the exception for the 503/404 case (message: fall back to `messageForCloseError(undefined)` — generic Czech message — since the exception carries no typed `errorCode`).
- `useGenerateDraftReply.ts`/`useSendMessage.ts`: same two-channel pattern (typed 200 `success: false` body **and** `try/catch` on the exception for the untyped 400/404/503 statuses) — this generalizes what FR-3/FR-4's acceptance criteria already say; do not assume the exception carries a usable `errorCode` since `400`/`404`/`503` are unannotated on the controller for both actions and will throw through the generic `ProblemDetails` path, not a typed response body.
- `ErrorCodes` is a generated string enum (`frontend/src/api/generated/api-client.ts:14023`). The four hand-maintained `Record<string, string>` error-message maps (`CLOSE_ERROR_MESSAGES`, `ERROR_MESSAGES` in `useGenerateDraftReply.ts`, `SEND_ERROR_MESSAGES`) should be retyped as `Partial<Record<ErrorCodes, string>>` (or equivalent) rather than left as `Record<string, string>` — this is a small, free upgrade in the same spirit as NFR-3: it makes a future rename of e.g. `SmartsuppConversationNotFound` on the backend produce a compile error in the message map too, not just at the call site.
- `usePresenceHeartbeat`: `smartsupp_RecordPresence(id)` for the periodic beat; `getApiBaseUrl()` + `getAuthenticatedFetch()` with `{ method: "DELETE", keepalive: true }` for the unmount leave (Decision 3). Both continue to swallow errors via `.catch(() => {})`.

### Data Flow

1. **List/detail/close/presence** (`useSmartsupp.ts`): `getAuthenticatedApiClient()` → `smartsupp_{GetConversations,GetConversation,CloseConversation,RecordPresence}` → generated response class flows straight into the `queryFn`/`mutationFn` return, no manual `response.json()`/`response.ok` handling (the generated client's `process*` methods already do this).
2. **Shoptet/visitor info**: `getApiBaseUrl()` + `getAuthenticatedFetch()` → manual fetch → `response.status === 404 ? null : ...` → `response.json()` cast to the generated `GetSmartsuppContactShoptetInfoResponse`/`GetVisitorInfoResponse` interface shape (Decision 1) — this path deliberately keeps today's manual JSON parsing since it's outside the typed client's throw-on-non-2xx contract.
3. **Draft reply / send message / feedback submit**: `getAuthenticatedApiClient()` → typed call inside `try/catch` → success path reads `data.success`/`data.errorCode` from the resolved (typed) response; failure path (thrown exception) discriminates on `err.status`.
4. **Feedback list**: `getAuthenticatedApiClient()` → `smartsupp_GetDraftReplyFeedbackList(...)` → shared `ragFeedbackMapping.ts` mapper (Decision 2) → `ragFeedbackTypes.ts`-shaped result, unchanged from the consumer's point of view.
5. **Optimistic send-message cache updates** (`useSendMessage.ts`): unchanged sequence (`onMutate` snapshots + injects optimistic `MessageDto`, `onSuccess` reconciles with `data.messageId`/`data.createdAt`, `onError` rolls back) — only the `MessageDto`'s `createdAt` field construction changes from `new Date().toISOString()` (string) to `new Date()` (Date), per Decision 4.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `Date`-vs-`string` fallout in downstream components silently mistyped rather than caught (e.g. a component keeps working at runtime by accident but produces `"Invalid Date"` or `[object Object]` in the UI instead of a compile error) | Medium | Rely on `tsc --noEmit`/`npm run build` — per Decision 4 the two formatter functions with explicit `string` parameter types are exactly what will fail to compile; do not add `as any`/`as unknown as string` casts to silence these (this is explicitly forbidden by NFR-1's acceptance criteria) — widen the parameter type instead. |
| `authenticated-api-usage.test.ts`'s `MIGRATED_HOOKS` regression guard does not include `useSmartsupp.ts`, so nothing currently prevents this fix from silently regressing back to a private-field cast in a future change | Medium | Add `"useSmartsupp.ts"` to the `MIGRATED_HOOKS` set in `frontend/src/api/__tests__/authenticated-api-usage.test.ts` (line ~197) as part of this PR — this is a one-line addition that converts the finding this issue exists to fix into a permanently-enforced regression test, closing the exact gap that let this drift go undetected for as long as it did (the test's own `hasLegacyAsAnyFetch` carve-out explicitly treats `smartsuppClient` as an accepted pattern today). |
| The four component-level hooks (`useGenerateDraftReply.ts`, `useSendMessage.ts`, `useSubmitDraftReplyFeedback.ts`, `useSmartsuppDraftReplyFeedbackListQuery.ts`) live under `components/customer-support/smartsupp/hooks/`, outside `authenticated-api-usage.test.ts`'s scanned `apiHooksDir` (`api/hooks/`) — so even after this migration, nothing automatically re-flags a future private-field-cast regression in those four files | Low | Out of scope to fix the test's scan scope generally, but worth a one-line callout in the PR description; if a future arch-review finding recurs here, that's the root cause to fix, not a re-litigation of this migration. |
| `useCloseConversation`'s two-channel error handling (typed `success:false` body vs. thrown exception for untyped 503/404) is easy to under-implement as "just try/catch, ignore the typed-body case" or vice versa, silently dropping one of the two current Czech error paths | Medium | FR-1's acceptance criteria already call this out; Decision/Interfaces section above states both channels explicitly — implementer must exercise both in the manual/E2E smoke pass required by NFR-2 (submit-twice-to-hit-409 equivalent: force a 503 via a backend feature-flag/mock if feasible, or code-review-verify both branches are present). |
| Existing unit tests (`useCloseConversation.test.ts`, `usePresenceHeartbeat.test.ts`, `useSmartsuppVisitorInfo.test.ts`, `useGenerateDraftReply.test.ts`, `useSendMessage.test.ts`) all mock `getAuthenticatedApiClient()` to return `{ baseUrl, http: { fetch: mockFetch } }` — this mock shape is incompatible with the post-migration code and every one of these tests will fail to compile/run as-is | High (certain, not hypothetical — confirmed by reading the test files) | Rewrite each test's mock to return an object with the specific `smartsupp_*` methods it exercises (e.g. `{ smartsupp_CloseConversation: jest.fn() }`), matching the mocking style already used by `useArticles`/`useKnowledgeBase`'s existing tests if any exist, or a fresh `jest.fn()`-per-method mock otherwise. This is FR-7's third acceptance criterion — treat it as mandatory, not optional cleanup. |
| Reused `ragFeedbackMapping.ts` extraction (Decision 2) is a refactor of already-shipped, presumably-working `useKnowledgeBase.ts` code, which is nominally outside this spec's Smartsupp-only scope | Low | The extraction is additive/mechanical (move two functions, no behavior change) and is the only way to satisfy FR-6 without duplicating logic; scope it narrowly — do not otherwise touch `useKnowledgeBase.ts`'s KB-specific hooks/types. |

## Specification Amendments

1. **FR-5's premise about dual-annotation is incorrect** — `SmartsuppController.cs`'s `SubmitDraftReplyFeedback` action annotates 409 as `[ProducesResponseType(StatusCodes.Status409Conflict)]` (untyped), not `[ProducesResponseType(typeof(SubmitDraftReplyFeedbackResponse), StatusCodes.Status409Conflict)]`. No implementation impact — the `try/catch`-on-`err.status` pattern doesn't depend on the 409 body being typed — but the spec's Background section should not claim this as "confirmed applicable" the way it does.
2. **FR-2's Open Question (choice of path (a) vs (b)) is resolved, not open**: per Decision 1 above, path (a) (escape hatch) is the correct choice, definitively — path (b)'s prerequisite (typed 404 branch) cannot be realized today regardless of controller annotation, because the wired-off template override predicate is hardcoded to 409 only (`nswag-templates/README.md`). Adopt path (a) as a requirement, not an implementer's choice; drop the "optionally add a typed 404" language in the Data Model section's closing paragraph.
3. **Open Question on `keepalive` is resolved, not open**: per Decision 3, the generated `smartsupp_RemovePresence` method's `RequestInit` is hardcoded inside the method body with no passthrough parameter, for structural reasons (every NSwag Fetch-template method owns its own `options_`) that no future template change will fix. `usePresenceHeartbeat`'s unmount leave must permanently use `getApiBaseUrl()` + `getAuthenticatedFetch()`; the periodic heartbeat itself uses the typed `smartsupp_RecordPresence`.
4. **Open Question on `SendMessageResponse` field casing is resolved, not open**: confirmed directly in `api-client.ts:42178-42213` — `messageId?: string` and `createdAt?: Date` match `useSendMessage.ts`'s current field-name usage exactly; only the `Date`-vs-`string` type (not name) differs, covered by Decision 4.
5. **FR-6's "reconciling field-shape differences... flag as open question if incompatible" is resolved, not open**: no incompatibility exists (Decision 2) — replace with a requirement to extract and share `useKnowledgeBase.ts`'s existing mapper rather than write a new one.
6. **New, spec-worthy addition not present in spec.r1.md**: add `useSmartsupp.ts` to the `MIGRATED_HOOKS` regression-guard set in `frontend/src/api/__tests__/authenticated-api-usage.test.ts` as part of this change (see Risks table). This is the concrete, low-cost way to make this fix self-enforcing, and its absence would mean the class of bug this spec fixes could quietly reappear in `useSmartsupp.ts` with no test catching it — the same gap that let the original finding go undetected as long as it did (the wrapper-function indirection in `smartsupp*Client.ts` hid the `(apiClient as any)` cast from the regex-based guard test).
7. **Decision 4 formalizes an implicit spec gap**: the spec's NFR-1/NFR-2 discuss field-shape drift in the abstract but do not call out that essentially every date field across `ConversationDto`/`MessageDto`/`ShoptetContactInfoDto` etc. changes from `string` to `Date`, nor enumerate the concrete consumer files affected. Treat the file list under Decision 4 as the authoritative "must touch" list for that mechanical fallout, in addition to the five hook files FR-1–FR-6 name directly.

## Prerequisites

None. No backend changes, no database migrations, no new configuration, no new infrastructure. Every generated `smartsupp_*` method and DTO this spec needs already exists in the current `frontend/src/api/generated/api-client.ts` without regeneration. The only sequencing constraint is internal to the change itself: extract `ragFeedbackMapping.ts` (Decision 2) before or alongside FR-6, since FR-6 depends on it; the other five FRs (FR-1 through FR-5, FR-7) have no ordering dependency on each other beyond FR-7 (retire `smartsuppClient.ts`) necessarily landing last, once FR-1–FR-6 have removed all its callers.
