# Implementation: add-email-filter-and-empty-tests

## What was implemented
Three tests for `ShoptetOrderClient.ListByExternalCodePrefixAsync`: email filter is case-insensitive and the filtered-out order's detail is never requested; pageCount 1 with no prefix match returns empty after a single list request; pageCount 0 with an empty list returns empty after a single request.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetOrderClientTests.cs` - 3 new tests

## Tests
`dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~ShoptetOrderClientTests`: 14 passed, 0 failed. `dotnet format --verify-no-changes` clean.

## Status
DONE
