using System.Text.Json;
using Anela.Heblo.API.Infrastructure.ExceptionHandling;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Infrastructure.ExceptionHandling;

public class ArgumentExceptionHandlerTests
{
    private static (ArgumentExceptionHandler Handler, DefaultHttpContext Context, MemoryStream Body) CreateSut()
    {
        var handler = new ArgumentExceptionHandler(
            NullLogger<ArgumentExceptionHandler>.Instance);
        var body = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Response.Body = body;
        return (handler, context, body);
    }

    [Fact]
    public async Task TryHandleAsync_WhenArgumentException_Returns400WithProblemDetails()
    {
        var (handler, context, body) = CreateSut();
        var exception = new ArgumentException("Unknown account name: TestAccount");

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        body.Position = 0;
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(400);
        doc.RootElement.GetProperty("title").GetString().Should().Be("Bad Request");
        doc.RootElement.GetProperty("detail").GetString().Should().Be("Unknown account name: TestAccount");
    }

    [Fact]
    public async Task TryHandleAsync_WhenArgumentNullException_Returns400()
    {
        var (handler, context, body) = CreateSut();
        var exception = new ArgumentNullException("param");

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task TryHandleAsync_WhenOtherException_ReturnsFalseAndDoesNotWriteBody()
    {
        var (handler, context, body) = CreateSut();
        var exception = new InvalidOperationException("unrelated");

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        handled.Should().BeFalse();
        body.Length.Should().Be(0);
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }
}
