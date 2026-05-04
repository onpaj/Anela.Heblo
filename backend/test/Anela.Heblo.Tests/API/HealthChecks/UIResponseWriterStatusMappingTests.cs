using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Anela.Heblo.Tests.API.HealthChecks;

public class UIResponseWriterStatusMappingTests
{
    [Fact]
    public async Task UIResponseWriter_MapsDegradedReport_ToHttp200()
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddHealthChecks()
                            .AddCheck(
                                name: "fake-degraded",
                                check: () => HealthCheckResult.Degraded("synthetic degraded"),
                                tags: new[] { "ready" });
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
                            {
                                Predicate = c => c.Tags.Contains("ready"),
                                ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
                            });
                        });
                    });
            })
            .StartAsync();

        var client = host.GetTestClient();
        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "UIResponseWriter must map a Degraded-only health report to 200; "
            + "if this assertion ever fails, FR-2 must change to return Unhealthy without an exception payload.");
    }
}
