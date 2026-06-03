using System.Net;
using System.Net.Http.Json;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureSettings;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Settings;

[Collection("WebApp")]
public class GetManufactureSettingsEndpointTests : IClassFixture<HebloWebApplicationFactory>
{
    private readonly HebloWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public GetManufactureSettingsEndpointTests(HebloWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GetSettings_ShouldReturnSuccessAndCorrectContentType()
    {
        var response = await _client.GetAsync("/api/manufacture/settings");
        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType?.ToString().Should().Be("application/json; charset=utf-8");
    }

    [Fact]
    public async Task GetSettings_ShouldExposeManufactureGroupIdField()
    {
        var response = await _client.GetAsync("/api/manufacture/settings");
        var settings = await response.Content.ReadFromJsonAsync<GetManufactureSettingsResponse>();
        settings.Should().NotBeNull();
        var hasProperty = typeof(GetManufactureSettingsResponse).GetProperty(nameof(GetManufactureSettingsResponse.ManufactureGroupId)) != null;
        hasProperty.Should().BeTrue();
    }

    [Fact]
    public async Task GetSettings_ShouldBeReachableAnonymously()
    {
        using var anonymousClient = _factory.CreateClient();
        anonymousClient.DefaultRequestHeaders.Authorization = null;
        var response = await anonymousClient.GetAsync("/api/manufacture/settings");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
