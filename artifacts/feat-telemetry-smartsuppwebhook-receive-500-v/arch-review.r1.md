I have enough context. Now writing the architecture review.

# Architecture Review: Fix Smartsupp Webhook 500 Errors Caused by Oversized String Fields

## Skip Design: true

Backend-only persistence fix — schema widening, mapper-level defensive truncation, controller-boundary hardening, and regression tests. No UI components, screens, or visual changes.

## Architectural Fit Assessment

The feature fits cleanly into the existing Smartsupp webhook pipeline and Vertical Slice organization. All proposed touch points already exist:

- **API layer** (`Anela.Heblo.API/Controllers/SmartsuppWebhookController.cs`, `Anela.Heblo.API/Webhooks/Smartsupp/SmartsuppWebhookMetrics.cs`) — receive, audit-create, mediator dispatch, audit-update.
- **Application layer** (`Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/`) — handler, reactions, `SmartsuppPayloadMapper`, `ISmartsuppWebhookMetrics`.
- **Persistence layer** (`Anela.Heblo.Persistence/Smartsupp/`) — `SmartsuppConversationConfiguration`, `SmartsuppWebhookAuditWriter`, `ISmartsuppRepository`.

Two pieces of grounded reality matter for the design:

1. The controller (`SmartsuppWebhookController.Receive`) **already** wraps `_mediator.Send(...)` in try/catch (lines 141–169). The catch path calls `_audit.UpdateOutcomeAsync(...)` with `error: ex.ToString()`. So the spec's hypothesis that the 500 escapes from "outside the catch" needs a sharper diagnosis: the real culprit is that **`_audit.UpdateOutcomeAsync` calls `ApplicationDbContext.SaveChangesAsync()` on a context whose change tracker still holds the failed `SmartsuppConversation` entity from the prior `SaveChangesAsync` failure**. EF Core does not auto-detach failed entities on `DbUpdateException`. The audit-write call therefore re-attempts the offending insert and re-throws the same 22001, which now escapes (no further catch).

2. `ApplicationDbContext` is registered scoped and **shared** between `SmartsuppWebhookAuditWriter` and `SmartsuppRepository`. This shared-context coupling is the root architectural cause of the 500. The schema widening (FR-1) eliminates the trigger; the audit-write hardening (FR-3) must address the coupling, not just symptom-wrap.

Otherwise, the proposal aligns with existing patterns: per-field constants in mappers, structured logs, `OpenTelemetry`-style metrics through `ISmartsuppWebhookMetrics`, xUnit tests under `backend/test/Anela.Heblo.Tests/Features/Smartsupp/`.

## Proposed Architecture

### Component Overview

```
HTTP POST /api/webhooks/smartsupp
  │
  ▼
SmartsuppWebhookController.Receive
  │  ├── HMAC verify ──────────────► (early-out)
  │  ├── audit.CreateAsync (entry) ─► AuditDbContext  ◄── NEW: dedicated scope
  │  │
  │  ▼ try
  │  │   _mediator.Send(ProcessWebhookEventRequest)
  │  │     │
  │  │     ▼
  │  │     ProcessWebhookEventHandler.Handle
  │  │       ├── reaction.HandleAsync
  │  │       │     └── SmartsuppPayloadMapper.MapConversation
  │  │       │           └── TruncateForColumn(...) ◄── NEW: defensive cap
  │  │       │                 └── metrics.RecordTruncation(field) ◄── NEW
  │  │       └── _repository.SaveChangesAsync (ApplicationDbContext)
  │  │
  │  ▼ catch (Exception) ──┐
  │                        │
  │  ▼ finally / outer try-catch (audit only)
  │      audit.UpdateOutcomeAsync ──► AuditDbContext (isolated; never re-throws)
  │
  ▼ return Ok()
```

Key change: audit writes go through a **dedicated `DbContext` scope** isolated from the domain `ApplicationDbContext` that the reactions/repository use. Failure of the domain save no longer poisons the audit save.

### Key Design Decisions

#### Decision 1: Schema widening AND defensive truncation (both, not either)
**Options considered:**
- (a) Widen schema only (FR-1).
- (b) Application truncation only (FR-2).
- (c) Both (spec proposal).

