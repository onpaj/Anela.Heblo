using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.GridLayouts;
using Anela.Heblo.Persistence.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Anela.Heblo.Tests.Persistence.GridLayouts;

public class GridLayoutRepositoryTranslationTests
{
    private sealed class ThrowingApplicationDbContext : ApplicationDbContext
    {
        public Exception? ThrowOnSaveChanges { get; set; }

        public ThrowingApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowOnSaveChanges is not null)
            {
                throw ThrowOnSaveChanges;
            }
            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private static ThrowingApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"GridLayoutTranslationTests_{Guid.NewGuid()}")
            .Options;
        return new ThrowingApplicationDbContext(options);
    }

    [Fact]
    public async Task DeleteAsync_WhenSaveChangesThrowsNpgsqlException_ThrowsGridLayoutPersistenceException()
    {
        // Arrange
        using var context = CreateContext();
        context.GridLayouts.Add(new GridLayout
        {
            UserId = "user-1",
            GridKey = "grid-1",
            LayoutJson = "{}",
            LastModified = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var npgsqlEx = new NpgsqlException("connection terminated");
        context.ThrowOnSaveChanges = npgsqlEx;

        var translator = new PostgresExceptionTranslator(NullLogger<PostgresExceptionTranslator>.Instance);
        var repository = new GridLayoutRepository(context, TimeProvider.System, translator);

        // Act
        Func<Task> act = () => repository.DeleteAsync("user-1", "grid-1", CancellationToken.None);

        // Assert
        var thrown = await act.Should().ThrowAsync<GridLayoutPersistenceException>();
        thrown.Which.InnerException.Should().BeSameAs(npgsqlEx);
    }
}
