# Specification: Deduplicate Shoptet order-status ID constants (ExpeditionList / Logistics.Picking)

## Summary
`ExpeditionPickingRequest` (ExpeditionList module) and `PrintPickingListRequest` (Logistics module) each independently declare the same three Shoptet order-status ID constants (`DefaultSourceStateId = -2`, `DefaultDesiredStateId = 26`, `DefaultNoteStateId = 35`). This spec eliminates the duplication by making `ExpeditionPickingRequest`'s constants the single source of truth and having `PrintPickingListRequest` reference them, since the Logistics module already depends on the `ExpeditionList.Contracts` namespace for this exact request/adapter pairing. No behavior, value, or public contract changes — this is a pure internal refactor.

## Background
`ExpeditionPickingRequest` (`Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs`) is the consumer-owned contract for the expedition picking flow. `LogisticsExpeditionPickingAdapter` (`Application/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapter.cs`) implements `ExpeditionList.Contracts.IExpeditionPickingSource` and translates an `ExpeditionPickingRequest` into a `PrintPickingListRequest` (`Application/Features/Logistics/Picking/PrintPickingListRequest.cs`) before delegating to `IPickingListSource`. `LogisticsModule.cs` explicitly documents this as a provider-side cross-module contract: "Logistics provides ExpeditionList's IExpeditionPickingSource via adapter... DI registration is owned by the provider (Logistics), not the consumer (ExpeditionList)."

Critically, the adapter file already has `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;` and references `ExpeditionPickingRequest`/`ExpeditionPickingResult` types directly. The test suite mirrors this: `PickingListIntegrationTests.cs` (in `Anela.Heblo.Adapters.Shoptet.Tests`) already imports both `ExpeditionList.Contracts` and `Logistics.Picking` namespaces side by side and references `ExpeditionPickingRequest.DefaultCarriers` alongside `PrintPickingListRequest`'s constants in the same test. In other words, the dependency direction **Logistics → ExpeditionList.Contracts already exists in shipped code** for this exact pairing — it is not a new architectural boundary being crossed, only a duplication being removed within a boundary that already exists.

This resolves the brief's stated alternative in favor of a single answer: designate `ExpeditionPickingRequest`'s constants as canonical, delete the duplicates from `PrintPickingListRequest`, and have `PrintPickingListRequest` reference `ExpeditionPickingRequest`'s constants for its own default property initializers.

These three IDs are Shoptet order-lifecycle status IDs (a business-domain fact, not an implementation detail): `-2` = "Vyřizuje se" (processing / source state to pick from), `26` = "Bali se" (packing / desired target state), `35` = a note state used to flag orders with an incomplete address.

## Functional Requirements

### FR-1: Single declaration of each state-ID constant
`DefaultSourceStateId`, `DefaultDesiredStateId`, and `DefaultNoteStateId` must each be declared exactly once in the codebase, on `ExpeditionPickingRequest` (`Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs`). Values are unchanged: `-2`, `26`, `35` respectively.

**Acceptance criteria:**
- `ExpeditionPickingRequest.cs` retains its existing `DefaultSourceStateId`, `DefaultDesiredStateId`, `DefaultNoteStateId` constant declarations and values, unchanged.
- `PrintPickingListRequest.cs` (`Application/Features/Logistics/Picking/PrintPickingListRequest.cs`) no longer declares `DefaultSourceStateId`, `DefaultDesiredStateId`, or `DefaultNoteStateId`.
- A repo-wide search for `public const int DefaultSourceStateId`, `public const int DefaultDesiredStateId`, and `public const int DefaultNoteStateId` returns exactly one match each, all in `ExpeditionPickingRequest.cs`.

### FR-2: `PrintPickingListRequest` defaults reference the canonical constants
`PrintPickingListRequest`'s `SourceStateId`, `DesiredStateId`, and `NoteStateId` auto-properties must keep defaulting to the same three values as before, now sourced from `ExpeditionPickingRequest`'s constants via a `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;` import (the same import already used by `LogisticsExpeditionPickingAdapter.cs` in the same module).

**Acceptance criteria:**
- `PrintPickingListRequest.SourceStateId` defaults to `ExpeditionPickingRequest.DefaultSourceStateId`.
- `PrintPickingListRequest.DesiredStateId` defaults to `ExpeditionPickingRequest.DefaultDesiredStateId`.
- `PrintPickingListRequest.NoteStateId` defaults to `ExpeditionPickingRequest.DefaultNoteStateId`.
- Default values observed by a caller constructing `new PrintPickingListRequest()` are unchanged: `-2`, `26`, `35`.
- No new project/assembly reference is required — `ExpeditionPickingRequest` and `PrintPickingListRequest` already live in the same `Anela.Heblo.Application` project, just different namespaces.

### FR-3: Preserve domain-meaning comments
`PrintPickingListRequest`'s constants currently carry inline comments identifying the Shoptet status names ("Vyrizuje se" on `DefaultSourceStateId`, "Bali se" on `DefaultDesiredStateId`) that `ExpeditionPickingRequest`'s constants lack (only `DefaultNoteStateId` has a comment there today: "Poznámka — orders with incomplete address"). These must not be lost.

**Acceptance criteria:**
- `ExpeditionPickingRequest.DefaultSourceStateId` carries a comment identifying it as Shoptet status "Vyřizuje se" (processing).
- `ExpeditionPickingRequest.DefaultDesiredStateId` carries a comment identifying it as Shoptet status "Bali se" (packing).
- `ExpeditionPickingRequest.DefaultNoteStateId`'s existing comment ("Poznámka — orders with incomplete address") is retained as-is.
- The stray commented-out dead code line in `PrintPickingListRequest.cs` (`//private const string DesiredStateId = "26"; // Bali se`), which sits inside the block of lines being removed, is removed along with it — it has no independent purpose once its adjacent constant is gone.

