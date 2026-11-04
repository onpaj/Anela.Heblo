using System.Net;
using System.Text.Json;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Persistence;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

/// <summary>
/// GET /api/meeting-tasks binds GetTranscriptListRequest from the query string, so these tests
/// go through real model binding: a default of true on the request is only worth anything if a
/// request that omits the parameter still binds to true.
/// </summary>
[Collection("WebApp")]
public class MeetingTasksListEndpointTests : IClassFixture<HebloWebApplicationFactory>, IAsyncLifetime
{
    private const string RecordingId = "rest-list-summary";
    private const string Summary = "Summary returned over REST";

    private readonly HebloWebApplicationFactory _factory;

    public MeetingTasksListEndpointTests(HebloWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (context.MeetingTranscripts.Any(x => x.PlaudRecordingId == RecordingId))
        {
            return;
        }

        context.MeetingTranscripts.Add(new MeetingTranscript
        {
            Id = Guid.NewGuid(),
            PlaudRecordingId = RecordingId,
            PlaudCreatedAt = new DateTime(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc),
            Subject = "REST meeting",
            Summary = Summary,
            RawTranscript = "raw",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc)
        });
        await context.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<JsonElement> GetListedMeetingAsync(string url)
    {
        var response = await _factory.CreateClient().GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("plaudRecordingId").GetString() == RecordingId)
            .Clone();
    }

    [Fact]
    public async Task List_ReturnsSummary_WhenIncludeSummaryIsOmitted()
    {
        // Act
        var item = await GetListedMeetingAsync("/api/meeting-tasks");

        // Assert
        item.GetProperty("summary").GetString().Should().Be(Summary);
    }

    [Fact]
    public async Task List_ReturnsNoSummary_WhenIncludeSummaryIsFalse()
    {
        // Act
        var item = await GetListedMeetingAsync("/api/meeting-tasks?IncludeSummary=false");

        // Assert
        item.GetProperty("summary").ValueKind.Should().Be(JsonValueKind.Null);
        item.GetProperty("subject").GetString().Should().Be("REST meeting");
    }
}
