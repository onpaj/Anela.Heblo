using System.Net.Http.Json;
using FluentAssertions;
using Anela.Heblo.API;
using FluentAssertions;
using Anela.Heblo.Application.Features.Configuration.Model;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.Configuration;

/// <summary>
/// Integration tests for GetConfigurationEndpoint
/// </summary>
public class GetConfigurationEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public GetConfigurationEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            // Use Automation environment - automatically loads appsettings.Automation.json
            builder.UseEnvironment("Automation");
        });
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
        configResponse.Environment.Should().Be("Automation");
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