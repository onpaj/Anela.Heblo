# Implementation: handler-propagate-exceptions

## What was implemented
Removed the catch-all try/catch in GetInvoiceDetailsHandler.Handle so infrastructure and mapping errors propagate to the global exception handler (HTTP 500) instead of being reported as Found=false (404). Null from the client still yields Invoice=null, Found=false.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetInvoiceDetails/GetInvoiceDetailsHandler.cs` -- try/catch removed
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/GetInvoiceDetailsHandlerTests.cs` -- two propagation tests added

## Tests
Handle_WhenClientThrows_PropagatesException, Handle_WhenMapperThrows_PropagatesException. All 4 handler tests pass.

## How to verify
cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~GetInvoiceDetailsHandlerTests"

## Notes
Red-phase run was skipped (build time); the old catch block trivially swallowed both exceptions. dotnet format whitespace check clean.

## PR Summary
GetInvoiceDetailsHandler no longer swallows exceptions; only a genuinely missing invoice returns Found=false.

### Changes
- GetInvoiceDetailsHandler.cs -- removed catch-all
- GetInvoiceDetailsHandlerTests.cs -- two propagation tests

## Status
DONE
