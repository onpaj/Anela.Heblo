using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

public class RecurringJobStatusCheckerTests
{
    private readonly Mock<IRecurringJobConfigurationRepository> _repo = new();

    private RecurringJobStatusChecker CreateSut() =>
        new(_repo.Object, NullLogger<RecurringJobStatusChecker>.Instance);

    [Fact]
    public async Task IsJobEnabledAsync_ReturnsConfiguredValue_WhenRowExists()
    {
        // Arrange
        _repo
            .Setup(r => r.GetByJobNameAsync("job-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecurringJobConfiguration(
                jobName: "job-a",
                displayName: "Job A",
                description: "Test job",
                cronExpression: "0 * * * *",
                isEnabled: false,
                lastModifiedBy: "test"));
        var sut = CreateSut();

        // Act
        var result = await sut.IsJobEnabledAsync("job-a", CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsJobEnabledAsync_ReturnsTrue_WhenRowMissing_AndDefaultIfMissingIsTrue()
    {
        // Arrange
        _repo
            .Setup(r => r.GetByJobNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RecurringJobConfiguration?)null);
        var sut = CreateSut();

        // Act
        var result = await sut.IsJobEnabledAsync("missing-job", CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsJobEnabledAsync_ReturnsFalse_WhenRowMissing_AndDefaultIfMissingIsFalse()
    {
        // Arrange
        _repo
            .Setup(r => r.GetByJobNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RecurringJobConfiguration?)null);
        var sut = CreateSut();

        // Act
        var result = await sut.IsJobEnabledAsync(
            "missing-job",
            CancellationToken.None,
            defaultIfMissing: false);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsJobEnabledAsync_ReturnsTrue_WhenRepositoryThrows()
    {
        // Arrange
        _repo
            .Setup(r => r.GetByJobNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var sut = CreateSut();

        // Act
        var result = await sut.IsJobEnabledAsync("any-job", CancellationToken.None);

        // Assert — error path stays fail-open to avoid blocking critical jobs
        result.Should().BeTrue();
    }
}
