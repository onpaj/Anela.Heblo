using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;
using SmartsuppWebhookReplay.Endpoints;
using SmartsuppWebhookReplay.Models;
using SmartsuppWebhookReplay.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'Default' is not configured. Add it to secrets.json.");

builder.Services.AddDbContext<ApplicationDbContext>(opt =>
    opt.UseNpgsql(connectionString));

builder.Services.Configure<ReplayOptions>(
    builder.Configuration.GetSection(ReplayOptions.SectionName));

builder.Services.AddHttpClient(nameof(WebhookForwarder));
builder.Services.AddScoped<WebhookForwarder>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapAuditEndpoints();
app.MapForwardEndpoint();

app.Run();
