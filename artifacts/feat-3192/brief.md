## Module
OrgChart

## Finding
`backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs` is a concrete `HttpClient`-based service that fetches data from a remote URL. It is compiled into the `Anela.Heblo.Application` project rather than a dedicated adapter project.

Every other I/O-bound service in this codebase lives under `backend/src/Adapters/`:
- `Anela.Heblo.Adapters.Flexi` — all Flexi API clients
- `Anela.Heblo.Adapters.Comgate` — `ComgateBankClient`
- `Anela.Heblo.Adapters.Cups` — `CupsPrintingService`
- `Anela.Heblo.Adapters.Azure` — `AzureBlobPrintQueueSink`
- `Anela.Heblo.Adapters.Anthropic` — `AnthropicChatClient`
- `Anela.Heblo.Adapters.GoogleAds` — `GoogleAdsTransactionSource`

`filesystem.md` states the rule explicitly: *"Concrete `IPrintQueueSink` implementations and any I/O-bound service live in adapter projects under `backend/src/Adapters/`, not in `Features/{Feature}/Services/`."*

`OrgChartService` is placed in `Features/OrgChart/Infrastructure/` (within the Application project), not in an adapter project. The consequence is that `Anela.Heblo.Application` carries a runtime dependency on `HttpClient` and external network I/O — a violation of the Clean Architecture rule that the Application layer must not depend on infrastructure concerns.

## Why it matters
- Breaks the dependency rule: Application layer should depend only on abstractions; the concrete `HttpClient`-based implementation belongs in the infrastructure/adapter ring.
- Inconsistent with every other I/O adapter in the repo, increasing cognitive load when navigating.
- Prevents the Application project from being tested in complete isolation (without network stubbing at the `HttpClient` level).

## Suggested fix
Create `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/` (new project, one file is sufficient):

1. Move `OrgChartService.cs` into `Anela.Heblo.Adapters.OrgChart/`.
2. Keep `IOrgChartService` in `Application/Features/OrgChart/Services/` (no change).
3. Move `services.AddHttpClient()` from `OrgChartModule.cs` into `OrgChartAdapterServiceCollectionExtensions` in the new adapter project (same pattern as `FlexiAdapterServiceCollectionExtensions`, `ComgateAdapterServiceCollectionExtensions`, etc.).
4. Wire the adapter in `Program.cs` alongside the other adapters.
5. `OrgChartOptions` and `OrgChartModule.AddOrgChartServices` can stay in Application if they only register options and MediatR handlers; otherwise move options to the adapter.

---
_Filed by daily arch-review routine on 2026-06-17._
