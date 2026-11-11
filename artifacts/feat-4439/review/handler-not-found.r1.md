# Review: handler-not-found

Change matches the task context: NotFound() uses existing ErrorCodes.BlobNotFound, only FileNotFoundException is caught (consistent with the azure-404-translation task), and the unexpected-exception path still propagates. Tests cover both paths and pass (8/8). No blocking issues.

**Status:** PASS
