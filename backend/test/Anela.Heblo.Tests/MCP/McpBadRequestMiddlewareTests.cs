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
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains("400") &&
                    v.ToString()!.Contains("mcp-client/2.0")),
                null,
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
