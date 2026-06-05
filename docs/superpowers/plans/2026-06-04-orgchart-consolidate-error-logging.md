# OrgChart Service — Consolidate Error Logging to Single Site

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove every `LogError` call from `OrgChartService.GetOrganizationStructureAsync` so the controller becomes the single error-logging site for the OrgChart slice, and lock the new contract behind regression tests.

**Architecture:** Backend-only refactor inside the `OrgChart` vertical slice. `OrgChartService` continues to catch the same exceptions and wrap/throw the same types; it just stops emitting `LogError`. `OrgChartController.GetOrganizationStructure` (unchanged) keeps its `catch (Exception ex) { _logger.LogError(ex, ...); ... }` block, so the exception chain (including the data-source URL inside `HttpRequestException`/`JsonException`) is still observable in Application Insights via `ex.ToString()`.

**Tech Stack:** .NET 8, xUnit, FluentAssertions, Moq, `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Options`, `System.Text.Json`, `HttpClient`/`HttpMessageHandler` test double pattern (already used elsewhere in the suite, e.g. `ComgateBankClientTests`).

---

## File Structure

**Modify**
- `backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs`
  - Remove `_logger.LogError(...)` at line 49 (null-deserialization guard inside the `try`).
  - Remove `_logger.LogError(...)` at line 62 (`HttpRequestException` catch).
  - Remove `_logger.LogError(...)` at line 67 (`JsonException` catch).
  - Remove `_logger.LogError(...)` at line 72 (generic `Exception` catch).
  - Keep all `throw`/`throw new InvalidOperationException(...)` statements with their messages and inner-exception parameters unchanged.
  - Keep the two `LogInformation` calls (start-of-fetch, success summary) unchanged.

- `backend/test/Anela.Heblo.Tests/Features/OrgChart/GetOrganizationStructureHandlerTests.cs`
  - Update the inline comment on the `Times.Never` assertion (lines 66) to reflect that the rule is no longer paired with a contradictory service-side log.

**Create**
- `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartServiceTests.cs`
  - Four xUnit tests covering each failure path: `HttpRequestException`, `JsonException`, null-deserialization (200 with body `"null"`), and a generic non-typed `Exception`.
  - A small private `HttpMessageHandler` test double that either returns a configured response or throws a configured exception.
  - Each test asserts the expected thrown type **and** asserts `Mock<ILogger<OrgChartService>>` received zero `LogLevel.Error` calls.

**No changes**
- `backend/src/Anela.Heblo.API/Controllers/OrgChartController.cs` — already the single error-logging site (line 49 `_logger.LogError(ex, "Error fetching organizational structure")`).
- `IOrgChartService`, `OrgChartResponse`, `OrgChartOptions`, `OrgChartModule.cs`, route, status codes, response envelope.
- No DB migration, no new NuGet packages, no Azure Key Vault changes.

---

## Task 1: Add the failing regression tests for `OrgChartService` (RED)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartServiceTests.cs`

These tests must exist **before** the production change so the change has a verifiable safety net. Each test currently fails for the same reason: the service emits `LogError` on the failure path that the test exercises.

- [ ] **Step 1: Write the new test file with four failing tests + local handler double**

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Application.Features.OrgChart;
using Anela.Heblo.Application.Features.OrgChart.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.OrgChart;

public class OrgChartServiceTests
{
    private const string TestDataSourceUrl = "https://example.test/orgchart.json";

    private readonly Mock<ILogger<OrgChartService>> _loggerMock = new();
    private readonly IOptions<OrgChartOptions> _options =
        Options.Create(new OrgChartOptions { DataSourceUrl = TestDataSourceUrl });

    [Fact]
    public async Task GetOrganizationStructureAsync_WrapsHttpRequestException_AndDoesNotLogError()
    {
        // Arrange
        var inner = new HttpRequestException("network is unreachable");
        var service = CreateService(StubHttpMessageHandler.ThrowsOnSend(inner));

        // Act
        var act = async () => await service.GetOrganizationStructureAsync(CancellationToken.None);

        // Assert: wrap preserved
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().StartWith("Failed to fetch organizational structure: ");
        thrown.Which.InnerException.Should().BeSameAs(inner);

        // Assert: service must not log Error (controller is the single owner)
        VerifyNoErrorLog();
    }

