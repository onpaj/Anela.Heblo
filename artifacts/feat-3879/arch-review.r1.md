# Architecture Review: Collapse duplicate Smartsupp webhook reaction implementations into shared base classes

## Skip Design: true

## Architectural Fit Assessment

This is a same-file-shape refactor inside a single existing folder (`Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/`). It touches no module boundary, no contract, no DI wiring pattern, and no persistence shape — it only reduces the number of places one behaviour is written. I verified all eight duplicated classes byte-for-byte against the spec's claims (Group A: `ConversationAgentRepliedReaction`, `ConversationBotRepliedReaction`, `ConversationContactRepliedReaction`; Group B: `ContactCreatedReaction`, `ContactUpdatedReaction`, `ContactAcquiredReaction`; Group C: `ContactBannedReaction`, `ContactUnbannedReaction`) and confirmed each group's `HandleAsync` body is identical apart from the `EventName` literal. I also confirmed `ConversationClosedReaction` and `ConversationClosedByContactReaction` genuinely diverge (different `CloseType` source, `ConversationClosedReaction` reads `agent_id` and falls back to `ctx.Data`; `ConversationClosedByContactReaction` hardcodes `"contact"` and has no `agent_id` read) — correctly left out of scope.

The spec's overall design — inheritance-based, thin sealed subclasses, no DI change — is sound and matches a real precedent in this codebase. There is one deviation from that precedent worth correcting before implementation: the accessibility level of the base classes.

**Precedent found:** issue #3612 (PR #3627, merged) is the same finding shape — `DailyInvoiceImportCzkJob`/`DailyInvoiceImportEurJob` were collapsed into `DailyInvoiceImportJobBase` with two `sealed` subclasses overriding only `Metadata`/`Currency`. I read the actual merged files:

- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/Jobs/DailyInvoiceImportJobBase.cs` — `public abstract class DailyInvoiceImportJobBase : IRecurringJob`
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/Jobs/DailyInvoiceImportCzkJob.cs` — `public sealed class DailyInvoiceImportCzkJob : DailyInvoiceImportJobBase`, colocated in the same folder, constructor forwards to `base(...)`, overrides only the per-instance members.

A second precedent, `BankImportJobBase` (`Features/Bank/Infrastructure/Jobs/BankImportJobBase.cs`), follows the identical shape. A repo-wide grep for `abstract class .*Base` under `Anela.Heblo.Application` turns up eight hits (`DailyInvoiceImportJobBase`, `BankImportJobBase`, `InventoryCountTileBase`, `InventorySummaryTileBase`, `TransportBoxBaseTile`, `UpcomingProductionTile`, plus two non-"Base"-pattern abstracts) — **every single one is `public abstract class`**. There is no existing `internal abstract class` used as a shared-behaviour base anywhere in `Anela.Heblo.Application`. The spec's proposal to make the three new base classes `internal` is therefore a departure from the codebase's own established convention for this exact refactor shape, not an application of one. See Decision 1 below — I'm overriding this part of the spec.

