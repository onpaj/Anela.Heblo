using System.Globalization;
using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.GoogleAds.Reporting;

internal static class GoogleAdsChangeEventMapper
{
    private const string ChangeEventsMarker = "/changeEvents/";
    private const string KeywordCriterionType = "KEYWORD";
    private const string AccountLocalFormat = "yyyy-MM-dd HH:mm:ss";
    private static readonly string[] CriterionObjectNames = { "adGroupCriterion", "campaignCriterion" };
    private static readonly string[] ResourceNames = { "newResource", "oldResource" };
    private static readonly string[] DateTimeFormats = { "yyyy-MM-dd HH:mm:ss.FFFFFF", AccountLocalFormat };

    public static AdChangeEventRow Map(JsonElement row, TimeZoneInfo accountTimeZone, string? hebloUserEmail)
    {
        var userEmail = GoogleAdsJson.String(row, "changeEvent", "userEmail");
        var operation = GoogleAdsJson.String(row, "changeEvent", "resourceChangeOperation") ?? "UNKNOWN";
        var resourceType = GoogleAdsJson.String(row, "changeEvent", "changeResourceType") ?? "UNKNOWN";
        var (level, externalId) = IsNonKeywordCriterion(row)
            ? ((AdEntityLevel?)null, (string?)null)
            : GoogleAdsMappings.EntityRef(
                GoogleAdsJson.String(row, "changeEvent", "changeResourceName"), IsNegativeCriterion(row));

        return new AdChangeEventRow(
            ExternalEventId: EventId(GoogleAdsJson.RequiredString(row, "changeEvent", "resourceName")),
            OccurredAt: ToUtc(GoogleAdsJson.RequiredString(row, "changeEvent", "changeDateTime"), accountTimeZone),
            Actor: string.IsNullOrWhiteSpace(userEmail) ? null : userEmail,
            ActorKind: GoogleAdsMappings.ActorKind(
                GoogleAdsJson.String(row, "changeEvent", "clientType"), userEmail, hebloUserEmail),
            EntityLevel: level,
            EntityExternalId: externalId,
            ChangeType: $"{operation}:{resourceType}",
            OldValueJson: GoogleAdsJson.Raw(row, "changeEvent", "oldResource"),
            NewValueJson: GoogleAdsJson.Raw(row, "changeEvent", "newResource"));
    }

    /// <summary>
    /// Account-local wall-clock time to UTC. GetUtcOffset never throws: an ambiguous autumn hour
    /// resolves to standard time, a skipped spring hour to the standard offset.
    /// </summary>
    public static DateTimeOffset ToUtc(string accountLocal, TimeZoneInfo timeZone)
    {
        var parsed = DateTime.ParseExact(accountLocal, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None);
        var local = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, timeZone.GetUtcOffset(local)).ToUniversalTime();
    }

    public static string ToAccountLocal(DateTimeOffset instant, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTime(instant, timeZone).ToString(AccountLocalFormat, CultureInfo.InvariantCulture);

    private static string EventId(string resourceName)
    {
        var index = resourceName.IndexOf(ChangeEventsMarker, StringComparison.Ordinal);
        return index < 0 ? resourceName : resourceName[(index + ChangeEventsMarker.Length)..];
    }

    // old/new resources hold only changed fields, so "negative" is present on CREATE but may be absent on UPDATE/REMOVE.
    private static bool IsNegativeCriterion(JsonElement row) =>
        GoogleAdsJson.Bool(row, "changeEvent", "newResource", "adGroupCriterion", "negative")
        || GoogleAdsJson.Bool(row, "changeEvent", "oldResource", "adGroupCriterion", "negative");

    // campaign_criterion / ad_group_criterion also hold targeting (location, language, device, audience...).
    // Such a change keeps its row but must not be labelled as a keyword change. Payloads without any type
    // information (typical UPDATE/REMOVE) fall back to the keyword mapping (spec deviation 8).
    private static bool IsNonKeywordCriterion(JsonElement row) =>
        CriterionObjectNames.Any(name => ResourceNames.Any(resource =>
            GoogleAdsJson.Find(row, "changeEvent", resource, name) is { ValueKind: JsonValueKind.Object } criterion
            && IsNonKeywordPayload(criterion)));

    private static bool IsNonKeywordPayload(JsonElement criterion)
    {
        var type = GoogleAdsJson.String(criterion, "type");
        if (type is not null)
            return !string.Equals(type, KeywordCriterionType, StringComparison.Ordinal);

        return !criterion.TryGetProperty("keyword", out _)
            && criterion.EnumerateObject().Any(p => p.Value.ValueKind == JsonValueKind.Object);
    }
}
