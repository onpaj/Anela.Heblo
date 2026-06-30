using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Bank;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank;

public sealed class BankImportStateRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly BankImportStateRepository _repository;

    public BankImportStateRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"BankImportStateTests_{Guid.NewGuid()}")
            .Options;
        _context = new ApplicationDbContext(options);
        _repository = new BankImportStateRepository(_context);
    }

    [Fact]
    public async Task GetByAccountAsync_ReturnsNull_WhenMissing()
    {
        var result = await _repository.GetByAccountAsync("ComgateCZK");
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpsertAsync_InsertsThenUpdates_SameRow()
    {
        var state = new BankImportState("ComgateCZK");
        state.RecordSuccess(new DateTime(2026, 6, 10), DateTime.UtcNow, DateTime.UtcNow);
        await _repository.UpsertAsync(state);

        var loaded = await _repository.GetByAccountAsync("ComgateCZK");
        loaded!.LastValidImportDate.Should().Be(new DateTime(2026, 6, 10));

        loaded.RecordSuccess(new DateTime(2026, 6, 11), DateTime.UtcNow, DateTime.UtcNow);
        await _repository.UpsertAsync(loaded);

        var all = await _repository.GetAllAsync();
        all.Should().HaveCount(1);
        all[0].LastValidImportDate.Should().Be(new DateTime(2026, 6, 11));
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