    [Fact]
    public async Task GetOrganizationStructureAsync_WrapsJsonException_AndDoesNotLogError()
    {
        // Arrange: 200 OK with a body that is not valid JSON
        var service = CreateService(StubHttpMessageHandler.Returns(HttpStatusCode.OK, "{ this is not json"));

        // Act
        var act = async () => await service.GetOrganizationStructureAsync(CancellationToken.None);

        // Assert: wrap preserved
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().StartWith("Failed to parse organizational structure: ");
        thrown.Which.InnerException.Should().BeOfType<JsonException>();

        // Assert: service must not log Error (controller is the single owner)
        VerifyNoErrorLog();
    }

    [Fact]
    public async Task GetOrganizationStructureAsync_RethrowsGenericException_AndDoesNotLogError()
    {
        // Arrange: handler throws a non-Http, non-Json exception so it lands in the generic catch
        var inner = new InvalidProgramException("unexpected transport-layer failure");
        var service = CreateService(StubHttpMessageHandler.ThrowsOnSend(inner));

        // Act
        var act = async () => await service.GetOrganizationStructureAsync(CancellationToken.None);

        // Assert: generic exception is re-thrown unwrapped (same instance, same type)
        var thrown = await act.Should().ThrowAsync<InvalidProgramException>();
        thrown.Which.Should().BeSameAs(inner);

        // Assert: service must not log Error (controller is the single owner)
        VerifyNoErrorLog();
    }

    [Fact]
    public async Task GetOrganizationStructureAsync_ThrowsOnNullDeserialization_AndDoesNotLogError()
    {
        // Arrange: 200 OK with a body of literal "null" — System.Text.Json returns null,
        // which triggers the in-method null guard (not a catch block).
        var service = CreateService(StubHttpMessageHandler.Returns(HttpStatusCode.OK, "null"));

        // Act
        var act = async () => await service.GetOrganizationStructureAsync(CancellationToken.None);

        // Assert: typed wrap preserved
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Be("Failed to deserialize organizational structure");
        thrown.Which.InnerException.Should().BeNull();

        // Assert: service must not log Error (controller is the single owner)
        VerifyNoErrorLog();
    }

    private OrgChartService CreateService(HttpMessageHandler handler) =>
        new(new HttpClient(handler), _options, _loggerMock.Object);

    private void VerifyNoErrorLog() =>
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _factory;

        private StubHttpMessageHandler(Func<HttpResponseMessage> factory) => _factory = factory;

        public static StubHttpMessageHandler Returns(HttpStatusCode status, string body) =>
            new(() => new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });

        public static StubHttpMessageHandler ThrowsOnSend(Exception toThrow) =>
            new(() => throw toThrow);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_factory());
    }
}
```

- [ ] **Step 2: Run the four new tests and verify they FAIL**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~OrgChartServiceTests" \
  --nologo --verbosity minimal
```

Expected: All four tests **fail** because `OrgChartService` currently emits `LogError` on every failure path. Each failure message will reference the `Times.Never` Moq verification.

If any test fails for a different reason (e.g. the `JsonException` test does not actually trigger a `JsonException` because of how `System.Text.Json` parses the chosen malformed body, or `InvalidProgramException` is wrapped by `HttpClient`), fix the arrange step first — the test must demonstrate the failure-mode the spec talks about, not a different one.

- [ ] **Step 3: Commit the failing tests**

```bash
git add backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartServiceTests.cs
git commit -m "test: add OrgChartService regression tests for single-owner error logging"
```

---

## Task 2: Remove the four `LogError` calls from `OrgChartService` (GREEN)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs`

- [ ] **Step 1: Delete the null-deserialization `LogError` (line 49)**

