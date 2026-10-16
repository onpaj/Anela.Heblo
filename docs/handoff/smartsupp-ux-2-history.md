# Handoff: Smartsupp UX — 2/3 Contact history

Brainstorm held 2026-10-16 between Ondrej and Claude. Sibling handoffs:
[1/3 Composer](smartsupp-ux-1-composer.md) · [3/3 Visitor context](smartsupp-ux-3-context.md).
The operator's feedback and the overall goal are quoted in full in 1/3, §1.

**Nothing is designed in detail or built yet.** Continue the `superpowers:brainstorming` flow on the
**architectural path**, starting at "Propose 2-3 approaches". §4 (fix what's broken) is a small,
separable PR and can ship first on the bounded path.

## 1. The complaint

> ve smartupp krásně vidím i historii chatu s danou osobou … To v heble taky nevidím.

**Goal:** while answering, the operator sees earlier conversations with the same person in
context, without leaving the current chat. Smartsupp shows them inline, above the current
conversation.

## 2. Key finding: Heblo already has history, and it is hidden and broken

The feature exists ("Jiné konverzace") but the operator effectively can't reach it:

1. **The right panel is closed by default.**
   - `pages/SmartsuppChatsPage.tsx` reads `readPanelOpen(CONTACT_PANEL_KEY, false)`, so the
     default is collapsed.
   - The only affordance is a 32 px chevron rail (`CollapsibleRail.tsx`).
   - The operator has probably never opened it.
2. **Clicking a past conversation does nothing.**
   - `ContactDetailsPanel` takes `onSelectConversation`, and `OtherConversationRow` uses it
     (`ContactDetailsPanel.tsx:13,57,222`).
   - But **neither call site passes it** (`SmartsuppChatsPage.tsx` around L131 desktop and L155
     mobile). Verified with grep 2026-10-16.
3. **Even if it were wired, it could fail.**
   - `selectedConversation` is resolved with `conversations.find(c => c.id === selectedId)` over
     the **currently loaded status list** (Open *or* Resolved).
   - A past conversation is usually Resolved, so selecting it from the Open tab finds nothing.
4. **Navigating away is the wrong UX anyway.**
   - Jumping to the old chat loses the current one.
   - Smartsupp shows history inline above the current conversation.

## 3. Data gaps (verified 2026-10-16)

- History comes from `SmartsuppRepository.ListConversationsForContactAsync`
  (`backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs:224`).
  - It filters on `ContactId == contactId`, newest first, and caps at `MaxOtherConversations = 20`.
  - It's used by `GetConversationHandler` (`OtherConversations`) and by
    `GetVisitorInfoHandler` (`ChatsCount`).
- **Only conversations Heblo received by webhook exist.** The integration went live around
  May 2026, so earlier chats are missing.
  - Nothing polls Smartsupp (`docs/processes/sync-smartsupp-webhooks.md:31`).
  - A missed webhook is never recovered.
- **Visitor-only chats aren't linked.**
  - A conversation without a `ContactId` has no history at all, even when the same `VisitorId`
    chatted before.
  - Smartsupp keys history on the visitor as well as the contact.
- The summary DTO carries only `Id`, `Status`, `LastMessageAt`, `LastMessagePreview` and
  `IsUnread`. There are no messages.
- Unused API client methods exist and could be reused for a backfill:
  - `SmartsuppApiClient` `POST conversations/search` (L78)
  - `GET conversations/{id}/messages?size=200` (L113)
  - Both are leftovers from a removed polling sync (`docs/processes/module-smartsupp.md:107`).

### Smartsupp API that matters (live spec `https://api.smartsupp.com/v2/specs/open-api.json`)

- `POST /conversations/search`:
  - body is `{ size, query: [{field, value, op}], sort: [{field: asc|desc}], after: [cursor],
    timezone }`, with at most 15 query clauses
  - examples show `status`, `finished_at` (range) and `created_at` sort
  - **The spec doesn't list the searchable fields.** Whether `contact_id` / `visitor_id` work is
    unverified and needs a spike.
- `GET /conversations/{id}/messages`: `size` (default 50), `after` cursor, `sort`.
- `GET /contacts/find`: exists. Check its parameters during the spike.

## 4. Fix what's broken (small, separable PR; can ship first)

- Wire `onSelectConversation` in both call sites.
- Make selection work for a conversation that isn't in the current list. For example, keep
  `selectedId` and fetch the detail by id (`GET conversations/{id}`) instead of `find` over the
  list.
- Make history discoverable without opening the panel:
  - **either** open the contact panel by default (stored preference still wins)
  - **or** add a "N předchozích konverzací" chip in the conversation header that opens history
  - Decide in design. The chip is less intrusive on narrow screens.
- Tests: an FE test proving the rows are clickable and that selecting a Resolved conversation from
  the Open tab works.

## 5. Proper fix: inline history (architectural)

### Target UX
Earlier conversations with the same person appear **above the current thread** in
`ConversationDetail`, Smartsupp-style:
- Each earlier conversation is a collapsed separator: "Konverzace 12. 9. 2026 · uzavřena ·
  ⭐ 5 · 6 zpráv".
- Clicking expands it inline (lazy-load its messages) in muted styling, read-only, with a "go to
  this conversation" link.
- The current conversation stays the one you reply to. The composer always targets it.

### Approaches to weigh (pick one in design)
1. **Local only (recommended start).** Use what Heblo stores.
   - Widen matching to `ContactId OR VisitorId`.
   - Add an endpoint that returns a past conversation's messages, which the existing
     `GET conversations/{id}` may already cover.
   - No new sync. History starts around May 2026.
2. **Local + one-off backfill.**
   - As (1), plus a manually triggered admin job.
   - It pages `POST /conversations/search` (closed conversations by `finished_at` range) and
     `GET …/messages`, then upserts through the existing repository.
   - This fills pre-webhook history once. Check the volume first and make it idempotent.
   - Long-running job rules: `[AutomaticRetry(Attempts=0)]`, and watch the 10 s Polly
     per-attempt timeout on HTTP and SQL.
3. **Live from Smartsupp per request.**
   - Query Smartsupp search by contact or visitor on each open.
   - Always complete, but it depends on unverified search fields, adds latency and rate limits
     (429) on every chat open, and duplicates data we already store.
   - Not recommended.

### Decisions the design must settle
- How far back to show by default (last 3 expanded-ready, then "load more"?). The repository cap
  of 20 exists today.
- Whether AI draft-reply (`draft-reply` endpoint) should also read earlier conversations as
  context. Probably yes and cheap, but it's a scope decision for Ondrej.
- Whether the conversation list item shows a "returning customer" marker.

## 6. Constraints and gotchas

- Process docs: `docs/processes/module-smartsupp.md` (and `sync-smartsupp-webhooks.md` if a
  backfill job is added) must be updated in the same PR, plus
  `python3 scripts/process-docs/check.py index`. A new Hangfire job needs its own `job-*.md`.
- Shared `DbContext` poisoning: a failed `SaveChanges` during a bulk backfill leaves entities
  tracked. Detach on `DbUpdateException` (see memory "shared DbContext poison").
- EF InMemory can't run `ExecuteDelete/Update`. Persistence tests should verify through a
  **second** context (FindAsync aliasing makes same-context assertions vacuous).
- Raw-JSON FE hooks return dates as strings. Don't call `.getTime()` on them unparsed.
- DTOs are classes, not records.

## 7. Success criteria

- Opening any chat from a returning customer shows "N předchozích konverzací" visibly, without
  touching the side panel.
- The operator expands a previous conversation and reads it **while staying in the current one**,
  then replies.
- A visitor who chatted before without leaving contact details is still recognised (VisitorId
  match).
- If backfill is chosen: conversations from before May 2026 appear for a known long-time
  customer.
