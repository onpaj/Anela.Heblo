# Smartsupp composer: emoji, links, files — design

Date: 2026-10-16 · Area 1/3 of the Smartsupp UX round
(handoff: `docs/handoff/smartsupp-ux-1-composer.md`; siblings 2/3 history, 3/3 context are out of scope).

## 1. Intent

The operator who answers Smartsupp chats every day still keeps the Smartsupp agent app open next to
Heblo, because Heblo's chat can't do things they need (Czech original in the handoff §1):
no emoji, no emphasis, can't upload a file. On top of that, **files customers send show up in Heblo
as empty bubbles**: a customer sending a photo of a damaged product is invisible.

Goal: for the composer half of their flow, Heblo is enough. They can reply with an emoji, a clickable
link and an attached photo or PDF, and they see what the customer sent, without opening Smartsupp.

## 2. Spike findings (2026-10-16, live account, test conversation "Ondra - Test3" `coTaX8UFWh4Sz`)

These decide the design. They must be copied into the integration docs (§7) as part of the work.

1. **Token scope.** The prod `Smartsupp--ApiToken` already has `files:upload`.
   `GET /files/upload/token` → `200 {"token":"KfJ0DU-bQ","url":"https://files.smartsupp.com/upload/KfJ0DU-bQ"}`.
2. **Upload mechanics.** `POST <url>` as `multipart/form-data`, field name `file`, **no
   Authorization header** (the token in the URL is the credential). Response `200`:
   ```json
   {"size":177,"type":"image/png","name":"test.png",
    "path":"/accounts/924500/attachments/7m8eef-Fr.png",
    "url":"https://files.smartsuppcdn.com/files/accounts/924500/attachments/7m8eef-Fr.png",
    "expireAt":"2026-10-17T07:54:14.899Z","height":64,"width":64,
    "thumb400":{"path":"…_thumb400.jpg","width":64,"height":64,"expireAt":"…"},"ok":true}
   ```
   An upload that is never attached to a message expires after 24 h. Once attached, the CDN serves
   it with `cache-control: public, max-age=31557600`. Size and type limits are not published and
   were not probed.
3. **Sending with an attachment.** `POST /conversations/{id}/messages` with
   `{"agent_id":"1257997","content":{"type":"text","text":"Test příloha 🙂"},
     "attachments":[{"type":"upload","token":"QlTT4XXa2","file_name":"test.png","file_type":"image/png"}]}`
   → `200`. The image rendered in the customer widget. The response (and later message reads) carry
   the file in `attachments`:
   `[{"type":"image","file_name":"test.png","file_type":"image/png","url":"https://files.smartsuppcdn.com/…png","size":177,"width":64,"height":64,"thumb_400":{"url":"…_thumb400.jpg","width":64,"height":64}}]`.
   Text and attachment travel in one message. **The `conversation.agent_replied` webhook echo carries
   the same `attachments` array** — verified in prod: `SmartsuppMessages.AttachmentsJson` for
   `msSGp4tJDyvVx` holds it, written by today's mapper. So the webhook echo is enough to persist
   sent files; Heblo does not need to store the sent message itself.
4. **Customer-sent files use a different shape.** A file sent from the widget is its own message
   with **`content.type:"upload"`, `content.text:null`** and the file in **`content.data`**:
   `{"type":"file","file_name":"1040963052.pdf","file_type":"application/pdf","url":"https://files.smartsuppcdn.com/files/accounts/924500/uploads/sEYrSOqaL.pdf","size":598612}`.
   `attachments` stays `[]`. Verified in the webhook audit too: event `conversation.contact_replied`,
   `data.message.content` = `{"type":"upload","text":null,"data":{…file…}}`. An agent file sent from the Smartsupp app came in `attachments` with
   `type:"file"` (no thumb). Last month: 4 customer uploads (3 JPEG, 1 PDF), 1 agent upload.
   CDN URLs are public (no auth): the FE can use them directly in `<img src>` / `<a href>`.
