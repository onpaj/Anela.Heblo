# Architecture Review: Remove Inline Exception Handling from BankStatementsController

## Skip Design: true

## Architectural Fit Assessment

This change is a pure refactoring within an already-established pattern. The codebase already has `IExceptionHandler`, `AddExceptionHandler<T>()`, `AddProblemDetails()`, and `app.UseExceptionHandler()` fully wired. The existing `UnauthorizedAccessExceptionHandler` provides a direct template for both new handlers. No new concepts are introduced; this is mechanical extraction.

The spec is correct and complete. The two exception types (`ArgumentException`, `FluentValidation.ValidationException`) are genuinely cross-cutting concerns and do not belong in the controller.

## Proposed Architecture

### Component Overview

```
Infrastructure/ExceptionHandling/
├── UnauthorizedAccessExceptionHandler.cs   (existing)
├── ArgumentExceptionHandler.cs             (new)
└── ValidationExceptionHandler.cs           (new)
```

Both new handlers follow the identical shape of `UnauthorizedAccessExceptionHandler`: implement `IExceptionHandler`, inject `ILogger<T>`, write a ProblemDetails response via `httpContext.Response.WriteAsJsonAsync(...)`, return `true`.

### Key Design Decisions

**Registration order matters.** ASP.NET Core tries handlers in registration order and stops at the first that returns `true`. `ArgumentException` and `ValidationException` must come after `UnauthorizedAccessExceptionHandler`. None of these exception types share an inheritance relationship, so their relative order is immaterial, but `ValidationExceptionHandler` is registered before `ArgumentExceptionHandler` for clarity.

**ValidationException error shape.** Populated from `ValidationException.Errors` (`IEnumerable<ValidationFailure>`). Map to an anonymous type — records are acceptable for internal domain use since this is not a DTO crossing the OpenAPI boundary.

**ArgumentExceptionHandler scope.** `ArgumentException` is a broad base type — `ArgumentNullException`, `ArgumentOutOfRangeException` etc. all derive from it. This is intentional and appropriate for the current use case. Subclass handlers can be inserted ahead of `ArgumentExceptionHandler` in the chain if tighter scoping is ever needed.

**Logging level.** Both handlers log at Warning. This aligns with `UnauthorizedAccessExceptionHandler` — client-induced errors warrant Warning, not Error, since they reflect bad input rather than system faults.

**No status code override on ValidationException.** Map to 400 (not 422), consistent with existing project convention.

## Implementation Guidance

### Directory / Module Structure

New files:
- `backend/src/Anela.Heblo.API/Infrastructure/ExceptionHandling/ArgumentExceptionHandler.cs`
- `backend/src/Anela.Heblo.API/Infrastructure/ExceptionHandling/ValidationExceptionHandler.cs`
- `backend/test/Anela.Heblo.Tests/Infrastructure/ExceptionHandling/ArgumentExceptionHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Infrastructure/ExceptionHandling/ValidationExceptionHandlerTests.cs`

Modified files:
- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs`
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`

### Interfaces and Contracts

Both handlers implement `Microsoft.AspNetCore.Diagnostics.IExceptionHandler` (same interface as `UnauthorizedAccessExceptionHandler`).

`ArgumentExceptionHandler` response shape:
```json
{
  "status": 400,
  "title": "Bad Request",
  "detail": "<exception.Message>",
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1"
}
```

`ValidationExceptionHandler` response shape:
```json
{
  "status": 400,
  "title": "Validation Failed",
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "extensions": {
    "errors": [
      { "propertyName": "Amount", "errorMessage": "Must be greater than zero." }
    ]
  }
}
```

Use `Microsoft.AspNetCore.Mvc.ProblemDetails` to construct responses.

### Data Flow

```
Request → Controller (dispatches MediatR, no try/catch)
              ↓ exception thrown
         UseExceptionHandler middleware
              ↓
         UnauthorizedAccessExceptionHandler  → handled if UnauthorizedAccessException
              ↓ not handled
         ValidationExceptionHandler          → handled if ValidationException
              ↓ not handled
         ArgumentExceptionHandler            → handled if ArgumentException
              ↓ not handled
         ASP.NET Core default problem details response (500)
```

### Test Pattern

Follow `UnauthorizedAccessExceptionHandlerTests.cs` exactly:
- Arrange: `DefaultHttpContext` with `MemoryStream` on `Response.Body`
- Act: call `TryHandleAsync(...)` with a constructed exception instance
- Assert: `result == true`, deserialize body JSON, verify status code and field values

For `ValidationExceptionHandler` tests, construct `ValidationException` with a list of `ValidationFailure` objects and assert the `errors` array in the response body. Also test the empty-errors case.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| `ArgumentException` is too broad — catches exceptions that should propagate | Low | Current usage in `ImportStatements` is intentional. Register a more specific handler before `ArgumentExceptionHandler` if needed. |
| Registration order silently broken by future insertions | Low | Add a comment in `ServiceCollectionExtensions.cs` noting that handler order is significant. |
| `ValidationException.Errors` is null or empty in edge cases | Low | Guard with null-coalescing to empty array; test the empty-errors case explicitly. |

## Specification Amendments

None. The spec is self-consistent and complete. No additions or changes are needed.

## Prerequisites

None. All infrastructure (`IExceptionHandler`, `AddProblemDetails`, `UseExceptionHandler`) is already in place. Implementation can begin immediately.
