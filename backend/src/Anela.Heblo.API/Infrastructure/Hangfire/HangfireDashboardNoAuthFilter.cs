using Hangfire.Dashboard;

namespace Anela.Heblo.API.Infrastructure.Hangfire;

public class HangfireDashboardNoAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        // Allow access without authentication for development and staging
        // In production, this should be secured with proper authentication
        return true;
    }
}