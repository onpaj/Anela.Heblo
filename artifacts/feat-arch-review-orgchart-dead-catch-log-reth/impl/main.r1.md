Implementation complete. Here's a summary of what was done:

**Task 1 — Tests (RED phase, commit `16454b0c`):**
Created `backend/test/Anela.Heblo.Tests/Features/OrgChart/GetOrganizationStructureHandlerTests.cs` with two xUnit/Moq/FluentAssertions tests. The exception-propagation test failed as expected (the current handler was calling `LogError` before rethrowing), confirming the RED state.

**Task 2 — Refactor (GREEN phase, commit `048d3ecd`):**
Removed the `try { var result = ...; return result; } catch (Exception ex) { _logger.LogError(...); throw; }` block from `GetOrganizationStructureHandler.Handle`. The method is now a two-liner: `LogInformation` + `return await _orgChartService.GetOrganizationStructureAsync(cancellationToken)`.

Both new tests now pass, and all 3623 existing tests continue to pass. Each org-chart failure now produces exactly one ERROR log entry (from the controller) instead of two.