using System.Net.Http.Json;
using Anela.Heblo.Application.Features.Configuration;
using FluentAssertions;
using Anela.Heblo.Tests.Common;

namespace Anela.Heblo.Tests.Features.Configuration;

/// <summary>
/// Integration tests for GetConfigurationEndpoint
/// </summary>
public class GetConfigurationEndpointTests : IClassFixture<HebloWebApplicationFactory>
{
    private readonly HebloWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public GetConfigurationEndpointTests(HebloWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GetConfiguration_ShouldReturnSuccessAndCorrectContentType()
    {
        // Act
        var response = await _client.GetAsync("/api/configuration");

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
    }

    [Fact]
    public async Task GetConfiguration_ShouldReturnValidConfigurationResponse()
    {
        // Act
        var response = await _client.GetAsync("/api/configuration");
        var configResponse = await response.Content.ReadFromJsonAsync<GetConfigurationResponse>();

        // Assert
        configResponse.Should().NotBeNull();
        configResponse.Version.Should().NotBeNull();
        configResponse.Environment.Should().NotBeNull();
        (configResponse.Timestamp > DateTime.MinValue).Should().BeTrue();
        Assert.True(configResponse.Timestamp <= DateTime.UtcNow.AddMinutes(1)); // Allow 1 minute tolerance
    }

    [Fact]
    public async Task GetConfiguration_ShouldIncludeMockAuthFlag()
    {
        // Act
        var response = await _client.GetAsync("/api/configuration");
        var configResponse = await response.Content.ReadFromJsonAsync<GetConfigurationResponse>();

        // Assert
        configResponse.Should().NotBeNull();
        // In test environment, mock auth should be enabled
        configResponse.UseMockAuth.Should().BeTrue();
    }

    [Fact]
    public async Task GetConfiguration_ShouldReturnTestEnvironment()
    {
        // Act
        var response = await _client.GetAsync("/api/configuration");
        var configResponse = await response.Content.ReadFromJsonAsync<GetConfigurationResponse>();

        // Assert
        configResponse.Should().NotBeNull();
        // In integration tests, environment should be Test
        configResponse.Environment.Should().Be("Test");
    }

    [Fact]
    public async Task GetConfiguration_ShouldReturnValidVersion()
    {
        // Act
        var response = await _client.GetAsync("/api/configuration");
        var configResponse = await response.Content.ReadFromJsonAsync<GetConfigurationResponse>();

        // Assert
        configResponse.Should().NotBeNull();
        configResponse.Version.Should().NotBeNull();
        configResponse.Version.Should().NotBeEmpty();
        // Version should be either from assembly or default fallback
        (configResponse.Version.Length > 0).Should().BeTrue();
    }
}