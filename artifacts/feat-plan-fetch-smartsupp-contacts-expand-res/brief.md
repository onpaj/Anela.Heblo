# Plan — Fetch Smartsupp contacts + expand response models

## Context

The current Smartsupp integration has three problems uncovered by the real API payloads:

1. **Contact fetching is missing.** `POST /conversations/search` returns only `contact_id` (and `visitor_id`) — there is no embedded `contact` object. The code at `SmartsuppApiClient.cs:148-150` reads `item.Contact?.Name/Email/AvatarUrl` against a DTO shape (`SmartsuppContactApiItem`, lines 209-214) that is never populated, so `ContactName/Email/AvatarUrl` are always null (only the visitor-message fallback at `SmartsuppSyncJob.cs:99-100` salvages a name). The Smartsupp v2 contract is to call `GET /contacts/{id}` per conversation.
2. **Message author detection is broken.** The current code reads `author.type` (`SmartsuppMessageApiItem`, lines 227-233), but the real Smartsupp messages payload has no `author` field — it uses `sub_type` (`"contact"`/`"agent"`/`"bot"`). Author-type parsing therefore always falls through to `Visitor`.
3. **Response models drop most of the available data.** Conversation, message, and (planned) contact responses include many operationally useful fields (page_url, trigger_name, is_offline, location, phone, gdpr_approved, tags, variables, etc.) that we're not capturing.

## Goal

Add a `GET /contacts/{id}` call wired into the sync job (with per-run de-duplication), persist contacts as their own entity, and expand the API/domain/persistence models to capture the operationally useful fields from all three endpoints. Fix the `sub_type`/`contact_id` mismapping at the same time.

## Out of scope

- Surfacing the new fields through `ConversationDto`/`MessageDto` to the frontend (separate PR).
- An `/agents/{id}` endpoint (not provided by user; agent message `AuthorName` stays null).
- Backfilling existing rows — the next sync run will repopulate.

---

## Changes by file

### 1. `backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppApiClient.cs`

**API response DTOs (private, snake_case via existing JsonOptions):**

- Replace `SmartsuppConversationApiItem` (lines 198-207) to drop embedded `Contact` and add: `ExtId`, `FinishedAt: DateTime?`, `Channel: SmartsuppChannelApiItem?`, `ContactId`, `VisitorId`, `AgentIds: List<string>?`, `AssignedIds: List<string>?`, `GroupId`, `RatingValue: int?`, `RatingText`, `Domain`, `Referer`, `IsOffline`, `IsServed`, `Variables: JsonElement?`, `Tags: JsonElement?`, `Location: SmartsuppLocationApiItem?`, plus existing `Id/Status/Unread/CreatedAt/UpdatedAt/LastMessage`.
- Remove `SmartsuppContactApiItem` (dead — never populated).
- Add `SmartsuppLocationApiItem { Ip, Code, Country, City }` and `SmartsuppChannelApiItem { Type, Id }`.
- Replace `SmartsuppMessageApiItem` (lines 227-233): remove `Author`, add `ExtId`, `UpdatedAt`, `Type`, `SubType`, `Channel: SmartsuppChannelApiItem?`, `ConversationId`, `VisitorId`, `AgentId`, `TriggerId`, `TriggerName`, `DeliveryTo`, `DeliveryStatus`, `DeliveredAt: DateTime?`, `IsReply`, `IsFirstReply`, `IsOffline`, `IsOfflineReply`, `ResponseTime: int?`, `Attachments: JsonElement?`, `PageUrl`. Keep `Id`, `CreatedAt`, `Content`.
- Remove `SmartsuppMessageAuthorApiItem` (dead).
- Extend `SmartsuppMessageContentApiItem`: add `Type`, `Data: JsonElement?`.
- Extend `SmartsuppMessagesApiResponse`: add `Total`, `After`.
- Add `SmartsuppContactApiResponse { Id, CreatedAt, UpdatedAt, Email, Name, Phone, Properties: JsonElement?, Note, BannedAt: DateTime?, BannedBy, Tags: JsonElement?, GdprApproved }`.

