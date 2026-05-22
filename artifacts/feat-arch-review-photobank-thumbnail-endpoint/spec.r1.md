I've written the complete specification to `artifacts/feat-arch-review-photobank-thumbnail-endpoint/spec.md`.

# Specification: Move Photobank thumbnail orchestration into a MediatR use case

## Summary
The `PhotobankController.GetThumbnail` action performs ~50 lines of application-layer orchestration directly inside the HTTP layer. This refactor extracts that into a new `GetThumbnail` MediatR use case, reduces the controller to a thin dispatch, and removes the two non-`IMediator` constructor dependencies — while preserving the endpoint's exact observable HTTP behaviour.

## Functional Requirements
- **FR-1** — Introduce a `GetThumbnail` use case (`Request`/`Response`/`Handler`) under the standard folder convention; the handler injects only the repository, graph service, and its own logger, and absorbs all current logging.
- **FR-2** — Reduce the controller action to `_mediator.Send` + outcome→status mapping; route and `[ProducesResponseType]` attributes unchanged.
- **FR-3** — Return `PhotobankController` to a single-dependency (`IMediator`) constructor; drop now-unused usings.
- **FR-4** — Preserve exact HTTP behaviour via an outcome table (404 / 503+Retry-After / 503 auth / 502 / 200).
- **FR-5** — Rework controller tests to mock `IMediator` only; add HTTP-free handler unit tests.

## Key decisions I made (and flagged in the spec)
1. **Preserved the 502-vs-503 distinction.** The brief's sketch collapsed both into a single `unavailable` flag, which would lose the difference between `HttpRequestException` (502) and `MsalException`/throttling (503). I modeled the response with a `GetThumbnailOutcome` discriminator enum instead.
2. **Response does not inherit `BaseResponse`/`HandleResponse`** — the success path streams raw binary, not a JSON envelope.
3. **Stream lifecycle (NFR-3)** — `GraphThumbnail` is `IDisposable` and owns the `Stream`; the handler must transfer ownership to the response and not dispose it early, since `FileStreamResult` disposes it after writing.

There were no blockers requiring user clarification, so **Open Questions is empty** and the status line is `COMPLETE` — the downstream dispatcher can proceed without the product agent.