**Chosen approach:** (c) — both layers, with the schema being the primary fix and truncation a defense-in-depth guard.

**Rationale:** The schema widening alone is correct for the known cause (Referer URLs). Truncation alone hides data; widening alone leaves the system fragile to the next unforeseen field. Layering both means future column-size mismatches degrade to a logged warning instead of a 5xx and Smartsupp retry storm. `RawBody` already preserves the untruncated payload for forensic replay.

#### Decision 2: `Referer` → `text`; `Subject` and `ContactAvatarUrl` → `varchar(2000)`
**Options considered:**
- Widen all three to `text`.
- Widen all three to `varchar(2000)`.
- Mixed (spec proposal).

**Chosen approach:** Mixed — `Referer` → `text`, `Subject` and `ContactAvatarUrl` → `varchar(2000)`.

**Rationale:** Referer URLs carry unbounded query strings (UTM, tracking IDs, ad-platform fragments); guessing any cap will be wrong again. `Subject` and `ContactAvatarUrl` are bounded in practice (conversation subjects fit in a couple of paragraphs; CDN signed-URL avatars rarely exceed 1–2 KB); `varchar(2000)` documents intent and helps DB-side observability. None of these columns are indexed, so neither type incurs an index-size penalty.

#### Decision 3: Audit writer uses a dedicated DbContext scope (architecturally separate from the domain context)
**Options considered:**
- (a) Spec-as-written: keep shared `ApplicationDbContext`, wrap `_audit.UpdateOutcomeAsync` in try/catch in the controller.
- (b) Reset the change tracker (`_context.ChangeTracker.Clear()`) inside `SmartsuppWebhookAuditWriter.UpdateOutcomeAsync` after detecting a polluted state.
- (c) Inject a separate, dedicated `DbContext` for the audit writer (DbContextFactory pattern or a named scope), so audit writes are always clean.

**Chosen approach:** (c), with (a) as a belt-and-braces fallback at the controller boundary.

**Rationale:** (a) alone treats the symptom; the audit write still fails (just silently). (b) reaches into EF internals from outside the audit-write call site and is fragile. (c) isolates the audit table at the persistence boundary — failed domain writes can never block audit writes. The controller still wraps the audit call in try/catch as defense-in-depth so any future audit-side failure (connection, network) degrades to a logged Ok().

This is the one architectural amendment to the spec: FR-3 should explicitly mandate context isolation (e.g. inject `IDbContextFactory<ApplicationDbContext>` into `SmartsuppWebhookAuditWriter` and use `await using var ctx = await _factory.CreateDbContextAsync(...)`), not just try/catch wrapping. See Specification Amendments.

#### Decision 4: Truncation utility lives next to the mapper, not in a global helper
**Options considered:**
- Add `StringTruncationExtensions` under `Anela.Heblo.Application/Common/`.
- Add a `static class StringTruncator` next to `SmartsuppPayloadMapper`.
- Inline `string.Length` checks per field in the mapper.

**Chosen approach:** Static `internal` helper in the Smartsupp mapper's folder (e.g. `Mappers/StringTruncation.cs`), exposing one method: `Truncate(string? input, int maxLength, string fieldName, ILogger? logger, ...)`. Internal to the Smartsupp feature slice.

**Rationale:** YAGNI — there is no second caller. Vertical Slice organization in this codebase prefers feature-local helpers over premature shared utilities (`docs/architecture/filesystem.md` and `docs/architecture/development_guidelines.md` reinforce module boundaries). Per-field max-length constants live as `const int` on `SmartsuppPayloadMapper` alongside the existing `LastMessagePreviewMaxLength`.

#### Decision 5: UTF-8 boundary safety via code-point slicing
**Options considered:**
- `string.Substring` by `char` index (UTF-16 code units) — can split surrogate pairs.
- `System.Globalization.StringInfo.SubstringByTextElements` — slices by grapheme cluster, full ICU rules.
- `Rune`/`Span<char>` enumeration to find the largest prefix ≤ maxLength chars whose last char is not a high surrogate.

**Chosen approach:** Char-based substring with a high-surrogate guard: if the truncation index would land mid-surrogate-pair, step back one char. This is what `SmartsuppPayloadMapper.MapConversation` effectively needs for `LastMessagePreview` today (and which it currently does not guard).

