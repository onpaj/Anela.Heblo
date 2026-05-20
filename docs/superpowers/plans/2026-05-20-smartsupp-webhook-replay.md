# Smartsupp Webhook Replay Helper — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a minimal Kestrel-hosted web UI (`localhost:5050`) that reads `SmartsuppWebhookAuditEntries` from the existing PostgreSQL database and lets you re-POST individual entries to a target URL (your local API) with the original HMAC signature verbatim — for debugging the full HTTP/HMAC/parsing stack, bypassing the existing in-process replay path.

**Architecture:** .NET 8 Minimal API in `backend/tools/SmartsuppWebhookReplay/` with a `ProjectReference` to `Anela.Heblo.Persistence` (which transitively pulls Domain). `ApplicationDbContext` is registered directly with `AddDbContext<ApplicationDbContext>(opt => opt.UseNpgsql(...))` — no `PersistenceModule`. A single `WebhookForwarder` service posts the stored `RawBody` to `Replay:TargetUrl` with `X-Smartsupp-Hmac` forwarded verbatim. Frontend is vanilla JS + `fetch` served from `wwwroot/`, no build step. No auth, localhost-only.

**Tech Stack:** .NET 8 Minimal API, EF Core 8 + Npgsql, `IHttpClientFactory`, vanilla JS + CSS, User Secrets (shared `UserSecretsId` with the API).

> **Dependency note:** Tasks 1 and 2 create `SmartsuppWebhookAuditEntry` and its Persistence wiring. These are stub definitions on this branch; the `feature/smartsupp-audit` branch owns the canonical versions. If that branch lands on `main` before you run these tasks, skip Tasks 1 and 2 (the types will already exist), resolve any merge conflicts, and start from Task 3.

---

## File Structure

**New files in Domain:**
```
backend/src/Anela.Heblo.Domain/Features/Smartsupp/
  SmartsuppWebhookSignatureStatus.cs
  SmartsuppWebhookProcessingStatus.cs
  SmartsuppWebhookAuditEntry.cs
```

**New files in Persistence:**
```
backend/src/Anela.Heblo.Persistence/Smartsupp/
  SmartsuppWebhookAuditEntryConfiguration.cs
```

**Modified Persistence file:**
```
backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs   — +1 DbSet
```

**New tool project:**
```
backend/tools/SmartsuppWebhookReplay/
  SmartsuppWebhookReplay.csproj
  Program.cs
  appsettings.json
  appsettings.Development.json
  Models/
    AuditSummary.cs
    AuditDetail.cs
    ForwardResult.cs
    ReplayOptions.cs
  Services/
    WebhookForwarder.cs
  Endpoints/
    AuditEndpoints.cs
    ForwardEndpoint.cs
  wwwroot/
    index.html
    app.js
    app.css
```

**Modified solution file:**
```
Anela.Heblo.sln   — +tools folder, +SmartsuppWebhookReplay project
```

---

## Task 1: Domain — audit enums and entity

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppWebhookSignatureStatus.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppWebhookProcessingStatus.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppWebhookAuditEntry.cs`

- [ ] **Step 1: Create `SmartsuppWebhookSignatureStatus.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.Smartsupp;

public enum SmartsuppWebhookSignatureStatus
{
    Unknown,
    Valid,
    Invalid,
    Missing,
}
```

- [ ] **Step 2: Create `SmartsuppWebhookProcessingStatus.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.Smartsupp;

public enum SmartsuppWebhookProcessingStatus
{
    Processed,
    HandlerException,
    ParsingFailed,
    Skipped,
}
```

- [ ] **Step 3: Create `SmartsuppWebhookAuditEntry.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.Smartsupp;

