using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anela.Heblo.Adapters.Microsoft365;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Application.Features.Marketing.Infrastructure;
using Anela.Heblo.Application.Features.Marketing.UseCases.ImportFromOutlook;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Tests.Domain.Marketing;
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
        private const string TestGroupId = "calendar@example.com";
        private const string FakeToken = "fake-token";

        private readonly Mock<ITokenAcquisition> _tokenAcquisition;
        private readonly Mock<IMarketingCategoryMapper> _mapperMock;

        public OutlookCalendarSyncServiceTests()
        {
            _tokenAcquisition = new Mock<ITokenAcquisition>();
            _tokenAcquisition
                .Setup(t => t.GetAccessTokenForAppAsync(It.IsAny<string>(), null, null))
                .ReturnsAsync(FakeToken);

            _mapperMock = new Mock<IMarketingCategoryMapper>();
            _mapperMock
                .Setup(m => m.MapToOutlookCategory(It.IsAny<MarketingActionType>()))
                .Returns((MarketingActionType t) => t.ToString());
        }

        private OutlookCalendarSyncService CreateService(FakeHttpMessageHandler handler)
        {
            var httpClient = new HttpClient(handler);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient("MicrosoftGraph")).Returns(httpClient);

            var options = Options.Create(new MarketingCalendarOptions
            {
                GroupId = TestGroupId,
                PushEnabled = true
            });

            return new OutlookCalendarSyncService(
                _tokenAcquisition.Object,
                factory.Object,
                options,
                _mapperMock.Object,
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

            return new MarketingActionTestBuilder()
                .WithId(42)
                .WithTitle("Spring Launch")
                .WithDescription("Big spring launch event")
                .WithActionType(MarketingActionType.Newsletter)
                .WithStartDate(startDate ?? DefaultStartDate)
                .WithEndDate(resolvedEndDate)
                .WithCreatedAt(DateTime.UtcNow)
                .WithModifiedAt(DateTime.UtcNow)
                .WithCreatedBy("user-1")
                .WithOutlookEventId(outlookEventId)
                .Build();
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
            handler.LastRequestUri!.ToString().Should().Contain(Uri.EscapeDataString(TestGroupId));
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

            // Assert — the declared zone matches the (UTC) values actually sent.
            // See CreateEventAsync_SendsZonelessDateTimeAndDeclaresTheZoneSeparately.
            handler.LastRequestBody.Should().Contain("UTC");
            handler.LastRequestBody.Should().NotContain("Europe/Prague");
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

        [Fact]
        public async Task CreateEventAsync_ForDateOnlyAction_SendsGraphsExclusiveEnd()
        {
            // Arrange — an all-day action as Heblo stores it: inclusive, midnight to midnight
            var responseJson = JsonSerializer.Serialize(new { id = "evt-allday" });
            var handler = new FakeHttpMessageHandler(HttpStatusCode.Created, responseJson);
            var service = CreateService(handler);
            var action = BuildAction(
                startDate: new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc),
                endDate: new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc));

            // Act
            await service.CreateEventAsync(action, CancellationToken.None);

            // Assert — Graph's end is exclusive, so a one-day action ends at the next midnight
            using var body = JsonDocument.Parse(handler.LastRequestBody!);
            body.RootElement.GetProperty("start").GetProperty("dateTime").GetString()
                .Should().StartWith("2026-09-18T00:00:00");
            body.RootElement.GetProperty("end").GetProperty("dateTime").GetString()
                .Should().StartWith("2026-09-19T00:00:00");
            // ...and Outlook only treats that span as all-day if the flag says so
            body.RootElement.GetProperty("isAllDay").GetBoolean().Should().BeTrue();
        }

        [Fact]
        public async Task CreateEventAsync_ForMultiDayDateOnlyAction_SendsGraphsExclusiveEnd()
        {
            // Arrange — 7.–9. 9. inclusive
            var responseJson = JsonSerializer.Serialize(new { id = "evt-allday-multi" });
            var handler = new FakeHttpMessageHandler(HttpStatusCode.Created, responseJson);
            var service = CreateService(handler);
            var action = BuildAction(
                startDate: new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc),
                endDate: new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc));

            // Act
            await service.CreateEventAsync(action, CancellationToken.None);

            // Assert
            using var body = JsonDocument.Parse(handler.LastRequestBody!);
            body.RootElement.GetProperty("end").GetProperty("dateTime").GetString()
                .Should().StartWith("2026-09-10T00:00:00");
            body.RootElement.GetProperty("isAllDay").GetBoolean().Should().BeTrue();
        }

        [Fact]
        public async Task CreateEventAsync_ForTimedAction_SendsItsEndUnchanged()
        {
            // Arrange — a timed meeting must not gain a day
            var responseJson = JsonSerializer.Serialize(new { id = "evt-timed" });
            var handler = new FakeHttpMessageHandler(HttpStatusCode.Created, responseJson);
            var service = CreateService(handler);
            var action = BuildAction(
                startDate: new DateTime(2026, 9, 1, 7, 0, 0, DateTimeKind.Utc),
                endDate: new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Utc));

            // Act
            await service.CreateEventAsync(action, CancellationToken.None);

            // Assert
            using var body = JsonDocument.Parse(handler.LastRequestBody!);
            body.RootElement.GetProperty("end").GetProperty("dateTime").GetString()
                .Should().StartWith("2026-09-01T08:30:00");
            body.RootElement.GetProperty("isAllDay").GetBoolean().Should().BeFalse();
        }

        [Theory]
        // single-day all-day
        [InlineData("2026-09-18T00:00:00", "2026-09-18T00:00:00")]
        // multi-day all-day
        [InlineData("2026-09-07T00:00:00", "2026-09-09T00:00:00")]
        // timed, same day
        [InlineData("2026-09-01T07:00:00", "2026-09-01T08:30:00")]
        // timed, starting at midnight but ending mid-day — not an all-day event
        [InlineData("2026-09-01T00:00:00", "2026-09-01T16:00:00")]
        public async Task CreateEventAsync_ThenImportingWhatWasSent_ReproducesTheOriginalDates(
            string startText,
            string endText)
        {
            // Arrange — the two halves of the conversion are written independently, so
            // only a round trip proves they agree on what an all-day event looks like.
            var start = DateTime.Parse(startText, null, DateTimeStyles.RoundtripKind);
            var end = DateTime.Parse(endText, null, DateTimeStyles.RoundtripKind);
            var handler = new FakeHttpMessageHandler(
                HttpStatusCode.Created,
                JsonSerializer.Serialize(new { id = "evt-roundtrip" }));
            var service = CreateService(handler);
            var action = BuildAction(startDate: start, endDate: end);

            // Act — export to Graph's wire format, then import that exact payload back.
            // Graph echoes the payload with the id it assigned, which the import requires.
            await service.CreateEventAsync(action, CancellationToken.None);
            var sentPayload = JsonNode.Parse(handler.LastRequestBody!)!.AsObject();
            sentPayload["id"] = "evt-roundtrip";
            var sentEvent = sentPayload.Deserialize<OutlookEventDto>()!;
            var reimported = OutlookEventImportMapper.BuildAction(
                sentEvent,
                new SyncActor("user-1", "Import User"),
                DateTime.UtcNow,
                MarketingActionType.Newsletter);

            // Assert
            reimported.StartDate.Should().Be(start);
            reimported.EndDate.Should().Be(end);
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

        // ─── GetEventAsync ────────────────────────────────────────────────────────

        [Fact]
        public async Task GetEventAsync_WhenFound_ReturnsParsedEvent()
        {
            // Arrange
            var graphResponse = new
            {
                id = "evt-a",
                subject = "Promotion Week",
                body = new { content = "Body text", contentType = "text" },
                start = new { dateTime = "2026-04-01T08:00:00.0000000", timeZone = "UTC" },
                end = new { dateTime = "2026-04-07T18:00:00.0000000", timeZone = "UTC" },
                categories = new[] { "Promotion" }
            };
            var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, JsonSerializer.Serialize(graphResponse));
            var service = CreateService(handler);

            // Act
            var result = await service.GetEventAsync("evt-a", CancellationToken.None);

            // Assert
            handler.LastMethod.Should().Be(HttpMethod.Get);
            handler.LastRequestUri!.ToString().Should().Contain("/calendar/events/evt-a");
            handler.LastRequestUri.ToString().Should().Contain("$select=");
            result.Should().NotBeNull();
            result!.Id.Should().Be("evt-a");
            result.Subject.Should().Be("Promotion Week");
            result.Categories.Should().Contain("Promotion");
        }

        [Fact]
        public async Task GetEventAsync_WhenNotFound_ReturnsNull()
        {
            // Arrange
            var handler = new FakeHttpMessageHandler(HttpStatusCode.NotFound, "{\"error\":{\"code\":\"ErrorItemNotFound\"}}");
            var service = CreateService(handler);

            // Act
            var result = await service.GetEventAsync("evt-gone", CancellationToken.None);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task GetEventAsync_WhenServerError_ThrowsOutlookCalendarSyncException()
        {
            // Arrange
            var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "{\"error\":{\"code\":\"Boom\"}}");
            var service = CreateService(handler);

            // Act
            var act = async () => await service.GetEventAsync("evt-x", CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<OutlookCalendarSyncException>();
        }

        [Fact]
        public async Task GetEventAsync_UsesAppToken()
        {
            // Arrange
            var handler = new FakeHttpMessageHandler(HttpStatusCode.NotFound, "{}");
            var service = CreateService(handler);

            // Act
            await service.GetEventAsync("evt-x", CancellationToken.None);

            // Assert
            _tokenAcquisition.Verify(
                t => t.GetAccessTokenForAppAsync("https://graph.microsoft.com/.default", null, null),
                Times.Once);
            handler.LastRequestHeaders!.Authorization!.Parameter.Should().Be(FakeToken);
        }

        // ─── BuildEventBody (mapper integration) ──────────────────────────────────

        [Fact]
        public async Task BuildEventBody_UsesMapperOutlookCategory_WhenConfigured()
        {
            // Arrange — use a plain ASCII name to avoid JSON unicode-escape differences
            _mapperMock
                .Setup(m => m.MapToOutlookCategory(MarketingActionType.PR))
                .Returns("PR-Summer");

            var responseJson = JsonSerializer.Serialize(new { id = "evt-x" });
            var handler = new FakeHttpMessageHandler(HttpStatusCode.Created, responseJson);
            var service = CreateService(handler);
            var action = new MarketingActionTestBuilder()
                .WithId(1)
                .WithTitle("Test")
                .WithDescription(string.Empty)
                .WithActionType(MarketingActionType.PR)
                .WithStartDate(DefaultStartDate)
                .WithEndDate(DefaultEndDate)
                .WithCreatedAt(DateTime.UtcNow)
                .WithModifiedAt(DateTime.UtcNow)
                .WithCreatedBy("user-1")
                .Build();

            // Act
            await service.CreateEventAsync(action, CancellationToken.None);

            // Assert
            handler.LastRequestBody.Should().Contain("PR-Summer");
        }

        [Fact]
        public async Task BuildEventBody_FallsBackToToString_WhenMapperReturnsEnumName()
        {
            // Arrange
            _mapperMock
                .Setup(m => m.MapToOutlookCategory(MarketingActionType.PR))
                .Returns("Campaign");

            var responseJson = JsonSerializer.Serialize(new { id = "evt-x" });
            var handler = new FakeHttpMessageHandler(HttpStatusCode.Created, responseJson);
            var service = CreateService(handler);
            var action = new MarketingActionTestBuilder()
                .WithId(1)
                .WithTitle("Test")
                .WithDescription(string.Empty)
                .WithActionType(MarketingActionType.PR)
                .WithStartDate(DefaultStartDate)
                .WithEndDate(DefaultEndDate)
                .WithCreatedAt(DateTime.UtcNow)
                .WithModifiedAt(DateTime.UtcNow)
                .WithCreatedBy("user-1")
                .Build();

            // Act
            await service.CreateEventAsync(action, CancellationToken.None);

            // Assert
            handler.LastRequestBody.Should().Contain("Campaign");
        }

        [Fact]
        public async Task BuildEventBody_UsesMapperOutput_NotDirectToString()
        {
            // Arrange
            _mapperMock
                .Setup(m => m.MapToOutlookCategory(MarketingActionType.PR))
                .Returns("FOOBAR");

            var responseJson = JsonSerializer.Serialize(new { id = "evt-x" });
            var handler = new FakeHttpMessageHandler(HttpStatusCode.Created, responseJson);
            var service = CreateService(handler);
            var action = new MarketingActionTestBuilder()
                .WithId(1)
                .WithTitle("Test")
                .WithDescription(string.Empty)
                .WithActionType(MarketingActionType.PR)
                .WithStartDate(DefaultStartDate)
                .WithEndDate(DefaultEndDate)
                .WithCreatedAt(DateTime.UtcNow)
                .WithModifiedAt(DateTime.UtcNow)
                .WithCreatedBy("user-1")
                .Build();

            // Act
            await service.CreateEventAsync(action, CancellationToken.None);

            // Assert
            handler.LastRequestBody.Should().Contain("FOOBAR");
            handler.LastRequestBody.Should().NotContain("\"Campaign\"");
        }

        // ─── Graph time-zone contract ─────────────────────────────────────────────
        //
        // Graph's dateTimeTimeZone.dateTime is a *zone-less* wall-clock string; the zone
        // travels separately in dateTimeTimeZone.timeZone. Emitting an ISO string with a
        // "Z" designator alongside a timeZone label states two different things at once,
        // and for an all-day event — which Graph requires to start at midnight *in the
        // declared zone* — the two readings disagree by the UTC offset.

        [Fact]
        public async Task CreateEventAsync_SendsZonelessDateTimeAndDeclaresTheZoneSeparately()
        {
            // Arrange
            var responseJson = JsonSerializer.Serialize(new { id = "evt-tz" });
            var handler = new FakeHttpMessageHandler(HttpStatusCode.Created, responseJson);
            var service = CreateService(handler);
            var action = BuildAction();

            // Act
            await service.CreateEventAsync(action, CancellationToken.None);

            // Assert — exact wire shape, not a substring: no "Z", no offset, zone stated once.
            var body = JsonNode.Parse(handler.LastRequestBody)!;
            body["start"]!["dateTime"]!.GetValue<string>().Should().Be("2026-03-01T09:00:00.0000000");
            body["start"]!["timeZone"]!.GetValue<string>().Should().Be("UTC");
            body["end"]!["dateTime"]!.GetValue<string>().Should().Be("2026-03-01T11:00:00.0000000");
            body["end"]!["timeZone"]!.GetValue<string>().Should().Be("UTC");
        }

        [Fact]
        public async Task CreateEventAsync_ForAllDayAction_SendsMidnightInTheDeclaredZone()
        {
            // Arrange — Graph rejects an all-day event that does not begin at midnight in
            // the zone it declares. "00:00Z" labelled "Europe/Prague" reads as 02:00 local.
            var responseJson = JsonSerializer.Serialize(new { id = "evt-allday-tz" });
            var handler = new FakeHttpMessageHandler(HttpStatusCode.Created, responseJson);
            var service = CreateService(handler);
            var action = BuildAction(
                startDate: new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc),
                endDate: new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc));

            // Act
            await service.CreateEventAsync(action, CancellationToken.None);

            // Assert
            var body = JsonNode.Parse(handler.LastRequestBody)!;
            body["isAllDay"]!.GetValue<bool>().Should().BeTrue();

            var zone = body["start"]!["timeZone"]!.GetValue<string>();
            zone.Should().Be("UTC");

            var start = DateTime.Parse(
                body["start"]!["dateTime"]!.GetValue<string>(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
            var end = DateTime.Parse(
                body["end"]!["dateTime"]!.GetValue<string>(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);

            start.TimeOfDay.Should().Be(TimeSpan.Zero, "Graph requires all-day events to start at midnight in the declared zone");
            end.TimeOfDay.Should().Be(TimeSpan.Zero, "Graph requires all-day events to end at midnight in the declared zone");
            start.Should().Be(new DateTime(2026, 9, 18, 0, 0, 0));
            end.Should().Be(new DateTime(2026, 9, 21, 0, 0, 0));
        }

        [Fact]
        public async Task CreateEventAsync_ForNonUtcStartDate_NormalizesToUtcBeforeSending()
        {
            // Arrange — a DateTime that is not already UTC must be converted, not relabelled,
            // otherwise the declared "UTC" zone would misdescribe the wall-clock value.
            var responseJson = JsonSerializer.Serialize(new { id = "evt-local" });
            var handler = new FakeHttpMessageHandler(HttpStatusCode.Created, responseJson);
            var service = CreateService(handler);

            var utcStart = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);
            var action = BuildAction(
                startDate: utcStart.ToLocalTime(),
                endDate: utcStart.AddHours(2).ToLocalTime());

            // Act
            await service.CreateEventAsync(action, CancellationToken.None);

            // Assert
            var body = JsonNode.Parse(handler.LastRequestBody)!;
            body["start"]!["dateTime"]!.GetValue<string>().Should().Be("2026-03-01T09:00:00.0000000");
            body["end"]!["dateTime"]!.GetValue<string>().Should().Be("2026-03-01T11:00:00.0000000");
        }

        [Fact]
        public async Task ListEventsAsync_AsksGraphToAnswerInUtc()
        {
            // Arrange — without this header Graph answers in the mailbox's default zone,
            // and StartUtc/EndUtc would read those local digits as if they were UTC.
            var responseJson = JsonSerializer.Serialize(new { value = Array.Empty<object>() });
            var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, responseJson);
            var service = CreateService(handler);

            // Act
            await service.ListEventsAsync(
                new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc),
                CancellationToken.None);

            // Assert
            handler.LastRequestHeaders.Should().NotBeNull();
            handler.LastRequestHeaders!.GetValues("Prefer")
                .Should().Contain("outlook.timezone=\"UTC\"");
        }

        [Fact]
        public async Task GetEventAsync_AsksGraphToAnswerInUtc()
        {
            // Arrange
            var responseJson = JsonSerializer.Serialize(new
            {
                id = "evt-1",
                subject = "S",
                start = new { dateTime = "2026-04-01T08:00:00.0000000", timeZone = "UTC" },
                end = new { dateTime = "2026-04-01T09:00:00.0000000", timeZone = "UTC" },
                categories = Array.Empty<string>()
            });
            var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, responseJson);
            var service = CreateService(handler);

            // Act
            await service.GetEventAsync("evt-1", CancellationToken.None);

            // Assert
            handler.LastRequestHeaders.Should().NotBeNull();
            handler.LastRequestHeaders!.GetValues("Prefer")
                .Should().Contain("outlook.timezone=\"UTC\"");
        }
    }
}
