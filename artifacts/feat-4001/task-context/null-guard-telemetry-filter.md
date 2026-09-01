### task: null-guard-telemetry-filter

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Infrastructure/Telemetry/McpProductNotFoundTelemetryFilter.cs:29`
- Test: `backend/test/Anela.Heblo.Tests/Infrastructure/Telemetry/McpProductNotFoundTelemetryFilterTests.cs`

- [ ] **Step 1: Write the failing regression test**

Add a new `[Fact]` to the end of `McpProductNotFoundTelemetryFilterTests`, right after the existing `Process_ForwardsNonExceptionTelemetry` test (before the closing brace of the class). It builds an `ExceptionTelemetry` with `Message` explicitly `null` — mirroring the plain (non-`BuildMcpExceptionTelemetry`) construction style used by `Process_ForwardsOtherMcpExceptionTypes` and `Process_ForwardsNonMcpExceptions` — and asserts the filter does not throw and forwards the item unchanged via `_next.Process(exc)`.

```csharp
    [Fact]
    public void Process_ForwardsExceptionTelemetryWithNullMessage()
    {
        var exception = new McpException("[ProductNotFound] ProductNotFound: productCode: SA014");
        var exc = new ExceptionTelemetry(exception);
        exc.Message = null;

        Action act = () => _filter.Process(exc);

        act.Should().NotThrow();
        _next.Verify(n => n.Process(exc), Times.Once);
    }
```

The full file after this change (only the new method is added; everything else is untouched):

```csharp
using Anela.Heblo.API.Telemetry;
using FluentAssertions;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using ModelContextProtocol;
using Moq;

namespace Anela.Heblo.Tests.Infrastructure.Telemetry;

public class McpProductNotFoundTelemetryFilterTests
{
    private readonly Mock<ITelemetryProcessor> _next = new();
    private readonly McpProductNotFoundTelemetryFilter _filter;

    public McpProductNotFoundTelemetryFilterTests()
    {
        _filter = new McpProductNotFoundTelemetryFilter(_next.Object);
    }

    private static ExceptionTelemetry BuildMcpExceptionTelemetry(string message)
    {
        var exception = new McpException(message);
        var exc = new ExceptionTelemetry(exception);
        exc.Message = message;
        return exc;
    }

    [Fact]
    public void Process_ConvertsMatchingMcpExceptionToWarningTrace()
    {
        var exc = BuildMcpExceptionTelemetry("[ProductNotFound] ProductNotFound: productCode: SA014");

        _filter.Process(exc);

        _next.Verify(n => n.Process(It.Is<ExceptionTelemetry>(_ => true)), Times.Never);
        _next.Verify(n => n.Process(It.Is<TraceTelemetry>(t =>
            t.SeverityLevel == SeverityLevel.Warning &&
            t.Message.Contains("[ProductNotFound]"))), Times.Once);
    }

    [Fact]
    public void Process_CopiesPropertiesFromExceptionToTrace()
    {
        var exc = BuildMcpExceptionTelemetry("[ProductNotFound] ProductNotFound: productCode: SA014");
        exc.Properties["productCode"] = "SA014";
        exc.Properties["someKey"] = "someValue";

        _filter.Process(exc);

        _next.Verify(n => n.Process(It.Is<TraceTelemetry>(t =>
            t.Properties.ContainsKey("productCode") &&
            t.Properties["productCode"] == "SA014" &&
            t.Properties.ContainsKey("someKey") &&
            t.Properties["someKey"] == "someValue")), Times.Once);
    }

    [Fact]
    public void Process_ForwardsOtherMcpExceptionTypes()
    {
        var exception = new McpException("[UNKNOWN_ERROR] Something went wrong.");
        var exc = new ExceptionTelemetry(exception);
        exc.Message = "[UNKNOWN_ERROR] Something went wrong.";

        _filter.Process(exc);

        _next.Verify(n => n.Process(exc), Times.Once);
    }

    [Fact]
    public void Process_ForwardsNonMcpExceptions()
    {
        var exception = new InvalidOperationException("[ProductNotFound] Something failed.");
        var exc = new ExceptionTelemetry(exception);
        exc.Message = "[ProductNotFound] Something failed.";

        _filter.Process(exc);

        _next.Verify(n => n.Process(exc), Times.Once);
    }

    [Fact]
    public void Process_ForwardsNonExceptionTelemetry()
    {
        var trace = new TraceTelemetry("hello world");

        _filter.Process(trace);

        _next.Verify(n => n.Process(trace), Times.Once);
    }

