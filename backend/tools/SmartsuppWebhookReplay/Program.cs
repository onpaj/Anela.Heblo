using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartsuppWebhookReplay.Endpoints;
using SmartsuppWebhookReplay.Models;
using SmartsuppWebhookReplay.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("ReplaySource");

if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException(
        "Connection string 'ReplaySource' is not configured. Add ConnectionStrings:ReplaySource to secrets.json.");

builder.Services.AddDbContext<ApplicationDbContext>(opt =>
    opt.UseNpgsql(connectionString));

builder.Services.Configure<ReplayOptions>(
    builder.Configuration.GetSection(ReplayOptions.SectionName));

builder.Services.AddHttpClient(nameof(WebhookForwarder), (sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<ReplayOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
});
builder.Services.AddScoped<WebhookForwarder>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapAuditEndpoints();
app.MapForwardEndpoint();

app.Run();
