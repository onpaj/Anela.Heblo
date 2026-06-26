using System.Net.Sockets;
using Anela.Heblo.Persistence.Infrastructure.Resilience;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anela.Heblo.Tests.Persistence.Resilience;

public class TransientErrorClassifierTests
{
    [Theory]
    [InlineData("57P01")]
    [InlineData("57P02")]
    [InlineData("57P03")]
    [InlineData("08000")]
    [InlineData("08003")]
    [InlineData("08006")]
    [InlineData("08001")]
    [InlineData("08004")]
    [InlineData("08007")]
    public void IsTransient_ReturnsTrue_ForTransientPostgresSqlStates(string sqlState)
    {
        var ex = CreatePostgresException(sqlState);

        TransientErrorClassifier.IsTransient(ex).Should().BeTrue();
    }

    [Theory]
    [InlineData("23505")]
    [InlineData("23503")]
    [InlineData("23502")]
    [InlineData("42P01")]
    public void IsTransient_ReturnsFalse_ForNonTransientPostgresSqlStates(string sqlState)
    {
        var ex = CreatePostgresException(sqlState);

        TransientErrorClassifier.IsTransient(ex).Should().BeFalse();
    }

    [Fact]
    public void IsTransient_ReturnsTrue_ForSocketException()
    {
        var ex = new SocketException();

        TransientErrorClassifier.IsTransient(ex).Should().BeTrue();
    }

    [Fact]
    public void IsTransient_ReturnsTrue_ForTimeoutException()
    {
        TransientErrorClassifier.IsTransient(new TimeoutException()).Should().BeTrue();
    }

    [Fact]
    public void IsTransient_ReturnsTrue_ForIOException()
    {
        TransientErrorClassifier.IsTransient(new IOException()).Should().BeTrue();
    }

    [Fact]
    public void IsTransient_UnwrapsInnerExceptions()
    {
        var inner = CreatePostgresException("57P03");
        var outer = new InvalidOperationException("wrapper", inner);

        TransientErrorClassifier.IsTransient(outer).Should().BeTrue();
    }

    [Theory]
    [InlineData("23505")]
    [InlineData("23503")]
    [InlineData("23502")]
    public void IsNonTransientLogical_ReturnsTrue_ForLogicalConflictCodes(string sqlState)
    {
        var pg = CreatePostgresException(sqlState);
        var update = new DbUpdateException("save failed", pg);

        TransientErrorClassifier.IsNonTransientLogical(update).Should().BeTrue();
    }

    [Fact]
    public void IsNonTransientLogical_ReturnsTrue_ForDbUpdateConcurrencyException()
    {
        TransientErrorClassifier
            .IsNonTransientLogical(new DbUpdateConcurrencyException("conflict"))
            .Should().BeTrue();
    }

    [Fact]
    public void IsNonTransientLogical_ReturnsFalse_ForTransientPostgresException()
    {
        var pg = CreatePostgresException("57P03");
        var update = new DbUpdateException("save failed", pg);

        TransientErrorClassifier.IsNonTransientLogical(update).Should().BeFalse();
    }

    [Fact]
    public void IsTransient_ReturnsFalse_WhenNonTransientLogicalWrapsTransient()
    {
        var pg = CreatePostgresException("23505");
        var update = new DbUpdateException("save failed", pg);

        TransientErrorClassifier.IsTransient(update).Should().BeFalse();
    }

    private static PostgresException CreatePostgresException(string sqlState)
    {
        return new PostgresException(
            messageText: $"simulated {sqlState}",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: sqlState);
    }
}
