### task: add-mcp-telemetry-helpers

**Files:**
- Create: `backend/src/Anela.Heblo.API/MCP/McpTelemetryHelpers.cs`
- Test: `backend/test/Anela.Heblo.Tests/MCP/McpTelemetryHelpersTests.cs`

Introduce a small shared helper for session-ID prefix truncation. Both `McpBadRequestMiddleware` (this feature) and `McpDiagnosticsMiddleware` (existing) need identical semantics; a single helper eliminates per-class drift.

- [ ] **Step 1: Write the failing test file**

Create `backend/test/Anela.Heblo.Tests/MCP/McpTelemetryHelpersTests.cs`:

```csharp
using Anela.Heblo.API.MCP;
using Xunit;

namespace Anela.Heblo.Tests.MCP;

public class McpTelemetryHelpersTests
{
    [Fact]
    public void TruncateSessionId_Null_ReturnsMissingSentinel()
    {
        Assert.Equal("(missing)", McpTelemetryHelpers.TruncateSessionId(null));
    }

    [Fact]
    public void TruncateSessionId_Empty_ReturnsMissingSentinel()
    {
        Assert.Equal("(missing)", McpTelemetryHelpers.TruncateSessionId(string.Empty));
    }

    [Fact]
    public void TruncateSessionId_ShorterThan8_ReturnsFullValue()
    {
        Assert.Equal("abc", McpTelemetryHelpers.TruncateSessionId("abc"));
    }

    [Fact]
    public void TruncateSessionId_Exactly8_ReturnsFullValue()
    {
        Assert.Equal("abcdefgh", McpTelemetryHelpers.TruncateSessionId("abcdefgh"));
    }

    [Fact]
    public void TruncateSessionId_LongerThan8_ReturnsFirstEightChars()
    {
        Assert.Equal("abcdefgh", McpTelemetryHelpers.TruncateSessionId("abcdefgh12345678"));
    }

    [Fact]
    public void TruncateSessionId_32CharValue_NeverExceedsEightChars()
    {
        var sessionId = new string('x', 32);
        var result = McpTelemetryHelpers.TruncateSessionId(sessionId);
        Assert.Equal(8, result.Length);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~McpTelemetryHelpersTests"`
Expected: FAIL — `McpTelemetryHelpers` type does not exist (compilation error).

- [ ] **Step 3: Create the helper**

Create `backend/src/Anela.Heblo.API/MCP/McpTelemetryHelpers.cs`:

```csharp
namespace Anela.Heblo.API.MCP;

/// <summary>
/// Small helpers shared across MCP middleware for consistent telemetry values.
/// </summary>
internal static class McpTelemetryHelpers
{
    /// <summary>
    /// Returns the first 8 characters of <paramref name="value"/> if non-empty,
    /// or "(missing)" if null/empty. Never returns more than 8 characters.
    /// A header present but empty is treated as absent per FR-1.
    /// </summary>
    public static string TruncateSessionId(string? value)
        => string.IsNullOrEmpty(value)
            ? "(missing)"
            : value.Length <= 8 ? value : value[..8];
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~McpTelemetryHelpersTests"`
Expected: PASS — all 6 tests green.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP/McpTelemetryHelpers.cs \
        backend/test/Anela.Heblo.Tests/MCP/McpTelemetryHelpersTests.cs
git commit -m "feat(mcp): add shared TruncateSessionId helper for MCP telemetry

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01536bqWhmDWH9P2wczh4YrV"
```

---
