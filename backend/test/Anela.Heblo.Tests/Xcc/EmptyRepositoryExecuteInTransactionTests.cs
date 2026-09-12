using Anela.Heblo.Xcc.Domain;
using Anela.Heblo.Xcc.Persistance;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Xcc;

public class EmptyRepositoryExecuteInTransactionTests
{
    private sealed class TestEntity : Entity<int>
    {
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_InvokesOperationAndReturnsItsResult()
    {
        var repository = new EmptyRepository<TestEntity, int>();
        var called = false;

        var result = await repository.ExecuteInTransactionAsync(ct =>
        {
            called = true;
            return Task.FromResult(42);
        });

        called.Should().BeTrue();
        result.Should().Be(42);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_PropagatesOperationExceptionUnchanged()
    {
        var repository = new EmptyRepository<TestEntity, int>();

        Func<Task> act = () => repository.ExecuteInTransactionAsync<int>(ct =>
            throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }
}
