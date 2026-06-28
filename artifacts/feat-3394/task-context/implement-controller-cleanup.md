### task: implement-controller-cleanup

**Goal:** Remove the try/catch blocks from `ImportStatements` and `GetBankStatements` in `BankStatementsController`, retaining only the `_logger.LogInformation(...)` calls and the happy-path logic.

**Files to modify:**
- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs`

**Implementation notes:**

`ImportStatements`:
- Keep `_logger.LogInformation("Importing bank statements for account {AccountName} from {DateFrom} to {DateTo}", ...)` at the top of the method body
- Remove the `try { ... } catch (ArgumentException ...) { ... } catch (Exception ...) { ... }` wrapper entirely
- The method body becomes: log → build `importRequest` → `await _mediator.Send(importRequest)` → build `result` → return `Ok(result)`

`GetBankStatements`:
- Keep `_logger.LogInformation("Getting bank statements with Skip={Skip}, Take={Take}", skip, take)` at the top of the method body
- Remove the `try { ... } catch (FluentValidation.ValidationException ...) { ... } catch (Exception ...) { ... }` wrapper entirely
- The method body becomes: log → build `request` → `await _mediator.Send(request)` → return `Ok(response)`
- Remove the `using FluentValidation;` using if it becomes unused (verify — no explicit FluentValidation using exists at the top of the controller file, only a catch-block reference)

**Success criteria:**
- Neither method contains `try`, `catch`, `return BadRequest(...)`, or `return StatusCode(500, ...)`
- `_logger.LogInformation(...)` is still present in both methods
- `dotnet build` succeeds
- `dotnet format` reports no changes
