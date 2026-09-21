using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Application.Features.Marketing.Infrastructure;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Domain.Features.Marketing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace Anela.Heblo.Adapters.Microsoft365
{
    // PushEnabled flag is enforced by the caller (handler) before invoking this service.
    // See Task 3 for the guard at the handler level.
    public class OutlookCalendarSyncService : IOutlookCalendarSync
    {
        private readonly ITokenAcquisition _tokenAcquisition;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly MarketingCalendarOptions _options;
        private readonly IMarketingCategoryMapper _mapper;
        private readonly ILogger<OutlookCalendarSyncService> _logger;

        private const string GraphScope = "https://graph.microsoft.com/.default";
        private const string DelegatedGraphScope = "https://graph.microsoft.com/Group.ReadWrite.All";
        private const string CalendarEventsBaseUrl = "https://graph.microsoft.com/v1.0/groups/{0}/calendar/events";
        private const string CalendarViewBaseUrl = "https://graph.microsoft.com/v1.0/groups/{0}/calendarView";
        // Graph's dateTimeTimeZone.dateTime is a zone-less wall-clock string and the zone travels
        // separately in dateTimeTimeZone.timeZone. Heblo stores marketing action dates as UTC, so
        // UTC is the zone we declare on write and the zone we ask for on read — naming a different
        // zone here without converting the value would misdescribe every timestamp, and Graph
        // rejects an all-day event that is not midnight in the zone it declares.
        private const string GraphTimeZone = "UTC";
        private const string GraphTimeZonePreference = "outlook.timezone=\"UTC\"";
        // Zone-less round-trip format: the "Z" designator of "O" would contradict GraphTimeZone.
        private const string GraphDateTimeFormat = "yyyy-MM-ddTHH:mm:ss.fffffff";
        // isAllDay drives the exclusive→inclusive end conversion on import; without it
        // Graph omits the flag and every all-day event is stored one day too long.
        private const string EventSelect = "id,subject,body,start,end,isAllDay,categories";
        private const int MaxResponseBodyLength = 500;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public OutlookCalendarSyncService(
            ITokenAcquisition tokenAcquisition,
            IHttpClientFactory httpClientFactory,
            IOptions<MarketingCalendarOptions> options,
            IMarketingCategoryMapper mapper,
            ILogger<OutlookCalendarSyncService> logger)
        {
            _tokenAcquisition = tokenAcquisition;
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<string> CreateEventAsync(MarketingAction action, CancellationToken ct)
        {
            _logger.LogDebug("Creating Outlook event for marketing action {ActionId} in mailbox {Mailbox}", action.Id, _options.GroupId);

            var token = await GetDelegatedTokenAsync();
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

            var token = await GetDelegatedTokenAsync();
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

            var token = await GetDelegatedTokenAsync();
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
            _logger.LogDebug("Listing Outlook events from {From} to {To} in mailbox {Mailbox}", fromUtc, toUtc, _options.GroupId);

            var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphScope);
            using var client = _httpClientFactory.CreateClient("MicrosoftGraph");

            var calendarViewBase = string.Format(CalendarViewBaseUrl, Uri.EscapeDataString(_options.GroupId));
            var url = $"{calendarViewBase}?startDateTime={fromUtc:O}&endDateTime={toUtc:O}&$select={EventSelect}";

            var allEvents = new List<OutlookEventDto>();
            string? nextUrl = url;

            while (nextUrl is not null)
            {
                var request = CreateReadRequest(nextUrl, token);
                var response = await client.SendAsync(request, ct);

                if (!response.IsSuccessStatusCode)
                {
                    await ThrowCalendarSyncException(response, "ListEvents", ct);
                }

                var stream = await response.Content.ReadAsStreamAsync(ct);
                var collection = await JsonSerializer.DeserializeAsync<GraphEventCollection>(stream, JsonOptions, ct)
                    ?? throw new InvalidOperationException("Graph ListEvents response deserialised to null.");

                allEvents.AddRange(collection.Value);
                nextUrl = collection.NextLink;
            }

            return allEvents.AsReadOnly();
        }

        public async Task<OutlookEventDto?> GetEventAsync(string outlookEventId, CancellationToken ct)
        {
            _logger.LogDebug("Fetching Outlook event {EventId} in mailbox {Mailbox}", outlookEventId, _options.GroupId);

            var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphScope);
            using var client = _httpClientFactory.CreateClient("MicrosoftGraph");

            var url = $"{BuildBaseUrl()}/{Uri.EscapeDataString(outlookEventId)}?$select={EventSelect}";
            var request = CreateReadRequest(url, token);

            var response = await client.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogDebug("Outlook event {EventId} not found (404)", outlookEventId);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                await ThrowCalendarSyncException(response, "GetEvent", ct);
            }

            var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync<OutlookEventDto>(stream, JsonOptions, ct)
                ?? throw new InvalidOperationException("Graph GetEvent response deserialised to null.");
        }

        private async Task<string> GetDelegatedTokenAsync()
        {
            try
            {
                return await _tokenAcquisition.GetAccessTokenForUserAsync(new[] { DelegatedGraphScope });
            }
            catch (MsalUiRequiredException ex)
            {
                _logger.LogError(ex,
                    "User consent required for Graph scope {Scope}. Grant admin consent in Azure Portal for app {AppId}.",
                    DelegatedGraphScope, ex.Claims);
                throw new OutlookCalendarSyncException(
                    HttpStatusCode.Forbidden,
                    ex.Message,
                    $"Microsoft 365 consent required for scope {DelegatedGraphScope}. An admin must grant consent in Azure Portal.");
            }
        }

        private string BuildBaseUrl() =>
            string.Format(CalendarEventsBaseUrl, Uri.EscapeDataString(_options.GroupId));

        private string BuildEventBody(MarketingAction action)
        {
            var isAllDay = action.IsAllDay;
            var endDate = BuildGraphEnd(action, isAllDay);

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
                    dateTime = FormatGraphDateTime(action.StartDate),
                    timeZone = GraphTimeZone
                },
                end = new
                {
                    dateTime = FormatGraphDateTime(endDate),
                    timeZone = GraphTimeZone
                },
                isAllDay,
                categories = new[] { _mapper.MapToOutlookCategory(action.ActionType) }
            };

            return JsonSerializer.Serialize(bodyObj);
        }

        /// <summary>
        /// Heblo's EndDate is inclusive, Graph's end is exclusive, so an all-day action's
        /// last day has to be pushed as the following midnight or Outlook drops that day.
        /// </summary>
        private static DateTime BuildGraphEnd(MarketingAction action, bool isAllDay)
        {
            if (action.EndDate is null)
            {
                return action.StartDate.AddHours(1);
            }

            return isAllDay ? action.EndDate.Value.AddDays(1) : action.EndDate.Value;
        }

        /// <summary>
        /// Renders a timestamp the way Graph expects it: wall-clock digits with no designator,
        /// paired with <see cref="GraphTimeZone"/>. A value that is not already UTC is converted
        /// rather than relabelled, so the digits always match the zone we declare.
        /// </summary>
        private static string FormatGraphDateTime(DateTime value)
        {
            var utc = value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };

            return utc.ToString(GraphDateTimeFormat, CultureInfo.InvariantCulture);
        }

        private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string token)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return request;
        }

        /// <summary>
        /// Graph answers read requests in the mailbox's default time zone unless asked otherwise.
        /// <see cref="OutlookEventDto.StartUtc"/> designates a zone-less value as UTC, so that
        /// default has to be pinned or those local digits would be read as if they were UTC.
        /// </summary>
        private static HttpRequestMessage CreateReadRequest(string url, string token)
        {
            var request = CreateRequest(HttpMethod.Get, url, token);
            request.Headers.TryAddWithoutValidation("Prefer", GraphTimeZonePreference);
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
