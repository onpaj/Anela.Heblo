using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Domain.Features.Marketing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

namespace Anela.Heblo.Application.Features.Marketing.Services
{
    // PushEnabled flag is enforced by the caller (handler) before invoking this service.
    // See Task 3 for the guard at the handler level.
    public class OutlookCalendarSyncService : IOutlookCalendarSync
    {
        private readonly ITokenAcquisition _tokenAcquisition;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly MarketingCalendarOptions _options;
        private readonly ILogger<OutlookCalendarSyncService> _logger;

        private const string GraphScope = "https://graph.microsoft.com/.default";
        private const string CalendarEventsBaseUrl = "https://graph.microsoft.com/v1.0/users/{0}/calendar/events";
        private const string CalendarViewBaseUrl = "https://graph.microsoft.com/v1.0/users/{0}/calendarView";
        private const string TimeZone = "Europe/Prague";
        private const int MaxResponseBodyLength = 500;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public OutlookCalendarSyncService(
            ITokenAcquisition tokenAcquisition,
            IHttpClientFactory httpClientFactory,
            IOptions<MarketingCalendarOptions> options,
            ILogger<OutlookCalendarSyncService> logger)
        {
            _tokenAcquisition = tokenAcquisition;
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<string> CreateEventAsync(MarketingAction action, CancellationToken ct)
        {
            _logger.LogDebug("Creating Outlook event for marketing action {ActionId} in mailbox {Mailbox}", action.Id, _options.MailboxUpn);

            var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphScope);
            using var client = _httpClientFactory.CreateClient("MicrosoftGraph");

            var url = BuildBaseUrl();
            var body = BuildEventBody(action);

            var request = CreateRequest(HttpMethod.Post, url, token);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                await ThrowCalendarSyncException(response, "CreateEvent", ct);
            }

            var stream = await response.Content.ReadAsStreamAsync(ct);
            var created = await JsonSerializer.DeserializeAsync<OutlookEventIdPayload>(stream, JsonOptions, ct)
                ?? throw new InvalidOperationException("Graph CreateEvent response deserialised to null.");

            _logger.LogInformation("Created Outlook event {EventId} for marketing action {ActionId}", created.Id, action.Id);
            return created.Id;
        }

        public async Task UpdateEventAsync(MarketingAction action, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(action.OutlookEventId))
                throw new ArgumentException("OutlookEventId must be set before calling UpdateEventAsync.", nameof(action));

            _logger.LogDebug("Updating Outlook event {EventId} for marketing action {ActionId}", action.OutlookEventId, action.Id);

            var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphScope);
            using var client = _httpClientFactory.CreateClient("MicrosoftGraph");

            var url = $"{BuildBaseUrl()}/{action.OutlookEventId}";
            var body = BuildEventBody(action);

            var request = CreateRequest(HttpMethod.Patch, url, token);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                await ThrowCalendarSyncException(response, "UpdateEvent", ct);
            }

            _logger.LogInformation("Updated Outlook event {EventId} for marketing action {ActionId}", action.OutlookEventId, action.Id);
        }

        public async Task DeleteEventAsync(string outlookEventId, CancellationToken ct)
        {
            _logger.LogDebug("Deleting Outlook event {EventId}", outlookEventId);

            var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphScope);
            using var client = _httpClientFactory.CreateClient("MicrosoftGraph");

            var url = $"{BuildBaseUrl()}/{outlookEventId}";
            var request = CreateRequest(HttpMethod.Delete, url, token);

            var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                await ThrowCalendarSyncException(response, "DeleteEvent", ct);
            }

            _logger.LogInformation("Deleted Outlook event {EventId}", outlookEventId);
        }

        public async Task<IReadOnlyList<OutlookEventDto>> ListEventsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
        {
            _logger.LogDebug("Listing Outlook events from {From} to {To} in mailbox {Mailbox}", fromUtc, toUtc, _options.MailboxUpn);

            var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphScope);
            using var client = _httpClientFactory.CreateClient("MicrosoftGraph");

            var select = "id,subject,body,start,end,categories";
            var calendarViewBase = string.Format(CalendarViewBaseUrl, Uri.EscapeDataString(_options.MailboxUpn));
            var url = $"{calendarViewBase}?startDateTime={fromUtc:O}&endDateTime={toUtc:O}&$select={select}";

            var request = CreateRequest(HttpMethod.Get, url, token);
            var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                await ThrowCalendarSyncException(response, "ListEvents", ct);
            }

            var stream = await response.Content.ReadAsStreamAsync(ct);
            var collection = await JsonSerializer.DeserializeAsync<GraphEventCollection>(stream, JsonOptions, ct)
                ?? throw new InvalidOperationException("Graph ListEvents response deserialised to null.");

            return collection.Value.AsReadOnly();
        }

        private string BuildBaseUrl() =>
            string.Format(CalendarEventsBaseUrl, Uri.EscapeDataString(_options.MailboxUpn));

        private static string BuildEventBody(MarketingAction action)
        {
            var endDate = action.EndDate ?? action.StartDate.AddHours(1);

            var bodyObj = new
            {
                subject = action.Title,
                body = new
                {
                    contentType = "text",
                    content = action.Description ?? string.Empty
                },
                start = new
                {
                    dateTime = action.StartDate.ToString("O"),
                    timeZone = TimeZone
                },
                end = new
                {
                    dateTime = endDate.ToString("O"),
                    timeZone = TimeZone
                },
                categories = new[] { action.ActionType.ToString() }
            };

            return JsonSerializer.Serialize(bodyObj);
        }

        private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string token)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return request;
        }

        private async Task ThrowCalendarSyncException(HttpResponseMessage response, string operation, CancellationToken ct)
        {
            var rawBody = await response.Content.ReadAsStringAsync(ct);
            var truncatedBody = rawBody.Length > MaxResponseBodyLength
                ? rawBody[..MaxResponseBodyLength]
                : rawBody;

            _logger.LogError(
                "Graph {Operation} failed with status {StatusCode}. Response: {GraphResponse}",
                operation,
                (int)response.StatusCode,
                truncatedBody);

            throw new OutlookCalendarSyncException(
                response.StatusCode,
                truncatedBody,
                $"Graph {operation} failed with status {(int)response.StatusCode} {response.StatusCode}.");
        }
    }
}