**Public method (mirrors `GetConversationMessagesAsync` at lines 102-130):**

```
public async Task<SmartsuppContactData?> GetContactAsync(string contactId, CancellationToken ct)
```

- ApiToken guard, `_pipeline.ExecuteAsync` wrapper, named `HttpClient` `"Smartsupp"`.
- URL: `$"{_options.BaseUrl}contacts/{contactId}"`, `Authorization: Bearer …` header.
- On non-success: log + throw `HttpRequestException` carrying status; on 429 attach `RetryAfter` (same pattern as search method, lines 88-90).
- Returns null only if the API returns 404 (treat as missing contact, not failure).
- Deserialize with the shared `JsonOptions`, run `MapContact`.

**Mappers:**

- `MapConversation` (lines 140-153) — populate all new fields; drop `ContactName/Email/AvatarUrl` (these now come from the fetched contact in the sync job, not the conversation response).
- `MapMessage` (lines 155-163) — drop `AuthorType` derived from `Author`; add `SubType` and the other new fields. **Do not** derive `AuthorName` here; the sync job composes it (contact name for visitor / trigger_name for bot / null for agent).
- Add `MapContact` and `MapLocation` private helpers using `Unspecified` for timestamps.

### 2. `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppApiClient.cs`

- Add: `Task<SmartsuppContactData?> GetContactAsync(string contactId, CancellationToken cancellationToken);`
- **`SmartsuppConversationData`** — drop `ContactName/Email/AvatarUrl`; add `ExtId`, `FinishedAt: DateTime?`, `ContactId`, `VisitorId`, `Domain`, `Referer`, `IsOffline`, `IsServed`, `GroupId`, `RatingValue: int?`, `RatingText`, `LocationCountry`, `LocationCity`, `LocationIp`, `LocationCode`, `VariablesJson: string?` (serialized `JsonElement`), `TagsJson: string?`, `ChannelType`, `ChannelId`, `AgentIds: List<string>`, `AssignedIds: List<string>`.
- **`SmartsuppMessageData`** — drop `AuthorType`/`AuthorName`; add `ExtId`, `UpdatedAt`, `Type`, `SubType`, `ConversationId`, `VisitorId`, `AgentId`, `TriggerId`, `TriggerName`, `DeliveryTo`, `DeliveryStatus`, `DeliveredAt: DateTime?`, `IsReply`, `IsFirstReply`, `IsOffline`, `IsOfflineReply`, `ResponseTime: int?`, `PageUrl`, `AttachmentsJson: string?`, `ChannelType`, `ChannelId`, `ContentType`. Keep `Id`, `Content`, `CreatedAt`.
- **New `SmartsuppContactData`** — `Id`, `CreatedAt`, `UpdatedAt`, `Email`, `Name`, `Phone`, `Note`, `BannedAt: DateTime?`, `BannedBy`, `GdprApproved`, `TagsJson: string?`, `PropertiesJson: string?`.

### 3. Domain entities

- **New** `backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppContact.cs`: `Id` (string PK), `Email?`, `Name?`, `Phone?`, `Note?`, `BannedAt: DateTime?`, `BannedBy?`, `GdprApproved: bool`, `TagsJson?`, `PropertiesJson?`, `CreatedAt`, `UpdatedAt`, `SyncedAt`.
- **`SmartsuppConversation.cs`** — add `ContactId?` (FK), `Contact: SmartsuppContact?` (nav), `VisitorId?`, `ExtId?`, `FinishedAt: DateTime?`, `Domain?`, `Referer?`, `IsOffline: bool`, `IsServed: bool`, `LocationCountry?`, `LocationCity?`, `LocationIp?`, `LocationCode?`, `VariablesJson?`, `TagsJson?`. **Keep** existing `ContactName/Email/AvatarUrl` for backward compatibility with the UI/DTOs; the sync job will populate them from the fetched contact going forward.
- **`SmartsuppMessage.cs`** — add `SubType?`, `PageUrl?`, `TriggerName?`, `TriggerId?`, `IsOffline: bool`, `IsReply: bool`, `IsFirstReply: bool`, `ResponseTime: int?`, `UpdatedAt`, `AgentId?`, `VisitorId?`, `DeliveredAt: DateTime?`, `DeliveryStatus?`. Keep existing fields.

