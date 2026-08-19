# Design: Route Smartsupp frontend hooks through the generated typed API client

## Component Design

This is a call-site-transparent refactor: every hook keeps its current exported name, parameter list, and return shape. Only the internals — how each hook reaches the backend and which types back its data — change. No new components are introduced except one shared mapper module.

### `frontend/src/api/hooks/useSmartsupp.ts`
Rewritten in place. Keeps all current exports: `useSmartsuppConversations`, `useSmartsuppConversation`, `useSmartsuppShoptetInfo`, `useSmartsuppVisitorInfo`, `useCloseConversation`, `usePresenceHeartbeat`, `otherActiveViewers`, `SMARTSUPP_QUERY_KEYS`.

- `useSmartsuppConversations`, `useSmartsuppConversation`: call `getAuthenticatedApiClient()` → `smartsupp_GetConversations(status, 1, 100)` / `smartsupp_GetConversation(id)`. No manual `response.ok`/`response.json()` handling — the generated client's `process*` methods already do this.
- `useCloseConversation`: `mutationFn` wraps `smartsupp_CloseConversation(conversationId)` in `try/catch`. Two distinct failure channels must both be handled: (1) a resolved typed response with `success: false` + `errorCode` (from `CloseConversationResponse extends BaseResponse`, `errorCode: ErrorCodes | undefined`), mapped through the existing `CLOSE_ERROR_MESSAGES`; and (2) a thrown exception for the controller's untyped 503/404 `ProducesResponseType`s, caught and mapped to the generic Czech message (`messageForCloseError(undefined)`) since the exception carries no typed `errorCode`. `SMARTSUPP_QUERY_KEYS`/cache-invalidation in `onSuccess` is unchanged.
- `usePresenceHeartbeat`: periodic heartbeat calls `smartsupp_RecordPresence(id)` through the typed client. The unmount "leave" call permanently uses the escape hatch — `getApiBaseUrl()` + `getAuthenticatedFetch()` with `{ method: "DELETE", keepalive: true }` — because the generated `smartsupp_RemovePresence` method builds its `RequestInit` internally with no way for a caller to pass `keepalive` through its public signature (a structural property of every NSwag Fetch-template method, not a fixable gap). Both calls continue to swallow errors via `.catch(() => {})`.
- `useSmartsuppShoptetInfo`, `useSmartsuppVisitorInfo`: stay on the escape hatch (`getApiBaseUrl()` + `getAuthenticatedFetch()`), keeping the current manual `response.status === 404 → null` / `!response.ok → throw` logic, but parsing the body against the generated `GetSmartsuppContactShoptetInfoResponse` / `GetVisitorInfoResponse` types instead of hand-declared ones. This is a deliberate, permanent choice, not an interim one: the (currently unwired) NSwag template-override predicate that would let the generated client return a typed non-throwing 404 branch is hardcoded to fire only for HTTP 409, so a typed-client `try/catch` path for 404 buys nothing today. Do not add a typed 404 `ProducesResponseType` to `GetShoptetInfo`/`GetVisitorInfo` on the controller.
- All hand-declared DTO interfaces (`ConversationSummaryDto`, `ConversationPresenceDto`, `ConversationDto`, `MessageDto`, `ListConversationsResponse`, `GetConversationResponse`, `ShoptetCustomerSnapshotDto`, `ShoptetOrderSnapshotDto`, `ShoptetContactInfoDto`, `GetSmartsuppShoptetInfoResponse`, `VisitorPageDto`, `VisitorInfoDto`, `GetSmartsuppVisitorInfoResponse`, `CloseConversationResponse`) are deleted. `ConversationDto`, `MessageDto`, `GetConversationResponse` (and any other generated type still consumed by sibling hooks) are re-exported from this file from `../generated/api-client`, so `useSendMessage.ts`'s existing import path keeps working unchanged.
- `otherActiveViewers` types against the generated `ConversationDto`/`ConversationPresenceDto`.
- Every field read across `ConversationDto`/`ConversationSummaryDto`/`ConversationPresenceDto` and consumers (`activeViewers`, `assignedAgentIds`, `contactProperties`, `variables`) is verified present with a compatible type on the generated DTO before deleting the corresponding hand-declared interface; no `as any`/`as unknown as X` casts are introduced to paper over a mismatch — a genuine mismatch is escalated as a backend contract defect instead.

