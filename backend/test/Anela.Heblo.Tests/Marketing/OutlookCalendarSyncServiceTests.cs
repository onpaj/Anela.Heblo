using System.Net;
using System.Text.Json;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Moq;

namespace Anela.Heblo.Tests.Marketing
{
    public class OutlookCalendarSyncServiceTests
    {
        private const string TestGroupEmail = "calendar@example.com";
        private const string FakeToken = "fake-token";

        private readonly Mock<ITokenAcquisition> _tokenAcquisition;

        public OutlookCalendarSyncServiceTests()
        {
            _tokenAcquisition = new Mock<ITokenAcquisition>();
            _tokenAcquisition
                .Setup(t => t.GetAccessTokenForAppAsync(It.IsAny<string>(), null, null))
                .ReturnsAsync(FakeToken);
        }

        private OutlookCalendarSyncService CreateService(FakeHttpMessageHandler handler)
        {
            var httpClient = new HttpClient(handler);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient("MicrosoftGraph")).Returns(httpClient);

            var options = Options.Create(new MarketingCalendarOptions
            {
                GroupEmail = TestGroupEmail,
                PushEnabled = true
            });

            return new OutlookCalendarSyncService(
                _tokenAcquisition.Object,
                factory.Object,
                options,
                NullLogger<OutlookCalendarSyncService>.Instance);
        }

