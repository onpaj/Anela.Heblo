using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Catalog.Inventory;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class LotLabelCalibrationRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly LotLabelCalibrationRepository _repo;

    public LotLabelCalibrationRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"LotLabelCalibration_{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);
        _repo = new LotLabelCalibrationRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetAsync_ReturnsTheDefault_WhenNothingIsStored()
    {
        var calibration = await _repo.GetAsync();

        calibration.PitchDots.Should().Be(LotLabelCalibration.DefaultPitchDots);
        calibration.DriftDotsPer100Labels.Should().Be(LotLabelCalibration.DefaultDriftDotsPer100Labels);
    }

    [Fact]
    public async Task SaveAsync_ThenGetAsync_RoundTripsTheValues()
    {
        await _repo.SaveAsync(new LotLabelCalibration(152, 40, "admin"));

        var calibration = await _repo.GetAsync();

        calibration.PitchDots.Should().Be(152);
        calibration.DriftDotsPer100Labels.Should().Be(40);
        calibration.ModifiedBy.Should().Be("admin");
    }

    [Fact]
    public async Task GetAsync_ReturnsASnapshot_ThatALaterSaveDoesNotMutate()
    {
        // A caller that reads the calibration, derives a correction from it and saves the
        // result must still be able to see what the value used to be — the nudge handler
        // logs exactly that as the audit trail for an operator-initiated change. If the
        // read handed back the tracked entity, SaveAsync would rewrite it underneath the
        // caller and the "before" values would silently become the "after" ones.
        await _repo.SaveAsync(new LotLabelCalibration(148, 30, "admin"));

        var before = await _repo.GetAsync();
        await _repo.SaveAsync(new LotLabelCalibration(150, 80, "operator"));

        before.PitchDots.Should().Be(148);
        before.DriftDotsPer100Labels.Should().Be(30);
    }
}