5. **No bold exists.** The operator typed `*tucne*` in the Smartsupp agent app; the widget showed
   the asterisks verbatim. The Smartsupp app has no formatting either.
6. **Links need no send-side work.** A plain-text URL in `content.text` is rendered clickable by the
   widget, with a link-preview card. `content.html` (only `<a>`, `<br>` allowed) is stored by
   Smartsupp as `Odkaz anela.cz [https://www.anela.cz]\ndruhý řádek`, which reads worse. We keep
   sending plain text.
7. **Emoji already work end to end.** Unicode emoji in `text` reach the widget and render in Heblo.
   Usage in 369 agent messages: 75 distinct, led by 😊 236×, 🙂 77×, 🌿 63×, 🌸 52×, then
   🌙 ❤️ 💧 💡 👉 💛 🧴 ✨ 🥰 ⚠️ 💆 ✅ 💚 🎁 🙌 👍 🙏 😁 …

## 3. Goals, non-goals, anti-goals

**Goals**
- G1 Incoming and outgoing files appear in Heblo bubbles: images as thumbnails that open full size,
  other files as a chip (icon, name, size) that opens/downloads the file.
- G2 The operator can attach up to 5 files to a reply (paperclip, drag-and-drop, paste image),
  see them listed before sending, remove any, and send them with or without text.
- G3 An emoji button in the composer inserts emoji at the cursor.
- G4 URLs in Heblo message bubbles (both directions) are clickable.

**Non-goals**
- Bold/italic or any formatting (finding 5). The operator is told plainly (see §8).
- Sending `content.html`. Plain text only (finding 6).
- Product cards (`attachments[].type:"cards"`): noted as a possible follow-up, not built.
- Typing indicator (`POST /typing`): not built.
- Recovering files for messages older than the webhook audit retention (7 days). Those bubbles stay
  as they are.
- Storing files in Heblo. Smartsupp hosts them.
- Full emoji picker library or `:)` → 🙂 auto-conversion.

**Anti-goals** (would be a failure even if it "works")
- Losing the operator's typed text or chosen files when a send fails.
- Rendering `*text*` as italic/bold in Heblo when the customer sees asterisks (so: no
  `react-markdown` on message content).
- Any operator-controlled HTML reaching the DOM unescaped.
- A file reaching the customer that the operator didn't see in the pre-send list.

## 4. Decisions

Decided with Ondrej:
- D1 **Sending files: one backend endpoint does it all (approach A).** The composer posts text +
  files as multipart to the backend; the backend validates, gets one upload token per file, uploads
  each file to Smartsupp, then sends one message with text + all attachments. The upload token never
  reaches the browser. (Rejected: browser uploads directly to `files.smartsupp.com` — depends on
  Smartsupp CORS for our origin, validation would live only in the browser; upload-on-attach with
  staged tokens — needless state/cleanup at a few files a day.)
- D2 Smartsupp stays the transport; agent identity via `SmartsuppSendMessageOptions.AgentMap`, as
  today (handoff §2).

Claude's calls, stated to Ondrej and not objected to (check these):
- C1 Normalise both incoming shapes into the existing `SmartsuppMessage.AttachmentsJson` column
  **at mapping time** — no migration.
- C2 Curated emoji set (~24) from real usage, no library. The OS picker (Ctrl+Cmd+Space on macOS,
  Win+. on Windows) covers the rest.
- C3 Small in-house linkify for bubbles; not `react-markdown`, not `content.html`.
- C4 Limits: max 5 files (API limit), **10 MB per file**, allowed types: `image/jpeg`, `image/png`,
  `image/gif`, `image/webp`, `application/pdf`, Word/Excel (`.doc/.docx/.xls/.xlsx`), `text/plain`
  (`.jpg/.jpeg/.png/.gif/.webp/.pdf/.doc/.docx/.xls/.xlsx/.txt`).
  Smartsupp's own limits are unknown, so we fail early with a Czech message.
