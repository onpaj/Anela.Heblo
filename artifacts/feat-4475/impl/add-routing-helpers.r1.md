# Implementation: add-routing-helpers

## What was implemented
Added private helpers `BuildOrderRoutingClient`, `ListPage` and `Detail` to `ShoptetOrderClientTests`. The routing client records every requested `PathAndQuery`, serves list pages by `page` query value, serves order details by code, and returns 404 otherwise.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetOrderClientTests.cs` - three new private static helpers

## Tests
No new test cases in this task (helpers only); consumed by the next two tasks.

## How to verify
`dotnet build backend/test/Anela.Heblo.Tests` (0 errors) and `dotnet format` reports no changes.

## Notes
Helpers are unused until the following tasks.

## PR Summary
Adds routing helpers for the upcoming `ListByExternalCodePrefixAsync` unit tests.

### Changes
- `ShoptetOrderClientTests.cs` - `BuildOrderRoutingClient`, `ListPage`, `Detail` helpers

## Status
DONE
