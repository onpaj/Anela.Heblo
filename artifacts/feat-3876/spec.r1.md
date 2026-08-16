# Specification: Route Smartsupp frontend hooks through the generated typed API client

## Summary
All five Smartsupp frontend hook files currently bypass the NSwag-generated, typed API client and instead call a private-field-reaching escape hatch (`smartsuppClient.ts`'s `asInternal()`), duplicating eleven hand-declared DTO interfaces that the generator already emits. This feature replaces those raw `fetch`-based call sites with calls to the generated `smartsupp_*` client methods and generated DTOs, deletes the hand-declared interfaces, and removes `frontend/src/api/smartsuppClient.ts` once nothing depends on it. Two call sites that branch on non-2xx status codes (409 on feedback submission, 404 on Shoptet/visitor info) need a documented alternative pattern rather than the plain typed call.

## Background
`docs/development/api-client-generation.md` establishes that all frontend hooks must call the backend through `getAuthenticatedApiClient()` (or, for status-code branching, `getApiBaseUrl()` + `getAuthenticatedFetch()`), and explicitly forbids reaching into the generated client's private `baseUrl`/`http` fields — because a template change to NSwag would silently break every caller at runtime with no compile-time warning.

`frontend/src/api/smartsuppClient.ts` violates this rule directly: it casts the typed client to a hand-written `ApiClientInternal` shape (`asInternal()`) to reach `.baseUrl` and `.http.fetch`, and hands back thin `apiGet`/`apiPost`/`apiDelete` wrappers. Every Smartsupp hook uses these wrappers instead of the generated `smartsupp_*` methods, and each hook re-declares the response/request DTOs by hand (`ConversationDto`, `MessageDto`, `ListConversationsResponse`, `GetConversationResponse`, `ShoptetContactInfoDto`, `VisitorInfoDto`, `CloseConversationResponse`, `GenerateDraftReplyApiResponse`, `SendMessageApiResponse`, `DraftReplyFeedbackListResponse`, plus supporting types), even though the generator already emits typed equivalents for every one of these endpoints and shapes.

This is a recurring class of finding from the ongoing arch-review sweep of the module map (#3873 CatalogDocuments — open, #3833 KnowledgeBase, #3823 Expedition, #3816 DataQuality, #3815 Photobank, #3810/#3802/#3797 Manufacture, #3852 Packaging — fixed by PR #3857). This issue is the Smartsupp instance of that class; #3852/PR #3857 (Packaging) and the #3221 rework of `usePackingMaterials` are the precedents to follow for both the typed-call migration and the escape-hatch pattern for status-code branching.

The risk this creates today: when a backend Smartsupp DTO changes (a field renamed, retyped, or added), the generated client picks up the change automatically, but these hand-declared interfaces do not — so a shape mismatch surfaces as a runtime `undefined` in the operator chat UI rather than a TypeScript compile error. Separately, if NSwag ever renames the generated client's internal `baseUrl`/`http` fields (a legitimate template change, not a contract change), all eleven call sites break at runtime simultaneously with zero build-time signal.

Ground truth used for this spec (current repo state; line numbers in the original finding have since shifted and are superseded by the file/method names below):
- `frontend/src/api/smartsuppClient.ts` — `asInternal()`, `getClientAndBaseUrl()`, `apiGet`/`apiPost`/`apiDelete`.
- `frontend/src/api/hooks/useSmartsupp.ts` — hand-declares `ConversationSummaryDto`, `ConversationPresenceDto`, `ConversationDto`, `MessageDto`, `ListConversationsResponse`, `GetConversationResponse`, `ShoptetCustomerSnapshotDto`, `ShoptetOrderSnapshotDto`, `ShoptetContactInfoDto`, `GetSmartsuppShoptetInfoResponse`, `VisitorPageDto`, `VisitorInfoDto`, `GetSmartsuppVisitorInfoResponse`, `CloseConversationResponse`; exports `useSmartsuppConversations`, `useSmartsuppConversation`, `useSmartsuppShoptetInfo`, `useSmartsuppVisitorInfo`, `useCloseConversation`, `usePresenceHeartbeat`, `otherActiveViewers`.
- `frontend/src/components/customer-support/smartsupp/hooks/useGenerateDraftReply.ts` — hand-declares `GenerateDraftReplyApiResponse`, `DraftReplySource`, `DraftReplyResult`.
- `frontend/src/components/customer-support/smartsupp/hooks/useSendMessage.ts` — hand-declares `SendMessageApiResponse`; imports `GetConversationResponse`/`MessageDto` from `useSmartsupp.ts` for optimistic cache updates.
- `frontend/src/components/customer-support/smartsupp/hooks/useSubmitDraftReplyFeedback.ts` — hand-declares `SubmitDraftReplyFeedbackRequest`, `SubmitDraftReplyFeedbackResult`; branches on HTTP 409.
- `frontend/src/components/customer-support/smartsupp/hooks/useSmartsuppDraftReplyFeedbackListQuery.ts` — hand-declares `DraftReplyFeedbackListParams`, `DraftReplyFeedbackListResponse`.
- `backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs` — confirms the generated client already exists for every endpoint (`smartsupp_GetConversations`, `smartsupp_GetConversation`, `smartsupp_GenerateDraftReply`, `smartsupp_GetShoptetInfo`, `smartsupp_GetVisitorInfo`, `smartsupp_SendMessage`, `smartsupp_SubmitDraftReplyFeedback`, `smartsupp_GetDraftReplyFeedbackList`, `smartsupp_CloseConversation`, `smartsupp_RecordPresence`, `smartsupp_RemovePresence`), and that `SubmitDraftReplyFeedback` is already annotated `[ProducesResponseType(typeof(SubmitDraftReplyFeedbackResponse), StatusCodes.Status200OK)]` + `[ProducesResponseType(StatusCodes.Status409Conflict)]` (the 409 has no typed DTO attached), while `GetShoptetInfo`/`GetVisitorInfo` are annotated with a typed 200 and an untyped `[ProducesResponseType(StatusCodes.Status404NotFound)]`.
- `frontend/src/api/generated/api-client.ts` — verified the generated `smartsupp_*` methods exist for all eleven backend actions, and that for the current OpenAPI annotations the generated `process*` handlers for `GetShoptetInfo`, `GetVisitorInfo`, and `SubmitDraftReplyFeedback` all route their non-200 status codes (404/409/403) through `ProblemDetails.fromJS(...)` + `throwException(...)` — i.e. the NSwag template does **not** currently emit a typed non-throwing branch for these status codes, even where the controller points a business-outcome status at the same response DTO. This confirms the "template override not yet active" state described in `docs/development/api-client-generation.md`, so the documented interim pattern (typed call wrapped in `try/catch`, discriminating on `err.status`) is what applies today, not the "typed non-throwing branch" aspirational path.

## Functional Requirements

### FR-1: Replace `useSmartsupp.ts` list/detail/close/presence hooks with typed generated calls
`useSmartsuppConversations`, `useSmartsuppConversation`, `useCloseConversation`, and `usePresenceHeartbeat` must call `getAuthenticatedApiClient()` and invoke `smartsupp_GetConversations`, `smartsupp_GetConversation`, `smartsupp_CloseConversation`, `smartsupp_RecordPresence`, and `smartsupp_RemovePresence` respectively, instead of `getClientAndBaseUrl()` + `apiGet`/`apiPost`/`apiDelete`.

**Acceptance criteria:**
- `useSmartsuppConversations` calls `smartsupp_GetConversations(status, 1, 100)` and its `queryFn` return type is the generated `ListConversationsResponse` (no hand-declared interface).
- `useSmartsuppConversation` calls `smartsupp_GetConversation(id)`; return type is generated `GetConversationResponse`.
- `useCloseConversation`'s `mutationFn` calls `smartsupp_CloseConversation(conversationId)`; success/error handling (including the `errorCode`-keyed Czech error messages in `CLOSE_ERROR_MESSAGES`) is preserved using the generated `CloseConversationResponse.errorCode` field and/or a `try/catch` on the typed exception, whichever the actual controller status annotations require (verify at implementation time whether `close` ever returns a non-2xx with `success: false` in the body vs. throwing — current controller has `[ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]` untyped, so a 503 will throw via the generated client; the existing manual `response.ok` check for a `{ success: false, errorCode }` body only applies to the typed 200 path unless the controller changes).
- `usePresenceHeartbeat`'s heartbeat (`smartsupp_RecordPresence`) and best-effort leave-on-unmount (`smartsupp_RemovePresence`) continue to swallow errors exactly as today (`.catch(() => {})`); the `keepalive: true` behavior on unmount is preserved. If the generated client's fetch call does not accept a `keepalive` option through its public surface, this is flagged as an open question (see Open Questions) rather than silently dropped.
- `otherActiveViewers` continues to type against `ConversationDto`/`ConversationPresenceDto`, now imported from `frontend/src/api/generated/api-client.ts` instead of the local hand-declared interfaces.
- All hand-declared interfaces in `useSmartsupp.ts` (`ConversationSummaryDto`, `ConversationPresenceDto`, `ConversationDto`, `MessageDto`, `ListConversationsResponse`, `GetConversationResponse`) are deleted; any other module importing these types (confirmed: `useSendMessage.ts` imports `GetConversationResponse` and `MessageDto` from this file) is updated to import the generated equivalents instead — either directly from `frontend/src/api/generated/api-client.ts` or re-exported from `useSmartsupp.ts` for call-site convenience (implementer's choice, consistent with existing conventions elsewhere in the codebase).
- `SMARTSUPP_QUERY_KEYS` and the `QUERY_KEYS.smartsupp` cache-invalidation call in `useCloseConversation`'s `onSuccess` are unchanged (no cache-key behavior change).

### FR-2: Replace Shoptet/visitor-info hooks with the documented status-code escape hatch
`useSmartsuppShoptetInfo` and `useSmartsuppVisitorInfo` currently branch on `response.status === 404` to return `null` instead of throwing. Per `docs/development/api-client-generation.md`, the current generated client throws a typed exception on 404 for both endpoints (verified: `ProblemDetails.fromJS` + `throwException`) rather than returning a typed non-throwing branch, because the 404 `ProducesResponseType` on the controller is untyped. Two conforming implementation paths exist; the implementer picks one per the guidance in the doc:
  - **(a) Escape hatch:** use `getApiBaseUrl()` + `getAuthenticatedFetch()` from `./client`, keeping the current manual `response.status === 404 → null` / `!response.ok → throw` logic, but parsing the body as the generated `GetSmartsuppContactShoptetInfoResponse` / `GetVisitorInfoResponse` type instead of the hand-declared `GetSmartsuppShoptetInfoResponse` / `GetSmartsuppVisitorInfoResponse`.
  - **(b) Typed exception catch:** call `smartsupp_GetShoptetInfo` / `smartsupp_GetVisitorInfo` directly and wrap in `try/catch`, returning `null` when the caught error's `status === 404`, mirroring `useSubmitArticleFeedbackMutation`'s pattern — this additionally requires annotating `GetShoptetInfo`/`GetVisitorInfo` on `SmartsuppController` with a typed 404 (`[ProducesResponseType(typeof(GetSmartsuppContactShoptetInfoResponse), StatusCodes.Status404NotFound)]`) if the team wants the generated client to eventually stop routing 404 through `ProblemDetails` — not required for the `try/catch` interim pattern to work, since the exception's `.status` field is available regardless of the exception's typed payload.

**Acceptance criteria:**
- Neither hook uses `smartsuppClient.ts`'s `apiGet`/`getClientAndBaseUrl`/`asInternal` after the change.
- Both hooks still return `null` (not throw, not reject the query) when the backend returns 404 for a missing Shoptet contact / visitor info record.
- Both hooks still throw (surfacing to the query's `error` state) for any other non-2xx status.
- Hand-declared `ShoptetCustomerSnapshotDto`, `ShoptetOrderSnapshotDto`, `ShoptetContactInfoDto`, `GetSmartsuppShoptetInfoResponse`, `VisitorPageDto`, `VisitorInfoDto`, `GetSmartsuppVisitorInfoResponse` are deleted from `useSmartsupp.ts` in favor of the generated `GetSmartsuppContactShoptetInfoResponse`, `IShoptetContactInfoDto`/`ShoptetContactInfoDto`, `GetVisitorInfoResponse`, `IVisitorInfoDto`/`VisitorInfoDto` types, with any field-shape differences reconciled (see NFR-1).
- `staleTime`/`retry: false` query options are unchanged.

### FR-3: Replace `useGenerateDraftReply.ts` with the typed generated call
Replace the `apiPost` + hand-declared `GenerateDraftReplyApiResponse` with `smartsupp_GenerateDraftReply(conversationId, { topic })` against the generated `GenerateDraftReplyResponse`/`GenerateDraftReplyBody` types.

**Acceptance criteria:**
- `mutationFn` calls `smartsupp_GenerateDraftReply(conversationId, new GenerateDraftReplyBody({ topic: topic ?? null }))` (or equivalent generated request-body construction).
- The `!response.ok || !data.success` combined check is replaced with logic appropriate to the typed call: a `try/catch` around the typed call for HTTP-level failures (400/404/503, all currently untyped on the controller and thus thrown by the generated client), plus the existing `data.success`/`errorCode` check on the 200 response body for business-level failure signaled in-band.
- `messageForError` / `ERROR_MESSAGES` Czech error-code-to-message mapping is preserved unchanged, fed from whichever `errorCode` source (thrown exception body vs. 200 response body) actually carries it per the controller's real behavior — verify against `GenerateDraftReplyResponse`'s `errorCode` field and the controller's `ProducesResponseType` set (400/404/503 all currently untyped, so `errorCode` on those paths is not guaranteed to be present in a typed shape; implementer confirms actual behavior against a live/staging call before finalizing).
- `DraftReplySource`/`DraftReplyResult` (the hook's own return-shape types, not wire DTOs) may remain as local types since they are not 1:1 mirrors of a generated DTO the hook receives, but their internal fields are populated from the generated `GenerateDraftReplyResponse`'s `sources`/`answer`/`id` fields instead of the deleted `GenerateDraftReplyApiResponse`.

### FR-4: Replace `useSendMessage.ts` with the typed generated call
Replace the `apiPost` + hand-declared `SendMessageApiResponse` with `smartsupp_SendMessage(conversationId, body)` against the generated `SendMessageResponse`/`SendMessageBody` types.

**Acceptance criteria:**
- `mutationFn` calls `smartsupp_SendMessage(conversationId, new SendMessageBody({ content, draftLogId }))`.
- The current two-stage error handling (`!response.ok` → parse best-effort `errorCode` from body; `!data.success` → same) is replaced with a `try/catch` around the typed call for the untyped 400/404/503 paths, plus a `data.success`/`errorCode` check on the typed 200 response, preserving `SEND_ERROR_MESSAGES`/`messageForSendError` output unchanged.
- The optimistic-update logic (`onMutate`/`onSuccess`/`onError` using `SMARTSUPP_QUERY_KEYS.conversation`, the `MessageDto` shape for the optimistic message, and cache rollback) is functionally unchanged, now importing `MessageDto`/`GetConversationResponse` from the generated client (via FR-1's re-export or direct import) instead of `useSmartsupp.ts`'s deleted local interfaces.
- `data.messageId`/`data.createdAt` field access continues to work against the generated `SendMessageResponse` shape (verify field names/casing match — see NFR-1).

### FR-5: Replace `useSubmitDraftReplyFeedback.ts` with the typed call + 409 exception catch
Replace the `apiPost` + hand-declared request/result types with `smartsupp_SubmitDraftReplyFeedback(request)` against the generated `SubmitDraftReplyFeedbackRequest`/`SubmitDraftReplyFeedbackResponse` types, catching the typed exception for the 409 case per the documented `useSubmitArticleFeedbackMutation` pattern (confirmed applicable: the controller already dual-annotates 200/409 with the same DTO, but the generated client currently still throws on 409 via `ProblemDetails`, matching the doc's "template override not yet active — use try/catch" guidance).

**Acceptance criteria:**
- `mutationFn` calls `smartsupp_SubmitDraftReplyFeedback(new SubmitDraftReplyFeedbackRequest({ logId, precisionScore, styleScore, comment }))` inside a `try/catch`.
- On success, returns `{}` (or the equivalent "no special result" shape) matching current behavior for the 200 path.
- On a caught exception whose `.status === 409`, returns `{ alreadySubmitted: true }` — matching current behavior — instead of rethrowing.
- Any other caught exception (or non-409 status) rethrows/propagates as a mutation error, matching current behavior for non-2xx-non-409 cases (currently: any `!response.ok` and not 409 → generic `Submit feedback failed: {status}` error).
- Hand-declared `SubmitDraftReplyFeedbackRequest`/`SubmitDraftReplyFeedbackResult` local interfaces are deleted in favor of the generated request type and a local result type (or the generated response type, extended/narrowed as needed) — since `alreadySubmitted` is not itself a wire field, a thin local result type wrapping the generated response is acceptable.

### FR-6: Replace `useSmartsuppDraftReplyFeedbackListQuery.ts` with the typed generated call
Replace the `apiGet` + manually-built query string + hand-declared `DraftReplyFeedbackListResponse` with `smartsupp_GetDraftReplyFeedbackList(pageNumber, pageSize, sortBy, sortDescending, hasFeedback, userId)` against the generated `GetDraftReplyFeedbackListResponse` type.

**Acceptance criteria:**
- `queryFn` calls the generated method directly, passing through the same optional-parameter semantics currently expressed via manual `URLSearchParams` construction (undefined params omitted / passed as `undefined` to the generated method, which the generated client already handles per its own query-string-building logic).
- `!response.ok` manual check is replaced with a `try/catch` (or is unnecessary if the generated client throws directly on non-2xx, per its own `process*` handler) that preserves the existing `Failed to fetch Smartsupp feedback list: {status}` error message semantics or the closest typed equivalent.
- The hand-declared `DraftReplyFeedbackListParams`/`DraftReplyFeedbackListResponse` interfaces are deleted; `logs`/`totalCount`/`pageNumber`/`pageSize`/`totalPages`/`stats` continue to be sourced from the generated `GetDraftReplyFeedbackListResponse`, reconciling field-shape differences against `RagFeedbackLogSummary`/`RagFeedbackStats` (imported from `frontend/src/components/feedback/ragFeedbackTypes`) if the generated DTO's nested types differ from these hand-maintained ones (out of scope to change `ragFeedbackTypes.ts` itself unless the generated shapes are incompatible — flag as open question if so).
- `staleTime`/`gcTime` query options are unchanged.

### FR-7: Retire `smartsuppClient.ts`
Once FR-1 through FR-6 land, no production code should import from `frontend/src/api/smartsuppClient.ts`.

**Acceptance criteria:**
- `frontend/src/api/smartsuppClient.ts` is deleted, OR, if any legitimate non-typed-endpoint use remains (none currently identified), the file is reduced to only what is still needed and the `asInternal()`/private-field cast is removed entirely.
- A repo-wide search for `smartsuppClient` and `asInternal` returns no remaining references outside test fixtures/mocks that are also updated or removed.
- Existing unit tests for the five hook files (if present) are updated to mock the generated `smartsupp_*` client methods instead of `apiGet`/`apiPost`/`apiDelete`/`getClientAndBaseUrl`.

## Non-Functional Requirements

### NFR-1: Type/field parity verification
The generated DTOs (`ConversationDto`, `MessageDto`, `ListConversationsResponse`, `GetConversationResponse`, `GetSmartsuppContactShoptetInfoResponse`, `IShoptetContactInfoDto`, `GetVisitorInfoResponse`, `IVisitorInfoDto`, `CloseConversationResponse`, `GenerateDraftReplyResponse`, `SendMessageResponse`, `SubmitDraftReplyFeedbackResponse`, `GetDraftReplyFeedbackListResponse`) must be diffed field-by-field against the hand-declared interfaces being deleted before each hook is migrated, since the hand-declared versions may have drifted from the current backend contract in either direction (missing fields, extra fields, or type mismatches — this is exactly the risk the migration is meant to close). Any field used by UI/business logic that is absent from the generated type is a defect to raise against the backend DTO/contract, not something to work around in the frontend.

**Acceptance criteria:**
- Every field read from a response object in the affected hooks (and their consuming components, transitively, for fields like `activeViewers`, `assignedAgentIds`, `contactProperties`, `variables` on `ConversationDto`) is confirmed present with a compatible type on the generated DTO before the hand-declared interface is deleted.
- No `as any` / `as unknown as X` casts are introduced to paper over a field mismatch discovered during migration; a genuine mismatch is escalated (fixed on the backend contract or flagged as a follow-up issue) rather than silently cast around.

### NFR-2: No behavior change to caching, optimistic updates, or user-facing error messages
This is a pure plumbing/typing migration. Query keys, `staleTime`/`gcTime`/`refetchInterval` values, optimistic-update sequencing in `useSendMessage`, presence-heartbeat cadence and best-effort-failure semantics, and every Czech user-facing error message string must be byte-identical after the change, except where a field-shape difference discovered under NFR-1 forces an adjustment (which must then be called out explicitly in the PR description).

**Acceptance criteria:**
- Manual/E2E smoke test of the Smartsupp conversation list, conversation detail (including Shoptet info panel, visitor info panel, presence badges), send-message flow, draft-reply generation, draft-reply feedback submission (including submitting twice to hit the 409 path), and conversation close, all behave identically to pre-change behavior.
- No new TypeScript compile errors or `npm run lint` warnings introduced.

### NFR-3: Compile-time contract enforcement
The core motivation for this change — catching backend DTO drift at compile time instead of at runtime — must actually hold after the migration: introducing a mismatched field on a generated Smartsupp DTO (e.g., renaming a property in the backend response class) and regenerating the client must produce a TypeScript compile error at one or more of the five hook files, not a silent pass-through.

**Acceptance criteria:**
- Spot-check: temporarily rename a field on one backend Smartsupp response DTO, regenerate the client, run `npm run build` (or `tsc --noEmit`), and confirm a compile error surfaces in the affected hook file(s) — then revert.

## Data Model
No new data model is introduced. This change re-points existing frontend hooks from hand-maintained mirror types to the generator-owned equivalents already produced from the backend's existing response classes:

| Hand-declared type (deleted) | Generated equivalent (from `frontend/src/api/generated/api-client.ts`) | Backend source |
|---|---|---|
| `ConversationSummaryDto` | `ConversationSummaryDto` / `IConversationSummaryDto` | `Application/Features/Smartsupp/...` conversation summary DTO |
| `ConversationPresenceDto` | `ConversationPresenceDto` / `IConversationPresenceDto` | presence DTO |
| `ConversationDto` | `ConversationDto` / `IConversationDto` | conversation DTO |
| `MessageDto` | `MessageDto` / `IMessageDto` | message DTO |
| `ListConversationsResponse` | `ListConversationsResponse` / `IListConversationsResponse` | `ListConversations` use case response |
| `GetConversationResponse` | `GetConversationResponse` / `IGetConversationResponse` | `GetConversation` use case response |
| `ShoptetCustomerSnapshotDto`, `ShoptetOrderSnapshotDto`, `ShoptetContactInfoDto`, `GetSmartsuppShoptetInfoResponse` | `IShoptetContactInfoDto` (+ nested), `GetSmartsuppContactShoptetInfoResponse` | `GetContactShoptetInfo` use case response |
| `VisitorPageDto`, `VisitorInfoDto`, `GetSmartsuppVisitorInfoResponse` | `IVisitorInfoDto` (+ nested), `GetVisitorInfoResponse` | `GetVisitorInfo` use case response |
| `CloseConversationResponse` | `CloseConversationResponse` / `ICloseConversationResponse` | `CloseConversation` use case response |
| `GenerateDraftReplyApiResponse` | `GenerateDraftReplyResponse` / `IGenerateDraftReplyResponse` | `GenerateDraftReply` use case response |
| `SendMessageApiResponse` | `SendMessageResponse` / `ISendMessageResponse` | `SendMessage` use case response |
| `SubmitDraftReplyFeedbackRequest`/`Result` | `SubmitDraftReplyFeedbackRequest`/`SubmitDraftReplyFeedbackResponse` | `SubmitDraftReplyFeedback` use case |
| `DraftReplyFeedbackListParams`/`Response` | generated `smartsupp_GetDraftReplyFeedbackList` params + `GetDraftReplyFeedbackListResponse` | `GetDraftReplyFeedbackList` use case response |

No backend changes are required to add types — every response DTO already exists and is already OpenAPI-annotated. The only backend-adjacent change this spec calls for (optional, see FR-2 path (b)) is adding a typed 404 `ProducesResponseType` to `GetShoptetInfo`/`GetVisitorInfo`, which is not required for correctness under the chosen interim pattern.

## API / Interface Design
No backend API surface changes. Frontend interface changes only:

- `frontend/src/api/hooks/useSmartsupp.ts`: same exported hook names and signatures (`useSmartsuppConversations`, `useSmartsuppConversation`, `useSmartsuppShoptetInfo`, `useSmartsuppVisitorInfo`, `useCloseConversation`, `usePresenceHeartbeat`, `otherActiveViewers`, `SMARTSUPP_QUERY_KEYS`); internals re-implemented against `getAuthenticatedApiClient()` and generated types; hand-declared DTO interfaces removed (re-exports of generated types may be added for downstream convenience).
- `frontend/src/components/customer-support/smartsupp/hooks/useGenerateDraftReply.ts`, `useSendMessage.ts`, `useSubmitDraftReplyFeedback.ts`, `useSmartsuppDraftReplyFeedbackListQuery.ts`: same exported hook names, return shapes, and consumer-facing behavior; internals re-implemented against the generated client.
- `frontend/src/api/smartsuppClient.ts`: deleted (or reduced to zero private-field access) once no hook imports from it.

## Dependencies
- NSwag-generated `frontend/src/api/generated/api-client.ts` — already produces every `smartsupp_*` method and DTO needed; no regeneration-triggering backend change is required to unblock this work.
- `frontend/src/api/client.ts` — `getAuthenticatedApiClient()`, `getApiBaseUrl()`, `getAuthenticatedFetch()` (for FR-2's escape-hatch path).
- Prior art: `useSubmitArticleFeedbackMutation` (`frontend/src/api/hooks/useArticles.ts`) for the try/catch-on-typed-exception 409 pattern; `usePackingMaterials.ts` (#3221) and PR #3857 (#3852, Packaging) as precedent for the same class of migration.
- `docs/development/api-client-generation.md` is the governing rule document; no changes to it are anticipated as part of this work, though FR-2's finding (that the "template override" typed-branch path described in the doc is not yet active for any endpoint checked here) may warrant a documentation clarification as a fast-follow.

## Out of Scope
- Activating the NSwag template override that would let the generated client emit a typed, non-throwing branch for business-outcome status codes (referenced in the doc as "when active"); this migration uses the documented interim `try/catch` pattern instead.
- Changing backend response DTOs, controller routes, or `ProducesResponseType` annotations, except optionally adding a typed 404 annotation to `GetShoptetInfo`/`GetVisitorInfo` if the implementer chooses FR-2 path (b) over path (a) — either is acceptable.
- Any UI/UX behavior change to the Smartsupp chat panels.
- Migrating other, unrelated hooks/modules flagged by other open arch-review issues (#3873 CatalogDocuments, etc.) — this spec covers only the Smartsupp module (module-map part #29).
- Adding automated test coverage where none currently exists, beyond what's needed to safely refactor call sites already covered by existing tests (if any exist for these hooks — not confirmed as part of this spec's research).
- Reworking `ragFeedbackTypes.ts` (`RagFeedbackLogSummary`/`RagFeedbackStats`) unless a genuine shape incompatibility with the generated `GetDraftReplyFeedbackListResponse` nested types is discovered during implementation.

## Open Questions
- **`keepalive` on unmount in `usePresenceHeartbeat`:** the current `apiDelete(apiClient, url, { keepalive: true })` passes `keepalive: true` to the raw `fetch` call so the "leave" signal survives page/tab unload. Does the generated `smartsupp_RemovePresence` method's underlying `this.http.fetch(...)` call accept a way to pass `keepalive` through its public method signature, or does achieving this require staying on the `getApiBaseUrl()` + `getAuthenticatedFetch()` escape hatch for this one call only (leaving the rest of `usePresenceHeartbeat` on the typed client)? Needs a quick check of the generated method signature and `getAuthenticatedFetch()`'s options-passthrough behavior before implementation.
- **`useCloseConversation`'s 503 path:** `CloseConversation` is annotated `[ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]` untyped, so the generated client throws on 503 rather than returning a `{ success: false, errorCode: "SmartsuppCloseConversationUnavailable" }` body. Does the current backend actually ever return a 200 with `success: false` for this errorCode (in which case the existing manual check on the response body still applies), or is that errorCode message effectively unreachable / only reachable via the thrown-exception path today? Needs a quick backend check (the use-case handler for `CloseConversationRequest`) before finalizing FR-1's error-handling rewrite.
- **`SendMessageResponse` field casing/nullability for `messageId`/`createdAt`:** confirm the generated `SendMessageResponse` interface's actual field names (`messageId`/`createdAt` vs. something generator-normalized) match what `useSendMessage.ts`'s optimistic-update logic reads today, since a mismatch here would be exactly the kind of drift this migration is meant to catch — flagging it now so the implementer checks it explicitly rather than assuming parity.
- **Scope of "delete `smartsuppClient.ts`" vs. "keep as thin escape hatch":** if FR-2 path (a) is chosen for the 404 endpoints, `smartsuppClient.ts` itself is not needed (the escape hatch is `getApiBaseUrl()`/`getAuthenticatedFetch()` from `./client`, not `smartsuppClient.ts`), so full deletion is expected. Confirm no other file outside the five hook files imports from `smartsuppClient.ts` before deleting it.

## Status: HAS_QUESTIONS