- C5 **The file extension decides the type** (from the builder check). Browsers often declare
  `application/octet-stream` or nothing for Office files; those are accepted when the extension is
  allowed. The backend derives the `file_type` it sends to Smartsupp from the extension, not from
  the browser. A declared *specific* type that contradicts the extension (e.g. `image/png` on
  `.pdf`) is rejected.

## 5. Design

### 5.1 Showing attachments (incoming and outgoing)

**Normalised shape** stored in `AttachmentsJson` (a JSON array; new rows only):
```jsonc
[{ "type": "image" | "file", "fileName": "x.png", "fileType": "image/png",
   "url": "https://files.smartsuppcdn.com/…", "size": 177,
   "thumbUrl": "https://…_thumb400.jpg" /* optional */ }]
```
- `SmartsuppPayloadMapper.MapMessage` (webhook path): build the list from `data.attachments[]`
  **plus**, when `content.type == "upload"`, from `content.data` (one item). Unknown attachment
  types (e.g. `cards`) are skipped. When the message carries no file, store `null` or `[]`
  (builder's choice; must be consistent with reading).
- `SmartsuppApiClient.MapMessage` (REST path, `SmartsuppMessageData.AttachmentsJson`) uses the same
  normaliser, so the two paths can't drift. Today nothing in the app consumes the REST message list
  except tests; keep it consistent anyway because the field already exists.
- **Reading old rows:** rows written before this change hold Smartsupp's raw `attachments` array
  (`file_name`, `file_type`, `thumb_400.url`, …). The DTO mapper must accept both the raw and the
  normalised field names, or the normaliser must be applied on read as well. Builder picks one;
  either way an old agent attachment row must render.
- `MessageDto` gains `List<MessageAttachmentDto> Attachments` (class, not record — CLAUDE.md). Map
  in `GetConversationHandler.MapMessageDto`. Malformed JSON → empty list + a warning log, never a
  500.
- **Backfill:** customer uploads from the last 7 days can be recovered by replaying their webhook
  audit entries (`ReplayWebhookEventHandler` re-runs the mapper). Do this once after deploy, by
  hand, for `contact_replied` entries whose body has `"type":"upload"`. Document the procedure in
  the process doc; no code for it.

**`MessageBubble`**
- Renders attachments under the text (or alone when there's no text, so an upload-only message no
  longer shows as an empty bubble).
- Image (`type: image`, or `fileType` starting `image/`): thumbnail (`thumbUrl` ?? `url`), max ~240
  px wide, rounded, `loading="lazy"`; click opens `url` in a new tab (`target="_blank"
  rel="noopener noreferrer"`). No lightbox component (YAGNI).
- Other files: a chip with a file icon (lucide), file name (truncated), human size (`584 kB`),
  opening `url` in a new tab.
- Only `https:` URLs are rendered; anything else is dropped.
- Works in both bubble colours and dark mode (`docs/design/ui_design_document.md`).

### 5.2 Clickable links in bubbles

- A small pure function splits message text into text and URL segments (`https?://…`, and
  `www.…` → `https://www.…`); trailing punctuation (`.,!?)` etc.) is not part of the link.
- Rendered as React nodes (`<a target="_blank" rel="noopener noreferrer">`), never via
  `dangerouslySetInnerHTML`. Applied to visitor and agent bubbles; system/bot lines unchanged.
- Link style must stay readable on the blue agent bubble (underline, inherit colour).

### 5.3 Emoji button

- A smile icon button in the composer opens a small popover grid of the curated set:
  😊 🙂 😁 🥰 😉 🙏 👍 🙌 👉 ✅ ⚠️ 💡 ❤️ 💛 💚 🤍 ✨ 🌿 🌸 🌙 💧 🧴 🎁 📦
  (order ≈ usage; builder may adjust within the spirit of "what the team uses").
- Click inserts at the textarea cursor (replacing a selection), keeps focus, moves the caret after
  the emoji, updates the draft the normal way (so `onDraftChange` / per-conversation drafts and
  the 4000 counter work). Popover closes on pick, Esc, or outside click.
- Emoji are counted by the existing `/ 4000` counter as JS string length (as today).

### 5.4 Sending files

**Composer (FE)**
- Paperclip button → hidden `<input type="file" multiple accept=…>`. Also drag-and-drop onto the
  composer and paste of images from the clipboard.
- Selected files show as a removable list above the textarea: image previews via
  `URL.createObjectURL` (revoked on remove/send/unmount), other files as name + size chips.
- Client-side checks mirror the server (max 5, 10 MB, allowed extensions per C5) and reject with a Czech
  message without adding the file. The server stays the authority.
- Send is enabled when there's text **or** at least one file. Ctrl/Cmd+Enter works the same.
- With files: a hand-built `fetch` (`FormData`) to the absolute URL
  `${apiClient.baseUrl}/api/smartsupp/conversations/{id}/messages/with-attachments`, through the
  authenticated client's `http.fetch` like `useIssuedInvoices.ts` does (CLAUDE.md absolute-URL
  rule). Without files: the existing `smartsupp_SendMessage` path, unchanged.