        private static readonly DateTime DefaultStartDate = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime DefaultEndDate = new(2026, 3, 1, 11, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Builds a <see cref="MarketingAction"/> for testing.
        /// Pass <see cref="DateTime.MinValue"/> for <paramref name="endDate"/> to leave it null (no end date).
        /// </summary>
        private static MarketingAction BuildAction(
            string? outlookEventId = null,
            DateTime? startDate = null,
            DateTime? endDate = null)
        {
            var resolvedEndDate = endDate == DateTime.MinValue ? (DateTime?)null : (endDate ?? DefaultEndDate);

            return new MarketingAction
            {
                Id = 42,
                Title = "Spring Launch",
                Description = "Big spring launch event",
                ActionType = MarketingActionType.Launch,
                StartDate = startDate ?? DefaultStartDate,
                EndDate = resolvedEndDate,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow,
                CreatedByUserId = "user-1",
                OutlookEventId = outlookEventId
            };
        }

        // ─── CreateEventAsync ─────────────────────────────────────────────────────

        [Fact]
        public async Task CreateEventAsync_PostsCorrectUrlAndBody_WhenCalled()
        {
            // Arrange
            var responseJson = JsonSerializer.Serialize(new { id = "evt-123" });
            var handler = new FakeHttpMessageHandler(HttpStatusCode.Created, responseJson);
            var service = CreateService(handler);
            var action = BuildAction();

            // Act
            await service.CreateEventAsync(action, CancellationToken.None);

            // Assert — URL contains mailbox UPN and /calendar/events
            handler.LastRequestUri.Should().NotBeNull();
            handler.LastRequestUri!.ToString().Should().Contain(Uri.EscapeDataString(TestGroupEmail));
            handler.LastRequestUri.ToString().Should().Contain("/calendar/events");

            // Assert — HTTP method
            handler.LastMethod.Should().Be(HttpMethod.Post);

            // Assert — body contains expected fields
            handler.LastRequestBody.Should().Contain("Spring Launch");
            handler.LastRequestBody.Should().Contain("Big spring launch event");
            handler.LastRequestBody.Should().Contain("Launch");

            // Assert — start/end are ISO 8601
            handler.LastRequestBody.Should().Contain("2026-03-01T09:00:00");
            handler.LastRequestBody.Should().Contain("2026-03-01T11:00:00");

            // Assert — timezone
            handler.LastRequestBody.Should().Contain("Europe/Prague");
        }

        [Fact]
        public async Task CreateEventAsync_ReturnsEventId_OnSuccess()
        {
            // Arrange
            var responseJson = JsonSerializer.Serialize(new { id = "evt-123" });
            var handler = new FakeHttpMessageHandler(HttpStatusCode.Created, responseJson);
            var service = CreateService(handler);
            var action = BuildAction();

            // Act
            var result = await service.CreateEventAsync(action, CancellationToken.None);

            // Assert
            result.Should().Be("evt-123");
        }

        [Fact]
        public async Task CreateEventAsync_ThrowsOutlookCalendarSyncException_OnGraphError()
        {
            // Arrange
            var handler = new FakeHttpMessageHandler(HttpStatusCode.Forbidden, "{\"error\":\"Forbidden\"}");
            var service = CreateService(handler);
            var action = BuildAction();

            // Act
            var act = async () => await service.CreateEventAsync(action, CancellationToken.None);

            // Assert
            var ex = await act.Should().ThrowAsync<OutlookCalendarSyncException>();
            ex.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task CreateEventAsync_UsesStartDatePlusOneHour_WhenEndDateIsNull()
        {
            // Arrange
            var responseJson = JsonSerializer.Serialize(new { id = "evt-999" });
            var handler = new FakeHttpMessageHandler(HttpStatusCode.Created, responseJson);
            var service = CreateService(handler);
            var action = BuildAction(
                startDate: new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
                endDate: DateTime.MinValue); // sentinel: no end date

            // Act
            await service.CreateEventAsync(action, CancellationToken.None);

            // Assert — end should be start + 1 hour
            handler.LastRequestBody.Should().Contain("2026-04-01T11:00:00");
        }

        // ─── UpdateEventAsync ─────────────────────────────────────────────────────

        [Fact]
        public async Task UpdateEventAsync_PatchesToCorrectUrl()
        {
            // Arrange
            var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "{}");
            var service = CreateService(handler);
            var action = BuildAction(outlookEventId: "evt-to-update");

            // Act
            await service.UpdateEventAsync(action, CancellationToken.None);

            // Assert
            handler.LastMethod.Should().Be(HttpMethod.Patch);
            handler.LastRequestUri!.ToString().Should().Contain("evt-to-update");
            handler.LastRequestUri.ToString().Should().Contain("/calendar/events/");
        }

        [Fact]
        public async Task UpdateEventAsync_ThrowsOutlookCalendarSyncException_OnGraphError()
        {
            // Arrange
            var handler = new FakeHttpMessageHandler(HttpStatusCode.NotFound, "{\"error\":\"NotFound\"}");
            var service = CreateService(handler);
            var action = BuildAction(outlookEventId: "evt-missing");

            // Act
            var act = async () => await service.UpdateEventAsync(action, CancellationToken.None);

            // Assert
            var ex = await act.Should().ThrowAsync<OutlookCalendarSyncException>();
            ex.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        // ─── DeleteEventAsync ─────────────────────────────────────────────────────

        [Fact]
        public async Task DeleteEventAsync_DeletesCorrectUrl()
        {
            // Arrange
            var handler = new FakeHttpMessageHandler(HttpStatusCode.NoContent, string.Empty);
            var service = CreateService(handler);

            // Act
            await service.DeleteEventAsync("evt-to-delete", CancellationToken.None);

            // Assert
            handler.LastMethod.Should().Be(HttpMethod.Delete);
            handler.LastRequestUri!.ToString().Should().Contain("evt-to-delete");
            handler.LastRequestUri.ToString().Should().Contain("/calendar/events/");
        }

        [Fact]
        public async Task DeleteEventAsync_ThrowsOutlookCalendarSyncException_OnGraphError()
        {
            // Arrange
            var handler = new FakeHttpMessageHandler(HttpStatusCode.Forbidden, "{\"error\":\"Forbidden\"}");
            var service = CreateService(handler);

            // Act
            var act = async () => await service.DeleteEventAsync("evt-403", CancellationToken.None);

            // Assert
            var ex = await act.Should().ThrowAsync<OutlookCalendarSyncException>();
            ex.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // ─── ListEventsAsync ──────────────────────────────────────────────────────

        [Fact]
        public async Task ListEventsAsync_ParsesResponseCorrectly()
        {
            // Arrange
            var graphResponse = new
            {
                value = new[]
                {
                    new
                    {
                        id = "evt-a",
                        subject = "Promotion Week",
                        body = new { content = "Body text", contentType = "text" },
                        start = new { dateTime = "2026-04-01T08:00:00.0000000", timeZone = "UTC" },
                        end = new { dateTime = "2026-04-07T18:00:00.0000000", timeZone = "UTC" },
                        categories = new[] { "Promotion" }
                    },
                    new
                    {
                        id = "evt-b",
                        subject = "Brand Campaign",
                        body = new { content = string.Empty, contentType = "text" },
                        start = new { dateTime = "2026-04-10T08:00:00.0000000", timeZone = "UTC" },
                        end = new { dateTime = "2026-04-10T18:00:00.0000000", timeZone = "UTC" },
                        categories = new[] { "Campaign" }
                    }
                }
            };

            var responseJson = JsonSerializer.Serialize(graphResponse);
            var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, responseJson);
            var service = CreateService(handler);

            var from = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
            var to = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc);

            // Act
            var result = await service.ListEventsAsync(from, to, CancellationToken.None);

            // Assert — URL uses calendarView endpoint with date-range query params
            handler.LastRequestUri.Should().NotBeNull();
            handler.LastRequestUri!.ToString().Should().Contain("calendarView");
            handler.LastRequestUri.ToString().Should().Contain("startDateTime=");
            handler.LastRequestUri.ToString().Should().Contain("endDateTime=");

            result.Should().HaveCount(2);

            result[0].Id.Should().Be("evt-a");
            result[0].Subject.Should().Be("Promotion Week");
            result[0].BodyText.Should().Be("Body text");
            result[0].Categories.Should().Contain("Promotion");

            result[1].Id.Should().Be("evt-b");
            result[1].Subject.Should().Be("Brand Campaign");
        }
    }
}
