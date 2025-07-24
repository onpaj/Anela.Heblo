using System.Net;
using System.Text.Json;
using Anela.Heblo.API;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Anela.Heblo.Tests.Auth;

public class MockAuthenticationIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public MockAuthenticationIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        
        // Configure the test server to use mock authentication
        _client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["UseMockAuth"] = "true",
                    ["ASPNETCORE_ENVIRONMENT"] = "Development"
                });
            });
        }).CreateClient();
    }

    [Fact]
    public async Task WeatherForecast_WithMockAuth_ReturnsSuccessAndCorrectData()
    {
        // Act
        var response = await _client.GetAsync("/WeatherForecast");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        var weatherData = JsonSerializer.Deserialize<WeatherForecast[]>(responseContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(weatherData);
        Assert.Equal(5, weatherData.Length);
        
        // Verify all forecasts have valid data
        foreach (var forecast in weatherData)
        {
            Assert.True(forecast.TemperatureC >= -20 && forecast.TemperatureC <= 55);
            Assert.NotNull(forecast.Summary);
            Assert.True(forecast.Date > DateOnly.FromDateTime(DateTime.Now));
        }
    }

    [Fact]
    public async Task WeatherForecast_WithMockAuth_ReturnsCorrectContentType()
    {
        // Act
        var response = await _client.GetAsync("/WeatherForecast");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
    }

    [Fact]
    public async Task Server_WithMockAuth_StartsSuccessfully()
    {
        // Act - just creating the client tests that the server starts
        var response = await _client.GetAsync("/");

        // Assert - We expect 404 for root path, but server should be running
        Assert.True(response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task WeatherForecast_MockAuthProvidesMockUserClaims()
    {
        // This test verifies that the mock authentication handler is working
        // by checking that the endpoint works without real authentication
        
        // Act
        var response = await _client.GetAsync("/WeatherForecast");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        // If we get OK instead of Unauthorized, mock auth is working
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }
}

// Test model that matches the API response
public class WeatherForecast
{
    public DateOnly Date { get; set; }
    public int TemperatureC { get; set; }
    public int TemperatureF { get; set; }
    public string? Summary { get; set; }
}