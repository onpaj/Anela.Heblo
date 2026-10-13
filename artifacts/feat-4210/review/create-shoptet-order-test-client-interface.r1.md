# Code Review: create-shoptet-order-test-client-interface

## Summary
The task required creating a new adapter-layer interface `IShoptetOrderTestClient` with exact specified content, verifying it builds standalone, and committing it. The implementation matches the task context verbatim, the standalone build succeeded with 0 errors, and the file was committed.

## Review Result: PASS

### task: create-shoptet-order-test-client-interface
**Status:** PASS

## Docs to Update
(none — this is an internal interface addition with no public behavior change; later tasks in this plan will update `IEshopOrderClient` and its call sites)

## Overall Notes
File content matches the task-context's exact-content block byte-for-byte, including the `using Anela.Heblo.Application.Features.ShoptetOrders;` dependency direction explicitly sanctioned by the task (mirroring `ShoptetOrderClient.cs`). Standalone build of the adapter project succeeded with 0 errors (140 pre-existing warnings unrelated to this change). Commit is present on the branch with only the new file. No functional requirements were missed; this is a preparatory step for later tasks (implementing the interface on `ShoptetOrderClient`, shrinking `IEshopOrderClient`, and updating integration test call sites).
