# Design: Document and remove the implicit MediatR registration-order dependency in ExpeditionListArchiveModule

## Component Design

Backend-only DI composition change; no UI component. Two existing components are touched, no new components are introduced.

### `ExpeditionListArchiveModule.AddExpeditionListArchiveModule`
- **Responsibility (unchanged):** bind `ExpeditionListArchiveOptions` from configuration.
- **Responsibility removed:** no longer manually registers `IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>`. `ReprintExpeditionListHandler` is registered exclusively through MediatR's normal assembly scan (`AddMediatR` in `ApplicationModule.cs`), like every other handler in the codebase.
- **Contract:** still an `IServiceCollection` extension method with the same signature `AddExpeditionListArchiveModule(this IServiceCollection services, IConfiguration configuration)`; callers (`ApplicationModule.cs`) are unaffected.

### `ReprintExpeditionListHandler`
- **Responsibility (unchanged):** given a `ReprintExpeditionListRequest.BlobPath`, download the archived PDF, spool it to a temp file, send it to the print sink that should physically re-emit it, and clean up.
- **Sink-selection responsibility (relocated, not changed in behavior):** prefer the keyed `"cups"` `IPrintQueueSink` when one is registered; otherwise fall back to the ambient (non-keyed) `IPrintQueueSink`. This selection previously lived in a factory delegate inside `ExpeditionListArchiveModule`; it now lives directly in the handler's constructor via a `[FromKeyedServices("cups")]`-annotated optional parameter plus an ordinary `IPrintQueueSink` fallback parameter, with the same `cupsSink ?? fallbackSink` expression.
- **Constructor contract (new shape):**
  ```csharp
  public ReprintExpeditionListHandler(
      IExpeditionListArchiveBlobStore blobStore,
      [FromKeyedServices("cups")] IPrintQueueSink? cupsSink,
      IPrintQueueSink fallbackSink,
      ITemporaryFileAccessor temporaryFileAccessor,
      IOptions<ExpeditionListArchiveOptions> options)
  ```
  `Handle(...)` and every other member is unchanged.

### Test components (existing patterns extended, no new pattern introduced)
- `ReprintExpeditionListHandlerTests` (direct construction, business-logic focus): constructor call site updated to the new 5-parameter signature; assertions unchanged in intent (still verifying the sink passed as the "cups" slot receives `SendAsync`).
- New registration test, following the existing `CombinedPrintQueueSinkRegistrationTests` pattern (real `ServiceCollection` + `AddPrintQueueSink` + MediatR/module registration, then `GetRequiredService`/resolve through the container): asserts that resolving `IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>` through the real DI container yields a handler wired to the keyed CUPS sink when one is registered, and to the fallback sink when it is not — independent of the order in which `AddMediatR`/`AddExpeditionListArchiveModule`/`AddPrintQueueSink` are called.

## Data Schemas

No data schema changes. No database migrations, no API request/response shape changes (`ReprintExpeditionListRequest`/`ReprintExpeditionListResponse` are untouched), no event payload changes. This change is entirely internal to backend service composition and is invisible to any client, contract, or persisted data.
