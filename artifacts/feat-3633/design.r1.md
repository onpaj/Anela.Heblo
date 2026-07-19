# Design: Inject TimeProvider into three Manufacture handlers

## Component Design
No new components. Three existing MediatR handlers each gain a constructor-injected `TimeProvider` dependency, resolved from the singleton already registered in `ServiceCollectionExtensions.cs` (`services.AddSingleton(TimeProvider.System)`), following the pattern already used by `UpdateManufactureOrderStatusHandler`:

- `GetManufactureProtocolHandler : IRequestHandler<GetManufactureProtocolRequest, GetManufactureProtocolResponse>` — adds `private readonly TimeProvider _timeProvider;` and a `TimeProvider timeProvider` constructor parameter (appended last); replaces `DateTime.UtcNow` with `_timeProvider.GetUtcNow().DateTime` when setting `GeneratedAt`.
- `ResolveManualActionHandler : IRequestHandler<ResolveManualActionRequest, ResolveManualActionResponse>` — same contract addition; replaces both `DateTime.UtcNow` call sites (`ErpDiscardResidueDocumentNumberDate`, `ManufactureOrderNote.CreatedAt`) with `_timeProvider.GetUtcNow().DateTime`.
- `GetSemiproductRecipePdfHandler : IRequestHandler<GetSemiproductRecipePdfRequest, GetSemiproductRecipePdfResponse>` — same contract addition; replaces `DateTime.Now` with `_timeProvider.GetUtcNow().DateTime` when setting `PrintedAt` (fixes local-time-vs-UTC inconsistency as a side effect).

Constructor contract for all three: `TimeProvider` is appended as the last parameter, resolved by type via MediatR's container-based handler resolution — no manual instantiation exists in production code, so no other call sites require changes. Test fixtures for all three handlers must be updated to pass a `TimeProvider` (e.g. `TimeProvider.System`, matching `UpdateManufactureOrderStatusHandlerTests.cs`'s existing convention).

## Data Schemas
None changed. No database schema, API request/response shape, or event payload is affected. Only the source of the timestamp value changes (from `DateTime.UtcNow`/`DateTime.Now` to `_timeProvider.GetUtcNow().DateTime`) for the existing fields `ManufactureProtocolData.GeneratedAt`, `ManufactureOrder.ErpDiscardResidueDocumentNumberDate`, `ManufactureOrderNote.CreatedAt`, and `SemiproductRecipeData.PrintedAt`. The one semantic change is that `SemiproductRecipeData.PrintedAt` will now hold a UTC value instead of local server time — an intended bug fix, not a shape change.
