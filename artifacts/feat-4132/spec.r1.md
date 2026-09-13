# Specification: Move `OutlookEventDto` (Microsoft Graph schema) from Marketing `Services/` to `Infrastructure/`

## Summary
`backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookEventDto.cs` defines three types (`OutlookEventDto`, `GraphEventBody`, `GraphEventDateTime`) that are pure Microsoft Graph API response schemas — `[JsonPropertyName]`-annotated wire-format DTOs with Graph-specific date parsing — misplaced in the `Services/` folder, which per `docs/architecture/filesystem.md` is reserved for domain services and business logic. This change relocates the file to `Features/Marketing/Infrastructure/`, updates its namespace to `Anela.Heblo.Application.Features.Marketing.Infrastructure`, and updates every consumer's `using` directives so the solution keeps compiling and behaving identically. The `IOutlookCalendarSync` interface itself stays in `Services/` — only its `using` directive changes, since the interface remains the correct module-boundary contract, it just now references a DTO type that lives in a sibling namespace.

## Background

### Current state
`Services/OutlookEventDto.cs` contains:
- `OutlookEventDto` — Graph calendar event shape (`Id`, `Subject`, `Body`, `Start`, `End`, `Categories`), with computed `BodyText`, `StartUtc`, `EndUtc` properties that parse Graph's wire-format strings via `DateTime.Parse(..., DateTimeStyles.RoundtripKind)`.
- `GraphEventBody` — Graph's `body` object (`Content`, `ContentType`).
- `GraphEventDateTime` — Graph's `start`/`end` object (`DateTimeString`, `TimeZone`).

All three carry `System.Text.Json.Serialization.JsonPropertyName` attributes and contain no domain/business logic — they are the deserialization target for Microsoft Graph API responses (both the live `OutlookCalendarSyncService` adapter in `Anela.Heblo.Adapters.Microsoft365`, which `JsonSerializer.DeserializeAsync`s them directly, and the `NoOpOutlookCalendarSync` no-op implementation used under mock auth).

`Services/` in this module also holds genuine domain services: `IMarketingCalendarSyncService`/`MarketingCalendarSyncService`, `IMarketingCategoryMapper`/`MarketingCategoryMapper`, `IOutlookCalendarSync`/`NoOpOutlookCalendarSync`, `SyncActor`, `OutlookCalendarSyncException`. `Features/Marketing/Infrastructure/` already exists (currently holding only `Jobs/MarketingCalendarSyncJob.cs`) and is the documented home for feature infrastructure per `docs/architecture/filesystem.md` (`Features/{Feature}/Infrastructure/`: Feature-specific infrastructure).

### Full consumer inventory (verified against source, not just the two files the triaging finding named)
A repo-wide search for `OutlookEventDto`, `GraphEventBody`, `GraphEventDateTime` found these files referencing the moved types by name (beyond the declaration file itself):

| File | References | Needs |
|---|---|---|
| `Application/Features/Marketing/Services/IOutlookCalendarSync.cs` | `OutlookEventDto` in `ListEventsAsync`/`GetEventAsync` signatures | Add new `using` (interface stays in `Services/`) |
| `Application/Features/Marketing/Services/NoOpOutlookCalendarSync.cs` | `OutlookEventDto` in `ListEventsAsync`/`GetEventAsync` return types | Add new `using` (class stays in `Services/`) |
| `Application/Features/Marketing/UseCases/ImportFromOutlook/OutlookEventImportMapper.cs` | `OutlookEventDto` parameter throughout | Add new `using` (existing `using ...Services;` stays — needed for `SyncActor`) |
| `Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookCalendarSyncService.cs` | `OutlookEventDto` return types, `JsonSerializer.DeserializeAsync<OutlookEventDto>` | Add new `using` (existing `using ...Services;` stays — needed for `IOutlookCalendarSync`, `IMarketingCategoryMapper`, `OutlookCalendarSyncException`) |
| `Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookInternalDtos.cs` | `List<OutlookEventDto> Value` in `GraphEventCollection` | Replace existing `using ...Services;` with the new namespace (no other `Services` symbol used in this file) |
| `Tests/Features/Marketing/UseCases/ImportFromOutlookHandlerTests.cs`* | `OutlookEventDto`, `GraphEventBody`, `GraphEventDateTime` constructed throughout | Add new `using` (existing `using ...Services;` stays — needed for `IOutlookCalendarSync`, `IMarketingCategoryMapper`) |
| `Tests/Features/Marketing/Services/MarketingCalendarSyncServiceTests.cs` | `OutlookEventDto`, `GraphEventDateTime` constructed throughout | Add new `using` (existing `using ...Services;` stays — needed for `SyncActor`, `IOutlookCalendarSync`, `IMarketingCategoryMapper`, `MarketingCalendarSyncService`) |

