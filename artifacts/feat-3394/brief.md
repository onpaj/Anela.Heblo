## Module
Bank

## Finding
`BankStatementsController` in `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs` contains try/catch blocks in two of its three action methods:

- `ImportStatements` (lines 44–69): catches `ArgumentException` → `BadRequest(new { message })`, catches `Exception` → `StatusCode(500, new { message })`.
- `GetBankStatements` (lines 102–134): catches `FluentValidation.ValidationException` → `BadRequest(new { message, errors })`, catches `Exception` → `StatusCode(500, new { message })`.

The third action, `GetBankStatement` (lines 143–150), has no try/catch at all — an inconsistency within the same controller.

The project rule is "Business logic must be in MediatR handlers, NOT in controllers". Mapping exceptions to HTTP response shapes is exception-to-HTTP translation that controllers should not own. It causes two concrete problems here:
1. The `ArgumentException` for an unknown account name is a domain signal; catching it in the controller means the response shape is controller-specific rather than consistent across the API.
2. The `FluentValidation.ValidationException` catch in `GetBankStatements` duplicates logic that a shared problem-details filter would handle uniformly for every endpoint.

## Why it matters
- Inconsistency: one action handles errors; another doesn't. A future developer adding error handling to `GetBankStatement` will pick a different style.
- Fragility: the anonymous `{ message }` and `{ message, errors }` response shapes are invisible to the OpenAPI spec and the generated TypeScript client.
- Duplication: every controller that catches exceptions inline re-implements the same mapping — already seen across multiple modules.

## Suggested fix
Remove the try/catch blocks from the controller actions. Register a global exception handler (e.g. `app.UseExceptionHandler` with `IProblemDetailsService`, or an `IExceptionFilter`) that maps `ArgumentException` → 400 and unhandled exceptions → 500 using `ProblemDetails`. The `FluentValidation.ValidationException` path is already handled by the `ValidationBehavior` MediatR pipeline — the catch in the controller is redundant if a global handler exists.

---
_Filed by daily arch-review routine on 2026-06-27._
