using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Repositories.Packaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Persistence.Packaging;

public class CarrierLabelOffsetRepositoryTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase($"CarrierLabelOffsetRepositoryTests_{Guid.NewGuid()}")
        .Options;

    private async Task UpsertAsync(CarrierLabelOffset offset)
    {
        await using var context = new ApplicationDbContext(_options);
        await new CarrierLabelOffsetRepository(context).UpsertAsync(offset);
    }

    // Read through a fresh context so the assertion sees what was saved, not a tracked instance.
    private async Task<IReadOnlyList<CarrierLabelOffset>> ReadAllAsync()
    {
        await using var context = new ApplicationDbContext(_options);
        return await new CarrierLabelOffsetRepository(context).GetAllAsync();
    }

    [Fact]
    public async Task Upsert_NewCarrier_InsertsRow()
    {
        await UpsertAsync(new CarrierLabelOffset(Carriers.Zasilkovna, 2, -5, "user-1"));

        var saved = (await ReadAllAsync()).Should().ContainSingle().Subject;
        (saved.Carrier, saved.OffsetXMm, saved.OffsetYMm, saved.ModifiedBy)
            .Should().Be((Carriers.Zasilkovna, 2, -5, "user-1"));
    }

    [Fact]
    public async Task Upsert_ExistingCarrier_UpdatesRowInPlace()
    {
        await UpsertAsync(new CarrierLabelOffset(Carriers.PPL, 2, -5, "user-1"));
        await UpsertAsync(new CarrierLabelOffset(Carriers.PPL, -3, 4, "user-2"));

        var saved = (await ReadAllAsync()).Should().ContainSingle().Subject;
        (saved.OffsetXMm, saved.OffsetYMm, saved.ModifiedBy).Should().Be((-3, 4, "user-2"));
    }
}
