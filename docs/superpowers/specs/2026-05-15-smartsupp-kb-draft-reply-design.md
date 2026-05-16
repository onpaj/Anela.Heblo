# Smartsupp ↔ Knowledge Base — AI Draft Reply

**Date:** 2026-05-15
**Status:** Approved design — ready for implementation plan

## Purpose

Give customer-support agents an AI-generated reply draft inside the Smartsupp
chat UI. The agent either clicks a predefined topic hint (e.g. "Reklamace") or a
free-form "Generovat odpověď" button. The system takes the whole conversation,
retrieves grounding context from the Knowledge Base (RAG), and generates a Czech
answer that matches the style of the current conversation. The answer lands
directly in the composer textarea where the agent can edit, regenerate, or
discard it before (eventually) sending.

This replaces the current static `KnowledgeBaseSuggestions` behaviour, which
inserts canned mock text.

## Background — current state

- `KnowledgeBaseSuggestions` renders a collapsible pill row above the composer
  and inserts **canned mock text** (`MOCK_SUGGESTIONS`) directly into the draft.
- `useKnowledgeBaseSuggestions` is a stub with a `TODO` to wire to the KB API.
- `ChatComposer` has a draft textarea, char counter (`MAX_CHARS = 4000`), and a
  disabled Send button (sending is not implemented yet — out of scope here).
- Backend KB `AskQuestion` does RAG over pgvector + Claude, but only accepts a
  `Question` string — no conversation history, no style input.

## Scope

**In scope:** topic hints, generate button, conversation-aware RAG generation,
style matching, in-composer editable preview with regenerate/discard, KB sources
tooltip.

**Out of scope:** sending the reply to Smartsupp (Send button stays disabled);
admin-managed hint lists; E2E tests (noted as follow-up — a full flow cannot
complete until sending is wired).

## Decisions

| Topic | Decision |
|-------|----------|
| Hint behaviour | Clicking a hint generates immediately; the topic only steers KB retrieval and focus, never replaces the conversation. |
| Hint source | Hardcoded frontend const list. |
| Preview UX | Answer fills the composer textarea directly ("AI draft" state); editable in place. |
| Controls | Toolbar above textarea with Regenerate + Discard; disappears once the agent edits manually or discards. |
| KB sources | Shown via an info-icon tooltip listing source filenames. |
| Style matching | Match tone/formality/length of existing `Agent:` lines; fallback = default polite formal Czech voice. |
| Backend placement | New `GenerateDraftReply` use case in the Smartsupp feature; KB reached only via the `SearchDocumentsRequest` mediator message. |
| Sources DTO | Smartsupp-local `DraftReplySource` DTO; handler maps KB's `SourceReference` into it. |

## Architecture & data flow

```
Agent clicks hint pill ("Reklamace")  OR  "Generovat odpověď" button
        │
        ▼
useGenerateDraftReply hook
        │  POST /api/smartsupp/conversations/{id}/draft-reply  { topic?: string }
        ▼
GenerateDraftReplyHandler (MediatR, Smartsupp feature)
        │  1. Load conversation + messages from SmartsuppRepository
        │  2. Build transcript (ordered, role-labelled)
        │  3. Derive retrieval query (topic ?? recent contact messages)
        │  4. IMediator.Send(SearchDocumentsRequest)  → KB module
        │  5. Build prompt: transcript + KB context + topic + style rules
        │  6. IChatClient.GetResponseAsync()
        ▼
Response { answer, sources[] }
        │
        ▼
Composer enters "AI draft" state — answer fills textarea,
toolbar shows Regenerate / Discard, sources behind a tooltip
```

The whole conversation is always sent to the model. The topic hint only steers
KB retrieval and focus.

## Backend

New use case under
`Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/`:

- **`GenerateDraftReplyRequest : IRequest<GenerateDraftReplyResponse>`**
  - `ConversationId` (string, from route)
  - `Topic` (string?, optional, max ~100 chars)
- **`GenerateDraftReplyResponse : BaseResponse`**
  - `Answer` (string)
  - `Sources` (`List<DraftReplySource>`)
- **`DraftReplySource`** — Smartsupp-local DTO: `DocumentId`, `Filename`,
  `Excerpt`, `Score`. Handler maps KB's `SourceReference` into it.
- **`GenerateDraftReplyHandler`** — performs steps 1–6.

**Endpoint** on the existing Smartsupp controller:
`POST /api/smartsupp/conversations/{id}/draft-reply` — protected (requires
login). Body `{ "topic": "Reklamace" }` or `{}`.

