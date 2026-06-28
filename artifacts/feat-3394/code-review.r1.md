# Code Review: feat-3394 — Remove Inline Exception Handling from BankStatementsController

## Summary
The implementation correctly moves exception-to-HTTP translation out of the controller and into global `IExceptionHandler` implementations. The pattern is well-applied. One blocking security finding: `ArgumentExceptionHandler` accepts `ArgumentNullException` subclasses whose message text reveals internal parameter names (e.g. `"Value cannot be null. (Parameter 'mediator')`).

## Review Result: CHANGES_REQUESTED

### Blocking

- **ArgumentExceptionHandler accepts ArgumentNullException** — `ArgumentNullException` is a subclass of `ArgumentException`. Its `.Message` typically contains internal parameter names (e.g. the constructor guard `throw new ArgumentNullException(nameof(mediator))` produces `"Value cannot be null. (Parameter 'mediator')"`) which must never be surfaced to API clients in the `detail` field. The handler must exclude `ArgumentNullException` so that class falls through to the default 500 handler. Fix: add `|| exception is ArgumentNullException` to the early-return guard.

### Advisory

- **Validation error property casing**: The old controller emitted `PropertyName`/`ErrorMessage` directly from `ValidationFailure` members. Whether ASP.NET Core serialized these as PascalCase or camelCase depends on the global `JsonSerializerOptions`. The new handler explicitly emits camelCase (`propertyName`/`errorMessage`) via an anonymous type with lowercase-named properties. This is consistent with the project's camelCase convention and is the correct behavior. No change needed; noted for awareness.
- **Test file stubs in diff**: The diff submission truncated the test files to comments. Actual test files exist with full `[Fact]` methods (verified: all 9 ExceptionHandling tests pass).
