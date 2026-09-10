## Module / File
`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/UpdateTransportBoxDescription/UpdateTransportBoxDescriptionHandler.cs`

## Coverage
Line coverage: 25% (filter threshold: 60%)

## What's not tested
The handler has three execution paths:

1. **Box not found** — `GetByIdWithDetailsAsync` returns null → handler returns a structured error response with `ErrorCodes.TransportBoxNotFound`
2. **Repository/mediator exception** — any exception in the try block is caught and returned as `ErrorCodes.TransportBoxStateChangeError`
3. **Success path** — box description updated, changes saved, updated box fetched and returned

Currently only a small portion of the success path is exercised; the two error-return branches are completely uncovered.

## Why it matters
If the error code constants or response construction for either error path are accidentally changed, API consumers would receive wrong error codes and be unable to distinguish "box not found" from "update failed" — a silent contract break. The exception catch also swallows errors that should surface as a specific code; without a test, any regression here is invisible.

## Suggested approach
Unit tests mocking `ITransportBoxRepository` and `IMediator`. Cover:
- Repository returns null → response has `ErrorCodes.TransportBoxNotFound`
- Repository throws an exception → response has `ErrorCodes.TransportBoxStateChangeError`
- Happy path: `UpdateAsync` and `SaveChangesAsync` called, mediator sends `GetTransportBoxByIdRequest`, response contains updated box

Estimated effort: ~1 h.

---
_Filed by weekly coverage-gap routine on 2026-08-31. Based on CI run #33077392747 (ba8f5eef168e0058dae1787bf6bb9f53fdcdf472)._