**Transcript build:** messages ordered by `CreatedAt`; each line role-labelled
(`Zákazník:` / `Agent:` / `Bot:`); no-op/system events and empty-content
messages skipped.

**Retrieval query:** `Topic` if present; otherwise the concatenation of the last
1–3 contact messages. Sent as `SearchDocumentsRequest` with `TopK = 5`.

**Prompt:** a new `DraftReplySystemPrompt` template in a Smartsupp options class
(mirroring `KnowledgeBaseOptions.AskQuestionSystemPrompt`, configured in
`appsettings.json`). Placeholders: `{transcript}`, `{context}`, `{topic}`. Style
rule: match tone/formality/length of the existing `Agent:` lines; if there are
none, use a default polite formal Czech voice. The answer must be grounded only
in `{context}`; no KB hit → a safe "nenašla jsem k tomu relevantní informaci"
reply (consistent with `AskQuestionHandler`).

**Module boundary:** Smartsupp → KB only via the `SearchDocumentsRequest`
mediator message. The KB module is untouched.

## Frontend

**Trigger bar** — reshape the existing `KnowledgeBaseSuggestions` area above the
composer:

- Hint pills (hardcoded const): `Výměna zboží`, `Reklamace`, `Doprava`,
  `Platba`, `Vrácení zboží`. Click → generate with that topic.
- `Generovat odpověď` button (`Sparkles` icon) → generate, no topic.
- Whole bar disabled while a generation is in flight.
- Collapse / `localStorage` logic from the old component is removed — the bar is
  always-visible triggers, not collapsible content.

**Composer states (`ChatComposer`):**

- *Idle* — plain textarea (current behaviour).
- *Generating* — textarea shows a loading/skeleton state; trigger bar disabled.
- *AI draft* — generated answer fills the textarea; a small toolbar above it
  shows **Regenerovat** (re-runs the last request — same topic or none),
  **Zahodit** (clears back to idle), and a **sources tooltip** (info icon
  listing source filenames). Toolbar disappears once the agent edits the text
  manually, or on Zahodit.
- `MAX_CHARS` (4000) cap and char counter unchanged.

**New hook** `useGenerateDraftReply(conversationId)` — exposes `generate(topic?)`,
`isLoading`, `error`, `result`. Built on the project API-hook pattern (absolute
URL `${apiClient.baseUrl}…`). Replaces the `useKnowledgeBaseSuggestions` mock
entirely.

## Error handling & edge cases

- **AI unavailable** — handler catches `HttpRequestException` /
  `TimeoutException` / `TaskCanceledException` / `ObjectDisposedException` (as
  `AskQuestionHandler` does) and returns `Success = false` with error code
  `SmartsuppDraftReplyAiUnavailable`. FE shows an inline error in the trigger bar
  with a retry; composer stays idle.
- **No KB hits** — the model still runs but is instructed to produce the safe
  "nenašla jsem k tomu relevantní informaci" reply; `Sources` empty.
- **Empty / agent-only conversation** — no contact messages and no topic ⇒ empty
  retrieval query. Handler returns `Success = false` with a clear message
  ("Konverzace neobsahuje zprávu zákazníka"). FE disables the
  `Generovat odpověď` button in that case; hint pills still work (a topic gives
  retrieval something to query).
- **Conversation not found** — 404 via `BaseResponse` error code.
- **Overwriting an edited draft** — if the agent has manually edited an AI draft,
  Regenerate or a hint click asks for confirmation before overwriting.
- **Concurrent clicks** — trigger bar disabled during flight; no request
  stacking.

## Testing

Target 80%+ coverage (project rule).

- **Backend unit** (`backend/test/.../Features/Smartsupp/`) —
  `GenerateDraftReplyHandlerTests`: transcript building, topic-vs-fallback
  retrieval query, style instruction wiring, no-KB-hit path, AI-unavailable path,
  empty-conversation path. Mock `IMediator`, `IChatClient`, `SmartsuppRepository`.
- **Backend integration** — controller test: auth required, 404, happy path.
- **Frontend unit** — `useGenerateDraftReply` hook (loading/error/result);
  `ChatComposer` state transitions (idle → generating → AI draft → discard);
  trigger bar disabled states; overwrite confirmation; sources tooltip.
- **E2E** — follow-up; deferred until sending is wired.

## Validation before completion

- BE: `dotnet build` + `dotnet format`
- FE: `npm run build` + `npm run lint`
- All touched tests pass