Replace
```csharp
            if (orgChart == null)
            {
                _logger.LogError("Failed to deserialize organizational structure from {Url}", _options.DataSourceUrl);
                throw new InvalidOperationException("Failed to deserialize organizational structure");
            }
```
with
```csharp
            if (orgChart == null)
            {
                throw new InvalidOperationException("Failed to deserialize organizational structure");
            }
```

- [ ] **Step 2: Delete the `HttpRequestException` catch `LogError` (line 62)**

Replace
```csharp
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error while fetching organizational structure from {Url}", _options.DataSourceUrl);
            throw new InvalidOperationException($"Failed to fetch organizational structure: {ex.Message}", ex);
        }
```
with
```csharp
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to fetch organizational structure: {ex.Message}", ex);
        }
```

- [ ] **Step 3: Delete the `JsonException` catch `LogError` (line 67)**

Replace
```csharp
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error for organizational structure from {Url}", _options.DataSourceUrl);
            throw new InvalidOperationException($"Failed to parse organizational structure: {ex.Message}", ex);
        }
```
with
```csharp
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse organizational structure: {ex.Message}", ex);
        }
```

- [ ] **Step 4: Delete the generic `Exception` catch `LogError` (line 72) and prune the now-unused exception variable**

Replace
```csharp
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching organizational structure from {Url}", _options.DataSourceUrl);
            throw;
        }
```
with
```csharp
        catch (Exception)
        {
            throw;
        }
```

Note: the bare `throw;` preserves the original exception with its stack trace. We drop the `ex` binding because nothing else inside the catch references it; this avoids an unused-variable analyzer warning.

- [ ] **Step 5: Run the four regression tests and verify they PASS**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~OrgChartServiceTests" \
  --nologo --verbosity minimal
```

Expected: All four tests **pass**. The service now throws the documented exceptions without invoking `LogError`.

- [ ] **Step 6: Format and build the backend**

Run:
```bash
dotnet format backend/Anela.Heblo.sln \
  --include backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs \
           backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartServiceTests.cs
dotnet build backend/Anela.Heblo.sln --nologo --verbosity minimal
```

Expected: `dotnet format` reports no remaining issues, `dotnet build` succeeds with no warnings introduced by this change.

- [ ] **Step 7: Commit the production change**

```bash
git add backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs
git commit -m "refactor: remove duplicate error logging from OrgChartService"
```

---

## Task 3: Update the comment on the existing handler test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/OrgChart/GetOrganizationStructureHandlerTests.cs:66`

The existing assertion at lines 67–74 is still correct — the handler must not emit its own `LogError`. The comment, however, was written when the controller and service *both* logged and the handler was the only well-behaved layer. Now that the service has been brought into line, refresh the comment so future readers understand the single-owner rule rather than the historical contradiction.

- [ ] **Step 1: Update the inline comment**

Replace the line
```csharp
        // Assert: the handler does NOT emit its own LogError — the controller owns failure logging.
```
with
```csharp
        // Assert: the handler does NOT emit its own LogError. The OrgChartController is the
        // single error-logging site for the OrgChart slice; service and handler stay silent.
```

- [ ] **Step 2: Run the handler tests and verify they still pass**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetOrganizationStructureHandlerTests" \
  --nologo --verbosity minimal
```

Expected: All handler tests pass — the change is comment-only.

- [ ] **Step 3: Commit the comment update**

```bash
git add backend/test/Anela.Heblo.Tests/Features/OrgChart/GetOrganizationStructureHandlerTests.cs
git commit -m "docs: clarify single-owner error-logging rule in OrgChart handler test"
```

---

## Task 4: Full backend validation

**Files:** none

- [ ] **Step 1: Run the full OrgChart-slice test set**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.OrgChart" \
  --nologo --verbosity minimal
```

Expected: every test in `Anela.Heblo.Tests/Features/OrgChart/` passes (handler tests + the four new service tests).

- [ ] **Step 2: Run the full backend build + test pass**

