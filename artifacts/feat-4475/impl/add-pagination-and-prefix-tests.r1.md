# Implementation: add-pagination-and-prefix-tests

## What was implemented
Three tests for `ShoptetOrderClient.ListByExternalCodePrefixAsync` using the routing helpers: single page returns only prefix matches (one list request), multi-page finds a match on page 2 and never requests page 3, prefix match is case sensitive and null ExternalCode is ignored.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetOrderClientTests.cs` - 3 new tests

## Tests
`dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~ShoptetOrderClientTests`: 11 passed, 0 failed.

## Status
DONE
