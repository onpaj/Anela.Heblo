# Design: Move OutlookEventImportMapper Out of UseCases into Services

## Component Design

**`OutlookEventImportMapper`** (relocated, no behavioral change)
- New location: `backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookEventImportMapper.cs`
- Removed from: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/ImportFromOutlook/OutlookEventImportMapper.cs`
- Namespace: `Anela.Heblo.Application.Features.Marketing.Services` (was `...Marketing.UseCases.ImportFromOutlook`)
- Visibility/type: unchanged — `internal static` class
- Public interface (unchanged signatures):
  - `HasChanges(...)`
  - `ApplyChanges(...)`
  - `BuildAction(...)`
- Responsibility: unchanged — pure mapping/diffing logic for Outlook calendar event import, now co-located with its sole consumer's layer (`Services/`), consistent with sibling `MarketingCategoryMapper.cs` already residing in `Services/`.

**`MarketingCalendarSyncService`** (consumer, edited only for the `using` directive)
- Location unchanged: `Features/Marketing/Services/MarketingCalendarSyncService.cs`
- Change: remove `using Anela.Heblo.Application.Features.Marketing.UseCases.ImportFromOutlook;` — call sites at `HasChanges` (line 131), `ApplyChanges` (line 150), `BuildAction` (line 166) require no edits, resolved via same-namespace membership once the mapper moves into `Services`.

**`ImportFromOutlookHandler`** and all other consumers
- No changes. Not affected by this move (per spec FR-4).

This eliminates the inverted `Services → UseCases` dependency by making the mapper a peer of its only consumer within `Services`.

## Data Schemas

No data schema, database, API request/response, MediatR contract, or event payload changes. The mapper is `internal static`, never exposed via HTTP endpoint or OpenAPI client, and no DTOs are involved in this relocation.

---
No UX/UI sections included — this is a backend-only structural refactor with no user-facing component (confirmed by spec's "Out of Scope: no UI changes" and arch-review's `Skip Design: true`).
