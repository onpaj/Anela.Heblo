using Anela.Heblo.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using System.Security.Claims;

namespace Anela.Heblo.Application.Services;

public class UserService : IUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;
    private readonly GraphServiceClient? _graphServiceClient;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IHttpContextAccessor httpContextAccessor,
        IConfiguration configuration,
        ILogger<UserService> logger,
        GraphServiceClient? graphServiceClient = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _configuration = configuration;
        _graphServiceClient = graphServiceClient;
        _logger = logger;
    }

    public async Task<string> GetCurrentUserNameAsync()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.User == null)
        {
            _logger.LogWarning("No HTTP context or user available");
            return "Unknown User";
        }

        var user = httpContext.User;
        string userName = "Unknown User";

        if (_configuration.GetValue<bool>("UseMockAuth"))
        {
            // Mock mode - get user from claims
            userName = user.Identity?.Name ?? "Mock User";
            _logger.LogDebug("Retrieved user from mock authentication: {UserName}", userName);
        }
        else if (_graphServiceClient != null)
        {
            try
            {
                // Real mode - get user from Graph API
                var graphUser = await _graphServiceClient.Me.GetAsync();
                userName = graphUser?.DisplayName ?? user.Identity?.Name ?? "Unknown User";
                _logger.LogDebug("Retrieved user from Graph API: {UserName}", userName);
            }
            catch (Exception ex)
            {
                // If Graph API fails (e.g., consent required), fallback to JWT claims
                _logger.LogWarning("Failed to get user from Graph API: {Error}. Using fallback.", ex.Message);
                userName = user.FindFirst("name")?.Value ??
                          user.FindFirst("preferred_username")?.Value ??
                          user.Identity?.Name ??
                          "Authenticated User";
                _logger.LogDebug("Retrieved user from JWT claims fallback: {UserName}", userName);
            }
        }

        return userName;
    }
}