I also checked the accessibility/compile concern the brief raised: `Anela.Heblo.Application.csproj` (line 47) has `<InternalsVisibleTo Include="Anela.Heblo.Tests" />`, and `AssemblyInfo.cs` additionally grants `Anela.Heblo.API` and `DynamicProxyGenAssembly2` (Castle DynamicProxy, used by the test project's mocking). So even if the base classes were `internal`, the test assembly could see them — but it doesn't matter either way, because C# permits a `public sealed class` to derive from an `internal abstract class` within the same assembly (the derived type's public surface is exactly the interface members it overrides, which are already public), and `SmartsuppModule.cs` sits in the same assembly as the reactions, so DI registration of the public concrete types is unaffected by the base class's accessibility. There is no compile blocker either way. This confirms accessibility is a style choice, not a constraint — and the codebase's existing style choice, consistently, is `public`.

## Proposed Architecture

### Component Overview

```
Reactions/
├── ISmartsuppWebhookReaction.cs                    (unchanged)
├── WebhookEventContext.cs                          (unchanged)
├── ConversationReplyReactionBase.cs                 [NEW] public abstract class
│   ├── ConversationAgentRepliedReaction.cs          sealed, EventName override only
│   ├── ConversationBotRepliedReaction.cs            sealed, EventName override only
│   └── ConversationContactRepliedReaction.cs        sealed, EventName override only
├── ContactUpsertWithBackfillReactionBase.cs         [NEW] public abstract class
│   ├── ContactCreatedReaction.cs                    sealed, EventName override only
│   ├── ContactUpdatedReaction.cs                    sealed, EventName override only
│   └── ContactAcquiredReaction.cs                   sealed, EventName override only
├── ContactUpsertOnlyReactionBase.cs                 [NEW] public abstract class
│   ├── ContactBannedReaction.cs                     sealed, EventName override only
│   └── ContactUnbannedReaction.cs                   sealed, EventName override only
└── (10 other reaction classes, untouched)
```

`ProcessWebhookEventHandler` continues to resolve `IEnumerable<ISmartsuppWebhookReaction>` and dictionary-key by `EventName` (`ProcessWebhookEventHandler.cs:22-25`) — no change there. `SmartsuppModule.cs:54-70` continues to register each concrete sealed type individually — no change there either, since none of the eighteen registration lines reference a base class.

### Key Design Decisions

#### Decision 1: Base class accessibility — `public`, not `internal`

**Options considered:**
- `internal abstract class` (spec's proposal) — reasoning given was "nothing outside `Reactions/` needs to construct them directly."
- `public abstract class` (matches `DailyInvoiceImportJobBase`, `BankImportJobBase`, and every other `*Base` abstract in `Anela.Heblo.Application`).

**Chosen approach:** `public abstract class`, following the codebase's existing pattern exactly.

**Rationale:** The "nothing outside this folder needs it" argument applies equally to `DailyInvoiceImportJobBase` (nothing outside `Invoices/Infrastructure/Jobs/` needs it) and `BankImportJobBase`, yet both are `public`. Introducing the first `internal` base class of this shape creates an unexplained inconsistency for the next developer to puzzle over, for zero functional benefit — the accessibility change doesn't affect DI, tests, or `GetType().Name` behavior. Matching the established convention is lower-risk and requires no extra justification during code review. This is a pure amendment to the spec's API/Interface Design section; everything else in that section (constructor shape, `protected` field, `EventName` abstract, `HandleAsync` body) is correct and should be implemented as written, just with `public` instead of `internal`.

#### Decision 2: Three separate base classes vs. one parameterised class

**Options considered:**
- Single class taking an `EventName` constructor parameter (rejected by the spec).
- Single class with a "should backfill" flag merging Group B and C (rejected by the spec's FR-3).
- Three separate base classes, one per behaviour (spec's choice).

**Chosen approach:** Three separate base classes, confirmed correct after reading `ProcessWebhookEventHandler.cs:63`, which logs `reaction.GetType().Name` on failure — this is genuinely used for error attribution today, and a parameterised single-class design would collapse three distinct log signatures (`ConversationAgentRepliedReaction`, `ConversationBotRepliedReaction`, `ConversationContactRepliedReaction`) into one indistinguishable type name. The existing test files also construct concrete types by name (`ContactReactionsTests.cs:45-50`, `ConversationReactionsTests.cs:134,149,164`), which a parameterised design would break without touching the tests. Inheritance is the only option that satisfies both constraints with zero test/registration changes.

**Rationale:** Verified directly — this is not speculative. `ProcessWebhookEventHandler.cs:62-64` does exactly what the spec claims, and every test call site the spec cites exists at the line numbers given, confirmed by reading both test files in full.

## Implementation Guidance

### Directory / Module Structure

New files, all in `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/`:
- `ConversationReplyReactionBase.cs`
- `ContactUpsertWithBackfillReactionBase.cs`
- `ContactUpsertOnlyReactionBase.cs`

Modified files (body reduced to constructor + `EventName` override only), same folder:
- `ConversationAgentRepliedReaction.cs`, `ConversationBotRepliedReaction.cs`, `ConversationContactRepliedReaction.cs`
- `ContactCreatedReaction.cs`, `ContactUpdatedReaction.cs`, `ContactAcquiredReaction.cs`
- `ContactBannedReaction.cs`, `ContactUnbannedReaction.cs`

No other file changes. `SmartsuppModule.cs` and both test files are correctly identified in the spec as needing zero changes — confirmed: `SmartsuppModule.cs:54-70` registers by concrete type name only, and both test files instantiate concrete types only, never a base class.

### Interfaces and Contracts

Follow the spec's FR-1–FR-3 exactly, with `public` in place of `internal`:

```csharp
namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public abstract class ConversationReplyReactionBase : ISmartsuppWebhookReaction
{
    protected readonly ISmartsuppRepository Repository;
    protected ConversationReplyReactionBase(ISmartsuppRepository repository) => Repository = repository;
    public abstract string EventName { get; }

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation();
        if (convEl.HasValue)
            await Repository.UpsertConversationAsync(
                SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp), cancellationToken);

        var msgEl = ctx.GetMessage();
        if (msgEl.HasValue)
        {
            var msg = SmartsuppPayloadMapper.MapMessage(msgEl.Value);
            await Repository.UpsertMessagesAsync(msg.ConversationId, new List<SmartsuppMessage> { msg }, cancellationToken);
        }
    }
}

public sealed class ConversationAgentRepliedReaction : ConversationReplyReactionBase
{
    public ConversationAgentRepliedReaction(ISmartsuppRepository repository) : base(repository) { }
    public override string EventName => "conversation.agent_replied";
}
```

Same shape for `ContactUpsertWithBackfillReactionBase` (body verified in `ContactCreatedReaction.cs:14-21`: null-guard on `ctx.GetContact()`, `MapContact`, `UpsertContactAsync`, then `BackfillConversationDenormFieldsAsync`) and `ContactUpsertOnlyReactionBase` (body verified in `ContactBannedReaction.cs:14-19`: same but no backfill call).

Naming convention note: `*Base` suffix at the end of the class name matches `DailyInvoiceImportJobBase` and `BankImportJobBase` (not `TransportBoxBaseTile`'s "Base" in the middle, which is the outlier in this codebase, not the pattern to copy). The spec's proposed names already follow the correct trailing-`Base` convention — no change needed there.

`ISmartsuppWebhookReaction`, `WebhookEventContext`, `SmartsuppPayloadMapper`, `ProcessWebhookEventHandler`, and `SmartsuppModule.cs` are all confirmed unchanged — verified by reading each file.

### Data Flow

No change to data flow. `ProcessWebhookEventHandler.Handle` → dictionary lookup by `EventName` → `reaction.HandleAsync(ctx, ct)` → (unchanged) `SmartsuppPayloadMapper` calls → (unchanged) `ISmartsuppRepository` calls → `_repository.SaveChangesAsync`. The only new element in the call graph is one additional virtual dispatch hop (concrete class → base class `HandleAsync`), which has no observable effect.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Implementer copies the spec's `internal` accessibility literally, diverging from codebase convention | Low | This review's Decision 1 amends the spec to `public`; flag in PR description or code review if `internal` appears |
| Body copied into base class introduces a subtle diff (e.g. reordering the conversation/message upsert calls) | Low | FR-1–FR-3 acceptance criteria already require byte-for-byte equivalence; a reviewer diff of old vs. new `HandleAsync` bodies against this review's verified snippets above is sufficient verification |
| A future reaction is accidentally added as a subclass of the wrong base (e.g., a class needing backfill inherits `ContactUpsertOnlyReactionBase`) | Low | Base class names are self-describing (`WithBackfill` vs `Only`); no further mitigation needed for a 3-base-class, 8-subclass surface |
| None of the eighteen DI registrations or two test files need touching, but a slip during implementation could still edit them unnecessarily | Low | FR-4's `git diff` acceptance criterion (only 11 files touched: 3 new + 8 modified) is easy to check mechanically before merge |

No medium/high risks — this is a pure structural refactor with unusually strong regression guardrails (existing tests already exercise every affected class's `HandleAsync` and `EventName` without modification).

## Specification Amendments

1. **Base class accessibility: change `internal abstract class` to `public abstract class`** in FR-1, FR-2, FR-3, and the API/Interface Design section. This is the only substantive change to the spec. Rationale is Decision 1 above: every existing shared-behaviour base class in `Anela.Heblo.Application` (`DailyInvoiceImportJobBase`, `BankImportJobBase`, `InventoryCountTileBase`, `InventorySummaryTileBase`) is `public`, and the spec's own stated justification for `internal` ("nothing outside `Reactions/` needs to construct them directly") applies equally to those precedents, which chose `public` anyway. No compile or test-visibility issue blocks `public` — it is strictly the more consistent choice.
2. Everything else in the spec — the three-base-class split, constructor/field shape, `sealed` concrete subclasses, "no DI change," "no test change," and the explicit non-goals (leave `ConversationClosedReaction`/`ConversationClosedByContactReaction` and the other ten reactions untouched) — is verified correct against the current source and requires no further amendment.

## Prerequisites

None. No migrations, no config, no infrastructure changes. This can be implemented directly against `main` (or the current feature branch) with `dotnet build` + existing test suite as the only gate, per the repo's standard validation step for a backend-only change.