\* File is at `backend/test/Anela.Heblo.Tests/Features/Marketing/ImportFromOutlookHandlerTests.cs`.

The original finding named only `IOutlookCalendarSync.cs` and `OutlookEventImportMapper.cs`; five more call sites exist and must be updated for the solution to compile. `MarketingCalendarSyncService.cs` was checked and does **not** reference any of the three type names directly (it only calls through `IOutlookCalendarSync`, receiving results via `var`/inferred types) — no change needed there.

### Why it matters
A developer opening `Services/` should find domain logic, not a third-party API's wire schema. The `using System.Text.Json.Serialization;` import and the Graph-specific `DateTime.Parse` logic in `OutlookEventDto` are signals this file belongs in `Infrastructure/`, matching how every other feature module in this codebase separates the two concerns (28 other `Features/{Module}/Infrastructure/` folders already exist). This is a pure move-and-rename with zero behavior change — no API, persistence, or business-logic impact.

## Functional Requirements

### FR-1: Relocate `OutlookEventDto.cs` to `Infrastructure/`
Move `backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookEventDto.cs` to `backend/src/Anela.Heblo.Application/Features/Marketing/Infrastructure/OutlookEventDto.cs` (file name unchanged; all three classes — `OutlookEventDto`, `GraphEventBody`, `GraphEventDateTime` — stay together in one file, matching their current colocation). Change the namespace declaration from `Anela.Heblo.Application.Features.Marketing.Services` to `Anela.Heblo.Application.Features.Marketing.Infrastructure`. No other content in the file changes — property shapes, `[JsonPropertyName]` attributes, and the `StartUtc`/`EndUtc`/`BodyText` computed properties are byte-identical.

**Acceptance criteria:**
- `Services/OutlookEventDto.cs` no longer exists.
- `Infrastructure/OutlookEventDto.cs` exists with identical class bodies to the original, only the `namespace` line changed.
- No `.csproj` changes required (SDK-style project globs `Features/**/*.cs` automatically; verified `Anela.Heblo.Application.csproj` uses no explicit `<Compile>` includes/excludes for this path).

### FR-2: Update `Services/IOutlookCalendarSync.cs`
Add `using Anela.Heblo.Application.Features.Marketing.Infrastructure;` to the interface file. The interface itself (`ListEventsAsync`, `GetEventAsync` signatures referencing `OutlookEventDto`) is otherwise unchanged and stays declared in the `Anela.Heblo.Application.Features.Marketing.Services` namespace — it remains the correct consumer-owned module-boundary contract.

**Acceptance criteria:**
- File compiles with the added `using`.
- No other line in the file changes.

### FR-3: Update `Services/NoOpOutlookCalendarSync.cs`
Add `using Anela.Heblo.Application.Features.Marketing.Infrastructure;`. Class body, namespace, and behavior unchanged.

**Acceptance criteria:**
- File compiles with the added `using`.
- No other line in the file changes.

### FR-4: Update `UseCases/ImportFromOutlook/OutlookEventImportMapper.cs`
Add `using Anela.Heblo.Application.Features.Marketing.Infrastructure;` alongside the existing `using Anela.Heblo.Application.Features.Marketing.Services;` (the latter stays — required for `SyncActor`). No other line changes.

**Acceptance criteria:**
- File compiles with both `using` directives present.
- `SyncActor` resolution is unaffected.

### FR-5: Update `Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookCalendarSyncService.cs`
Add `using Anela.Heblo.Application.Features.Marketing.Infrastructure;` alongside the existing `using Anela.Heblo.Application.Features.Marketing.Services;` (the latter stays — required for `IOutlookCalendarSync`, `IMarketingCategoryMapper`, `OutlookCalendarSyncException`). No other line changes.

**Acceptance criteria:**
- File compiles with both `using` directives present.
- `JsonSerializer.DeserializeAsync<OutlookEventDto>(...)` call resolves against the new namespace.

