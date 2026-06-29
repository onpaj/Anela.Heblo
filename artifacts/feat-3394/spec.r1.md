# Specification: Remove Inline Exception Handling from BankStatementsController

## Summary

Two of the three action methods in `BankStatementsController` contain inline try/catch blocks that perform exception-to-HTTP translation, duplicating logic that belongs in the global exception-handling infrastructure. This specification covers adding two new `IExceptionHandler` implementations — one for `ArgumentException` and one for `FluentValidation.ValidationException` — and removing the redundant try/catch blocks from the controller, leaving the controller responsible only for dispatching MediatR requests and returning their results.

## Background

The project rule is that business logic must live in MediatR handlers, not in controllers. Exception-to-HTTP mapping is a cross-cutting infrastructure concern and is already partially solved: `UnauthorizedAccessExceptionHandler` demonstrates the established pattern, `app.UseExceptionHandler()` is already wired into the pipeline, and `services.AddProblemDetails()` is already called.

The current state in `BankStatementsController` has three concrete problems:

1. `ImportStatements` (lines 44–69) catches `ArgumentException` and returns `BadRequest(new { message })` — an anonymous shape invisible to OpenAPI and the generated TypeScript client.
2. `GetBankStatements` (lines 102–134) catches `FluentValidation.ValidationException` and returns `BadRequest(new { message, errors })` — also an anonymous shape, and redundant because `ValidationBehavior` in the MediatR pipeline already throws this exception; a global handler already handles it correctly everywhere else.
3. `GetBankStatement` (lines 143–150) has no try/catch at all, creating an inconsistency within the same controller.

## Functional Requirements

### FR-1: ArgumentExceptionHandler — global mapping of ArgumentException to 400 ProblemDetails

Add a new `IExceptionHandler` class `ArgumentExceptionHandler` in `backend/src/Anela.Heblo.API/Infrastructure/ExceptionHandling/` that:

- Matches `ArgumentException` (and subclasses, e.g. `ArgumentNullException`, `ArgumentOutOfRangeException`).
- Sets the HTTP status code to 400 Bad Request.
- Writes a `ProblemDetails` response body with:
  - `Status`: 400
  - `Title`: `"Bad Request"`
  - `Detail`: the exception's `Message` property
  - `Type`: `"https://tools.ietf.org/html/rfc7231#section-6.5.1"`
- Logs the exception at `Warning` level using the injected `ILogger<ArgumentExceptionHandler>`.
- Returns `true` to signal that the exception has been handled.

**Acceptance criteria:**
- A request to `POST /api/bank-statements/import` with an unknown account name results in HTTP 400 with `Content-Type: application/problem+json` and `status: 400` in the body.
- The server log contains a `Warning`-level entry with the exception details.

### FR-2: ValidationExceptionHandler — global mapping of FluentValidation.ValidationException to 400 ProblemDetails

Add a new `IExceptionHandler` class `ValidationExceptionHandler` in `backend/src/Anela.Heblo.API/Infrastructure/ExceptionHandling/` that:

- Matches `FluentValidation.ValidationException` exactly.
- Sets the HTTP status code to 400 Bad Request.
- Writes a `ProblemDetails` response body with:
  - `Status`: 400
  - `Title`: `"Validation Failed"`
  - `Type`: `"https://tools.ietf.org/html/rfc7231#section-6.5.1"`
  - An `Extensions` entry keyed `"errors"` containing a list of objects with `propertyName` and `errorMessage` fields derived from `exception.Errors`.
- Logs the exception at `Warning` level using the injected `ILogger<ValidationExceptionHandler>`.
- Returns `true` to signal that the exception has been handled.

**Acceptance criteria:**
- A request to `GET /api/bank-statements` with invalid query parameters results in HTTP 400 with `Content-Type: application/problem+json` and an `errors` extension array.

### FR-3: Register the new handlers in the correct order

In `ServiceCollectionExtensions.AddCrossCuttingServices()`, register after existing `UnauthorizedAccessExceptionHandler`:

