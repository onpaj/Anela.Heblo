using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Persistence.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;

namespace Anela.Heblo.Tests.Persistence.GridLayouts;

public class PostgresExceptionTranslatorTests
{
    private static PostgresExceptionTranslator CreateTranslator(ILogger<PostgresExceptionTranslator>? logger = null) =>
        new(logger ?? NullLogger<PostgresExceptionTranslator>.Instance);

    [Fact]
    public void TryTranslateGridLayout_GivenDirectNpgsqlException_ReturnsTranslatedExceptionWithOriginalAsInner()
    {
        // Arrange
        var inner = new NpgsqlException("connection refused");
        var translator = CreateTranslator();

        // Act
        var result = translator.TryTranslateGridLayout(inner, "Get");

        // Assert
        result.Should().NotBeNull();
        result!.InnerException.Should().BeSameAs(inner);
        result.Message.Should().Contain("Get");
    }

    [Fact]
    public void TryTranslateGridLayout_GivenDbUpdateExceptionWrappingNpgsqlException_ReturnsTranslatedException()
    {
        // Arrange
        var npgsqlInner = new NpgsqlException("duplicate key value violates unique constraint");
        var outer = new DbUpdateException("An error occurred while saving the entity changes.", npgsqlInner);
        var translator = CreateTranslator();

        // Act
        var result = translator.TryTranslateGridLayout(outer, "Upsert");

        // Assert
        result.Should().NotBeNull();
        result!.InnerException.Should().BeSameAs(outer);
        result.Message.Should().Contain("Upsert");
    }

    [Fact]
    public void TryTranslateGridLayout_GivenOperationCanceledException_ReturnsNull()
    {
        // Arrange
        var ex = new OperationCanceledException("cancelled by caller");
        var translator = CreateTranslator();

        // Act
        var result = translator.TryTranslateGridLayout(ex, "Get");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void TryTranslateGridLayout_GivenDbUpdateConcurrencyExceptionWithoutNpgsqlInner_ReturnsNull()
    {
        // Arrange
        var ex = new DbUpdateConcurrencyException("Concurrency token mismatch.");
        var translator = CreateTranslator();

        // Act
        var result = translator.TryTranslateGridLayout(ex, "Upsert");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void TryTranslateGridLayout_GivenPlainInvalidOperationException_ReturnsNull()
    {
        // Arrange
        var ex = new InvalidOperationException("something else");
        var translator = CreateTranslator();

        // Act
        var result = translator.TryTranslateGridLayout(ex, "Get");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void TryTranslateGridLayout_GivenNpgsqlException_LogsWarningWithSqlStateAndOperation()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<PostgresExceptionTranslator>>();
        var translator = CreateTranslator(loggerMock.Object);
        var npgsqlEx = new NpgsqlException("relation \"GridLayouts\" does not exist");

        // Act
        var result = translator.TryTranslateGridLayout(npgsqlEx, "GetAsync");

        // Assert
        result.Should().NotBeNull();

        // Verify LogWarning was called exactly once
        loggerMock.Invocations.Should().HaveCount(1);
        var invocation = loggerMock.Invocations.First();

        // Verify it was a Log call with Warning level
        invocation.Method.Name.Should().Be("Log");
        invocation.Arguments[0].Should().Be(LogLevel.Warning);

        // Verify the log message contains SqlState, Operation name and the operation passed as argument
        var logMessage = invocation.Arguments[2].ToString();
        logMessage.Should().Contain("SqlState=");
        logMessage.Should().Contain("GetAsync"); // Operation name is in the message
    }

    [Fact]
    public void TryTranslateGridLayout_GivenNonPostgresException_DoesNotLog()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<PostgresExceptionTranslator>>();
        var translator = CreateTranslator(loggerMock.Object);

        // Act
        var result = translator.TryTranslateGridLayout(new InvalidOperationException("unrelated"), "Get");

        // Assert
        result.Should().BeNull();
        loggerMock.Invocations.Should().BeEmpty("logger should not be called for non-Postgres exceptions");
    }
}
