# Smartsupp Composer (emoji, links, files) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Operators can send emoji, links and files from Heblo's Smartsupp chat and see files customers send.

**Architecture:** Attachments are stored in the existing `SmartsuppMessage.AttachmentsJson` column in
**Smartsupp's own attachment format** (snake_case `type,file_name,file_type,url,size,thumb_400.url`),
so rows written before this change need no conversion; the webhook mapper additionally folds a
customer upload (`content.type=="upload"`, file in `content.data`) into that array. Sending files
extends the existing `SendMessageRequest`/`SendMessageHandler` with an optional file list; a new
multipart controller action feeds it, and the handler does token → upload → one message. FE adds
linkify + attachment rendering in bubbles, a curated emoji popover, and a file list in the composer
posted through a hand-built `fetch`.

**Tech Stack:** .NET 8, MediatR, FluentValidation, Polly, xUnit/FluentAssertions/Moq; React (CRA),
TanStack Query, Tailwind, lucide-react, Jest/RTL via `react-scripts test`.

**Spec:** `docs/superpowers/specs/2026-10-16-smartsupp-composer-design.md` (read it first; §2 has the
verified API shapes).

## Global Constraints

- DTOs are classes, never records (CLAUDE.md).
- FE API calls use absolute URLs: `${apiClient.baseUrl}${relativeUrl}` (CLAUDE.md).
- Max **5** files per message, **10 MB** (10 × 1024 × 1024 bytes) per file.
- Allowed extensions → `file_type` sent to Smartsupp: `.jpg`/`.jpeg` → `image/jpeg`, `.png` → `image/png`,
  `.gif` → `image/gif`, `.webp` → `image/webp`, `.pdf` → `application/pdf`, `.doc` → `application/msword`,
  `.docx` → `application/vnd.openxmlformats-officedocument.wordprocessingml.document`,
  `.xls` → `application/vnd.ms-excel`,
  `.xlsx` → `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, `.txt` → `text/plain`.
- Extension decides; declared type `""`, missing, or `application/octet-stream` is accepted; any other
  declared type must equal the mapped type (case-insensitive) or the file is rejected (spec C5).
- Message text max **4000** chars (unchanged).
- Upload URL must be `https://files.smartsupp.com/…`; upload POST is multipart, field `file`, **no
  Authorization header**.