public class SmartsuppWebhookAuditEntry
{
    public Guid Id { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string EventName { get; set; } = null!;
    public string? AccountId { get; set; }
    public SmartsuppWebhookSignatureStatus SignatureStatus { get; set; }
    public string? SignatureHeader { get; set; }
    public SmartsuppWebhookProcessingStatus ProcessingStatus { get; set; }
    public string RawBody { get; set; } = null!;
    public int BodySizeBytes { get; set; }
    public string? HeadersJson { get; set; }
    public int? ProcessingDurationMs { get; set; }
    public string? ProcessingError { get; set; }
}
```

- [ ] **Step 4: Verify Domain builds**

```bash
dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppWebhookSignatureStatus.cs \
        backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppWebhookProcessingStatus.cs \
        backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppWebhookAuditEntry.cs
git commit -m "feat(smartsupp): add SmartsuppWebhookAuditEntry domain types"
```

---

## Task 2: Persistence — EF config + DbSet

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppWebhookAuditEntryConfiguration.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`

- [ ] **Step 1: Create `SmartsuppWebhookAuditEntryConfiguration.cs`**

```csharp
using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Smartsupp;

public sealed class SmartsuppWebhookAuditEntryConfiguration : IEntityTypeConfiguration<SmartsuppWebhookAuditEntry>
{
    public void Configure(EntityTypeBuilder<SmartsuppWebhookAuditEntry> builder)
    {
        builder.ToTable("SmartsuppWebhookAuditEntries", "public");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.EventName).HasMaxLength(100);
        builder.Property(e => e.AccountId).HasMaxLength(100);
        builder.Property(e => e.SignatureStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.SignatureHeader).HasMaxLength(200);
        builder.Property(e => e.ProcessingStatus).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.RawBody).HasColumnType("text");
        builder.Property(e => e.HeadersJson).HasColumnType("text");
        builder.Property(e => e.ProcessingError).HasColumnType("text");
        builder.Property(e => e.ReceivedAt).HasColumnType("timestamp without time zone");
        builder.HasIndex(e => e.ReceivedAt);
        builder.HasIndex(e => new { e.ProcessingStatus, e.ReceivedAt });
    }
}
```

- [ ] **Step 2: Add DbSet to `ApplicationDbContext.cs`**

Open `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`. Find the existing Smartsupp DbSets block (search for `SmartsuppConversations`). Add the new DbSet immediately after the existing Smartsupp sets:

```csharp
    public DbSet<SmartsuppWebhookAuditEntry> SmartsuppWebhookAuditEntries { get; set; } = null!;
```

The block should look like:
```csharp
    public DbSet<SmartsuppConversation> SmartsuppConversations { get; set; } = null!;
    public DbSet<SmartsuppMessage> SmartsuppMessages { get; set; } = null!;
    public DbSet<SmartsuppContact> SmartsuppContacts { get; set; } = null!;
    public DbSet<SmartsuppWebhookAuditEntry> SmartsuppWebhookAuditEntries { get; set; } = null!;
```

- [ ] **Step 3: Verify Persistence builds**

```bash
dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppWebhookAuditEntryConfiguration.cs \
        backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs
git commit -m "feat(smartsupp): wire SmartsuppWebhookAuditEntry into Persistence"
```

---

## Task 3: Project scaffold — csproj, config, solution wiring

**Files:**
- Create: `backend/tools/SmartsuppWebhookReplay/SmartsuppWebhookReplay.csproj`
- Create: `backend/tools/SmartsuppWebhookReplay/appsettings.json`
- Create: `backend/tools/SmartsuppWebhookReplay/appsettings.Development.json`
- Modify: `Anela.Heblo.sln`

- [ ] **Step 1: Create the tools directory**

```bash
mkdir -p backend/tools/SmartsuppWebhookReplay/wwwroot \
         backend/tools/SmartsuppWebhookReplay/Models \
         backend/tools/SmartsuppWebhookReplay/Services \
         backend/tools/SmartsuppWebhookReplay/Endpoints
```

- [ ] **Step 2: Create `SmartsuppWebhookReplay.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UserSecretsId>f4e6382a-aefd-47ef-9cd7-7e12daac7e45</UserSecretsId>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Anela.Heblo.Persistence\Anela.Heblo.Persistence.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Create `appsettings.json`**

```json
{
  "ConnectionStrings": {
    "Default": ""
  },
  "Replay": {
    "TargetUrl": "http://localhost:5001/api/webhooks/smartsupp",
    "TimeoutSeconds": 30
  },
  "Kestrel": {
    "Endpoints": {
      "Http": { "Url": "http://localhost:5050" }
    }
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

- [ ] **Step 4: Create `appsettings.Development.json`**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information"
    }
  }
}
```

- [ ] **Step 5: Wire into `Anela.Heblo.sln`**

Open `Anela.Heblo.sln`. Make three additions:

**A. Add project declarations** — insert before the `Global` line (line containing `Global`):

```
Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "tools", "tools", "{7D3E9F2B-4A1C-4E6D-8B5F-3C2D7F9E0A1B}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "SmartsuppWebhookReplay", "backend\tools\SmartsuppWebhookReplay\SmartsuppWebhookReplay.csproj", "{2F8C5E3A-1B7D-4F9E-A3C8-6D1E5B2F7A4C}"
EndProject
```

**B. Add build configs** — inside `GlobalSection(ProjectConfigurationPlatforms)`, before `EndGlobalSection`:

```
		{2F8C5E3A-1B7D-4F9E-A3C8-6D1E5B2F7A4C}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{2F8C5E3A-1B7D-4F9E-A3C8-6D1E5B2F7A4C}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{2F8C5E3A-1B7D-4F9E-A3C8-6D1E5B2F7A4C}.Debug|x64.ActiveCfg = Debug|Any CPU
		{2F8C5E3A-1B7D-4F9E-A3C8-6D1E5B2F7A4C}.Debug|x64.Build.0 = Debug|Any CPU
		{2F8C5E3A-1B7D-4F9E-A3C8-6D1E5B2F7A4C}.Debug|x86.ActiveCfg = Debug|Any CPU
		{2F8C5E3A-1B7D-4F9E-A3C8-6D1E5B2F7A4C}.Debug|x86.Build.0 = Debug|Any CPU
		{2F8C5E3A-1B7D-4F9E-A3C8-6D1E5B2F7A4C}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{2F8C5E3A-1B7D-4F9E-A3C8-6D1E5B2F7A4C}.Release|Any CPU.Build.0 = Release|Any CPU
		{2F8C5E3A-1B7D-4F9E-A3C8-6D1E5B2F7A4C}.Release|x64.ActiveCfg = Release|Any CPU
		{2F8C5E3A-1B7D-4F9E-A3C8-6D1E5B2F7A4C}.Release|x64.Build.0 = Release|Any CPU
		{2F8C5E3A-1B7D-4F9E-A3C8-6D1E5B2F7A4C}.Release|x86.ActiveCfg = Release|Any CPU
		{2F8C5E3A-1B7D-4F9E-A3C8-6D1E5B2F7A4C}.Release|x86.Build.0 = Release|Any CPU
