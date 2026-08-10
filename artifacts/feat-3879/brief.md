## Module
Customer Support (Smartsupp) — module-map part #29

## Finding
In `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/`, eight of the eighteen `ISmartsuppWebhookReaction` implementations carry only three distinct behaviours. Within each group the `HandleAsync` bodies are character-for-character identical; only the `EventName` string differs.

**Group A — upsert conversation + upsert message (3 copies):**
- `ConversationAgentRepliedReaction.cs:16-30`
- `ConversationBotRepliedReaction.cs:16-30`
- `ConversationContactRepliedReaction.cs:16-30`

**Group B — upsert contact + backfill conversation denorm fields (3 copies):**
- `ContactCreatedReaction.cs:14-21`
- `ContactUpdatedReaction.cs:14-21`
- `ContactAcquiredReaction.cs:14-21`

**Group C — upsert contact only (2 copies):**
- `ContactBannedReaction.cs:14-19`
- `ContactUnbannedReaction.cs:14-19`

All eight are registered individually in `Application/Features/Smartsupp/SmartsuppModule.cs:54,55,56` and `:66,67,68,69,70`.

## Rule
`docs/architecture/development_guidelines.md` places all code for a behaviour in one place (*Feature cohesion*), and this repository has already accepted the same finding shape: #3612 ("Invoices: `DailyInvoiceImportCzkJob` and `DailyInvoiceImportEurJob` are identical except for currency constant"), and #3853 ("Packaging: shipment-creation logic is copy-pasted between `ScanPackingOrder` and `ResetOrderShipment` handlers, **and the copies drifted**").

## Why it matters
One rule about how a Smartsupp event is persisted now lives in three files. Any change to the message-upsert path — an added field, a null guard, an idempotency check, a different `ConversationId` fallback when `msg.ConversationId` comes back empty from `SmartsuppPayloadMapper.MapMessage` (`Mappers/SmartsuppPayloadMapper.cs:72-74`) — has to be made three times, and nothing fails if it is made twice. #3853 is the same shape after it drifted, in a module where the drift silently stopped persisting rows.

The copies have not diverged yet, which makes this cheap to collapse now and progressively more expensive later.

## Suggested direction
Give each group a shared base class (or a single implementation parameterised by event name) so the behaviour is written once and the per-event classes carry only their `EventName`. `ConversationClosedReaction` / `ConversationClosedByContactReaction` are a useful contrast — they share a shape but genuinely differ in what they set, so they should stay separate. Do not implement the fix here — this issue only records the finding.

---
_Filed by arch-review of module-map part #29._