    [Fact]
    public void Process_ForwardsExceptionTelemetryWithNullMessage()
    {
        var exception = new McpException("[ProductNotFound] ProductNotFound: productCode: SA014");
        var exc = new ExceptionTelemetry(exception);
        exc.Message = null;

        Action act = () => _filter.Process(exc);

        act.Should().NotThrow();
        _next.Verify(n => n.Process(exc), Times.Once);
    }
}
```

- [ ] **Step 2: Run the new test to verify it fails against the current (buggy) code**

```bash
cd /home/user/worktrees/feature-4001-Telemetry-Nullreferenceexception-In-Mcpproductnotf
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~McpProductNotFoundTelemetryFilterTests.Process_ForwardsExceptionTelemetryWithNullMessage"
```

Expected: **FAIL**. `_filter.Process(exc)` throws `NullReferenceException` from the unguarded `exc.Message.Contains(...)` call in `McpProductNotFoundTelemetryFilter.Process` (line 29), so `act.Should().NotThrow()` reports a failure whose message includes `System.NullReferenceException` — e.g.:

```
Failed! - Failed: 1, Passed: 0, Skipped: 0, Total: 1
Process_ForwardsExceptionTelemetryWithNullMessage
  Expected act to not throw, but it does throw System.NullReferenceException with message ...
```

This confirms the test actually reproduces the bug before the fix is applied.

- [ ] **Step 3: Implement the null-guard fix**

In `backend/src/Anela.Heblo.API/Infrastructure/Telemetry/McpProductNotFoundTelemetryFilter.cs`, change the `Contains` check on line 29 from an unguarded call to a null-safe one. Only this one line changes; `IsMcpException` and everything else in the file stays exactly as-is.

Before:
```csharp
        if (item is ExceptionTelemetry exc
            && exc.Message.Contains(ProductNotFoundMarker, StringComparison.Ordinal)
            && IsMcpException(exc))
```

After:
```csharp
        if (item is ExceptionTelemetry exc
            && exc.Message?.Contains(ProductNotFoundMarker, StringComparison.Ordinal) == true
            && IsMcpException(exc))
```

Full method after the change (rest of the file is untouched):

```csharp
    public void Process(ITelemetry item)
    {
        if (item is ExceptionTelemetry exc
            && exc.Message?.Contains(ProductNotFoundMarker, StringComparison.Ordinal) == true
            && IsMcpException(exc))
        {
            var trace = new TraceTelemetry(exc.Message, SeverityLevel.Warning);
            foreach (var prop in exc.Properties)
            {
                trace.Properties[prop.Key] = prop.Value;
            }
            _next.Process(trace);
            return;
        }

        _next.Process(item);
    }
```

- [ ] **Step 4: Run the new test to verify it now passes**

```bash
cd /home/user/worktrees/feature-4001-Telemetry-Nullreferenceexception-In-Mcpproductnotf
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~McpProductNotFoundTelemetryFilterTests.Process_ForwardsExceptionTelemetryWithNullMessage"
```

Expected: **PASS**.

```
Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1
```

- [ ] **Step 5: Run the full test class to confirm no regressions**

```bash
cd /home/user/worktrees/feature-4001-Telemetry-Nullreferenceexception-In-Mcpproductnotf
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~McpProductNotFoundTelemetryFilterTests"
```

Expected: **PASS**, all 6 tests green (`Process_ConvertsMatchingMcpExceptionToWarningTrace`, `Process_CopiesPropertiesFromExceptionToTrace`, `Process_ForwardsOtherMcpExceptionTypes`, `Process_ForwardsNonMcpExceptions`, `Process_ForwardsNonExceptionTelemetry`, `Process_ForwardsExceptionTelemetryWithNullMessage`).

```
Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6
```

- [ ] **Step 6: Build and format the solution**

```bash
cd /home/user/worktrees/feature-4001-Telemetry-Nullreferenceexception-In-Mcpproductnotf
dotnet build
dotnet format
```

Expected: `dotnet build` reports `Build succeeded. 0 Error(s)`; `dotnet format` completes with no formatting violations reported (the two changed lines already match the surrounding indentation/style, and the new test method matches the existing methods' formatting).

- [ ] **Step 7: Commit**

```bash
cd /home/user/worktrees/feature-4001-Telemetry-Nullreferenceexception-In-Mcpproductnotf
git add backend/src/Anela.Heblo.API/Infrastructure/Telemetry/McpProductNotFoundTelemetryFilter.cs backend/test/Anela.Heblo.Tests/Infrastructure/Telemetry/McpProductNotFoundTelemetryFilterTests.cs
git commit -m "fix: null-guard McpProductNotFoundTelemetryFilter against null ExceptionTelemetry.Message"
```