```

**C. Add nested project membership** — inside `GlobalSection(NestedProjects)`, before `EndGlobalSection`:

```
		{7D3E9F2B-4A1C-4E6D-8B5F-3C2D7F9E0A1B} = {AF9F0ADE-F207-48FB-B82C-1F2DBC866114}
		{2F8C5E3A-1B7D-4F9E-A3C8-6D1E5B2F7A4C} = {7D3E9F2B-4A1C-4E6D-8B5F-3C2D7F9E0A1B}
```

- [ ] **Step 6: Verify csproj restore**

```bash
dotnet restore backend/tools/SmartsuppWebhookReplay/SmartsuppWebhookReplay.csproj
```

Expected: `Restore succeeded.`

- [ ] **Step 7: Commit**

```bash
git add backend/tools/SmartsuppWebhookReplay/SmartsuppWebhookReplay.csproj \
        backend/tools/SmartsuppWebhookReplay/appsettings.json \
        backend/tools/SmartsuppWebhookReplay/appsettings.Development.json \
        Anela.Heblo.sln
git commit -m "feat(replay): scaffold SmartsuppWebhookReplay project"
```

---

## Task 4: Models and configuration options

**Files:**
- Create: `backend/tools/SmartsuppWebhookReplay/Models/ReplayOptions.cs`
- Create: `backend/tools/SmartsuppWebhookReplay/Models/AuditSummary.cs`
- Create: `backend/tools/SmartsuppWebhookReplay/Models/AuditDetail.cs`
- Create: `backend/tools/SmartsuppWebhookReplay/Models/ForwardResult.cs`

- [ ] **Step 1: Create `ReplayOptions.cs`**

```csharp
namespace SmartsuppWebhookReplay.Models;

