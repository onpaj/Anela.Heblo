using System.Text.Json;
using Anela.Heblo.API.Infrastructure.ExceptionHandling;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Infrastructure.ExceptionHandling;

public class ValidationExceptionHandlerTests
{
    private static (ValidationExceptionHandler Handler, DefaultHttpContext Context, MemoryStream Body) CreateSut()
    {
        var handler = new ValidationExceptionHandler(
            NullLogger<ValidationExceptionHandler>.Instance);
        var body = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Response.Body = body;
        return (handler, context, body);
    }

    [Fact]
    public async Task TryHandleAsync_WhenValidationException_Returns400WithProblemDetails()
    {
        var (handler, context, body) = CreateSut();
        var exception = new ValidationException(new[]
        {
            new ValidationFailure("Amount", "Must be greater than zero")
        });

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        body.Position = 0;
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(400);
        doc.RootElement.GetProperty("title").GetString().Should().Be("Validation Failed");
    }

    [Fact]
    public async Task TryHandleAsync_WhenValidationException_ExposesErrorsInExtensions()
    {
        var (handler, context, body) = CreateSut();
        var exception = new ValidationException(new[]
        {
            new ValidationFailure("Amount", "Must be greater than zero")
        });

        await handler.TryHandleAsync(context, exception, CancellationToken.None);

        body.Position = 0;
        using var doc = JsonDocument.Parse(body);
        var errors = doc.RootElement.GetProperty("errors");
        errors.GetArrayLength().Should().Be(1);
        errors[0].GetProperty("propertyName").GetString().Should().Be("Amount");
        errors[0].GetProperty("errorMessage").GetString().Should().Be("Must be greater than zero");
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
