## Module
Customer Support (Smartsupp) — module-map part #29

## Finding
Four MediatR handlers and one recurring job in the Smartsupp Application slice take a direct constructor dependency on the Persistence layer's `ApplicationDbContext` instead of going through a repository abstraction:

- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ListWebhookAudit/ListWebhookAuditHandler.cs:12,14` — builds the whole filtered/paged query against `_context.SmartsuppWebhookAuditEntries` (`:26-53`)
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GetWebhookAuditEntry/GetWebhookAuditEntryHandler.cs:11,13`
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ReplayWebhookEvent/ReplayWebhookEventHandler.cs:13,16` — reads the entry, mutates `ReplayCount`/`LastReplayedAt` and calls `_context.SaveChangesAsync` (`:27,55-58`)
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RefreshOrphanContacts/RefreshOrphanContactsHandler.cs:14,20` — injects **both** `ISmartsuppRepository` (`:13`) and `ApplicationDbContext`, then queries `_db.SmartsuppConversations` directly at `:47`
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/Jobs/SmartsuppWebhookAuditCleanupJob.cs:17,31` — `_context.SmartsuppWebhookAuditEntries` at `:51-64`

This is an outlier, not the house pattern. Across the entire `Anela.Heblo.Application` assembly (1 631 `.cs` files) only 12 files mention `ApplicationDbContext`. Removing the `*Module.cs` DI-wiring files (`LogisticsModule`, `InvoicesModule`, `BankModule`, `GiftPackageManufactureModule`), the interface doc-comment in `Logistics/Contracts/IInventoryReservationService.cs:11`, and the two deliberately-documented `Infrastructure/` adapters (`InvoiceImportStatisticsSourceAdapter`, `ManufactureInventoryReservationAdapter`), **the five Smartsupp classes above are the only handlers/jobs in the Application layer that inject the DbContext.** Every other feature reaches persistence through a Domain-declared repository interface.

Related, and part of the same gap: the one persistence abstraction the webhook-audit sub-slice does have — `ISmartsuppWebhookAuditWriter` — is declared in the **Persistence** assembly (`backend/src/Anela.Heblo.Persistence/Smartsupp/ISmartsuppWebhookAuditWriter.cs:5`) rather than in `Anela.Heblo.Domain/Features/Smartsupp/` where every other Smartsupp contract lives (`ISmartsuppRepository`, `ISmartsuppPresenceRepository`, `ISmartsuppApiClient`). It is one of only two `public interface` declarations in the whole Persistence assembly, and it makes `SmartsuppWebhookController.cs:8,27` the only controller in the API project with a feature-level `using Anela.Heblo.Persistence`.

## Rule
`docs/architecture/development_guidelines.md`, *Forbidden Practices*:

> | **Shared DbContext** | Violates separation, creates coupling |

*Common Pitfalls to Avoid*:

> 5. **Don't bypass contracts** - Always communicate through interfaces

and **ADR-002 (Generic Repository Pattern)** — "Generic repository in Xcc, extended per feature".

This is an established arch-review class in this repository: #1827 (Photobank: `PhotobankIndexJob` bypasses `IPhotobankRepository` and directly injects `ApplicationDbContext`), #3278 / #3393 (Bank: `BankStatementStatisticsSourceAdapter`), #1952 (Analytics). No equivalent issue exists for Smartsupp.

## Why it matters
`ISmartsuppRepository` already exists and already owns this table family — `RefreshOrphanContactsHandler` injects it and the raw `DbContext` side by side, so the same handler both honours and bypasses the abstraction. The consequences are concrete:

- **ADR-001 Phase 2 is blocked for this module.** The stated migration path is one `DbContext` per module; five Application-layer classes typed against the shared `ApplicationDbContext` have to be rewritten before Smartsupp can move, and the compiler will not point at them.
- **The handlers cannot be unit-tested without EF.** Every other feature's handlers are tested against a mocked repository interface; these four require an in-memory or real `DbContext`.
- **Query rules have no single home.** `ListWebhookAuditHandler`'s `MaxTake` clamp (`:10,24`) and the cleanup job's retention window (`:11,50`) both encode audit-table access policy in the Application layer, where a future second reader will not find them.

## Suggested direction
Declare an audit repository contract next to the others in `Anela.Heblo.Domain/Features/Smartsupp/` — absorbing `ISmartsuppWebhookAuditWriter`'s two write methods plus the list/get/replay-stamp/purge reads — implement it in `Anela.Heblo.Persistence/Smartsupp/`, bind it in `SmartsuppModule.cs` per ADR-004, and drop the `ApplicationDbContext` and `using Anela.Heblo.Persistence` from the five classes above and from `SmartsuppWebhookController`. `RefreshOrphanContactsHandler`'s single direct query is already close to `ISmartsuppRepository`'s existing surface. Do not implement the fix here — this issue only records the finding.

---
_Filed by arch-review of module-map part #29._