public sealed class ReplayOptions
{
    public const string SectionName = "Replay";
    public required string TargetUrl { get; init; }
    public int TimeoutSeconds { get; init; } = 30;
}
```

- [ ] **Step 2: Create `AuditSummary.cs`**

```csharp
namespace SmartsuppWebhookReplay.Models;

public sealed class AuditSummary
{
    public Guid Id { get; init; }
    public DateTime ReceivedAt { get; init; }
    public string EventName { get; init; } = null!;
    public string? AccountId { get; init; }
    public string SignatureStatus { get; init; } = null!;
    public string ProcessingStatus { get; init; } = null!;
    public int BodySizeBytes { get; init; }
    public int? ProcessingDurationMs { get; init; }
}
```

- [ ] **Step 3: Create `AuditDetail.cs`**

```csharp
namespace SmartsuppWebhookReplay.Models;

public sealed class AuditDetail
{
    public Guid Id { get; init; }
    public DateTime ReceivedAt { get; init; }
    public string EventName { get; init; } = null!;
    public string? AccountId { get; init; }
    public string SignatureStatus { get; init; } = null!;
    public string? SignatureHeader { get; init; }
    public string ProcessingStatus { get; init; } = null!;
    public string RawBody { get; init; } = null!;
    public int BodySizeBytes { get; init; }
    public string? HeadersJson { get; init; }
    public int? ProcessingDurationMs { get; init; }
    public string? ProcessingError { get; init; }
}
```

- [ ] **Step 4: Create `ForwardResult.cs`**

```csharp
namespace SmartsuppWebhookReplay.Models;

public sealed class ForwardResult
{
    public int HttpStatus { get; init; }
    public string ResponseBody { get; init; } = null!;
    public int DurationMs { get; init; }
    public DateTime SentAt { get; init; }
}
```

- [ ] **Step 5: Commit**

```bash
git add backend/tools/SmartsuppWebhookReplay/Models/
git commit -m "feat(replay): add Models and ReplayOptions"
```

---

## Task 5: WebhookForwarder service

**Files:**
- Create: `backend/tools/SmartsuppWebhookReplay/Services/WebhookForwarder.cs`

- [ ] **Step 1: Create `WebhookForwarder.cs`**

```csharp
using System.Diagnostics;
using System.Text;
using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.Extensions.Options;
using SmartsuppWebhookReplay.Models;

namespace SmartsuppWebhookReplay.Services;

public sealed class WebhookForwarder
{
    private readonly IHttpClientFactory _http;
    private readonly ReplayOptions _options;

