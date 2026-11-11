# Handoff: Smartsupp UX — 1/3 Composer (emoji, formatting, files)

Brainstorm held 2026-10-16 between Ondrej and Claude. Part of a three-way split of one round of
operator feedback. Sibling handoffs:
[2/3 Contact history](smartsupp-ux-2-history.md) ·
[3/3 Visitor context](smartsupp-ux-3-context.md).

**Nothing is designed in detail or built yet.** Continue the `superpowers:brainstorming` flow on the
**architectural path**, starting at "Propose 2-3 approaches". Run the spikes in §5 first, because
their answers decide the design.

## 1. Why this exists

Operator feedback (daily user of the Heblo chat, Czech original):

> Já ho používám denně, ale pořád je mi smartupp sympatičtější - v heble nejdou používat smajlíci,
> nejde zvýraznit text, nejde nahrát soubor. Navíc ve smartupp krásně vidím i historii chatu s danou
> osobou a vidím co si prohlížela, o co se zajímá. To v heble taky nevidím. Takže vlastně pořád
> koukám i do smartupp a odepisuju přes heblo.

**Goal:** an operator can handle a whole chat in Heblo without opening Smartsupp. This handoff
covers the composer half: **emoji, text emphasis, sending files**. It also covers one gap the
operator didn't name: **files customers send are invisible in Heblo today**.

## 2. Decisions already made

1. Ondrej: "let's work on all of it". All three areas are in scope, each as its own
   spec → plan → PR cycle.
2. The goal is parity in the operator's daily flow, not a pixel copy of the Smartsupp agent app.
3. Smartsupp stays the transport. Heblo sends through the Smartsupp REST API as the operator's
   mapped agent (`SmartsuppSendMessageOptions.AgentMap`). There is no change to how agent
   identity works.

## 3. Current state (verified 2026-10-16)

**Frontend**: `frontend/src/components/customer-support/smartsupp/`
- `ChatComposer.tsx` (199 lines):
  - plain `<textarea>` (around L148), expand toggle, `/ 4000` counter (L16), Ctrl/Cmd+Enter
    sends (L100-101)
  - AI draft bar: `DraftReplyTriggerBar`, `DraftReplyToolbar`, `TopicPickerSheet`
  - per-conversation drafts are kept in `SmartsuppChatsPage` state (`drafts`)
- `MessageBubble.tsx` (79 lines):
  - renders `content` as plain text with `whitespace-pre-wrap` (L63)
  - no link detection, no attachment rendering
- `hooks/useSendMessage.ts` sends the text with an optimistic update.
- Dependencies: `react-markdown` is present. There is no emoji library.

**Backend**
- Send endpoint: `POST api/smartsupp/conversations/{id}/messages` →
  `UseCases/SendMessage/SendMessageHandler.cs` → `SmartsuppApiClient.SendMessageAsync`
  (around L558-575).
- Body sent today: `{ content: { type: "text", text }, agent_id }`. Text only.
- Incoming attachments:
  - `SmartsuppPayloadMapper.cs:91` stores `data.message.attachments` raw into
    `SmartsuppMessage.AttachmentsJson`
  - `SmartsuppMessage.cs:26` has the column
  - nothing reads it: no DTO field, no UI
- Docs: `docs/integrations/smartsupp-api.md` (send section, L40-70) and
  `docs/integrations/smartsupp-webhook.md` (`attachments: []` in samples, L512+). Neither has a
  real non-empty attachment sample.

## 4. What the Smartsupp API allows

Source: the live OpenAPI spec `https://api.smartsupp.com/v2/specs/open-api.json`, read
2026-10-16. Copy what you rely on into `docs/integrations/smartsupp-api.md` **before** using it
(that doc's living-document rule, and CLAUDE.md's "document before use" rule).

`POST /conversations/{id}/messages` request body:

```jsonc
{
  "sub_type": "agent",            // default
  "agent_id": "301345",           // required for agent
  "content": {
    "type": "text",               // always "text"
    "text": "plain text",         // either text …
    "html": "a <a href=…>link</a><br>next line"  // … or html. ONLY <a> and <br> allowed, http/https/mailto links
  },
  "attachments": [                // maxItems 5
    { "type": "image", "url": "https://…/x.png" },
    { "type": "file",  "url": "https://…/x.pdf", "file_name": "x.pdf", "file_type": "application/pdf" },
    { "type": "upload", "token": "<from upload API>", "file_name": "…", "file_type": "…" },
    { "type": "cards", "layout": "carousel", "items": [ { "title", "description", "price", "image",
        "actions": [ { "type": "open_url", "title": "…", "value": "https://…" } ] } ] }
  ],
  "ext_id": "your-own-message-id" // optional; GET /messages/find?ext_id= looks it up
}
```

Upload flow:
- `GET /files/upload/token` (scope `files:upload`) → `{ token, url }`, where url looks like
  `https://files.smartsupp.com/upload/<token>`
- the file is then sent with attachment `type: "upload"` and that `token`
- **The spec does not describe how the bytes are put to `url`** (method, field name, size and
  type limits). This needs a spike.

Also available: `POST /conversations/{id}/typing { active: bool }`, which sends an "agent is
typing" indicator to the widget.

### Implications