### 4. Persistence

- **New** `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppContactConfiguration.cs` — table `public.SmartsuppContacts`, PK `Id` varchar(100), `Email`/`Name`/`Phone`/`BannedBy` varchar(200), `Note` text, `TagsJson`/`PropertiesJson` text, timestamps `without time zone`. Index on `Email`.
- **`SmartsuppConversationConfiguration.cs`** — add column configs for the new fields (varchar lengths matching existing pattern: 100 for ids, 200 for short strings, 500 for URLs, text for JSON blobs; bools default false). Add `HasOne(c => c.Contact).WithMany().HasForeignKey(c => c.ContactId).OnDelete(DeleteBehavior.SetNull)`. Add index on `ContactId`.
- **`SmartsuppMessageConfiguration.cs`** — add column configs (text for `PageUrl`, varchar(200) for `TriggerName`, etc.). Add index on `(ConversationId, SubType)` for filtering.
- **`ApplicationDbContext.cs`** (lines 121-124) — add `DbSet<SmartsuppContact> SmartsuppContacts`.
- **`ISmartsuppRepository` + `SmartsuppRepository.cs`** — add `Task UpsertContactAsync(SmartsuppContact, CancellationToken)` (load-or-insert, copies all mutable fields like the existing upserts). Extend `UpsertConversationAsync` (lines 43-66) to copy the new conversation columns. Extend `UpsertMessagesAsync` (lines 68-90) to copy the new message columns.
- **New migration** `AddSmartsuppContactsAndExtendedFields` — creates `SmartsuppContacts` table + indexes; adds new columns to `SmartsuppConversations` and `SmartsuppMessages`; adds FK + indexes. (Project owner runs migrations manually per `CLAUDE.md`.)

### 5. `backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/Jobs/SmartsuppSyncJob.cs`

- Inject nothing new (already has `ISmartsuppApiClient` + `ISmartsuppRepository`).
- In `ExecuteAsync`, add `var contactCache = new Dictionary<string, SmartsuppContactData?>(StringComparer.Ordinal);` (per-run de-dup: same contact across pages = one HTTP call).
- New helper `Task<SmartsuppContactData?> FetchContactCachedAsync(string contactId, CancellationToken)` — checks cache, calls `_apiClient.GetContactAsync`, stores result (including nulls). On exception: log warning, cache null, continue.
- In `ProcessConversationAsync` (lines 79-135):
  - If `data.ContactId` non-empty → fetch via cache → if non-null, map to `SmartsuppContact` entity, `await _repository.UpsertContactAsync(contact, ct)`.
  - Set `conversation.ContactId = data.ContactId`. Populate `ContactName/Email/AvatarUrl` from the fetched contact (Email comes from contact, not conversation now). Avatar URL stays null since `/contacts/{id}` doesn't return one — set to null.
  - **Keep** the visitor-message name fallback (line 99-100) as a last resort if contact fetch returned null.
  - Populate all new conversation columns from `data`.
  - For each message: replace `ParseAuthorType(m.AuthorType)` with `ParseAuthorType(m.SubType)` and update the helper's mapping: `"contact" => Visitor`, `"agent" => Agent`, `"bot" => Bot`, default `Visitor`. Compose `AuthorName`:
    - `Visitor` → fetched contact's `Name` (or visitor fallback).
    - `Bot` → `m.TriggerName`.
    - `Agent` → null (no `/agents/{id}` endpoint yet).
  - Populate new message columns (`SubType`, `PageUrl`, `TriggerName`, `IsOffline`, `IsReply`, `IsFirstReply`, `ResponseTime`, `UpdatedAt`, `DeliveredAt`, `DeliveryStatus`, `AgentId`, `VisitorId`).