- Plain text only to Smartsupp: never send `content.html`.
- Never `dangerouslySetInnerHTML` for message content; never `react-markdown` for message content.
- Rendered attachment/link URLs: only `https:` (plus `blob:` for the sender's own optimistic preview).
- All user-facing strings in Czech.
- Validation gates: `dotnet build`, `dotnet format --verify-no-changes`, `CI=false npm run build`,
  `npm run lint`, FE tests via `npx react-scripts test --watchAll=false <path>` (not `npx jest`).
- BE tests: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Smartsupp"`
  (if it hangs at 0 % CPU, build first then `--no-build -p:UseSharedCompilation=false`).

## Review Focus

1. Attachment-only message (files, empty text): Smartsupp must accept it. We omit `content` when the
   text is empty; covered by an API-client body test, and verified live in Task 9's acceptance step.
2. A message with **both** text and files where one upload fails: nothing is sent and the operator
   keeps text + files — handler test in Task 5, composer test in Task 8.
3. A customer upload whose `content.data` lacks `url` or has a non-https URL: dropped, bubble still
   renders (no crash) — mapper test in Task 2, bubble test in Task 6.
4. Text such as `Viz https://anela.cz/produkt.` or `(www.anela.cz)`: trailing punctuation/brackets
   stay outside the link; `javascript:alert(1)` is never linked — linkify tests in Task 6.
5. Office file declared `application/octet-stream` by the browser: accepted, sent with the mapped
   type — policy test in Task 4, FE policy test in Task 8.

---

### Task 1: Document the verified API facts

**Files:**
- Modify: `docs/integrations/smartsupp-api.md`
- Modify: `docs/integrations/smartsupp-webhook.md`

Doc-before-use rule: this lands before any code relies on it.

- [ ] **Step 1:** In `smartsupp-api.md`, add sections: `GET /files/upload/token` (scope `files:upload`,
  response `{token,url}`), upload `POST <url>` (multipart, field `file`, no auth, full response from spec
  §2.2, 24 h expiry when unattached), `POST /conversations/{id}/messages` with `attachments`
  (request + response shape from spec §2.3, max 5), "html is flattened to `text [url]`", "widget
  auto-links plain URLs with a preview card", "no bold/italic exists (verified 2026-10-16)". Fix the
  "Response (assumed)" note: the real response is the full message object.
- [ ] **Step 2:** In `smartsupp-webhook.md`, add two real samples (spec §2.3, §2.4):
  `conversation.contact_replied` with `content.type:"upload"` + `content.data`, and
  `conversation.agent_replied` with a non-empty `attachments[]` incl. `thumb_400`. State that
  customer files never appear in `attachments`.
- [ ] **Step 3: Commit** — `git commit -m "docs: smartsupp upload, attachment and formatting findings"`

---

### Task 2: Attachment storage format + mapping (webhook and REST)

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppAttachments.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Mappers/SmartsuppPayloadMapper.cs:62-95`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppApiClient.cs:282-310` (`MapMessage`)
- Test: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppAttachmentsTests.cs`,
  `backend/test/Anela.Heblo.Tests/Features/Smartsupp/Mappers/SmartsuppPayloadMapperTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public class SmartsuppAttachment   // Domain, plain class
  { string Type ("image"|"file"); string FileName; string? FileType; string Url; long? Size; string? ThumbUrl; }

  public static class SmartsuppAttachments
  {
      // Returns a JSON array (Smartsupp format) of image/file items from `attachments`,
      // plus content.data when contentType == "upload". Other types (e.g. "cards") skipped.
      // Never null: "[]" when there is nothing.
      public static string BuildJson(JsonElement? attachments, string? contentType, JsonElement? contentData);

      // Parses stored JSON (old raw rows and new rows share the format). Items without an
      // https url are dropped. Returns false on malformed JSON (list is then empty).
      public static bool TryParse(string? json, out IReadOnlyList<SmartsuppAttachment> attachments);
  }
  ```

- [ ] **Step 1: Write failing tests** in `SmartsuppAttachmentsTests`:
  - `BuildJson_IncludesCustomerUploadFromContentData` — contentType `"upload"`, data = spec §2.4 PDF
    object, attachments `[]` → parsed back: one item, `Type=="file"`, `FileName=="1040963052.pdf"`,
    `Size==598612`, `Url` = the `.pdf` URL.
  - `BuildJson_KeepsAgentAttachmentWithThumb` — attachments = spec §2.3 array → `ThumbUrl` =
    `…_thumb400.jpg`, `Type=="image"`.
  - `BuildJson_SkipsCardsAndReturnsEmptyArrayWhenNothing` — `[{"type":"cards",…}]` → `"[]"`.
  - `BuildJson_IgnoresContentDataWhenContentTypeIsText`.
  - `TryParse_DropsNonHttpsUrls` — `http://…` and `javascript:…` items dropped.
  - `TryParse_ReturnsFalseOnMalformedJson` and `TryParse_NullOrEmpty_ReturnsTrueAndEmpty`.
  - `TryParse_ReadsLegacyRawRow` — the exact stored prod row from spec §2.3.
  In `SmartsuppPayloadMapperTests`: `MapMessage_StoresCustomerUploadAsAttachment` — feed a
  `contact_replied` `data.message` with spec §2.4 content → `AttachmentsJson` parses to one file and
  `Content` is null.
- [ ] **Step 2: Run** `dotnet test … --filter "FullyQualifiedName~SmartsuppAttachments|FullyQualifiedName~SmartsuppPayloadMapper"` → FAIL (type missing).
- [ ] **Step 3: Implement** `SmartsuppAttachments` (System.Text.Json; write items with
  `Utf8JsonWriter`, copying `type,file_name,file_type,url,size,thumb_400` from each source). In
  `SmartsuppPayloadMapper.MapMessage`, set `AttachmentsJson = SmartsuppAttachments.BuildJson(data.attachments?, content.type, content.data?)`.
  In `SmartsuppApiClient.MapMessage`, same call with `item.Attachments`, `item.Content?.Type`, `item.Content?.Data`.
- [ ] **Step 4: Run** the same filter → PASS; also run all `~Smartsupp` tests → PASS.
- [ ] **Step 5: Commit** — `feat: store smartsupp customer uploads as attachments`

---

### Task 3: Expose attachments on `MessageDto`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/Contracts/MessageAttachmentDto.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/Contracts/MessageDto.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GetConversation/GetConversationHandler.cs:98` (`MapMessageDto`, becomes an instance method or takes a logger)
- Test: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/GetConversationHandlerTests.cs`

**Interfaces:**
- Consumes: `SmartsuppAttachments.TryParse` (Task 2).
- Produces: `MessageAttachmentDto { string Type; string FileName; string? FileType; string Url; long? Size; string? ThumbUrl; }` (class); `MessageDto.Attachments : List<MessageAttachmentDto>` (default empty). After the BE build the generated TS `MessageDto.attachments?: MessageAttachmentDto[]` exists (Tasks 6, 8).

- [ ] **Step 1: Failing tests:** `Handle_MapsAttachmentsOntoMessageDto` (stored spec §2.3 row →
  one attachment with `ThumbUrl`), `Handle_MalformedAttachmentsJson_ReturnsEmptyListAndLogsWarning`
  (verify logger `LogLevel.Warning` once; response still `Success`).
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement** the DTO and mapping; log `"Malformed AttachmentsJson on Smartsupp message {MessageId}"` at Warning.
- [ ] **Step 4: Run** `~Smartsupp` tests → PASS. Run `dotnet build` (regenerates the TS client) and check `frontend/src/api/generated/api-client.ts` contains `MessageAttachmentDto`.
- [ ] **Step 5: Commit** — `feat: expose smartsupp message attachments in conversation DTO` (`frontend/src/api/generated/api-client.ts` is tracked — commit it).

---

### Task 4: API client — upload token, upload, send with attachments

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppApiClient.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppApiClient.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/SendMessage/SmartsuppAttachmentPolicy.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppApiClientTests.cs`,
  `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppAttachmentPolicyTests.cs`

**Interfaces:**
- Produces (Domain, next to `SmartsuppSentMessageData`):
  ```csharp
  public class SmartsuppUploadToken { string Token; string Url; }
  public class SmartsuppOutgoingAttachment { string Token; string FileName; string FileType; }

  Task<SmartsuppUploadToken> GetUploadTokenAsync(CancellationToken ct);
  Task UploadFileAsync(string uploadUrl, string fileName, string fileType, byte[] content, CancellationToken ct);
  Task<SmartsuppSentMessageData> SendMessageAsync(string conversationId, string? content, string? agentId,
      IReadOnlyList<SmartsuppOutgoingAttachment> attachments, CancellationToken ct);   // new overload
  ```
  The existing 4-arg `SendMessageAsync` stays and is unchanged in behaviour.
- Produces (Application):
  ```csharp
  public static class SmartsuppAttachmentPolicy
  {
      public const int MaxFiles = 5;
      public const long MaxFileBytes = 10 * 1024 * 1024;
      // false when the extension is not allowed or a specific declared type contradicts it
      public static bool TryResolveFileType(string fileName, string? declaredType, out string fileType);
  }
  ```

- [ ] **Step 1: Failing tests** (`SmartsuppApiClientTests`, existing fake-handler pattern; assert
  bodies by **parsing** JSON / multipart, never substring):
  - `GetUploadTokenAsync_GetsFilesUploadToken_WithBearer` → `GET …/files/upload/token`, returns token+url.
  - `UploadFileAsync_PostsMultipartFieldFile_WithoutAuthorization` → method POST, no `Authorization`
    header, one part named `file` with the file name and bytes.
  - `UploadFileAsync_RejectsNonSmartsuppUrl` → `https://evil.example/upload/x` and
    `http://files.smartsupp.com/…` throw `ArgumentException`, no HTTP call made.
  - `UploadFileAsync_ThrowsHttpRequestException_OnNon2xx`.
  - `SendMessageAsync_WithAttachments_SendsUploadItems` → parsed body has
    `attachments[0] == {type:"upload", token, file_name, file_type}` and `agent_id`.
  - `SendMessageAsync_WithAttachmentsAndEmptyText_OmitsContent` → body has no `content` property.
  Policy tests: `.docx` + `application/octet-stream` → true with the docx type; `.pdf` + `""` → true;
  `.PNG` + `image/png` → true; `.png` + `application/pdf` → false; `.exe` → false; no extension → false.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement.** All three calls run through `_pipeline` (retries only 429 — keep it).
  Upload uses `MultipartFormDataContent` + `ByteArrayContent` with `Content-Type: fileType`. Send body
  gets `attachments` (null when empty, so it is omitted) and `content` null when text is empty.
- [ ] **Step 4: Run** `~Smartsupp` tests → PASS.
- [ ] **Step 5: Commit** — `feat: smartsupp api client upload and attachment send`

---

### Task 5: Send-with-attachments use case + endpoint + error codes

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/SendMessage/SendMessageRequest.cs`, `SendMessageValidator.cs`, `SendMessageHandler.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/SendMessage/SendMessageFile.cs`
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` (after `2710`)
- Modify: `backend/src/Anela.Heblo.API/Controllers/SmartsuppController.cs`
- Modify: `frontend/src/i18n.ts` (Smartsupp block, ~L270)
- Test: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SendMessageHandlerTests.cs`, new `SendMessageValidatorTests.cs`

**Interfaces:**
- Consumes: Task 4 client methods + `SmartsuppAttachmentPolicy`.
- Produces:
  - `SendMessageFile { string FileName; string? ContentType; byte[] Content; }` (class);
    `SendMessageRequest.Files : List<SendMessageFile>` (default empty); `Content` becomes `string?`.
  - `ErrorCodes.SmartsuppAttachmentInvalid = 2711` (400), `SmartsuppAttachmentTooLarge = 2712` (400),
    `SmartsuppAttachmentUploadFailed = 2713` (503).
  - `POST api/smartsupp/conversations/{conversationId}/messages/with-attachments`,
    `multipart/form-data`: `content` (string?), `draftLogId` (Guid?), `files` (`List<IFormFile>`);
    returns `SendMessageResponse`. FE (Task 8) calls this with field names exactly `content`,
    `draftLogId`, `files`.
- i18n texts: `SmartsuppAttachmentInvalid: "Soubor nelze odeslat — nepodporovaný typ nebo příliš mnoho souborů (max 5)."`,
  `SmartsuppAttachmentTooLarge: "Soubor je větší než 10 MB."`,
  `SmartsuppAttachmentUploadFailed: "Nahrání souboru selhalo. Zprávu můžete odeslat bez přílohy."`

- [ ] **Step 1: Failing tests** (handler; existing tests must stay green untouched):
  - `Handle_WithFiles_UploadsEachThenSendsOneMessageWithAllTokens` — 2 files → 2× token, 2× upload
    (with policy-resolved file types), 1× attachment `SendMessageAsync` with both tokens in order;
    the 4-arg overload is never called.
  - `Handle_WithFilesAndNoText_SendsAttachmentOnlyMessage` (content passed as null).
  - `Handle_WhenAnUploadFails_SendsNothing_ReturnsAttachmentUploadFailed` (second upload throws
    `HttpRequestException` → no send call).
  - `Handle_RejectsSixFiles_WithAttachmentInvalid`, `Handle_RejectsDisallowedExtension_WithAttachmentInvalid`,
    `Handle_RejectsEmptyFile_WithAttachmentInvalid`, `Handle_RejectsFileOver10MB_WithAttachmentTooLarge` —
    each makes no API call.
  - `Handle_WithFiles_ChecksConversationAndAgentMappingFirst` (unknown conversation → not found, no upload).
  - `Handle_WithFiles_RecordsDraftSentText` (draft log updated with the text).
  - `Handle_WhenAttachmentSendFails_ReturnsSendMessageUnavailable`.
  Validator tests: text-only empty → invalid; empty text + 1 file → valid; text 4001 chars → invalid.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement.** Handler order: conversation → agent → (if `Files.Count > 0`) policy
  checks (count, empty, size, `TryResolveFileType`) → per file token+upload (catch the same exception
  filter the existing send uses → `SmartsuppAttachmentUploadFailed`) → attachment send (same filter →
  `SmartsuppSendMessageUnavailable`) → draft log (unchanged code path, record `Content ?? ""`).
  With no files, the existing path is untouched. Validator: `Content` `NotEmpty()` only `When(r => r.Files.Count == 0)`, `MaximumLength(4000)` always.
  Controller action (copy the `SendMessage` attributes + comment, add `[Consumes("multipart/form-data")]`,
  `[RequestSizeLimit(60 * 1024 * 1024)]`, `[RequestFormLimits(MultipartBodyLengthLimit = 60 * 1024 * 1024)]`);
  read each `IFormFile` into bytes as `KnowledgeBaseController.UploadDocument` does. Add the three
  codes with `[HttpStatusCode]` attributes and the i18n lines.
- [ ] **Step 4: Run** `~Smartsupp` + `~ErrorHandling` tests → PASS; `dotnet build` → OK.
- [ ] **Step 5: Commit** — `feat: send smartsupp messages with file attachments`

---

### Task 6: Bubbles — clickable links and attachments

**Files:**
- Create: `frontend/src/components/customer-support/smartsupp/utils/linkify.ts`
- Create: `frontend/src/components/customer-support/smartsupp/LinkifiedText.tsx`
- Create: `frontend/src/components/customer-support/smartsupp/MessageAttachments.tsx`
- Modify: `frontend/src/components/customer-support/smartsupp/MessageBubble.tsx`
- Test: `utils/__tests__/linkify.test.ts`, `__tests__/MessageBubble.test.tsx`, `__tests__/MessageAttachments.test.tsx`

**Interfaces:**
- Consumes: generated `MessageAttachmentDto` (Task 3).
- Produces:
  ```ts
  export type TextSegment = { kind: "text"; text: string } | { kind: "link"; text: string; href: string };
  export function splitLinks(text: string): TextSegment[];
  export function isRenderableUrl(url: string | undefined, allowBlob?: boolean): boolean; // https: (+ blob:)
  // <MessageAttachments attachments={MessageAttachmentDto[]} tone="visitor" | "agent" />
  ```

- [ ] **Step 1: Failing tests.** linkify: `"Viz https://anela.cz/produkt."` → link href
  `https://anela.cz/produkt`, trailing `.` is text; `"(www.anela.cz)"` → href `https://www.anela.cz`,
  brackets are text; `"javascript:alert(1)"` → single text segment; `"*tucne*"` → single text segment;
  two URLs in one line → two links. MessageBubble: URL rendered as `<a target="_blank" rel="noopener noreferrer">`;
  `*tucne*` renders literally (no `<em>/<strong>`); upload-only message (content null, one image
  attachment) renders an `<img>` with `src` = `thumbUrl` inside a link to `url`; a `file` attachment
  renders a link whose text contains the file name and `584 kB` for size 598612; an attachment with
  `http:` url renders nothing and does not crash.
- [ ] **Step 2: Run** `CI=true npx react-scripts test --watchAll=false src/components/customer-support/smartsupp` → FAIL.
- [ ] **Step 3: Implement.** Size format: `< 1024 → "N B"`, `< 1 MiB → "N kB"` (rounded), else one-decimal `"N,N MB"`.
  Image: `max-w-[240px] rounded-lg`, `loading="lazy"`, `alt` = file name. File chip: lucide `FileText`/`Paperclip`,
  truncated name, size. Links `underline` with inherited colour so they read on the blue agent bubble.
  The text `<p>` is rendered only when `content` is non-empty.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** — `feat: clickable links and attachments in smartsupp bubbles`

---

### Task 7: Emoji button

**Files:**
- Create: `frontend/src/components/customer-support/smartsupp/EmojiPicker.tsx`
- Create: `frontend/src/components/customer-support/smartsupp/utils/insertAtCursor.ts`
- Modify: `frontend/src/components/customer-support/smartsupp/ChatComposer.tsx` (textarea gets a ref; button row)
- Test: `utils/__tests__/insertAtCursor.test.ts`, `__tests__/EmojiPicker.test.tsx`, `__tests__/ChatComposer.test.tsx`

**Interfaces:**
- Produces:
  ```ts
  export const COMPOSER_EMOJI: readonly string[] =
    ["😊","🙂","😁","🥰","😉","🙏","👍","🙌","👉","✅","⚠️","💡","❤️","💛","💚","🤍","✨","🌿","🌸","🌙","💧","🧴","🎁","📦"];
  export function insertAtCursor(value: string, start: number, end: number, insert: string): { value: string; caret: number };
  // <EmojiPicker onPick={(emoji: string) => void} disabled={boolean} />  button aria-label "Vložit emoji"
  ```

- [ ] **Step 1: Failing tests.** `insertAtCursor("ahoj světe", 5, 5, "😊")` → `{value:"ahoj 😊světe", caret:7}`;
  replaces a selection. EmojiPicker: click button → grid with 24 buttons; click `🌿` → `onPick("🌿")`
  and the grid closes; `Escape` closes. ChatComposer: type `Dobrý den`, place caret at end, pick `😊`
  → textarea value `Dobrý den😊`, `onDraftChange` called with it, counter shows `11 / 4000`.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement.** Popover anchored above the button, closes on pick / Esc / outside
  mousedown. After insert: `setDraft` via the existing `handleDraftChange`, then restore focus and
  `setSelectionRange(caret, caret)` in a `requestAnimationFrame`.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** — `feat: emoji picker in smartsupp composer`

---

### Task 8: Attach files in the composer

**Files:**
- Create: `frontend/src/components/customer-support/smartsupp/utils/attachmentPolicy.ts`
- Create: `frontend/src/components/customer-support/smartsupp/hooks/useComposerAttachments.ts`
- Create: `frontend/src/components/customer-support/smartsupp/PendingAttachments.tsx`
- Modify: `frontend/src/components/customer-support/smartsupp/hooks/useSendMessage.ts`
- Modify: `frontend/src/components/customer-support/smartsupp/ChatComposer.tsx`
- Test: `utils/__tests__/attachmentPolicy.test.ts`, `hooks/__tests__/useSendMessage.test.ts`, `__tests__/ChatComposer.test.tsx`

**Interfaces:**
- Consumes: endpoint + field names + error codes from Task 5; `MessageAttachments` with blob
  allowed (Task 6) for the optimistic bubble.
- Produces:
  ```ts
  // attachmentPolicy.ts — mirrors Global Constraints
  export const MAX_FILES = 5; export const MAX_FILE_BYTES = 10 * 1024 * 1024;
  export function validateNewFiles(current: File[], incoming: File[]): { accepted: File[]; error: string | null };
  // useComposerAttachments(conversationId) → { files: File[]; error: string | null; add(files: File[]): void; remove(index: number): void; clear(): void }
  // useSendMessage: send(content: string, draftLogId?: string | null, files?: File[]) — rest unchanged
  ```
- Client error texts: too many → `"Najednou lze poslat nejvýše 5 souborů."`, too large →
  `"Soubor {name} je větší než 10 MB."`, type → `"Soubor {name} má nepodporovaný typ."`.

- [ ] **Step 1: Failing tests.**
  - policy: `.docx` with `type ""` accepted; `.exe` rejected with the type message; 4 current + 2
    incoming → accepts 1, too-many message; 11 MB file rejected.
  - useSendMessage: with files → `http.fetch` called once with URL
    `${baseUrl}/api/smartsupp/conversations/c1/messages/with-attachments`, method POST, body `FormData`
    with `content`, `draftLogId`, `files` ×2 and no manual `Content-Type` header; on 503 JSON
    `{errorCode:"SmartsuppAttachmentUploadFailed"}` → `error` equals the i18n text from Task 5 and the
    optimistic message is rolled back; without files → existing `smartsupp_SendMessage` path (existing
    tests unchanged).
  - ChatComposer: paperclip (`aria-label "Přiložit soubor"`) + file input → file listed with a
    remove button (`aria-label "Odebrat {name}"`); Send enabled with a file and empty text; failed
    send keeps text and files; successful send clears both; pasting an image file adds it; changing
    `conversationId` clears files.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement.** Hidden `<input type="file" multiple accept=".jpg,.jpeg,.png,.gif,.webp,.pdf,.doc,.docx,.xls,.xlsx,.txt">`
  (reset its value after each pick); drop zone = the composer box (`onDragOver` preventDefault,
  `onDrop`); `onPaste` takes `clipboardData.files`. Previews via `URL.createObjectURL`, revoked on
  remove / clear / unmount. Optimistic `MessageDto` carries `attachments` built from the object
  URLs. Error text on the fetch path: read `errorCode` from the JSON body, map through the same
  table as today's errors, extended with the three new codes. Ctrl/Cmd+Enter sends when text **or**
  files are present.
- [ ] **Step 4: Run** all smartsupp FE tests → PASS; `CI=false npm run build` and `npm run lint` → OK.
- [ ] **Step 5: Commit** — `feat: attach files to smartsupp replies`

---

### Task 9: Process docs, full validation, live acceptance

**Files:**
- Modify: `docs/processes/flow-smartsupp-reply.md`, `docs/processes/sync-smartsupp-webhooks.md`
- Generated: process-docs index via the script

- [ ] **Step 1:** `flow-smartsupp-reply.md`: send-with-attachments flow (endpoint, limits, token →
  upload → send, nothing sent on upload failure, no local persistence — the `agent_replied` echo
  stores attachments), bump `verified_at`. `sync-smartsupp-webhooks.md`: mapper folds
  `content.type=="upload"` into `AttachmentsJson`; backfill = replay `contact_replied` audit rows with
  `"type":"upload"` in `RawBody` within the 7-day retention (manual, `POST api/admin/smartsupp/webhooks/{id}/replay`).
- [ ] **Step 2:** `python3 scripts/process-docs/check.py index` → exit 0.
- [ ] **Step 3:** `dotnet build`, `dotnet format --verify-no-changes`, all `~Smartsupp` + `~ErrorHandling`
  BE tests, `CI=false npm run build`, `npm run lint`, all smartsupp FE tests → all green.
- [ ] **Step 4: Commit** — `docs: smartsupp reply and webhook process docs for attachments`
- [ ] **Step 5 (controller, not a subagent; needs Ondrej's OK for the write calls):** run the app
  locally against prod Smartsupp in test conversation `coTaX8UFWh4Sz` only: send emoji + link + photo + PDF,
  and one attachment-only message; Ondrej checks the widget. If Smartsupp rejects the
  attachment-only body, change the client to send `content.text: ""` and record the finding in
  `smartsupp-api.md`.