| Request | Feasibility |
|---|---|
| **Emoji** | ✅ Trivial. Emoji are Unicode in `text`. Only the UI picker is missing. Rendering incoming emoji already works. |
| **Bold / italic** | ❌ **Not possible through the API.** `html` allows only `<a>` and `<br>`. Open question: what does the operator see as "zvýraznění" in Smartsupp's own app? It might be markdown-ish `*bold*` that the widget renders. That needs a spike before we promise anything. |
| **Clickable links** | ✅ Through `html` with `<a>`. Probably the most useful "formatting" for e-shop support (product links, tracking links). |
| **Send a file / image** | ✅ Through the upload-token flow, which needs a spike, or through `type: file/image` with a public URL. Avoid the public URL route: it means hosting customer-facing files ourselves. |
| **See customer files** | ✅ The data is already stored. Only a DTO field and rendering are missing. |
| **Product cards** (not requested) | ✅ `cards` carousel. A Heblo-only advantage: "send product card" from the catalogue. **Out of scope** unless Ondrej pulls it in. Note it in the spec as a follow-up. |

## 5. Spikes to run first (each a short, throwaway probe against the live account; no sandbox exists)

1. **Token scopes.** Does our `Smartsupp:ApiToken` have `files:upload`? Call
   `GET /files/upload/token`. A 403/401 means the token has to be reissued with that scope.
   Ondrej does this in the Smartsupp admin, and the secret goes to Key Vault
   (`Smartsupp--ApiToken`), not App Settings.
2. **Upload mechanics.** Find out how to put bytes to the returned `url`: try multipart `POST`
   with field `file`, then a raw `PUT`. Record the response, size limit and allowed types. Then
   send a test message with `type: "upload"` into an internal test conversation and check how it
   looks in the widget.
3. **What "zvýraznit" means.** Have the operator send a formatted message from the Smartsupp agent
   app into a test chat. Inspect the webhook audit entry (`SmartsuppWebhookAuditEntries`) to see
   whether `content` carries `html`, markdown in `text`, or something else. Then decide: mirror
   it, or settle on links + line breaks only.
4. **Real incoming attachment payload.** Pull one `conversation.contact_replied` audit entry with a
   non-empty `attachments` array from prod and document its shape in `smartsupp-webhook.md`.
   Check whether the URLs are publicly fetchable or need auth, which decides whether the FE can
   use `<img src>` directly or we must proxy.

## 6. Suggested scope (to be confirmed in the design step)

**A. Show incoming attachments** (cheap, high value; ship first)
- Map `AttachmentsJson` → `List<SmartsuppAttachmentDto>` on the message DTO. The DTO must be a
  class, not a record (CLAUDE.md).
- `MessageBubble`: images as thumbnails that open full size; other files as a chip with name and
  icon.
- Our own sent attachments should come back through `agent_replied` the same way, so the same
  rendering covers both directions.

**B. Emoji picker**
- A button in the composer opens a picker and inserts at the cursor. Pick a small, maintained
  library and check bundle size (CRA build), or use a curated set of the ~30 emoji the team
  actually uses. YAGNI favours the curated set. Decide in the design step.
- Typing `:)` and similar stays as-is. No auto-conversion unless asked.

**C. Links in sent messages**
- Linkify URLs automatically and send as `content.html` (escape everything, then wrap URLs in
  `<a>`, `\n` → `<br>`). The backend must sanitise and allow only `<a href=http|https|mailto>`
  and `<br>`. Never pass operator HTML through unchecked.
- `MessageBubble` should also linkify incoming text, and render incoming `html` safely if any
  ever arrives.

**D. Send files**
- Composer: paperclip button, drag-and-drop and paste-image, up to 5 per message (API limit).
  Show a preview before sending.
- Backend: new endpoint (multipart) → validate type and size → get upload token → upload →
  send message with `attachments: [{type:"upload", token, file_name, file_type}]` and optional
  text.
- Do not store files in Heblo. Smartsupp hosts them, and the echo comes back through the
  webhook.
- Errors must be user-facing in Czech: too large, wrong type, upload failed. The text-only
  message must still be sendable if the upload fails.

**E. Formatting (bold/italic)**: only if spike 3 shows a path. Otherwise tell the operator
honestly that the Smartsupp API doesn't allow it, and offer links + line breaks.

**Optional, cheap:** a typing indicator through `/typing` while the operator types (debounced,
`active:false` on blur or send). Include it only if the design step agrees.

## 7. Constraints and gotchas

- **No sandbox.** Every spike call hits the live Smartsupp account. Use an internal test
  conversation (the operator opens one on anela.cz) and never a real customer chat.
- Generated client and FE error handling: the generated client **throws** on non-200, so read
  `errorCode` from the caught exception, not from `response.success`. New `ErrorCodes` also need
  the `ErrorHandlingTests` range bucket and a Czech translation in `i18n.ts`.
- Multipart uploads through the NSwag-generated TS client can be awkward. A hand-built `fetch`
  with an absolute URL (`${apiClient.baseUrl}/…`) is acceptable (CLAUDE.md absolute-URL rule).
- Process docs: `docs/processes/flow-smartsupp-reply.md` owns the send path. Update it in the
  same PR and run `python3 scripts/process-docs/check.py index`, or the 📚 Process Docs CI gate
  fails.
- FE validation: `CI=false npm run build` + `npm run lint`. `tsc` alone gives false greens. Run FE
  tests with `react-scripts test`.

## 8. Success criteria

- The operator sends a reply with an emoji, a clickable link and an attached photo or PDF without
  leaving Heblo, and the customer sees all three correctly in the widget.
- A photo the customer sends shows up in the Heblo conversation.
- Formatting: either works (spike 3) or the operator has been told plainly why not.
- Tests: BE handler tests for send-with-attachments and HTML sanitisation (allow-list,
  escaping); FE tests for the composer attachment flow and attachment rendering.