### 6. Tests

**`backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppApiClientTests.cs`**

- Update `SearchConversationsAsync_ReturnsItems_WhenApiResponds` (lines 33-77) — replace the embedded `contact` object in the canned JSON with `contact_id`, `visitor_id`, `domain`, `location`, etc. (use the real sample provided). Assert the new fields land in `SmartsuppConversationData`.
- Update `GetConversationMessagesAsync_ReturnsMessages_WhenApiResponds` (lines 101-147) — replace `author.type` with `sub_type` (`"bot"`/`"contact"`), add `trigger_name`, `page_url`, etc. Assert new fields populated.
- **Add** `GetContactAsync_ReturnsContact_WhenApiResponds` — happy path, asserts all fields incl. `phone`, `gdpr_approved`, `tags` serialized as JSON string.
- **Add** `GetContactAsync_ReturnsNull_On404` — 404 = no contact, returns null (not throw).
- **Add** `GetContactAsync_ThrowsHttpRequestException_On500` — using `ResiliencePipeline.Empty` (mirrors lines 79-99 / 149-168).

**`backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppSyncJobTests.cs`**

- Update existing mock setups to also `Setup(_ => _.GetContactAsync(...))`.
- **Add** `ExecuteAsync_FetchesContact_AndUpsertsIt` — verifies `GetContactAsync` called once per unique `contact_id`, `UpsertContactAsync` called with the mapped entity.
- **Add** `ExecuteAsync_CachesContact_AcrossPages` — same `contact_id` on two conversations in the same run → `GetContactAsync` invoked exactly once.
- **Add** `ExecuteAsync_UsesContactName_ForConversationContactName` — contact returns `Name="Monča"` → conversation persisted with that name (not the visitor-message fallback).
- **Update** `ExecuteAsync_FallsBackToVisitorMessageAuthorName_WhenContactNameIsNull` (lines 104-133) — adjust to: contact fetch returns contact with null name → falls back to visitor message author name.
- Update message-author tests to use `sub_type` instead of `author.type` in mocked data.

---

## Critical files (modified)

- `backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppApiClient.cs`
- `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppApiClient.cs`
- `backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppConversation.cs`
- `backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppMessage.cs`
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/Jobs/SmartsuppSyncJob.cs`
- `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppConversationConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppMessageConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs`
- `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppApiClientTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppSyncJobTests.cs`

## Critical files (new)

- `backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppContact.cs`
- `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppContactConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddSmartsuppContactsAndExtendedFields.cs` (+ designer + snapshot diff)

---

## Verification

1. `dotnet build` + `dotnet format` from `backend/` — clean build, no formatting drift.
2. `dotnet test backend/test/Anela.Heblo.Tests --filter Smartsupp` — all updated + new tests pass.
3. Run the new migration locally against a dev DB:
   - `dotnet ef migrations add AddSmartsuppContactsAndExtendedFields --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API`
   - `dotnet ef database update --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API`
   - Verify `SmartsuppContacts` table created and new columns exist on `SmartsuppConversations`/`SmartsuppMessages`.
4. Manual end-to-end: enable the `smartsupp-sync` job (currently `DefaultIsEnabled = false`), trigger one run, then query:
   - `SELECT * FROM "SmartsuppContacts" LIMIT 10;` — rows present with phone/gdpr/email.
   - `SELECT "Id","ContactId","Domain","Referer","LocationCountry" FROM "SmartsuppConversations" WHERE "ContactId" IS NOT NULL LIMIT 5;` — new columns populated.
   - `SELECT "Id","SubType","TriggerName","PageUrl" FROM "SmartsuppMessages" LIMIT 10;` — `SubType` now correctly differentiates `"contact"`/`"agent"`/`"bot"`, `PageUrl` populated for visitor messages.
5. Re-run sync; verify `GetContactAsync` is not called more than once per unique contact within a run (check logs or add a temporary debug log gated on `_logger.IsEnabled(Information)`).