### `frontend/src/components/customer-support/smartsupp/hooks/useGenerateDraftReply.ts`
`mutationFn` calls `smartsupp_GenerateDraftReply(conversationId, new GenerateDraftReplyBody({ topic: topic ?? null }))` inside `try/catch`. Same two-channel pattern as `useCloseConversation`: resolved-response `success`/`errorCode` check for the 200 path, plus exception catch for the untyped 400/404/503 statuses (no assumption that the exception carries a typed `errorCode`). `DraftReplySource`/`DraftReplyResult` (the hook's own return-shape types) may remain local since they aren't 1:1 wire-DTO mirrors, but are populated from the generated `GenerateDraftReplyResponse`'s `sources`/`answer`/`id` fields. `ERROR_MESSAGES` Czech mapping is preserved.

### `frontend/src/components/customer-support/smartsupp/hooks/useSendMessage.ts`
`mutationFn` calls `smartsupp_SendMessage(conversationId, new SendMessageBody({ content, draftLogId }))`. Same two-channel try/catch pattern as above, preserving `SEND_ERROR_MESSAGES`/`messageForSendError` output unchanged. Optimistic-update sequencing (`onMutate`/`onSuccess`/`onError`, `SMARTSUPP_QUERY_KEYS.conversation`, cache rollback) is functionally unchanged; `MessageDto`/`GetConversationResponse` are imported from `useSmartsupp.ts`'s re-export, matching the current import layout. `data.messageId`/`data.createdAt` field access continues to work unchanged against the generated `SendMessageResponse` (confirmed `messageId?: string`, `createdAt?: Date` match current usage by name); only the optimistic message's `createdAt` construction changes from `new Date().toISOString()` (string) to `new Date()` (Date), per the `Date`-vs-`string` decision below.

### `frontend/src/components/customer-support/smartsupp/hooks/useSubmitDraftReplyFeedback.ts`
`mutationFn` calls `smartsupp_SubmitDraftReplyFeedback(new SubmitDraftReplyFeedbackRequest({ logId, precisionScore, styleScore, comment }))` inside `try/catch`, mirroring the codebase's canonical `useSubmitArticleFeedbackMutation` (`frontend/src/api/hooks/useArticles.ts`) pattern: on success returns `{}` (no special result); on a caught exception with `.status === 409`, returns `{ alreadySubmitted: true }`; any other caught exception rethrows/propagates as a mutation error. Hand-declared `SubmitDraftReplyFeedbackRequest`/`SubmitDraftReplyFeedbackResult` interfaces are deleted in favor of the generated request type plus a thin local result type wrapping the generated response (since `alreadySubmitted` is not itself a wire field).

### `frontend/src/components/customer-support/smartsupp/hooks/useSmartsuppDraftReplyFeedbackListQuery.ts`
`queryFn` calls `smartsupp_GetDraftReplyFeedbackList(pageNumber, pageSize, sortBy, sortDescending, hasFeedback, userId)` directly — no manual `URLSearchParams` construction. The generated response is passed through the shared `ragFeedbackMapping.ts` mapper (see below) to produce the `ragFeedbackTypes.ts`-shaped result consumers already expect. `staleTime`/`gcTime` are unchanged.

### `frontend/src/components/feedback/ragFeedbackMapping.ts` (new)
Extracted from `useKnowledgeBase.ts`'s existing `toLocalFeedbackChunk`/`toLocalFeedbackListResponse` functions. `smartsupp_GetDraftReplyFeedbackList` and `knowledgeBase_GetFeedbackList` return the identical generated classes (`RagFeedbackLogSummary`, `RagFeedbackStatsDto`), so this single mapper — typed against those shared generated classes — covers both call sites, converting the generator's `Date`/`undefined` shapes into `ragFeedbackTypes.ts`'s `string`/`null` shapes. `useKnowledgeBase.ts` is updated to import from here instead of keeping its own copy; this is the module's only change (no other KB-specific hooks/types touched).

### `frontend/src/api/smartsuppClient.ts`
Deleted. `asInternal()`/`getClientAndBaseUrl()`/`apiGet`/`apiPost`/`apiDelete` and the private-field cast (`ApiClientInternal`) are removed entirely; confirmed no importers remain outside the five hook files this change touches.

### Test / regression-guard updates (in scope, not new components)
- `useCloseConversation.test.ts`, `usePresenceHeartbeat.test.ts`, `useSmartsuppVisitorInfo.test.ts`, `useGenerateDraftReply.test.ts`, `useSendMessage.test.ts` currently mock `getAuthenticatedApiClient()` to return `{ baseUrl, http: { fetch: mockFetch } }`; each is rewritten to mock the specific `smartsupp_*` method(s) it exercises.
- `frontend/src/api/__tests__/authenticated-api-usage.test.ts`: add `"useSmartsupp.ts"` to the `MIGRATED_HOOKS` regression-guard set, closing the gap that let the private-field cast go undetected (its `hasLegacyAsAnyFetch` carve-out currently treats `smartsuppClient` as accepted). The four component-level hooks under `components/customer-support/smartsupp/hooks/` remain outside this test's scanned `apiHooksDir` scope — noted as a known residual gap, not fixed by this change.

## Data Schemas

No new data model or backend contract changes. This change re-points frontend hooks from hand-maintained mirror types to the generator-owned equivalents already produced from the backend's existing response classes (`frontend/src/api/generated/api-client.ts`).

| Hand-declared type (deleted) | Generated equivalent | Backend source (use case response) |
|---|---|---|
| `ConversationSummaryDto` | `ConversationSummaryDto` / `IConversationSummaryDto` | conversation summary DTO |
| `ConversationPresenceDto` | `ConversationPresenceDto` / `IConversationPresenceDto` | presence DTO |
| `ConversationDto` | `ConversationDto` / `IConversationDto` | conversation DTO |
| `MessageDto` | `MessageDto` / `IMessageDto` | message DTO |
| `ListConversationsResponse` | `ListConversationsResponse` / `IListConversationsResponse` | `ListConversations` |
| `GetConversationResponse` | `GetConversationResponse` / `IGetConversationResponse` | `GetConversation` |
| `ShoptetCustomerSnapshotDto`, `ShoptetOrderSnapshotDto`, `ShoptetContactInfoDto`, `GetSmartsuppShoptetInfoResponse` | `IShoptetContactInfoDto` (+ nested), `GetSmartsuppContactShoptetInfoResponse` | `GetContactShoptetInfo` |
| `VisitorPageDto`, `VisitorInfoDto`, `GetSmartsuppVisitorInfoResponse` | `IVisitorInfoDto` (+ nested), `GetVisitorInfoResponse` | `GetVisitorInfo` |
| `CloseConversationResponse` | `CloseConversationResponse` / `ICloseConversationResponse` (`extends BaseResponse`, `errorCode: ErrorCodes \| undefined`) | `CloseConversation` |
| `GenerateDraftReplyApiResponse` | `GenerateDraftReplyResponse` / `IGenerateDraftReplyResponse` | `GenerateDraftReply` |
| `SendMessageApiResponse` | `SendMessageResponse` / `ISendMessageResponse` (`messageId?: string`, `createdAt?: Date`) | `SendMessage` |
| `SubmitDraftReplyFeedbackRequest`/`Result` | `SubmitDraftReplyFeedbackRequest` / `SubmitDraftReplyFeedbackResponse` | `SubmitDraftReplyFeedback` |
| `DraftReplyFeedbackListParams`/`Response` | `smartsupp_GetDraftReplyFeedbackList` params + `GetDraftReplyFeedbackListResponse` (`{logs, totalCount, pageNumber, pageSize, totalPages, stats}`), sharing `RagFeedbackLogSummary`/`RagFeedbackStatsDto` with `knowledgeBase_GetFeedbackList` | `GetDraftReplyFeedbackList` |

**`Date` vs `string` boundary:** every generated Smartsupp DTO's date-ish field (`ConversationDto.lastMessageAt/createdAt/updatedAt`, `MessageDto.createdAt/deliveredAt`, `ConversationSummaryDto.lastMessageAt`, `ConversationPresenceDto.enteredAt`, `ShoptetContactInfoDto.cartUpdatedAt`, `ShoptetOrderSnapshotDto.orderDate`) is generator-typed `Date`, not `string`. This is adopted as-is end-to-end for FR-1 through FR-5 rather than re-wrapped to `string` at the hook boundary — the small set of downstream consumers with explicit `string` parameter types are widened instead:
- `frontend/src/components/customer-support/smartsupp/ConversationListItem.tsx` — `formatRelativeTime(dateStr?: string | null)` → widen to accept `Date`.
- `frontend/src/components/customer-support/smartsupp/MessageBubble.tsx` — `formatTime(dateStr: string)` → widen to accept `Date`.
- `ContactDetailsPanel.tsx`, `ConversationDetail.tsx`, `ConversationList.tsx`, `ShoptetCustomerCard.tsx` already do `new Date(fieldValue)` before use, which is idempotent for both `Date` and `string` inputs — no change needed there.

The `ragFeedbackTypes.ts` boundary (`RagFeedbackLogSummary`/`RagFeedbackStats`) is the one deliberate exception: it stays `string`-typed via the shared `ragFeedbackMapping.ts` mapper, since it is an existing shared contract already consumed as strings by both KnowledgeBase and Smartsupp feedback UI.

**Error-code typing:** `ErrorCodes` is a generated string enum. The hand-maintained `Record<string, string>` error-message maps (`CLOSE_ERROR_MESSAGES`, `ERROR_MESSAGES` in `useGenerateDraftReply.ts`, `SEND_ERROR_MESSAGES`) are retyped as `Partial<Record<ErrorCodes, string>>` (or equivalent), so a future backend rename of an error code produces a compile error in the message map as well as at the call site.

**No backend changes.** Every response DTO and `smartsupp_*` generated client method this design requires already exists in the current generated client; no OpenAPI/controller annotation changes are required or made as part of this change.
