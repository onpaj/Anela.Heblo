using System;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

/// <summary>
/// Integration tests for MarketingCalendarController.GetMarketingActions endpoint.
/// Tests model binding for query parameters, particularly the ActionType enum filtering.
/// </summary>
[Collection("WebApp")]
public class MarketingCalendarControllerTests : IClassFixture<HebloWebApplicationFactory>
{
    private readonly HebloWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public MarketingCalendarControllerTests(HebloWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GetMarketingActions_WithValidActionTypeQueryParam_ReturnsOk()
    {
        // Arrange
        // Query with ActionType=Blog (a valid enum value)
        var url = "/api/MarketingCalendar?ActionType=Blog";

        // Act
        var response = await _client.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        // Verify response can be deserialized to GetMarketingActionsResponse
        var content = await response.Content.ReadAsAsync<GetMarketingActionsResponse>();
        content.Should().NotBeNull();
        content.Actions.Should().NotBeNull();
        content.PageNumber.Should().Be(1);
        content.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task GetMarketingActions_WithInvalidActionTypeQueryParam_ReturnsBadRequest()
    {
        // Arrange
        // Query with an invalid enum value for ActionType
        var url = "/api/MarketingCalendar?ActionType=NotAType";

        // Act
        var response = await _client.GetAsync(url);

        // Assert
        // ASP.NET Core model binding rejects invalid enum values with 400 Bad Request
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetMarketingActions_WithoutActionTypeQueryParam_ReturnsOk()
    {
        // Arrange
        // Query without ActionType parameter (should default to null)
        var url = "/api/MarketingCalendar";

        // Act
        var response = await _client.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var content = await response.Content.ReadAsAsync<GetMarketingActionsResponse>();
        content.Should().NotBeNull();
        content.Actions.Should().NotBeNull();
    }

    [Theory]
    [InlineData("SocialMedia")]
    [InlineData("Newsletter")]
    [InlineData("PR")]
    [InlineData("Event")]
    [InlineData("Meeting")]
    public async Task GetMarketingActions_WithValidActionTypeValues_ReturnsOk(string actionType)
    {
        // Arrange
        var url = $"/api/MarketingCalendar?ActionType={actionType}";

        // Act
        var response = await _client.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsAsync<GetMarketingActionsResponse>();
        content.Should().NotBeNull();
        content.Actions.Should().NotBeNull();
    }

    [Theory]
    [InlineData("InvalidType")]
    [InlineData("12345")]
    [InlineData("BlogPost")]
    [InlineData("NonExistentType")]
    public async Task GetMarketingActions_WithInvalidActionTypeValues_ReturnsBadRequest(string invalidActionType)
    {
        // Arrange
        // Undefined integer values are rejected because they don't map to any enum member.
        // Defined ordinals (0=SocialMedia, 1=Blog, 2=Newsletter, 3=PR, 4=Event, 99=Meeting) bind successfully — see GetMarketingActions_WithNumericActionTypeOrdinal_ReturnsOk.
        var url = $"/api/MarketingCalendar?ActionType={invalidActionType}";

        // Act
        var response = await _client.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("0")]    // SocialMedia ordinal
    [InlineData("1")]    // Blog ordinal
    [InlineData("99")]   // Meeting ordinal
    public async Task GetMarketingActions_WithNumericActionTypeOrdinal_ReturnsOk(string ordinalValue)
    {
        // Arrange — defined numeric ordinals bind successfully (ASP.NET Core default behavior)
        var url = $"/api/MarketingCalendar?ActionType={ordinalValue}";

        // Act
        var response = await _client.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("blog")]
    [InlineData("BLOG")]
    [InlineData("Blog")]
    [InlineData("SOCIALMEDIA")]
    public async Task GetMarketingActions_WithCaseInsensitiveValidActionType_ReturnsOk(string actionType)
    {
        // Arrange
        // ASP.NET Core enum binding is case-insensitive
        var url = $"/api/MarketingCalendar?ActionType={actionType}";

        // Act
        var response = await _client.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsAsync<GetMarketingActionsResponse>();
        content.Should().NotBeNull();
        content.Actions.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMarketingActions_WithPaginationAndActionType_ReturnsOk()
    {
        // Arrange
        var url = "/api/MarketingCalendar?ActionType=Blog&PageNumber=1&PageSize=10";

        // Act
        var response = await _client.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsAsync<GetMarketingActionsResponse>();
        content.Should().NotBeNull();
        content.PageNumber.Should().Be(1);
        content.PageSize.Should().Be(10);
    }
}