    public WebhookForwarder(IHttpClientFactory http, IOptions<ReplayOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<ForwardResult> ForwardAsync(SmartsuppWebhookAuditEntry entry, CancellationToken ct)
    {
        using var client = _http.CreateClient(nameof(WebhookForwarder));
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

        var request = new HttpRequestMessage(HttpMethod.Post, _options.TargetUrl)
        {
            Content = new StringContent(entry.RawBody, Encoding.UTF8, "application/json"),
        };

        if (!string.IsNullOrEmpty(entry.SignatureHeader))
            request.Headers.TryAddWithoutValidation("X-Smartsupp-Hmac", entry.SignatureHeader);

        var sw = Stopwatch.StartNew();
        var response = await client.SendAsync(request, ct);
        sw.Stop();

        var body = await response.Content.ReadAsStringAsync(ct);
        return new ForwardResult
        {
            HttpStatus = (int)response.StatusCode,
            ResponseBody = body,
            DurationMs = (int)sw.ElapsedMilliseconds,
            SentAt = DateTime.UtcNow,
        };
    }
}
```

- [ ] **Step 2: Verify project builds so far**

```bash
dotnet build backend/tools/SmartsuppWebhookReplay/SmartsuppWebhookReplay.csproj
```

Expected: `Build succeeded.` (Program.cs doesn't exist yet — that's fine, the build will fail on the entry point. If it fails only on `Program.cs` missing, create a temporary stub: `var app = WebApplication.Create(); app.Run();` in `Program.cs` and re-run. Remove the stub in Task 7.)

- [ ] **Step 3: Commit**

```bash
git add backend/tools/SmartsuppWebhookReplay/Services/WebhookForwarder.cs
git commit -m "feat(replay): add WebhookForwarder service"
```

---

## Task 6: Endpoints

**Files:**
- Create: `backend/tools/SmartsuppWebhookReplay/Endpoints/AuditEndpoints.cs`
- Create: `backend/tools/SmartsuppWebhookReplay/Endpoints/ForwardEndpoint.cs`

- [ ] **Step 1: Create `AuditEndpoints.cs`**

```csharp
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;
using SmartsuppWebhookReplay.Models;

namespace SmartsuppWebhookReplay.Endpoints;

public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this WebApplication app)
    {
        app.MapGet("/api/audit", ListAuditEntries);
        app.MapGet("/api/audit/{id:guid}", GetAuditEntry);
    }

    private static async Task<IResult> ListAuditEntries(
        ApplicationDbContext db,
        string? @event,
        string? processingStatus,
        string? signatureStatus,
        DateTime? from,
        DateTime? to,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default)
    {
        take = Math.Min(take, 500);

        var query = db.SmartsuppWebhookAuditEntries.AsNoTracking();

        if (!string.IsNullOrEmpty(@event))
            query = query.Where(e => e.EventName == @event);

        if (!string.IsNullOrEmpty(processingStatus))
            query = query.Where(e => EF.Property<string>(e, "ProcessingStatus") == processingStatus);

        if (!string.IsNullOrEmpty(signatureStatus))
            query = query.Where(e => EF.Property<string>(e, "SignatureStatus") == signatureStatus);

        if (from.HasValue)
            query = query.Where(e => e.ReceivedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(e => e.ReceivedAt <= to.Value);

        var items = await query
            .OrderBy(e => e.ReceivedAt)
            .Skip(skip)
            .Take(take)
            .Select(e => new AuditSummary
            {
                Id = e.Id,
                ReceivedAt = e.ReceivedAt,
                EventName = e.EventName,
                AccountId = e.AccountId,
                SignatureStatus = e.SignatureStatus.ToString(),
                ProcessingStatus = e.ProcessingStatus.ToString(),
                BodySizeBytes = e.BodySizeBytes,
                ProcessingDurationMs = e.ProcessingDurationMs,
            })
            .ToListAsync(ct);

        return Results.Ok(items);
    }

    private static async Task<IResult> GetAuditEntry(
        Guid id,
        ApplicationDbContext db,
        CancellationToken ct)
    {
        var entry = await db.SmartsuppWebhookAuditEntries
            .AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new AuditDetail
            {
                Id = e.Id,
                ReceivedAt = e.ReceivedAt,
                EventName = e.EventName,
                AccountId = e.AccountId,
                SignatureStatus = e.SignatureStatus.ToString(),
                SignatureHeader = e.SignatureHeader,
                ProcessingStatus = e.ProcessingStatus.ToString(),
                RawBody = e.RawBody,
                BodySizeBytes = e.BodySizeBytes,
                HeadersJson = e.HeadersJson,
                ProcessingDurationMs = e.ProcessingDurationMs,
                ProcessingError = e.ProcessingError,
            })
            .FirstOrDefaultAsync(ct);

        return entry is null ? Results.NotFound() : Results.Ok(entry);
    }
}
```

- [ ] **Step 2: Create `ForwardEndpoint.cs`**

```csharp
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;
using SmartsuppWebhookReplay.Services;

namespace SmartsuppWebhookReplay.Endpoints;

public static class ForwardEndpoint
{
    public static void MapForwardEndpoint(this WebApplication app)
    {
        app.MapPost("/api/audit/{id:guid}/forward", ForwardEntry);
    }

    private static async Task<IResult> ForwardEntry(
        Guid id,
        ApplicationDbContext db,
        WebhookForwarder forwarder,
        CancellationToken ct)
    {
        var entry = await db.SmartsuppWebhookAuditEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct);

