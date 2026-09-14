## Module
Marketing

## Finding
`backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookEventDto.cs` contains three classes:

- `OutlookEventDto` — Microsoft Graph API response shape with `[JsonPropertyName]` attributes and `DateTime.Parse` invocations in computed properties (`StartUtc`, `EndUtc`)
- `GraphEventBody` — Graph API `body` object with `[JsonPropertyName]`
- `GraphEventDateTime` — Graph API `start`/`end` object with `[JsonPropertyName]`

These are pure HTTP/JSON response schemas for the Microsoft Graph API. They carry `System.Text.Json.Serialization` annotations and contain format-parsing logic specific to the Graph API wire format. Per `docs/architecture/filesystem.md`, the `Services/` folder is for domain services and business logic; `Infrastructure/` is for feature infrastructure.

## Why it matters
The `Services/` folder currently mixes:
- Domain service interfaces (`IMarketingCalendarSyncService`, `IOutlookCalendarSync`, `IMarketingCategoryMapper`) — correct
- Their concrete implementations (`MarketingCalendarSyncService`, `MarketingCategoryMapper`) — correct
- External API response schema types (`OutlookEventDto`, `GraphEventBody`, `GraphEventDateTime`) — misplaced

A developer looking at `Services/` should find domain logic; they would not expect to find Microsoft Graph response deserialization schemas there. The file's `using System.Text.Json.Serialization;` import is a signal that this belongs closer to infrastructure. Misplacement makes it harder to distinguish application service logic from adapter code.

## Suggested fix
Move `OutlookEventDto.cs` (all three classes) to `Infrastructure/` — for example `Features/Marketing/Infrastructure/OutlookEventDto.cs` or a subfolder `Infrastructure/Graph/`. Update namespace to `Anela.Heblo.Application.Features.Marketing.Infrastructure` (or `.Infrastructure.Graph`) and fix the two existing usages:
- `Services/IOutlookCalendarSync.cs` (import)
- `UseCases/ImportFromOutlook/OutlookEventImportMapper.cs` (import)

The `IOutlookCalendarSync` interface can remain in `Services/` — the interface uses the DTO as part of its contract, so update only its using directive.

---
_Filed by daily arch-review routine on 2026-09-10._
