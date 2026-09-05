using Anela.Heblo.API.MCP;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP;

public class McpBadRequestMiddlewareTests
{
    private readonly Mock<ILogger<McpBadRequestMiddleware>> _loggerMock;

    public McpBadRequestMiddlewareTests()
    {
        _loggerMock = new Mock<ILogger<McpBadRequestMiddleware>>();
    }

    private McpBadRequestMiddleware CreateMiddleware(RequestDelegate next)
        => new(next, _loggerMock.Object);

    // ── helper ──────────────────────────────────────────────────────────────

    private static HttpContext CreateContext(
        string method,
        string path,
        int responseStatus = 200,
        string? acceptHeader = null,
        string? userAgent = null,
        string? origin = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.StatusCode = responseStatus;

        if (acceptHeader is not null)
            context.Request.Headers.Accept = acceptHeader;
        if (userAgent is not null)
            context.Request.Headers.UserAgent = userAgent;
        if (origin is not null)
            context.Request.Headers.Origin = origin;

        return context;
    }

    private void VerifyNoLogCalled()
    {
        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    // ── non-MCP paths ────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_NonMcpPath_PassesThrough()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled = true;
            ctx.Response.StatusCode = 400;
            return Task.CompletedTask;
        });

        var context = CreateContext("GET", "/api/other");
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(400); // unchanged
        VerifyNoLogCalled();
    }

    [Fact]
    public async Task InvokeAsync_PostMcpPath_PassesThrough()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateContext("POST", "/mcp", acceptHeader: "application/json");
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        VerifyNoLogCalled();
    }

    // ── probe blocking (missing/invalid Accept) ──────────────────────────────

    [Fact]
    public async Task InvokeAsync_GetMcpWithoutAcceptHeader_Returns404WithoutCallingNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateContext("GET", "/mcp"); // no Accept header
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task InvokeAsync_GetMcpWithUnrecognizedAcceptHeader_Returns404()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateContext("GET", "/mcp", acceptHeader: "text/html");
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task InvokeAsync_GetMcpWithoutAcceptHeader_LogsInformationWithUserAgent()
    {
        var middleware = CreateMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        var context = CreateContext("GET", "/mcp", userAgent: "scanner-bot/1.0");
        await middleware.InvokeAsync(context);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("scanner-bot/1.0")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ── valid Accept header — passes through ─────────────────────────────────

    [Fact]
    public async Task InvokeAsync_GetMcpWithEventStreamAccept_CallsNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled = true;
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        var context = CreateContext("GET", "/mcp", acceptHeader: "text/event-stream");
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_GetMcpWithJsonAccept_CallsNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled = true;
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        var context = CreateContext("GET", "/mcp", acceptHeader: "application/json");
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_GetMcpWithMultiValueAccept_CallsNextWhenContainsEventStream()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled = true;
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        var context = CreateContext("GET", "/mcp", acceptHeader: "text/html, text/event-stream, */*");
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    // ── 400 diagnostics logging ──────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_GetMcpWith400Response_LogsWarningWithDiagnostics()
    {
        var middleware = CreateMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 400;
            return Task.CompletedTask;
        });

        var context = CreateContext(
            "GET", "/mcp",
            responseStatus: 400,
            acceptHeader: "text/event-stream",
            userAgent: "mcp-client/2.0",
            origin: "https://claude.ai");

        await middleware.InvokeAsync(context);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.Is<EventId>(e => e.Name == "McpBadRequest"),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains("400") &&
                    v.ToString()!.Contains("mcp-client/2.0") &&
                    v.ToString()!.Contains("ContentType") &&
                    v.ToString()!.Contains("McpSessionIdPresent") &&
                    v.ToString()!.Contains("McpSessionIdPrefix") &&
                    v.ToString()!.Contains("RemoteIp") &&
                    v.ToString()!.Contains("ElapsedMs")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetBadRequest_Log_IncludesAllUnionFields()
    {
        // Arrange: GET /mcp that reaches next() and gets a 400 back
        var middleware = CreateMiddleware(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        });
        var context = CreateContext(
            "GET", "/mcp",
            responseStatus: 400,
            acceptHeader: "application/json, text/event-stream",
            userAgent: "test-agent");
        context.Request.Headers["Mcp-Session-Id"] = "abcdef1234567890";

        // Act
        await middleware.InvokeAsync(context);

        // Assert — one warning log call, whose state contains every union-schema field.
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.Is<EventId>(e => e.Name == "McpBadRequest"),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("HTTPMethod") &&
                    state.ToString()!.Contains("Path") &&
                    state.ToString()!.Contains("StatusCode") &&
                    state.ToString()!.Contains("UserAgent") &&
                    state.ToString()!.Contains("Origin") &&
                    state.ToString()!.Contains("Accept") &&
                    state.ToString()!.Contains("ContentType") &&
                    state.ToString()!.Contains("McpSessionIdPresent") &&
                    state.ToString()!.Contains("McpSessionIdPrefix") &&
                    state.ToString()!.Contains("RemoteIp") &&
                    state.ToString()!.Contains("ElapsedMs")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_GetMcpWith200Response_DoesNotLogWarning()
    {
        var middleware = CreateMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        var context = CreateContext("GET", "/mcp", acceptHeader: "text/event-stream");
        await middleware.InvokeAsync(context);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_GetMcpWith401Response_DoesNotLogWarning()
    {
        var middleware = CreateMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 401;
            return Task.CompletedTask;
        });

        var context = CreateContext("GET", "/mcp", acceptHeader: "text/event-stream");
        await middleware.InvokeAsync(context);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    // ── POST /mcp 400 diagnostics logging ────────────────────────────────────

    [Fact]
    public async Task PostBadRequest_NoSessionHeader_LogsOneWarningWithSessionAbsent()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<McpBadRequestMiddleware>>();
        var next = new RequestDelegate(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        });
        var middleware = new McpBadRequestMiddleware(next, loggerMock.Object);
        var context = CreateContext("POST", "/mcp");
        context.Request.Headers["User-Agent"] = "test-client";
        context.Request.Headers["Accept"] = "application/json";
        // Deliberately no Mcp-Session-Id.

        // Act
        await middleware.InvokeAsync(context);

        // Assert — exactly one warning, EventName=McpBadRequest, McpSessionIdPresent=false.
        loggerMock.Verify(l => l.Log(
            LogLevel.Warning,
            It.Is<EventId>(e => e.Name == "McpBadRequest" && e.Id == 5932),
            It.Is<It.IsAnyType>((state, _) =>
                state.ToString()!.Contains("HTTPMethod") &&
                state.ToString()!.Contains("POST") &&
                state.ToString()!.Contains("McpSessionIdPresent=False")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // POST response was NOT rewritten.
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task PostBadRequest_WithSessionHeader_LogsPresenceAndEightCharPrefix()
    {
        var loggerMock = new Mock<ILogger<McpBadRequestMiddleware>>();
        var next = new RequestDelegate(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        });
        var middleware = new McpBadRequestMiddleware(next, loggerMock.Object);
        var context = CreateContext("POST", "/mcp");
        context.Request.Headers["Mcp-Session-Id"] = "abcdef1234567890";

        await middleware.InvokeAsync(context);

        loggerMock.Verify(l => l.Log(
            LogLevel.Warning,
            It.Is<EventId>(e => e.Name == "McpBadRequest" && e.Id == 5932),
            It.Is<It.IsAnyType>((state, _) =>
                state.ToString()!.Contains("McpSessionIdPresent=True") &&
                state.ToString()!.Contains("McpSessionIdPrefix=abcdef12")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task PostHeaderlessBodyless_400_LoggedButNotRewritten()
    {
        var loggerMock = new Mock<ILogger<McpBadRequestMiddleware>>();
        var next = new RequestDelegate(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        });
        var middleware = new McpBadRequestMiddleware(next, loggerMock.Object);
        // No Accept, no User-Agent, no body — the GET equivalent short-circuits to 404;
        // POST must NOT short-circuit.
        var context = CreateContext("POST", "/mcp");

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        loggerMock.Verify(l => l.Log(
            LogLevel.Warning,
            It.Is<EventId>(e => e.Name == "McpBadRequest" && e.Id == 5932),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task PostSuccess200_EmitsNoLog()
    {
        var loggerMock = new Mock<ILogger<McpBadRequestMiddleware>>();
        var next = new RequestDelegate(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });
        var middleware = new McpBadRequestMiddleware(next, loggerMock.Object);
        var context = CreateContext("POST", "/mcp");
        context.Request.Headers["Mcp-Session-Id"] = "abcdef1234567890";

        await middleware.InvokeAsync(context);

        loggerMock.Verify(l => l.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task PostServerError500_EmitsNoLog()
    {
        var loggerMock = new Mock<ILogger<McpBadRequestMiddleware>>();
        var next = new RequestDelegate(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Task.CompletedTask;
        });
        var middleware = new McpBadRequestMiddleware(next, loggerMock.Object);
        var context = CreateContext("POST", "/mcp");

        await middleware.InvokeAsync(context);

        loggerMock.Verify(l => l.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task PostBadRequest_LowercaseSessionHeader_TreatedAsPresent()
    {
        var loggerMock = new Mock<ILogger<McpBadRequestMiddleware>>();
        var next = new RequestDelegate(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        });
        var middleware = new McpBadRequestMiddleware(next, loggerMock.Object);
        var context = CreateContext("POST", "/mcp");
        context.Request.Headers["mcp-session-id"] = "abcdef1234567890"; // lowercase

        await middleware.InvokeAsync(context);

        loggerMock.Verify(l => l.Log(
            LogLevel.Warning,
            It.Is<EventId>(e => e.Name == "McpBadRequest"),
            It.Is<It.IsAnyType>((state, _) =>
                state.ToString()!.Contains("McpSessionIdPresent=True") &&
                state.ToString()!.Contains("McpSessionIdPrefix=abcdef12")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task PostBadRequest_ShortSessionId_LogsFullValueNoOverflow()
    {
        var loggerMock = new Mock<ILogger<McpBadRequestMiddleware>>();
        var next = new RequestDelegate(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        });
        var middleware = new McpBadRequestMiddleware(next, loggerMock.Object);
        var context = CreateContext("POST", "/mcp");
        context.Request.Headers["Mcp-Session-Id"] = "abc"; // shorter than 8 chars

        await middleware.InvokeAsync(context);

        loggerMock.Verify(l => l.Log(
            LogLevel.Warning,
            It.Is<EventId>(e => e.Name == "McpBadRequest"),
            It.Is<It.IsAnyType>((state, _) =>
                state.ToString()!.Contains("McpSessionIdPresent=True") &&
                state.ToString()!.Contains("McpSessionIdPrefix=abc")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task PostBadRequest_EmptySessionId_TreatedAsAbsent()
    {
        var loggerMock = new Mock<ILogger<McpBadRequestMiddleware>>();
        var next = new RequestDelegate(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        });
        var middleware = new McpBadRequestMiddleware(next, loggerMock.Object);
        var context = CreateContext("POST", "/mcp");
        context.Request.Headers["Mcp-Session-Id"] = ""; // present but empty

        await middleware.InvokeAsync(context);

        loggerMock.Verify(l => l.Log(
            LogLevel.Warning,
            It.Is<EventId>(e => e.Name == "McpBadRequest"),
            It.Is<It.IsAnyType>((state, _) =>
                state.ToString()!.Contains("McpSessionIdPresent=False") &&
                state.ToString()!.Contains("McpSessionIdPrefix=(missing)")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ── PII guard: sensitive headers must never reach the log ───────────────

    [Fact]
    public async Task PostBadRequest_WithAuthorizationHeader_LogNeverContainsBearer()
    {
        var loggerMock = new Mock<ILogger<McpBadRequestMiddleware>>();
        var next = new RequestDelegate(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        });
        var middleware = new McpBadRequestMiddleware(next, loggerMock.Object);
        var context = CreateContext("POST", "/mcp");
        context.Request.Headers["Authorization"] = "Bearer supersecrettoken";
        context.Request.Headers["Cookie"] = "session=secret";
        context.Request.Headers["X-Api-Key"] = "topsecret";

        await middleware.InvokeAsync(context);

        // Assert that no log call contains any of the sensitive substrings anywhere in its
        // formatted state.
        loggerMock.Verify(l => l.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) =>
                state.ToString()!.Contains("Bearer") ||
                state.ToString()!.Contains("supersecrettoken") ||
                state.ToString()!.Contains("session=secret") ||
                state.ToString()!.Contains("topsecret")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    // ── non-/mcp paths never trigger the middleware ──────────────────────────

    [Theory]
    [InlineData("GET", "/api/foo", 400)]
    [InlineData("POST", "/api/foo", 400)]
    [InlineData("POST", "/health", 500)]
    [InlineData("POST", "/mcpx", 400)]  // path prefix must be a segment match, not a raw prefix
    public async Task NonMcpPath_NeverLogs(string method, string path, int status)
    {
        var loggerMock = new Mock<ILogger<McpBadRequestMiddleware>>();
        var next = new RequestDelegate(ctx =>
        {
            ctx.Response.StatusCode = status;
            return Task.CompletedTask;
        });
        var middleware = new McpBadRequestMiddleware(next, loggerMock.Object);
        var context = CreateContext(method, path);

        await middleware.InvokeAsync(context);

        loggerMock.Verify(l => l.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    // ── HasValidMcpAcceptHeader static helper ────────────────────────────────

    [Theory]
    [InlineData("text/event-stream", true)]
    [InlineData("application/json", true)]
    [InlineData("TEXT/EVENT-STREAM", true)]         // case-insensitive
    [InlineData("text/html, text/event-stream", true)]
    [InlineData("text/html", false)]
    [InlineData("", false)]
    public void HasValidMcpAcceptHeader_ReturnsExpected(string acceptValue, bool expected)
    {
        var context = new DefaultHttpContext();
        if (!string.IsNullOrEmpty(acceptValue))
            context.Request.Headers.Accept = acceptValue;

        McpBadRequestMiddleware.HasValidMcpAcceptHeader(context).Should().Be(expected);
    }

    [Fact]
    public void HasValidMcpAcceptHeader_MissingHeader_ReturnsFalse()
    {
        var context = new DefaultHttpContext();
        // no Accept header set at all
        McpBadRequestMiddleware.HasValidMcpAcceptHeader(context).Should().BeFalse();
    }
}