Run:
```bash
dotnet build backend/Anela.Heblo.sln --nologo --verbosity minimal
dotnet test  backend/Anela.Heblo.sln --nologo --verbosity minimal
```

Expected: clean build, all tests green. If any unrelated test fails on `main` it must also fail on this branch — pull `main`, rerun the comparison, and flag it in the PR description; do not paper over an unrelated failure.

- [ ] **Step 3: Sanity-check that no `LogError` remains in `OrgChartService`**

Run:
```bash
grep -n "LogError" backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs || echo "no LogError found"
```

Expected: `no LogError found` — confirms FR-1 (as amended) is satisfied: zero `LogError`/`LogWarning`/`LogCritical` calls in the file.

(If you also want belt-and-braces coverage:)
```bash
grep -nE "_logger\.(LogError|LogWarning|LogCritical)" \
  backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs \
  || echo "no error/warn/critical logs found"
```

Expected: `no error/warn/critical logs found`.

- [ ] **Step 4: Confirm controller logging is unchanged**

Run:
```bash
grep -n "LogError" backend/src/Anela.Heblo.API/Controllers/OrgChartController.cs
```

Expected: one match — line 49, `_logger.LogError(ex, "Error fetching organizational structure");`. This is the **single** error-logging site.

---

## PR-time notes (not a code change)

When the implementing agent opens the PR, include a single-line operator-facing note in the PR description so anyone watching the `Anela.Heblo.Application.Features.OrgChart.Infrastructure.OrgChartService` logger category understands the expected drop:

> **Observability note:** OrgChart failures now emit exactly one `Error` log line, from the `OrgChartController` logger category, instead of two. Existing controller-level alerts continue to work; any alert keyed specifically on the `OrgChartService` logger category should be re-pointed to the controller category (or made category-agnostic). No alert configuration changes are strictly required.

This corresponds to spec NFR-3 and the operator-comms risk in the architecture review. No file change is required; it lives in the PR body only.

---

## Spec → Plan coverage map

| Spec / arch-review requirement | Where it is implemented |
|---|---|
| FR-1: remove `LogError` from each of the three catch blocks | Task 2, Steps 2–4 |
| FR-1 (amended): remove `LogError` from the null-deserialization guard (line 49) | Task 2, Step 1 |
| FR-1: preserve `HttpRequestException → InvalidOperationException` wrap + inner exception | Task 2 Step 2 (code) + Task 1 Step 1 (test `…WrapsHttpRequestException_…`) |
| FR-1: preserve `JsonException → InvalidOperationException` wrap + inner exception | Task 2 Step 3 (code) + Task 1 Step 1 (test `…WrapsJsonException_…`) |
| FR-1: re-throw generic `Exception` unchanged | Task 2 Step 4 (code) + Task 1 Step 1 (test `…RethrowsGenericException_…`) |
| FR-1: preserve happy-path `LogInformation` | Not touched in Task 2 (all `LogInformation` lines deliberately left intact) |
| FR-2: controller remains the sole error-logging site | No code change (verified in Task 4 Step 4); existing controller block unchanged |
| FR-3: existing handler-test assertion still valid, comment refreshed | Task 3 |
| FR-4: regression test per failure path with `LogLevel.Error` Times.Never | Task 1, all four tests, via `VerifyNoErrorLog()` |
| FR-4 (amended): regression test for null-deserialization path | Task 1 Step 1, test `…ThrowsOnNullDeserialization_…` |
| NFR-1: no measurable perf impact | Trivially satisfied — the change only deletes log calls on the failure path |
| NFR-2: no security/PII change | No new data is logged; the URL was already exposed via the existing controller `ex.ToString()` |
| NFR-3: operators see ~50% drop in OrgChart error count | Communicated via the PR-note section above |
| NFR-4: no public API / DTO / route change | No contract changes; verified by Task 4 Step 2 (full build) |
| Risk: future maintainer re-adds `LogError` in service | Tests in Task 1 will fail if any `LogError` is reintroduced |
| Risk: null-deserialization log still present | Task 2 Step 1 removes it; Task 1 Step 1 locks the rule |
