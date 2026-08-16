## Module
Customer Support (Smartsupp) — module-map part #29

## Finding
Every Smartsupp frontend hook in the part's `Owns` list reaches the backend **without** the generated typed API client. They go through `frontend/src/api/smartsuppClient.ts`, whose `asInternal()` helper casts the NSwag client to its private transport:

```ts
// frontend/src/api/smartsuppClient.ts:5-12
type ApiClientInternal = {
  baseUrl: string;
  http: { fetch: (url: string, init: RequestInit) => Promise<Response> };
};
function asInternal(apiClient: ...): ApiClientInternal {
  return apiClient as unknown as ApiClientInternal;
}
```

Call sites, all inside this part's `Owns` paths:

- `frontend/src/api/hooks/useSmartsupp.ts:3` imports `getClientAndBaseUrl, apiGet, apiPost, apiDelete`; used at `:110`, `:123`, `:184`, `:203`, `:238`, `:283`, `:296`.
- `frontend/src/components/customer-support/smartsupp/hooks/useGenerateDraftReply.ts:2,57`
- `frontend/src/components/customer-support/smartsupp/hooks/useSendMessage.ts:2,53`
- `frontend/src/components/customer-support/smartsupp/hooks/useSubmitDraftReplyFeedback.ts:2,24`
- `frontend/src/components/customer-support/smartsupp/hooks/useSmartsuppDraftReplyFeedbackListQuery.ts:2,45`

The same files hand-declare DTOs the generator already owns — `ConversationSummaryDto` (`useSmartsupp.ts:6`), `ConversationPresenceDto` (`:14`), `ConversationDto` (`:22`), `MessageDto` (`:59`), `ListConversationsResponse` (`:74`), `GetConversationResponse` (`:82`), `ShoptetContactInfoDto` (`:149`), `VisitorInfoDto` (`:164`), `CloseConversationResponse` (`:217`), plus `GenerateDraftReplyApiResponse` (`useGenerateDraftReply.ts:20`), `SendMessageApiResponse` (`useSendMessage.ts:9`) and `DraftReplyFeedbackListResponse` (`useSmartsuppDraftReplyFeedbackListQuery.ts:20`).

The generated client already exposes **every one** of these endpoints, typed: `frontend/src/api/generated/api-client.ts:11735 smartsupp_GetConversations`, `:11788 smartsupp_GetConversation`, `:11832 smartsupp_GenerateDraftReply`, `:11891 smartsupp_GetShoptetInfo`, `:11935 smartsupp_GetVisitorInfo`, `:11979 smartsupp_SendMessage`, `:12038 smartsupp_SubmitDraftReplyFeedback`, `:12090 smartsupp_GetDraftReplyFeedbackList`, `:12144 smartsupp_CloseConversation`, `:12192 smartsupp_RecordPresence`, `:12229 smartsupp_RemovePresence` — with generated DTOs `ListConversationsResponse` (`:40031`), `ConversationDto` (`:40084`), `GetConversationResponse` (`:40416`), `MessageDto` (`:40477`).

## Rule
`docs/development/api-client-generation.md`, *Enforcement Rules*:

> 2. **ALWAYS use `getAuthenticatedApiClient()`** for standard typed calls, or `getApiBaseUrl()` + `getAuthenticatedFetch()` when you need to branch on HTTP status codes
> 3. **NEVER use `(apiClient as any)`** to access private fields — use public helper functions instead

and, in the same document:

> **❌ AVOID**: `(apiClient as any).baseUrl` and `(apiClient as any).http.fetch` — These reach into private fields of the NSwag-generated class. If NSwag renames those fields, the code breaks at runtime with no compile-time warning.

Wrapping the cast in a named helper (`asInternal`) does not change what it reaches for. This is an established, still-live arch-review class: #3873 (CatalogDocuments, open), #3833 (KnowledgeBase), #3823 (Expedition), #3816 (DataQuality), #3815 (Photobank), #3810 / #3802 / #3797 (Manufacture), #3852 (Packaging, fixed by PR #3857). No equivalent issue exists for Smartsupp.

## Why it matters
The hand-written interfaces are a second copy of a contract the generator rewrites on every build. When a backend DTO changes — a field added, renamed or re-typed — the generated client updates and these hooks keep compiling against the stale shape, so the drift surfaces as a runtime `undefined` in the chat UI instead of a compile-time error. The `as Promise<...>` casts on `response.json()` disable response type-checking entirely. Separately, `smartsuppClient.ts` depends on NSwag's private `http` and `baseUrl` fields surviving regeneration; a template change breaks all eleven call sites at runtime with no build failure.

## Suggested direction
Route these hooks through the generated `smartsupp_*` client methods and delete the hand-declared DTO interfaces in favour of the generated types, as `usePackingMaterials` was reworked under #3221. Two hooks branch on status codes and cannot use the plain typed call as-is — `useSubmitDraftReplyFeedback` (409 Conflict) and `useSmartsuppShoptetInfo` / `useSmartsuppVisitorInfo` (404 → `null`); the same document prescribes `getApiBaseUrl()` + `getAuthenticatedFetch()` for exactly that case, or annotating the controller action per the `useSubmitArticleFeedbackMutation` example. Do not implement the fix here — this issue only records the finding.

---
_Filed by arch-review of module-map part #29._
