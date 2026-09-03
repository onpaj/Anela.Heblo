using System;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Marketing.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Marketing.Services;

public class NoOpOutlookCalendarSyncTests
{
    private readonly NoOpOutlookCalendarSync _sync = new(NullLogger<NoOpOutlookCalendarSync>.Instance);

    [Fact]
    public async Task GetEventAsync_ThrowsInsteadOfReturningNull()
    {
        // Arrange
        const string outlookEventId = "evt-123";

        // Act
        Func<Task> act = () => _sync.GetEventAsync(outlookEventId, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ListEventsAsync_StillReturnsEmptyList()
    {
        // Arrange
        var fromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var result = await _sync.ListEventsAsync(fromUtc, toUtc, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}
