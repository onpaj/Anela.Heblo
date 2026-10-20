# Code Review: update-shoptet-test-environment-hydration-tests

## Summary
The implementation repoints `ShoptetTestEnvironmentHydrationTests` to
`IShoptetOrderTestClient` for `CreateOrderAsync`, `DeleteOrderAsync`, and
`ListByExternalCodePrefixAsync`, while leaving `UpdateStatusAsync` on
`IEshopOrderClient` as specified. The diff matches the task-context's
step-by-step instructions exactly (using directive, field, constructor
DI resolution, and every call-site repoint).

## Review Result: PASS

### task: update-shoptet-test-environment-hydration-tests
**Status:** PASS

## Docs to Update
(none — internal test-only client swap, no public behavior or docs impact)

## Overall Notes
The test project (`Anela.Heblo.Adapters.Shoptet.Tests`) does not build
as a whole right now: `Integration/PickingListIntegrationTests.cs` still
calls `IEshopOrderClient.GetRecentOrdersAsync`, which was removed from
the interface by the earlier `shrink-ieshoporderclient-interface` task.
This is out of scope for this task — `PickingListIntegrationTests.cs`
has its own task-context file
(`task-context/update-picking-list-integration-tests.md`) and is the
next pending task in `state.json`. Confirmed by grepping the full build
log for `ShoptetTestEnvironmentHydrationTests`: zero matches, i.e. none
of the 6 build errors are in the file this task touched. Not treated as
a blocking issue for this task.
