# Review: controller-status-mapping r1

Change is minimal and matches the task context: failure branch delegates to BaseApiController.HandleResponse, ErrorCodes.BlobNotFound is attributed 404 and InvalidBlobPath 400. Null-stream guard preserved. Tests cover 404, 400, success file result and null-stream case. No scope creep.

**Status:** PASS