**Rationale:** `StringInfo` is correct but heavy (grapheme cluster segmentation cost per webhook). For storage-fitting truncation, the only correctness requirement is "do not emit invalid UTF-16," which a one-char-back step achieves. Grapheme integrity for visual rendering is a UI concern; the field is never rendered raw from the DB without further processing.

#### Decision 6: Truncation metric is a tagged extension of the existing meter, not a new abstraction
**Options considered:**
- Add `void RecordTruncation(string field)` to `ISmartsuppWebhookMetrics`.
- Skip the metric (spec's "optional").
- Create a new metrics interface.

**Chosen approach:** Add `RecordTruncation(string field)` to the existing `ISmartsuppWebhookMetrics` interface and emit a counter `smartsupp.webhook.field_truncations_total{field}` on the existing `Anela.Heblo.Smartsupp.Webhooks` meter.

**Rationale:** This is a one-line addition (NFR-3 sets the bar). Cardinality is bounded — ~12 field names, all known. Without the metric, "is the new ceiling holding?" can only be answered by grepping logs in Application Insights, which is operationally worse. Reuses the existing meter, no new DI registration.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/
├── Mappers/
│   ├── SmartsuppPayloadMapper.cs       (modified: per-field consts, truncation calls)
│   └── StringTruncation.cs             (NEW: internal static Truncate helper)
└── ISmartsuppWebhookMetrics.cs         (modified: + RecordTruncation(string))

backend/src/Anela.Heblo.API/Webhooks/Smartsupp/
└── SmartsuppWebhookMetrics.cs          (modified: implement RecordTruncation)

backend/src/Anela.Heblo.API/Controllers/
└── SmartsuppWebhookController.cs       (modified: wrap audit.UpdateOutcomeAsync in try/catch)

backend/src/Anela.Heblo.Persistence/Smartsupp/
├── SmartsuppConversationConfiguration.cs   (modified: widen 3 columns)
├── SmartsuppWebhookAuditWriter.cs          (modified: use IDbContextFactory; defensive truncation on bounded audit columns)
└── ISmartsuppWebhookAuditWriter.cs         (unchanged contract)

backend/src/Anela.Heblo.Persistence/Migrations/
└── 2026MMDD_WidenSmartsuppConversationColumns.cs  (NEW: AlterColumn ×3)

backend/test/Anela.Heblo.Tests/Features/Smartsupp/
├── Mappers/SmartsuppPayloadMapperTests.cs       (modified: truncation + UTF-8 + boundary cases)
├── ProcessWebhookEventHandlerTests.cs           (modified: oversized payload end-to-end)
├── WebhookAudit/SmartsuppWebhookAuditWriterTests.cs  (modified: factory-based context, isolation under failure)
└── (existing) SmartsuppWebhookControllerTests.cs    (verify Ok() when audit throws)
```

### Interfaces and Contracts

```csharp
// Internal helper — lives in feature slice, not shared utilities.
internal static class StringTruncation
{
    public static string? Truncate(
        string? value,
        int maxLength,
        string fieldName,
        string? contextId,
        ILogger logger,
        ISmartsuppWebhookMetrics metrics);
    // Returns null if value is null. UTF-16-safe: never returns a string ending in a lone high surrogate.
    // On truncation: emits LogWarning with (Field, OriginalLength, TruncatedLength, ContextId)
    //                and calls metrics.RecordTruncation(fieldName). Does NOT log the value itself.
}

// Extension to existing interface — additive, non-breaking.
public interface ISmartsuppWebhookMetrics
{
    void RecordReceived(string eventName, string outcome, double durationMs);
    void RecordSignatureFailure(string reason);
    void RecordPayloadBytes(int bytes);
    void RecordTruncation(string field); // NEW
}

// Audit writer constructor changes to take a context factory.
public sealed class SmartsuppWebhookAuditWriter : ISmartsuppWebhookAuditWriter
{
    public SmartsuppWebhookAuditWriter(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        ILogger<SmartsuppWebhookAuditWriter> logger);
    // CreateAsync and UpdateOutcomeAsync each open a fresh context, save, dispose.
    // Defensive truncation applied to entry fields before SaveChanges.
}
```

The mapper's signature does **not** change; it gains an `ILogger` and `ISmartsuppWebhookMetrics` parameter only if the truncation helper requires them threaded through. Spec says the mapper is `static`; the simplest path is to make `MapConversation` accept `ILogger` and `ISmartsuppWebhookMetrics` as parameters (called by the reactions, which already get both via DI) rather than turn the mapper into an instance class. Mappers stay stateless and feature-internal.

Per-field constants on `SmartsuppPayloadMapper`:

```csharp
private const int SubjectMaxLength = 2000;
private const int ContactAvatarUrlMaxLength = 2000;
// Referer: no app-level cap (column is now `text`). Truncation skipped for Referer.
private const int ContactNameMaxLength = 200;
private const int ContactEmailMaxLength = 200;
private const int DomainMaxLength = 200;
private const int LocationCountryMaxLength = 100;
private const int LocationCityMaxLength = 100;
private const int LocationIpMaxLength = 50;
private const int LocationCodeMaxLength = 10;
private const int CloseTypeMaxLength = 50;
private const int ChannelMaxLength = 50;
private const int RatingTextMaxLength = 1000;
private const int LastMessagePreviewMaxLength = 200; // existing
```

### Data Flow

**Happy path (small payload):**
`Receive` → `audit.CreateAsync` (fresh ctx, save, dispose) → mediator → handler → reaction → `MapConversation` (no truncation triggered) → `repository.SaveChangesAsync` → `audit.UpdateOutcomeAsync(Success)` → 200.

**Oversized payload, post-fix:**
`Receive` → `audit.CreateAsync` (preserves full `RawBody`) → mediator → handler → reaction → `MapConversation` (`subject` triggers `Truncate` → logs warning, increments `smartsupp.webhook.field_truncations_total{field=subject}`, returns 2000-char string) → `repository.SaveChangesAsync` succeeds → `audit.UpdateOutcomeAsync(Success)` → 200.

**Hypothetical future schema-drift payload (defense-in-depth):**
`Receive` → `audit.CreateAsync` → mediator → handler throws `DbUpdateException` (e.g. some other column overflows in the future) → controller catch → `audit.UpdateOutcomeAsync(HandlerException, ex.ToString())` runs on **fresh** context → succeeds → 200.

**Hypothetical audit-write failure (DB down, connection lost):**
Controller's audit try/catch swallows the audit exception, logs error with structured fields, returns 200 anyway. Caller never sees 5xx; Smartsupp does not retry; oncall sees the structured error in Application Insights.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `AlterColumn` to widen `varchar` rewrites the table on PostgreSQL, blocking writes during deploy. | Low | PostgreSQL's `ALTER TABLE ... ALTER COLUMN ... TYPE varchar(N)` is metadata-only when N grows; `TYPE text` from `varchar` is also metadata-only since `text` and `varchar` share storage. Verify on staging with `EXPLAIN`/timing before prod. |
| Introducing `IDbContextFactory<ApplicationDbContext>` requires a DI registration change that affects other consumers. | Medium | `AddDbContextFactory` can be added alongside `AddDbContext` so the existing `ApplicationDbContext` scoped registration keeps working for `SmartsuppRepository` and other code paths. Only `SmartsuppWebhookAuditWriter` is migrated to the factory. |
| Defensive truncation in the mapper silently corrupts business data (URLs, emails). | Medium | Truncation is logged + metered. `RawBody` preserves the original in the audit log. Operators can replay or backfill from `RawBody` if business need arises (out of scope here, but possible). |
| Truncation log entries accidentally include PII (visitor messages, emails). | High | Truncation log template is fixed at `{Field}/{OriginalLength}/{TruncatedLength}/{ContextId}` — never includes the value. Code review must enforce. Unit test asserts the warning does not contain the truncated string. |
| UTF-8 multi-byte char split produces invalid string in DB. | Low | Char-truncation guard handles high-surrogate boundary. Unit test with emoji at the boundary verifies. |
| `_audit.UpdateOutcomeAsync` change to use factory loses the in-flight tracked entry from `CreateAsync`. | Medium | `UpdateOutcomeAsync` already re-loads the entry by Id (line 32 of `SmartsuppWebhookAuditWriter`); switching to a fresh context per call is correct by design. |
| Replay against the new schema still fails because the original audit entry's `RawBody` was truncated mid-stream by ASP.NET request size limit (1 MB). | Low | Verified: `MaxBodyBytes = 1_048_576` is enforced; payloads above this never reached the handler and therefore aren't in the audit log to replay. No change needed. |
| Adding `RecordTruncation` to `ISmartsuppWebhookMetrics` breaks test doubles that implement the interface. | Low | Update existing mocks. Provide a default no-op via interface default method only if Moq/NSubstitute mocks complain — prefer explicit updates. |

## Specification Amendments

1. **FR-3 — Audit-writer hardening: strengthen to mandate context isolation.**
   Current text proposes "wrap `_audit.UpdateOutcomeAsync` calls in try/catch." This treats the symptom (audit-write fails → re-throws) without addressing the cause (the audit writer's `DbContext` carries the failed domain entity in its change tracker). The amendment: `SmartsuppWebhookAuditWriter` is reworked to take `IDbContextFactory<ApplicationDbContext>` and create a fresh `DbContext` per `CreateAsync` and `UpdateOutcomeAsync` call. The controller-side try/catch becomes defense-in-depth (for connection/transient failures), not the primary fix. This is the only architecturally significant change beyond the spec.

2. **FR-2 — Mapper signature.** Spec implies static helper extension but doesn't specify how `ILogger`/`ISmartsuppWebhookMetrics` reach the mapper. Amendment: `SmartsuppPayloadMapper.MapConversation` accepts an extra `(ILogger logger, ISmartsuppWebhookMetrics metrics)` parameter pair; reactions thread their existing DI-injected instances in. No behavior change to reactions other than parameter passing.

3. **FR-2 — `Referer` is NOT truncated at the application layer.** Column is now `text` (unbounded). Defensive truncation only applies to columns with a `HasMaxLength` cap. Spec lists `referer` under truncation; remove it for consistency.

4. **NFR-3 — Truncation metric is in-scope, not optional.** Per Decision 6, the metric is a one-line addition that pays for itself in operability. Move from "Optional" to required and add `RecordTruncation(string field)` to `ISmartsuppWebhookMetrics`.

5. **FR-3 — Audit-entry defensive truncation list.** Spec calls out `RemoteIp`, `SignatureHeader`, `EventName`, `AccountId`, `AppId`, `ProcessingError`. Verified column types: `RemoteIp varchar(64)`, `SignatureHeader varchar(256)`, `EventName varchar(100)`, `AccountId varchar(100)`, `AppId varchar(100)`, `LastReplayedBy varchar(200)`, `ProcessingError text` (no truncation needed), `HeadersJson text` (no truncation needed), `RawBody text` (no truncation needed). Truncate the six `varchar` fields defensively when persisting.

## Prerequisites

- **EF Core tooling already wired** (existing migrations under `backend/src/Anela.Heblo.Persistence/Migrations/`). Run `dotnet ef migrations add WidenSmartsuppConversationColumns --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API`. No new tooling.
- **DI registration adjustment**: register `AddDbContextFactory<ApplicationDbContext>(...)` alongside the existing `AddDbContext<ApplicationDbContext>(...)` so the new `SmartsuppWebhookAuditWriter` constructor can resolve `IDbContextFactory<ApplicationDbContext>`. Confirm via `dotnet build` that no other consumer needs the factory.
- **Test infrastructure**: existing `SmartsuppWebhookAuditWriterTests` likely uses in-memory or SQLite `DbContext`; verify it supports `IDbContextFactory` (`AddDbContextFactory` is supported by EF Core In-Memory and SQLite providers).
- **Manual migration deploy**: per project convention (`CLAUDE.md`: "Database migrations are manual, not automated in deployment"). Operator must apply the migration on staging and production after merge but before declaring the feature live.
- **Replay tool sanity check**: `tools/SmartsuppWebhookReplay` must be runnable against the affected `auditId` values captured 2026-06-13 12:25 UTC. No code change required if the existing tool already targets `ReplayWebhookEventRequest` by id; operator note in PR description per spec FR-5.