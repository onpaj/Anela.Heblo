using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Persistence.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anela.Heblo.Tests.Persistence.GridLayouts;

public class PostgresExceptionTranslatorTests
{
    [Fact]
    public void TryTranslateGridLayout_GivenDirectNpgsqlException_ReturnsTranslatedExceptionWithOriginalAsInner()
    {
        // Arrange
        var inner = new NpgsqlException("connection refused");

        // Act
        var result = PostgresExceptionTranslator.TryTranslateGridLayout(inner, "Get");

        // Assert
        result.Should().NotBeNull();
        result!.InnerException.Should().BeSameAs(inner);
        result.Message.Should().Contain("Get");
        result.SqlState.Should().BeNull();
    }

    [Fact]
    public void TryTranslateGridLayout_GivenDbUpdateExceptionWrappingNpgsqlException_ReturnsTranslatedException()
    {
        // Arrange
        var npgsqlInner = new NpgsqlException("duplicate key value violates unique constraint");
        var outer = new DbUpdateException("An error occurred while saving the entity changes.", npgsqlInner);

        // Act
        var result = PostgresExceptionTranslator.TryTranslateGridLayout(outer, "Upsert");

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

        // Act
        var result = PostgresExceptionTranslator.TryTranslateGridLayout(ex, "Get");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void TryTranslateGridLayout_GivenDbUpdateConcurrencyExceptionWithoutNpgsqlInner_ReturnsNull()
    {
        // Arrange
        var ex = new DbUpdateConcurrencyException("Concurrency token mismatch.");

        // Act
        var result = PostgresExceptionTranslator.TryTranslateGridLayout(ex, "Upsert");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void TryTranslateGridLayout_GivenPlainInvalidOperationException_ReturnsNull()
    {
        // Arrange
        var ex = new InvalidOperationException("something else");

        // Act
        var result = PostgresExceptionTranslator.TryTranslateGridLayout(ex, "Get");

        // Assert
        result.Should().BeNull();
    }
}