- Optimistic message as today, also showing the attachment list (using the local object URLs)
  while sending.
- **On failure:** text and files stay in the composer, a Czech error is shown, the optimistic
  message is rolled back. The operator can remove files and send text only.
- AI draft linkage (`draftLogId`) is passed along on the attachment path too.
- Switching conversation clears the pending file list (text drafts keep their current behaviour).

**Backend**
- New action on `SmartsuppController`:
  `POST api/smartsupp/conversations/{id}/messages/with-attachments`, `multipart/form-data`,
  fields `content` (optional string, ≤ 4000), `draftLogId` (optional), `files` (1–5 `IFormFile`).
  Same authorization as the existing send endpoint. Set a request-size limit on the action
  (≈ 5 × 10 MB + headroom). Follow the existing `IFormFile` controllers (e.g.
  `KnowledgeBaseController`) for how files reach MediatR.
- New use case `SendMessageWithAttachments` (vertical slice, handler + request/response classes +
  validator, registered like the module's other validators — validators are registered per
  module by hand). Either a new handler or the existing `SendMessageHandler` extended — builder's
  choice, but conversation lookup, agent mapping, the error handling of `HttpRequestException` /
  timeouts, and the RAG draft-log update must behave exactly as in today's send.
- Validation (server is authoritative): 1–5 files, each ≤ 10 MB, non-empty, extension in the
  allow-list, declared type either generic/empty or consistent with the extension (C5),
  `content` ≤ 4000. `file_type` sent to Smartsupp = mapped from the extension.
- Flow: lookup conversation → resolve agent → for each file: `GET files/upload/token`,
  `POST` bytes to the returned `url` (multipart, field `file`, original file name, no auth header;
  the URL must be `https://files.smartsupp.com/…` — reject anything else) → one
  `POST conversations/{id}/messages` with `content.text` (omit/empty when no text — **verify the
  API accepts an attachment-only message**; if it rejects, send without `content` or document the
  real requirement) and `attachments: [{type:"upload", token, file_name, file_type}]`.
- If any upload fails, **nothing is sent** (no partial message); return an error. Already-uploaded
  files just expire after 24 h.
- `ISmartsuppApiClient` gains `GetUploadTokenAsync` and `UploadFileAsync` (and `SendMessageAsync`
  gains an optional attachments parameter or an overload). Uploads use the existing resilience
  pipeline only if it doesn't retry non-transient 4xx (see memory: retrying
  `HttpRequestException` retries 4xx) and doesn't impose a timeout too short for a 10 MB upload.
- No local persistence of the sent message: the webhook echo stores it with its attachments, as
  for text today.