```
services.AddExceptionHandler<UnauthorizedAccessExceptionHandler>();  // existing — 401
services.AddExceptionHandler<ValidationExceptionHandler>();           // new — FluentValidation 400
services.AddExceptionHandler<ArgumentExceptionHandler>();             // new — ArgumentException 400
services.AddProblemDetails();                                         // existing
```

**Acceptance criteria:**
- `dotnet build` succeeds without errors.

### FR-4: Remove try/catch blocks from BankStatementsController

In `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs`:

**`ImportStatements`:** Remove the outer `try/catch (ArgumentException)/catch (Exception)` wrapper. Retain the `_logger.LogInformation(…)` call. Method becomes a straight-line dispatch.

**`GetBankStatements`:** Remove the outer `try/catch (FluentValidation.ValidationException)/catch (Exception)` wrapper. Retain the `_logger.LogInformation(…)` call.

**`GetBankStatement`:** No change required.

The `_logger` field remains and is used for the retained `LogInformation` calls.

**Acceptance criteria:**
- `BankStatementsController` contains no `catch` blocks.
- `dotnet build` succeeds.
- `dotnet format` produces no diff.

## Non-Functional Requirements

### NFR-1: Consistency
After this change, all three action methods handle errors identically: by not catching them at the controller level.

### NFR-2: Logging parity
- `ArgumentExceptionHandler`: `Warning` with exception (preserves removed `LogWarning` in `ImportStatements`).
- `ValidationExceptionHandler`: `Warning` with exception (preserves removed `LogWarning` in `GetBankStatements`).
- Generic catch (Exception) → 500: ASP.NET's built-in fallback inside `UseExceptionHandler()` already logs at `Error` level.

### NFR-3: No new NuGet packages
All required types (`IExceptionHandler`, `ProblemDetails`, `ValidationException`) are already project dependencies.

## Data Model

No database schema changes.

ProblemDetails shape used by both handlers (RFC 7807):
```
{
  type:     string,
  title:    string,
  status:   int,
  detail:   string?,    // ArgumentExceptionHandler: exception.Message; ValidationExceptionHandler: omitted
  extensions: {
    errors: [{ propertyName: string, errorMessage: string }]  // ValidationExceptionHandler only
  }
}
```

## API / Interface Design

No new endpoints. Error response shape changes for existing endpoints:

| Endpoint | Before | After |
|---|---|---|
| `POST /api/bank-statements/import` (ArgumentException) | `{ "message": "..." }` | ProblemDetails 400 with `detail` |
| `GET /api/bank-statements` (ValidationException) | `{ "message": "Invalid request", "errors": [...] }` | ProblemDetails 400 with `extensions.errors` |

New files:
- `backend/src/Anela.Heblo.API/Infrastructure/ExceptionHandling/ArgumentExceptionHandler.cs`
- `backend/src/Anela.Heblo.API/Infrastructure/ExceptionHandling/ValidationExceptionHandler.cs`

Modified files:
- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs`
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`

Test files to add:
- `backend/test/Anela.Heblo.Tests/Infrastructure/ExceptionHandling/ArgumentExceptionHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Infrastructure/ExceptionHandling/ValidationExceptionHandlerTests.cs`

## Dependencies

- `Microsoft.AspNetCore.Diagnostics` — `IExceptionHandler` (already used)
- `Microsoft.AspNetCore.Mvc` — `ProblemDetails` (already registered)
- `FluentValidation` — `ValidationException` (already a project dependency)
- No new NuGet packages required.

## Out of Scope

- Removing try/catch blocks from other controllers (future follow-up).
- Adding `[ProducesResponseType]` attributes to action methods.
- Replacing `ArgumentException` in `ImportBankStatementHandler` with a domain exception type.
- Frontend error-handling changes beyond ensuring `npm run build` succeeds.

## Open Questions

None. Assumptions made:
1. `[ProducesResponseType]` decoration is out of scope per brief.
2. `ArgumentNullException` from DI constructor guards fires at DI resolution time, before the request pipeline; it will never reach `UseExceptionHandler`. The handler correctly catches argument exceptions from MediatR handlers only.
3. Frontend `useBankStatements.ts` uses `as any` casts and manual DTO types (issue #3395); the error shape change is acceptable. The frontend currently does not strongly type error responses from these endpoints.

## Status: COMPLETE
