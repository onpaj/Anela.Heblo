using Anela.Heblo.Adapters.GoogleAds.Reporting;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Reporting;

public sealed class GoogleAdsMappingsTests
{
    private const string Heblo = "heblo-ads@anela.cz";

    [Theory]
    [InlineData("ENABLED", AdEntityStatus.Enabled)]
    [InlineData("PAUSED", AdEntityStatus.Paused)]
    [InlineData("REMOVED", AdEntityStatus.Removed)]
    [InlineData("UNKNOWN", AdEntityStatus.Unknown)]
    [InlineData(null, AdEntityStatus.Unknown)]
    public void maps_statuses(string? api, AdEntityStatus expected) =>
        GoogleAdsMappings.Status(api).Should().Be(expected);

    [Theory]
    [InlineData("EXACT", KeywordMatchType.Exact)]
    [InlineData("PHRASE", KeywordMatchType.Phrase)]
    [InlineData("BROAD", KeywordMatchType.Broad)]
    [InlineData("NEAR_EXACT", null)]
    [InlineData(null, null)]
    public void maps_keyword_match_types(string? api, KeywordMatchType? expected) =>
        GoogleAdsMappings.KeywordMatch(api).Should().Be(expected);

    [Theory]
    [InlineData("EXACT", KeywordMatchType.Exact)]
    [InlineData("NEAR_EXACT", KeywordMatchType.Exact)]
    [InlineData("PHRASE", KeywordMatchType.Phrase)]
    [InlineData("NEAR_PHRASE", KeywordMatchType.Phrase)]
    [InlineData("BROAD", KeywordMatchType.Broad)]
    [InlineData("AI_MAX", null)]
    [InlineData("PERFORMANCE_MAX", null)]
    [InlineData(null, null)]
    public void maps_search_term_match_types(string? api, KeywordMatchType? expected) =>
        GoogleAdsMappings.SearchTermMatch(api).Should().Be(expected);

    [Theory]
    [InlineData("GOOGLE_ADS_RECOMMENDATIONS", null, AdChangeActorKind.PlatformAutomation)]
    [InlineData("GOOGLE_ADS_RECOMMENDATIONS_SUBSCRIPTION", null, AdChangeActorKind.PlatformAutomation)]
    [InlineData("GOOGLE_ADS_AUTOMATED_RULE", "owner@anela.cz", AdChangeActorKind.PlatformAutomation)]
    [InlineData("INTERNAL_TOOL", null, AdChangeActorKind.PlatformAutomation)]
    [InlineData("GOOGLE_ADS_WEB_CLIENT", "specialist@agency.example", AdChangeActorKind.User)]
    [InlineData("GOOGLE_ADS_EDITOR", "specialist@agency.example", AdChangeActorKind.User)]
    [InlineData("GOOGLE_ADS_MOBILE_APP", "owner@anela.cz", AdChangeActorKind.User)]
    [InlineData("GOOGLE_ADS_BULK_UPLOAD", "owner@anela.cz", AdChangeActorKind.User)]
    [InlineData("GOOGLE_ADS_SCRIPTS", "specialist@agency.example", AdChangeActorKind.User)]
    [InlineData("GOOGLE_ADS_API", "heblo-ads@anela.cz", AdChangeActorKind.Heblo)]
    [InlineData("GOOGLE_ADS_API", "HEBLO-ADS@anela.cz", AdChangeActorKind.Heblo)]
    [InlineData("GOOGLE_ADS_API", "tool@agency.example", AdChangeActorKind.User)]
    [InlineData("GOOGLE_ADS_WEB_CLIENT", "heblo-ads@anela.cz", AdChangeActorKind.User)]
    [InlineData("OTHER", "x@example.com", AdChangeActorKind.Unknown)]
    [InlineData("UNSPECIFIED", null, AdChangeActorKind.Unknown)]
    [InlineData(null, null, AdChangeActorKind.Unknown)]
    public void maps_client_types_to_actor_kinds(string? clientType, string? email, AdChangeActorKind expected) =>
        GoogleAdsMappings.ActorKind(clientType, email, Heblo).Should().Be(expected);

    [Fact]
    public void an_api_change_is_never_heblo_when_no_heblo_user_is_configured() =>
        GoogleAdsMappings.ActorKind("GOOGLE_ADS_API", "", "").Should().Be(AdChangeActorKind.User);

    [Theory]
    [InlineData("customers/1/campaigns/111", false, AdEntityLevel.Campaign, "111")]
    [InlineData("customers/1/adGroups/221", false, AdEntityLevel.AdGroup, "221")]
    [InlineData("customers/1/adGroupAds/221~441", false, AdEntityLevel.Ad, "221~441")]
    [InlineData("customers/1/adGroupCriteria/221~331", false, AdEntityLevel.Keyword, "221~331")]
    [InlineData("customers/1/adGroupCriteria/221~341", true, AdEntityLevel.NegativeKeyword, "adGroupCriteria/221~341")]
    [InlineData("customers/1/campaignCriteria/111~351", false, AdEntityLevel.NegativeKeyword, "campaignCriteria/111~351")]
    public void maps_resource_names_to_entity_refs(string resource, bool negative, AdEntityLevel level, string id) =>
        GoogleAdsMappings.EntityRef(resource, negative).Should().Be(((AdEntityLevel?)level, id));

    [Theory]
    [InlineData("customers/1/campaignBudgets/901")]
    [InlineData("not a resource")]
    [InlineData(null)]
    public void unknown_resources_have_no_entity_ref(string? resource) =>
        GoogleAdsMappings.EntityRef(resource, false).Should().Be(((AdEntityLevel?)null, (string?)null));
}
