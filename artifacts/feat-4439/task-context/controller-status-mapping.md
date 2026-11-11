### task: controller-status-mapping
Files: `src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs`, tests under `test/Anela.Heblo.Tests/Controllers` or `ExpeditionListArchive` (inspect for existing controller tests of this controller first).
- [ ] Change the failure branch of `Download` from `return BadRequest(response);` to `return HandleResponse(response).Result!;` (404 for BlobNotFound, 400 for InvalidBlobPath, via the ErrorCode HTTP attribute). Keep the `response.Stream == null` guard consistent: if `Success` true but stream null, keep `BadRequest(response)`.
- [ ] Add controller tests (mock `IMediator`): BlobNotFound -> `NotFoundObjectResult`; InvalidBlobPath -> `BadRequestObjectResult`; success -> `FileStreamResult`. Mirror setup used by existing controller tests (e.g. `PurchaseOrdersControllerErrorTests`).
- [ ] Check frontend usage of the download URL (`grep -rn "expedition-list-archive/download" frontend/src`) to confirm a 404 does not break handling; no FE change expected.
- [ ] Run tests; commit.

