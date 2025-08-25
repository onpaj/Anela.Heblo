using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Anela.Heblo.API;
using Anela.Heblo.API.Infrastructure.Authentication;
using Anela.Heblo.Application.Features.Catalog.Model;
using Anela.Heblo.Domain.Features.Configuration;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using System.Text.Encodings.Web;

namespace Anela.Heblo.Tests.Features.Catalog;

public class ProductMarginsAuthorizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ProductMarginsAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetProductMargins_WhenUnauthenticated_Returns401Unauthorized()
    {
        // Arrange
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Configure without authentication
                services.Configure<Microsoft.AspNetCore.Builder.WebApplicationOptions>(options =>
                {
                    options.ContentRootPath = System.IO.Directory.GetCurrentDirectory();
                });
                
                // Replace with unauthenticated handler
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, UnauthenticatedTestHandler>("Test", _ => { });
            });
        });

        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/ProductMargins?pageNumber=1&pageSize=10");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProductMargins_WithBasicMarginsAccess_ReturnsSuccess()
    {
        // Arrange
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Mock authentication with basic access
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, BasicAccessTestHandler>("Test", _ => { });
                
                // Mock successful mediator response
                var mockMediator = new Mock<IMediator>();
                mockMediator.Setup(x => x.Send(It.IsAny<GetProductMarginsRequest>(), default))
                    .ReturnsAsync(new GetProductMarginsResponse
                    {
                        Items = new List<ProductMarginDto>(),
                        TotalCount = 0,
                        PageNumber = 1,
                        PageSize = 10
                    });

                services.AddScoped(_ => mockMediator.Object);
            });
        });

        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/ProductMargins?pageNumber=1&pageSize=10");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetProductMargins_WithDetailedMarginsAccess_ReturnsSuccess()
    {
        // Arrange
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Mock authentication with detailed access
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, DetailedAccessTestHandler>("Test", _ => { });
                
                // Mock successful mediator response
                var mockMediator = new Mock<IMediator>();
                mockMediator.Setup(x => x.Send(It.IsAny<GetProductMarginsRequest>(), default))
                    .ReturnsAsync(new GetProductMarginsResponse
                    {
                        Items = new List<ProductMarginDto>(),
                        TotalCount = 0,
                        PageNumber = 1,
                        PageSize = 10
                    });

                services.AddScoped(_ => mockMediator.Object);
            });
        });

        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/ProductMargins?pageNumber=1&pageSize=10");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetProductMargins_WithoutRequiredRole_Returns403Forbidden()
    {
        // Arrange
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Mock authentication without required roles/claims for production mode
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, NoAccessTestHandler>("Test", _ => { });
                
                // Disable mock authentication to test real authorization
                services.Configure<Dictionary<string, object>>(options =>
                {
                    options[ConfigurationConstants.USE_MOCK_AUTH] = false;
                });
            });
        });

        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/ProductMargins?pageNumber=1&pageSize=10");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}

// Test authentication handlers for different scenarios
public class UnauthenticatedTestHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public UnauthenticatedTestHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock)
        : base(options, logger, encoder, clock)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        return Task.FromResult(AuthenticateResult.NoResult());
    }
}

public class BasicAccessTestHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public BasicAccessTestHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock)
        : base(options, logger, encoder, clock)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user-basic"),
            new Claim(ClaimTypes.Name, "Test User Basic"),
            new Claim("auth_scheme", "MockAuthentication"),
            new Claim("department", "finance")
        };

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public class DetailedAccessTestHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public DetailedAccessTestHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock)
        : base(options, logger, encoder, clock)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user-detailed"),
            new Claim(ClaimTypes.Name, "Test User Detailed"),
            new Claim("auth_scheme", "MockAuthentication"),
            new Claim("role", "FinancialManager"),
            new Claim("clearance", "confidential")
        };

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public class NoAccessTestHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public NoAccessTestHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock)
        : base(options, logger, encoder, clock)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user-no-access"),
            new Claim(ClaimTypes.Name, "Test User No Access")
            // No required roles or claims
        };

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}