        if (entry is null)
            return Results.NotFound();

        var result = await forwarder.ForwardAsync(entry, ct);
        return Results.Ok(result);
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add backend/tools/SmartsuppWebhookReplay/Endpoints/
git commit -m "feat(replay): add AuditEndpoints and ForwardEndpoint"
```

---

## Task 7: Program.cs

**Files:**
- Create (or replace stub): `backend/tools/SmartsuppWebhookReplay/Program.cs`

- [ ] **Step 1: Create `Program.cs`**

```csharp
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
```

- [ ] **Step 2: Build the full tool**

```bash
dotnet build backend/tools/SmartsuppWebhookReplay/SmartsuppWebhookReplay.csproj
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/tools/SmartsuppWebhookReplay/Program.cs
git commit -m "feat(replay): wire up Program.cs"
```

---

## Task 8: Frontend (wwwroot)

**Files:**
- Create: `backend/tools/SmartsuppWebhookReplay/wwwroot/index.html`
- Create: `backend/tools/SmartsuppWebhookReplay/wwwroot/app.js`
- Create: `backend/tools/SmartsuppWebhookReplay/wwwroot/app.css`

- [ ] **Step 1: Create `index.html`**

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>Smartsupp Webhook Replay</title>
  <link rel="stylesheet" href="/app.css" />
</head>
<body>
  <header>
    <div id="target-url"></div>
    <button id="btn-send-next" title="Keyboard: n or Enter">Send next ↵</button>
    <span class="kbd-hint">n / ↵ = next · s = skip · r = refresh</span>
  </header>

  <div class="filters">
    <input id="f-event" type="text" placeholder="Event name" />
    <select id="f-proc-status">
      <option value="">All processing statuses</option>
      <option value="Processed">Processed</option>
      <option value="HandlerException">HandlerException</option>
      <option value="ParsingFailed">ParsingFailed</option>
      <option value="Skipped">Skipped</option>
    </select>
    <select id="f-sig-status">
      <option value="">All signature statuses</option>
      <option value="Valid">Valid</option>
      <option value="Invalid">Invalid</option>
      <option value="Missing">Missing</option>
      <option value="Unknown">Unknown</option>
    </select>
    <input id="f-from" type="datetime-local" />
    <input id="f-to" type="datetime-local" />
    <button id="btn-refresh">Refresh</button>
  </div>

  <table>
    <thead>
      <tr>
        <th>Time</th>
        <th>Event</th>
        <th>Account</th>
        <th>Sig</th>
        <th>Proc</th>
        <th>Size</th>
        <th>Dur</th>
        <th>Action</th>
      </tr>
    </thead>
    <tbody id="tbody"></tbody>
  </table>

  <script src="/app.js"></script>
</body>
</html>
```

- [ ] **Step 2: Create `app.css`**

```css
* { box-sizing: border-box; margin: 0; padding: 0; }
body { font-family: monospace; font-size: 13px; background: #0d1117; color: #c9d1d9; }

header {
  display: flex; align-items: center; gap: 12px;
  padding: 8px 12px; background: #161b22; border-bottom: 1px solid #30363d;
}
#target-url { color: #58a6ff; flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
#btn-send-next {
  background: #238636; color: #fff; border: none; border-radius: 4px;
  padding: 6px 14px; font-size: 13px; cursor: pointer; white-space: nowrap;
}
#btn-send-next:hover { background: #2ea043; }
.kbd-hint { color: #8b949e; font-size: 11px; white-space: nowrap; }

.filters {
  display: flex; flex-wrap: wrap; gap: 6px;
  padding: 8px 12px; background: #161b22; border-bottom: 1px solid #30363d;
}
.filters input, .filters select {
  background: #0d1117; color: #c9d1d9; border: 1px solid #30363d;
  border-radius: 4px; padding: 4px 8px; font-size: 12px;
}
#btn-refresh {
  background: #21262d; color: #c9d1d9; border: 1px solid #30363d;
  border-radius: 4px; padding: 4px 10px; font-size: 12px; cursor: pointer;
}
#btn-refresh:hover { background: #30363d; }

table { width: 100%; border-collapse: collapse; }
thead tr { background: #161b22; }
th { padding: 6px 10px; text-align: left; font-weight: 600; color: #8b949e; border-bottom: 1px solid #30363d; }
td { padding: 5px 10px; border-bottom: 1px solid #21262d; white-space: nowrap; }
tr.cursor { background: #1f2d3d; }
tr:hover { background: #1c2128; }

.status-valid, .status-processed { color: #3fb950; }
.status-invalid, .status-handlerexception, .status-parsingfailed { color: #f85149; }
.status-missing, .status-skipped { color: #d29922; }
.status-unknown { color: #8b949e; }

.pill {
  display: inline-block; font-size: 11px; padding: 2px 6px;
  border-radius: 10px; margin-left: 4px; font-weight: 600;
}
.pill-ok { background: #0d3318; color: #3fb950; border: 1px solid #238636; }
.pill-err { background: #3d1111; color: #f85149; border: 1px solid #f85149; }

button.send-btn {
  background: #21262d; color: #c9d1d9; border: 1px solid #30363d;
  border-radius: 3px; padding: 2px 8px; font-size: 11px; cursor: pointer;
}
button.send-btn:hover { background: #30363d; }
button.send-btn:disabled { opacity: 0.5; cursor: default; }
```

- [ ] **Step 3: Create `app.js`**

```js
const TARGET_URL = window.location.origin;
const LS_KEY = `replay-cursor:${TARGET_URL}`;

let rows = [];
let cursor = parseInt(localStorage.getItem(LS_KEY) || '0', 10);
const results = new Map(); // id -> last 5 ForwardResult[]

document.getElementById('target-url').textContent =
  `→ ${document.getElementById('f-event') ? '' : ''}Target: loading…`;

async function loadTargetUrl() {
  try {
    const cfg = await fetch('/api/audit?take=0').then(r => r.json());
    // derive from appsettings via a meta endpoint — fall back to showing the base URL
  } catch (_) {}
  document.getElementById('target-url').textContent =
    `Target: ${TARGET_URL}/api/audit`;
}
loadTargetUrl();

async function fetchRows() {
  const event = document.getElementById('f-event').value.trim();
  const proc = document.getElementById('f-proc-status').value;
  const sig = document.getElementById('f-sig-status').value;
  const from = document.getElementById('f-from').value;
  const to = document.getElementById('f-to').value;

  const params = new URLSearchParams({ take: '500' });
  if (event) params.set('event', event);
  if (proc) params.set('processingStatus', proc);
  if (sig) params.set('signatureStatus', sig);
  if (from) params.set('from', new Date(from).toISOString());
  if (to) params.set('to', new Date(to).toISOString());

  const data = await fetch(`/api/audit?${params}`).then(r => r.json());
  rows = data;
  cursor = Math.min(cursor, rows.length > 0 ? rows.length - 1 : 0);
  render();
}

function render() {
  const tbody = document.getElementById('tbody');
  tbody.innerHTML = '';
  rows.forEach((row, i) => {
    const tr = document.createElement('tr');
    if (i === cursor) tr.classList.add('cursor');
    tr.dataset.idx = i;
    tr.innerHTML = `
      <td>${new Date(row.receivedAt).toLocaleTimeString()}</td>
      <td>${row.eventName}</td>
      <td>${row.accountId ?? '-'}</td>
      <td class="status-${row.signatureStatus.toLowerCase()}">${row.signatureStatus}</td>
      <td class="status-${row.processingStatus.toLowerCase()}">${row.processingStatus}</td>
      <td>${row.bodySizeBytes}</td>
      <td>${row.processingDurationMs ?? '-'}</td>
      <td>
        <button class="send-btn" data-id="${row.id}" data-idx="${i}">Send</button>
        <span class="pills" id="pills-${row.id}"></span>
      </td>`;
    tr.addEventListener('click', e => {
      if (e.target.classList.contains('send-btn')) return;
      cursor = i;
      saveCursor();
      render();
    });
    tbody.appendChild(tr);
  });
}

async function sendRow(id, idx) {
  const btn = document.querySelector(`button[data-id="${id}"]`);
  if (btn) btn.disabled = true;

  try {
    const result = await fetch(`/api/audit/${id}/forward`, { method: 'POST' }).then(r => r.json());
    const list = results.get(id) ?? [];
    list.unshift(result);
    if (list.length > 5) list.pop();
    results.set(id, list);
    renderPills(id, list);
  } finally {
    if (btn) btn.disabled = false;
  }
}

function renderPills(id, list) {
  const el = document.getElementById(`pills-${id}`);
  if (!el) return;
  el.innerHTML = list.slice(0, 5).map(r => {
    const ok = r.httpStatus >= 200 && r.httpStatus < 300;
    return `<span class="pill ${ok ? 'pill-ok' : 'pill-err'}">${r.httpStatus} · ${r.durationMs}ms</span>`;
  }).join('');
}

function saveCursor() {
  localStorage.setItem(LS_KEY, String(cursor));
}

document.getElementById('btn-send-next').addEventListener('click', async () => {
  if (rows.length === 0) return;
  const row = rows[cursor];
  await sendRow(row.id, cursor);
  cursor = Math.min(cursor + 1, rows.length - 1);
  saveCursor();
  render();
});

document.getElementById('btn-refresh').addEventListener('click', fetchRows);

document.querySelectorAll('.send-btn').forEach(btn => {
  // delegated below
});
document.getElementById('tbody').addEventListener('click', e => {
  const btn = e.target.closest('.send-btn');
  if (!btn) return;
  sendRow(btn.dataset.id, parseInt(btn.dataset.idx, 10));
});

document.addEventListener('keydown', e => {
  if (e.target.tagName === 'INPUT' || e.target.tagName === 'SELECT') return;
  if (e.key === 'n' || e.key === 'Enter') {
    document.getElementById('btn-send-next').click();
  } else if (e.key === 's') {
    cursor = Math.min(cursor + 1, rows.length - 1);
    saveCursor();
    render();
  } else if (e.key === 'r') {
    fetchRows();
  }
});

fetchRows();
```

- [ ] **Step 4: Commit**

```bash
git add backend/tools/SmartsuppWebhookReplay/wwwroot/
git commit -m "feat(replay): add frontend (index.html, app.js, app.css)"
```

---

## Task 9: Build verification and solution smoke test

- [ ] **Step 1: Full solution build**

```bash
dotnet build Anela.Heblo.sln
```

Expected: `Build succeeded.` (0 errors). If any existing project fails, investigate before continuing — don't suppress errors.

- [ ] **Step 2: Verify the tool project specifically**

```bash
dotnet build backend/tools/SmartsuppWebhookReplay/SmartsuppWebhookReplay.csproj -c Release
```

Expected: `Build succeeded.` 0 errors, 0 warnings (or only nullable warnings from transitively referenced code that existed before this change).

- [ ] **Step 3: Verify `dotnet run` starts without crashing**

The connection string will be empty unless secrets are populated, so Npgsql will throw on first DB call (not on startup). The server should start and listen. Run:

```bash
dotnet run --project backend/tools/SmartsuppWebhookReplay 2>&1 | head -20
```

Expected output contains: `Now listening on: http://localhost:5050`

Press Ctrl+C after confirming.

- [ ] **Step 4: Final commit**

```bash
git add -p   # review any remaining unstaged changes
git commit -m "feat(replay): complete SmartsuppWebhookReplay helper tool"
```

---

## Verification checklist (post-implementation, requires real DB + running local API)

1. `dotnet build Anela.Heblo.sln` succeeds.
2. `dotnet run --project backend/tools/SmartsuppWebhookReplay` starts and listens on `localhost:5050`.
3. `http://localhost:5050` loads and shows the audit table (requires `SmartsuppWebhookAuditEntries` table populated by the audit branch work, and `secrets.json:ConnectionStrings:Default` pointing at that DB).
4. Click Send on a row — helper UI shows e.g. `200 · 42ms`. Local API log confirms controller received the request and HMAC verified.
5. Press `n` repeatedly — rows fire in order, cursor advances, `localStorage` survives a page refresh.
