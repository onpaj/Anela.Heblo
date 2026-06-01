All tasks complete and the final review is **APPROVED**. Writing the output artifact.

---

# Implementation: Move Photobank thumbnail orchestration into a MediatR use case

## What was implemented

Extracted the `PhotobankController.GetThumbnail` action's ~50 lines of orchestration into a new `GetThumbnail` MediatR use case. The controller is now a single-dependency thin dispatch layer that maps `GetThumbnailResponse` outcomes to HTTP statuses. All exception handling, logging, and result shaping moved into the handler.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — added 4 Photobank thumbnail error codes (2610–2613)
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetThumbnail/GetThumbnailRequest.cs` — new: `IRequest<GetThumbnailResponse>` with `Id`, `Size`
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetThumbnail/GetThumbnailResponse.cs` — new: `BaseResponse` carrier with `Content`, `ContentType`, `ContentLength`, `RetryAfterSeconds`
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetThumbnail/GetThumbnailHandler.cs` — new: full orchestration + all logging
- `backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs` — reduced to single-dep constructor; thin `GetThumbnail` dispatch + outcome→HTTP switch
- `backend/test/Anela.Heblo.Tests/Features/Photobank/GetThumbnailHandlerTests.cs` — new: 8 HTTP-free handler unit tests
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankControllerThumbnailTests.cs` — rewritten: 7 controller tests mocking only `IMediator`

## Tests

- `GetThumbnailHandlerTests.cs` (8 tests): all handler outcomes, stream identity, `CanRead` guard (NFR-3), cancellation token threading
- `PhotobankControllerThumbnailTests.cs` (7 tests): 404 / 503+Retry-After / 503 no hint / 503 auth / 502 / 200+CacheControl / 200+ContentLength
- Full suite: 156 Photobank tests, all passing

## How to verify

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Photobank"
```

## Notes

- The arch review reversed spec Decision #2: `GetThumbnailResponse` inherits `BaseResponse`, matching the three existing binary-download use cases (`GetShipmentLabelPdf`, `DownloadExpeditionList`, `GetManufactureProtocol`).
- Stream ownership: handler does NOT dispose `GraphThumbnail` on success — stream ownership transfers to the response; `FileStreamResult` disposes it after writing.
- Final reviewer flagged two minor observations: `{ get; set; }` vs `init` on request/response properties, and the default switch arm silently mapping unknown codes to 502. Neither is blocking; left as-is to match existing project conventions.

## PR Summary

Moves all orchestration logic out of `PhotobankController.GetThumbnail` into a dedicated `GetThumbnail` MediatR use case, reducing the controller to a thin MediatR dispatch with a single `IMediator` constructor dependency. Infrastructure exception types (`HttpRequestException`, `MsalException`, `GraphThrottledException`) no longer appear in the API project.

The response shape follows the established binary-download pattern (`GetThumbnailResponse : BaseResponse` with a `Stream? Content` carrier), and the 502-vs-503 distinction from the original action is preserved via four new `ErrorCodes` members (2610–2613) mapped explicitly in the controller switch.

### Changes
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — 4 new thumbnail error codes
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetThumbnail/` — new request, response, handler files
- `backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs` — single-dep constructor, thin dispatch action
- `backend/test/Anela.Heblo.Tests/Features/Photobank/GetThumbnailHandlerTests.cs` — new 8-test HTTP-free handler suite
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankControllerThumbnailTests.cs` — rewritten 7-test controller suite

## Status
DONE