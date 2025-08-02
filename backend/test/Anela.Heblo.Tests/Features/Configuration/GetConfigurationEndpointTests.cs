using System.Net.Http.Json;
using Anela.Heblo.API;
using Anela.Heblo.Application.Features.Configuration.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

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
            // Ensure test environment is set to enable mock authentication
            builder.UseEnvironment("Test");
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    {"UseMockAuth", "true"}
                });
            });
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
        Assert.NotNull(configResponse);
        Assert.NotNull(configResponse.Version);
        Assert.NotNull(configResponse.Environment);
        Assert.True(configResponse.Timestamp > DateTime.MinValue);
        Assert.True(configResponse.Timestamp <= DateTime.UtcNow.AddMinutes(1)); // Allow 1 minute tolerance
    }

    [Fact]
    public async Task GetConfiguration_ShouldIncludeMockAuthFlag()
    {
        // Act
        var response = await _client.GetAsync("/api/configuration");
        var configResponse = await response.Content.ReadFromJsonAsync<GetConfigurationResponse>();

        // Assert
        Assert.NotNull(configResponse);
        // In test environment, mock auth should be enabled
        Assert.True(configResponse.UseMockAuth);
    }

    [Fact]
    public async Task GetConfiguration_ShouldReturnTestEnvironment()
    {
        // Act
        var response = await _client.GetAsync("/api/configuration");
        var configResponse = await response.Content.ReadFromJsonAsync<GetConfigurationResponse>();

        // Assert
        Assert.NotNull(configResponse);
        // In integration tests, environment should be Test
        Assert.Equal("Test", configResponse.Environment);
    }

    [Fact]
    public async Task GetConfiguration_ShouldReturnValidVersion()
    {
        // Act
        var response = await _client.GetAsync("/api/configuration");
        var configResponse = await response.Content.ReadFromJsonAsync<GetConfigurationResponse>();

        // Assert
        Assert.NotNull(configResponse);
        Assert.NotNull(configResponse.Version);
        Assert.NotEmpty(configResponse.Version);
        // Version should be either from assembly or default fallback
        Assert.True(configResponse.Version.Length > 0);
    }
}