**New error codes** (27XX range, each needs the `ErrorHandlingTests` bucket and a Czech
`i18n.ts` translation):
- `SmartsuppAttachmentInvalid` (400): wrong type/extension, empty, too many.
  FE text e.g. „Soubor nelze odeslat — nepodporovaný typ nebo příliš mnoho souborů (max 5)."
- `SmartsuppAttachmentTooLarge` (400 or 413): „Soubor je větší než 10 MB."
- `SmartsuppAttachmentUploadFailed` (503): „Nahrání souboru selhalo. Zprávu můžete odeslat bez
  přílohy."
The FE reads `errorCode` from the caught exception / response body (generated client throws on
non-200; on the hand-built fetch path read the JSON body).

## 6. Error handling summary

| Situation | Result |
|---|---|
| File too big / wrong type / >5 (client) | Not added; inline Czech message |
| Same, bypassing client | 400 + specific code; composer keeps everything |
| Token or upload call fails | 503 `SmartsuppAttachmentUploadFailed`; nothing sent; composer keeps everything |
| Final message send fails | 503 `SmartsuppSendMessageUnavailable` (existing); composer keeps everything |
| Agent not mapped / conversation missing | Existing codes, same as text send |
| Malformed `AttachmentsJson` on read | Message shown without attachments; warning logged |

## 7. Documentation (same PR)

- `docs/integrations/smartsupp-api.md`: upload-token endpoint, upload POST mechanics + response,
  `attachments` request/response shapes, the html → text flattening, widget auto-linking, no bold.
  Write these **before** the code that relies on them (doc's living-document rule).
- `docs/integrations/smartsupp-webhook.md`: real non-empty samples — customer `content.type:"upload"`
  + `content.data`, agent `attachments[]` with `thumb_400`.
- `docs/processes/flow-smartsupp-reply.md`: send-with-attachments flow, limits, failure behaviour.
- `docs/processes/sync-smartsupp-webhooks.md`: attachment normalisation in the mapper; the 7-day
  replay backfill procedure.
- Run `python3 scripts/process-docs/check.py index`.

## 8. Telling the operator about bold

Not code. In the PR description (and for Ondrej to pass on): "Tučné písmo nejde ani ve Smartsuppu —
zákazník vidí hvězdičky tak, jak jsou. Odkazy stačí vložit jako text, chat je sám udělá klikací
i s náhledem."

## 9. Testing

- BE unit: mapper normalisation (customer upload in `content.data`; agent `attachments` with and
  without `thumb_400`; `cards` skipped; no files); DTO mapping incl. old raw rows and malformed
  JSON; handler: happy path (token → upload → send with all tokens), validation failures, upload
  failure sends nothing, agent mapping / conversation not found, draft-log update; API client:
  upload request shape asserted by **parsing** the multipart/JSON body, not substring matching;
  no auth header on the upload call; non-`files.smartsupp.com` upload URL rejected.
- FE (`react-scripts test`): `MessageBubble` renders image thumbnail, file chip, upload-only
  message, linkified text (and `*x*` stays literal, `javascript:` not linked); linkify unit tests;
  composer: add/remove files, client validation messages, send enabled with files only, failure
  keeps text+files, emoji inserts at cursor.
- Manual acceptance on staging/prod test chat (Test3): send emoji + link + photo + PDF from Heblo,
  check the widget; send a photo from the widget, check Heblo.
- Validation gates: `dotnet build`, `dotnet format`, `CI=false npm run build`, `npm run lint`.

## 10. Success criteria (from the handoff, adjusted)

- Operator sends a reply with an emoji, a clickable link and an attached photo or PDF from Heblo;
  the customer sees all three in the widget.
- A photo the customer sends shows in the Heblo conversation as an image.
- Bold: operator has been told why not (§8).

## 11. Left to the builder

Component split and file names; normalised-JSON read strategy for old rows (§5.1); new handler vs
extended handler; exact emoji order; popover implementation; how the optimistic message models
local attachments; whether `null` or `[]` means "no attachments" in storage.