### FR-6: Update `Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookInternalDtos.cs`
This file's only reference to the `Services` namespace is `OutlookEventDto` (used in `GraphEventCollection.Value`). Replace `using Anela.Heblo.Application.Features.Marketing.Services;` with `using Anela.Heblo.Application.Features.Marketing.Infrastructure;` (swap, not add — no other symbol from `Services` is used in this file).

**Acceptance criteria:**
- File compiles with the swapped `using`.
- No `using Anela.Heblo.Application.Features.Marketing.Services;` remains in this file.

### FR-7: Update `Tests/Features/Marketing/ImportFromOutlookHandlerTests.cs`
Add `using Anela.Heblo.Application.Features.Marketing.Infrastructure;` alongside the existing `using Anela.Heblo.Application.Features.Marketing.Services;` (the latter stays — required for `IOutlookCalendarSync`, `IMarketingCategoryMapper` mocks). No test logic or assertion changes.

**Acceptance criteria:**
- File compiles with both `using` directives present.
- All existing tests in this file continue to pass unmodified.

### FR-8: Update `Tests/Features/Marketing/Services/MarketingCalendarSyncServiceTests.cs`
Add `using Anela.Heblo.Application.Features.Marketing.Infrastructure;` alongside the existing `using Anela.Heblo.Application.Features.Marketing.Services;` (the latter stays — required for `SyncActor`, `IOutlookCalendarSync`, `IMarketingCategoryMapper`, `MarketingCalendarSyncService`). No test logic or assertion changes.

**Acceptance criteria:**
- File compiles with both `using` directives present.
- All existing tests in this file continue to pass unmodified.

### FR-9: No change to `MarketingCalendarSyncService.cs`
Verified this file does not reference `OutlookEventDto`, `GraphEventBody`, or `GraphEventDateTime` by name (it consumes `IOutlookCalendarSync` results via inferred `var` typing). Leave untouched.

**Acceptance criteria:**
- `git diff` shows zero changes to this file.

## Non-Functional Requirements

### NFR-1: Behavior preservation
Purely a file-move plus namespace/using-directive change. No property, method, attribute, or logic in the moved types changes. No API, persistence, serialization wire-format, or public contract changes — Graph JSON payloads deserialize identically (the `[JsonPropertyName]` attributes are untouched).

### NFR-2: Security
Not applicable — no auth, data-sensitivity, or serialization-surface impact.

## Data Model
No data model changes. `OutlookEventDto`, `GraphEventBody`, `GraphEventDateTime` keep their exact current shapes; only their namespace moves from `...Marketing.Services` to `...Marketing.Infrastructure`.

## API / Interface Design
`IOutlookCalendarSync`'s public signature is unchanged (`ListEventsAsync`, `GetEventAsync`, `CreateEventAsync`, `UpdateEventAsync`, `DeleteEventAsync`) — only the namespace of its `OutlookEventDto` return/parameter type changes, resolved via the new `using` directive in the interface file. No controller, MediatR request/response DTO (`Contracts/`), or NSwag-generated client surface is touched — these Graph-schema types never cross the HTTP boundary.

## Dependencies
None beyond the existing codebase. No new NuGet packages, no `.csproj` changes, no NSwag regeneration (these types are not part of the generated OpenAPI surface), no CI/CD or Docker changes.

## Out of Scope
- Splitting `Infrastructure/OutlookEventDto.cs` into a `Infrastructure/Graph/` subfolder — the flat `Infrastructure/` placement matches the issue's primary suggestion and keeps the change minimal; `Infrastructure/Jobs/` remains the only existing subfolder and is unrelated in concern.
- Moving `OutlookEventDto` into the `Anela.Heblo.Adapters.Microsoft365` project instead of `Infrastructure/` within the Application project — rejected because `Anela.Heblo.Adapters.Microsoft365` has a `ProjectReference` *to* `Anela.Heblo.Application` (not the reverse), and `IOutlookCalendarSync` (which must stay in the Application-layer `Services/` folder as the module-boundary contract) references `OutlookEventDto` in its signature. Moving the DTO into the adapter project would make the Application layer depend on the Adapters layer, inverting Clean Architecture's dependency direction.
- Renaming any of the three types or restructuring their properties.
- Any change to `IOutlookCalendarSync`'s method signatures or the `MarketingCalendarSyncService` business logic.
- The dead/unused-import cleanup or any other arch-review findings not covered by this issue.

## Open Questions
None.

## Status: COMPLETE
