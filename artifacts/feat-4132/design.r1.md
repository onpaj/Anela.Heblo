# Design: Move `OutlookEventDto` (Microsoft Graph schema) from Marketing `Services/` to `Infrastructure/`

## Component Design

No components are added, removed, or behaviorally changed. This is a file relocation and namespace change to three existing types, plus mechanical `using`-directive updates at every consumer.

### `OutlookEventDto`, `GraphEventBody`, `GraphEventDateTime` (Marketing module)
- Relocate as one file: `Features/Marketing/Services/OutlookEventDto.cs` → `Features/Marketing/Infrastructure/OutlookEventDto.cs` (via `git mv`, to preserve history).
- Namespace changes from `Anela.Heblo.Application.Features.Marketing.Services` to `Anela.Heblo.Application.Features.Marketing.Infrastructure`.
- All properties, `[JsonPropertyName]` attributes, and computed members (`BodyText`, `StartUtc`, `EndUtc`) are unchanged — this is not a shape change, only a location/namespace change.

### `IOutlookCalendarSync` (Marketing module, `Services/`)
- Interface signature unchanged. Stays in `Services/` — it is the module-boundary contract, correctly placed there.
- Gains one `using Anela.Heblo.Application.Features.Marketing.Infrastructure;` directive to resolve `OutlookEventDto` in `ListEventsAsync`/`GetEventAsync`.

### `NoOpOutlookCalendarSync` (Marketing module, `Services/`)
- Implementation unchanged. Stays in `Services/`. Gains the same new `using` directive.

### `OutlookEventImportMapper` (Marketing module, `UseCases/ImportFromOutlook/`)
- Logic unchanged (`BuildAction`, `HasChanges`, `ApplyChanges`, all private parsing helpers). Gains the new `using` directive *in addition to* the existing `using Anela.Heblo.Application.Features.Marketing.Services;` (still required for `SyncActor`).

### `OutlookCalendarSyncService` (`Anela.Heblo.Adapters.Microsoft365`)
- Graph HTTP call and JSON deserialization logic unchanged. Gains the new `using` directive *in addition to* the existing `Services` using (still required for `IOutlookCalendarSync`, `IMarketingCategoryMapper`, `OutlookCalendarSyncException`).

### `OutlookInternalDtos` (`Anela.Heblo.Adapters.Microsoft365`)
- `GraphEventCollection`/`OutlookEventIdPayload` unchanged. This file's *only* reference to the `Services` namespace was `OutlookEventDto` — its `using Anela.Heblo.Application.Features.Marketing.Services;` is **replaced** (not supplemented) by `using Anela.Heblo.Application.Features.Marketing.Infrastructure;`.

### Test doubles
- `ImportFromOutlookHandlerTests.cs` and `MarketingCalendarSyncServiceTests.cs` — each gains the new `using` directive alongside its existing `Services` using. No test bodies, mocks, or assertions change.

## Data Schemas

No database schema, API request/response shape, or event payload is affected. `OutlookEventDto` et al. are in-memory Application-layer deserialization targets for the Microsoft Graph API — never serialized by NSwag, never returned from a controller action, never persisted.

Public shape after the change (namespace only differs from today):

```csharp
namespace Anela.Heblo.Application.Features.Marketing.Infrastructure
{
    public class OutlookEventDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("subject")]
        public string Subject { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public GraphEventBody? Body { get; set; }

        public string? BodyText => Body?.Content;

        [JsonPropertyName("start")]
        public GraphEventDateTime? Start { get; set; }

        [JsonPropertyName("end")]
        public GraphEventDateTime? End { get; set; }

        [JsonPropertyName("categories")]
        public string[] Categories { get; set; } = Array.Empty<string>();

        public DateTime StartUtc => /* unchanged */ ...;
        public DateTime EndUtc => /* unchanged */ ...;
    }

    public class GraphEventBody { /* unchanged */ }
    public class GraphEventDateTime { /* unchanged */ }
}
```