### FR-4: Update dependent test code
`PickingListIntegrationTests.cs` (`Anela.Heblo.Adapters.Shoptet.Tests`) references `PrintPickingListRequest.DefaultSourceStateId` and `PrintPickingListRequest.DefaultDesiredStateId` directly. These references must be updated to the canonical `ExpeditionPickingRequest` constants; no other production or test code references the removed constants (confirmed by repo-wide search — see Dependencies).

**Acceptance criteria:**
- `PickingListIntegrationTests.cs` line ~23 (`private const int SourceStateId = PrintPickingListRequest.DefaultSourceStateId;`) is changed to reference `ExpeditionPickingRequest.DefaultSourceStateId`.
- `PickingListIntegrationTests.cs` line ~88 (`DesiredStateId = PrintPickingListRequest.DefaultDesiredStateId`) is changed to reference `ExpeditionPickingRequest.DefaultDesiredStateId`.
- The file already imports `Anela.Heblo.Application.Features.ExpeditionList.Contracts`, so no new `using` is needed there.
- The explanatory comment above the constant reference ("Must match `PrintPickingListRequest.DefaultSourceStateId`...") is updated to reflect the new source (`ExpeditionPickingRequest.DefaultSourceStateId`) rather than describing a match between two separate declarations, since there is now only one declaration.
- `LogisticsExpeditionPickingAdapterTests.cs` and `LogisticsExpeditionPickingAdapter.cs` require no changes — they never reference the removed constants by name; the adapter builds `PrintPickingListRequest` by copying field values from an already-constructed `ExpeditionPickingRequest` instance, not by reading the removed default constants.

### FR-5: No behavior change
This is a structural deduplication only. Runtime behavior, DTO shape (property names/types), serialization, and all effective default values must be identical before and after.

**Acceptance criteria:**
- `ExpeditionPickingRequest` and `PrintPickingListRequest` retain identical public property lists, types, and default values as before this change.
- `LogisticsExpeditionPickingAdapter`'s field-by-field copy logic (`CreatePickingListAsync`) is unchanged.
- All existing tests in `LogisticsExpeditionPickingAdapterTests.cs` pass unmodified.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable. This is a compile-time constant reference change with zero runtime cost difference — `const int` fields are inlined by the C# compiler regardless of which class declares them.

### NFR-2: Security
Not applicable. No change to authentication, authorization, data sensitivity, or external-facing surface. The constants are internal default configuration values, not secrets.

## Data Model
No persisted or transmitted data model changes. Two existing DTOs are affected structurally (not in shape):

- **`ExpeditionPickingRequest`** (`ExpeditionList.Contracts`) — becomes the sole owner of `DefaultSourceStateId`, `DefaultDesiredStateId`, `DefaultNoteStateId` (already owns `DefaultCarriers`, unaffected).
- **`PrintPickingListRequest`** (`Logistics.Picking`) — loses its local constant declarations; its `SourceStateId`, `DesiredStateId`, `NoteStateId` properties now default from `ExpeditionPickingRequest`'s constants instead of local ones. Property names, types, and JSON/serialization shape are unchanged.

Relationship: `LogisticsExpeditionPickingAdapter.CreatePickingListAsync` continues to map an `ExpeditionPickingRequest` instance's *runtime property values* (not the class-level default constants) onto a new `PrintPickingListRequest` instance — this mapping code is untouched by this change. The constant consolidation only affects what each class's *own* defaults are drawn from when no explicit value is supplied by a caller.

## API / Interface Design
No public API, controller, or MediatR handler changes. This is an internal `Application` project code change:

- Remove 3 `public const int` lines (plus 1 stray commented-out line) from `PrintPickingListRequest.cs`.
- Add a `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;` to `PrintPickingListRequest.cs`.
- Change the 3 property-default expressions in `PrintPickingListRequest.cs` to reference `ExpeditionPickingRequest.DefaultSourceStateId` / `.DefaultDesiredStateId` / `.DefaultNoteStateId`.
- Add 2 explanatory comments to `ExpeditionPickingRequest.cs` (Shoptet status names for source/desired state IDs; note-state-ID comment already exists).
- Update 2 constant references in `PickingListIntegrationTests.cs` from `PrintPickingListRequest.*` to `ExpeditionPickingRequest.*`, plus its adjacent explanatory comment.

## Dependencies
- No new project or package dependencies. `ExpeditionPickingRequest` and `PrintPickingListRequest` are both in the `Anela.Heblo.Application` project; the namespace-level dependency (`Logistics` code referencing `ExpeditionList.Contracts`) already exists via `LogisticsExpeditionPickingAdapter.cs` and is documented as an intentional provider-side pattern in `LogisticsModule.cs`.
- Repo-wide search (excluding docs/plan markdown, which are historical and not updated) confirms only two production/test locations reference the constants by their declaring type: `LogisticsExpeditionPickingAdapter`'s own instance construction (uses runtime values, not the constants — unaffected) and `PickingListIntegrationTests.cs` (FR-4).

## Out of Scope
- Changing any of the three constant *values* (`-2`, `26`, `35`).
- Renaming the constants or the two request classes.
- Restructuring `LogisticsExpeditionPickingAdapter`'s mapping logic.
- Addressing any other duplication findings elsewhere in the codebase not named in this brief.
- Updating the historical planning documents under `docs/superpowers/plans/` that happen to mention these constants — they are point-in-time records, not living documentation.
- Introducing a shared `Anela.Heblo.Domain` or `Xcc`-level constants class — the brief's suggested fix, and the code's existing dependency direction, both point to keeping this within the existing `ExpeditionPickingRequest` ownership rather than inventing a new shared location.

## Open Questions
None.

## Status: COMPLETE
