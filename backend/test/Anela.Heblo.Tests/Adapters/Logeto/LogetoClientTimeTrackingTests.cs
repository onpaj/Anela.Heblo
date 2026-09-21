using System.Net;
using System.Text;
using Anela.Heblo.Adapters.Logeto;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Tests.Adapters.Logeto;

/// <summary>
/// The break-insertion job decides whether a day is stale by comparing Revision values. If that
/// field ever stopped binding, every entry would read 0, every comparison would say "already fine",
/// and the job would silently do nothing while reporting a clean run — so the wire shape is pinned
/// here rather than assumed.
/// </summary>
public class LogetoClientTimeTrackingTests
{
    private const string TimeTrackingPayload = """
    {
      "continuationToken": null,
      "items": [
        {
          "guid": "8f1d5c1e-2b3a-4f5c-9d7e-1a2b3c4d5e6f",
          "person": "cccccccc-cccc-cccc-cccc-cccccccccccc",
          "date": "2026-09-07",
          "from": "2026-09-07T06:43:00",
          "to": "2026-09-07T11:30:00",
          "hours": "04:47",
          "activity": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
          "revision": 45609,
          "description": "Ruční výroba",
          "externalKey": "payroll-42",
          "billable": true,
          "location": null,
          "endLocation": null
        }
      ]
    }
    """;

    private static LogetoClient CreateClient(string payload)
    {
        var handler = new StubHandler(payload);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://logeto.test") };

        return new LogetoClient(
            httpClient,
            Options.Create(new LogetoOptions { AccessKey = "test-key" }),
            NullLogger<LogetoClient>.Instance);
    }

    [Fact]
    public async Task GetTimeTrackingAsync_BindsRevision_FromTheWirePayload()
    {
        // Arrange
        var client = CreateClient(TimeTrackingPayload);

        // Act
        var entries = await client.GetTimeTrackingAsync(
            new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 7), CancellationToken.None);

        // Assert
        entries.Should().ContainSingle();
        entries[0].Revision.Should().Be(45609);
    }

    [Fact]
    public async Task GetTimeTrackingAsync_BindsTheFieldsATouchResends()
    {
        // Arrange — a touch is a full replacement, so anything it resends must survive the read.
        var client = CreateClient(TimeTrackingPayload);

        // Act
        var entry = (await client.GetTimeTrackingAsync(
            new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 7), CancellationToken.None))[0];

        // Assert
        entry.Person.Should().Be(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        entry.Activity.Should().Be(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        entry.Date.Should().Be(new DateOnly(2026, 9, 7));
        entry.From.Should().NotBeNull();
        entry.To.Should().NotBeNull();
        entry.Description.Should().Be("Ruční výroba");
        entry.ExternalKey.Should().Be("payroll-42");
        entry.Billable.Should().BeTrue();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _payload;

        public StubHandler(string payload) => _payload = payload;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_payload, Encoding.UTF8, "application/json")
            });
    }
}
