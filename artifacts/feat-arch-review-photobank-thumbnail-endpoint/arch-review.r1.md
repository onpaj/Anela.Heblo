I explored the codebase and wrote the architecture review to `artifacts/feat-arch-review-photobank-thumbnail-endpoint/arch-review.md`.

# Architecture Review: Move Photobank thumbnail orchestration into a MediatR use case

## Skip Design: true
Backend-only refactor — no UI work.

## The headline finding

The refactor is a clean fit with the project's "no business logic in controllers" rule and the Vertical Slice `UseCases/{Request,Response,Handler}` convention. Every integration point in the brief/spec checks out against the actual code (`GetLocatorAsync`, `GetThumbnailAsync`, `GraphThumbnail : IDisposable`, `GraphThrottledException.RetryAfter`), and MSAL is already a transitive reference of the Application project, so moving the `catch (MsalException)` down a layer needs no new package.

**One material correction to the spec.** Spec Decision #2 says the response should *not* inherit `BaseResponse` because the success path streams binary. I verified that's wrong against reality: the codebase has **three** binary-download MediatR use cases — `GetShipmentLabelPdf`, `DownloadExpeditionList`, `GetManufactureProtocol` — and **all three inherit `BaseResponse`** and carry a `Stream?`/`byte[]`. Inheriting `BaseResponse` does not force a JSON success body; the controller still returns `File(...)`. So my guidance is to follow the established sibling pattern: `GetThumbnailResponse : BaseResponse` with `Stream? Content`, metadata, `int? RetryAfterSeconds`, and discriminate failures via `ErrorCodes` rather than a bespoke `GetThumbnailOutcome` enum. The spec's *intent* — preserving the 502-vs-503-vs-404 distinction — is correct and kept.

Two supporting decisions, both grounded in code I read:
- **Don't route through `HandleResponse`** — its `HttpStatusCodeAttribute` enum has no 502 or 503, and it wraps success in `Ok()`. `ShipmentLabelsController` bypasses it for exactly this reason; the thumbnail action maps `ErrorCode`→status explicitly.
- **Headers stay in the controller, handler returns data** — including pre-rounded `RetryAfterSeconds` (`Math.Ceiling`) so the existing `29.3s → "30"` test holds, and `Cache-Control` set on the success branch.

The review also flags the key risk (disposing `GraphThumbnail` early closes the stream before `FileStreamResult` writes), confirms no OpenAPI/TS client drift since the action signature is unchanged, and specifies the FR-5 test rework (existing controller test must drop to a single `IMediator` mock; add an HTTP-free `GetThumbnailHandlerTests`).

The only prerequisite is adding four `ErrorCodes` members — no migration, config, DI, or package changes.