# Logeto Automatic Break Insertion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A nightly Hangfire job that finds Logeto (Výkaz práce) working days of ≥ 6 h without a recorded break for opted-in workers and inserts a 30-minute break that splits the work record.

**Architecture:** New `Anela.Heblo.Adapters.Logeto` HTTP adapter (AccessKey auth, ContinuationToken pagination, resilience handler) + `Attendance` vertical slice in `Anela.Heblo.Application` holding a pure break-placement calculator, an orchestration service, and an `IRecurringJob` that Hangfire auto-discovers. Client contract (`ILogetoClient` + DTOs) lives in `Anela.Heblo.Domain/Features/Attendance` (same direction as `IWeatherForecastClient`).

**Tech Stack:** .NET 8, xUnit + FluentAssertions + Moq, Microsoft.Extensions.Http.Resilience, Hangfire (existing `IRecurringJob` auto-discovery), System.Text.Json.

**Spec:** `docs/superpowers/specs/2026-08-05-logeto-break-insertion-design.md`

## Verified external API facts (do not re-research)

- Base URL: `https://[AccountName].logeto.com`, header `AccessKey: <key>`.
- `GET /api/v2/Activities` — query: `ContinuationToken` only. Item: `{Guid, Revision, PersonCreated, PersonChanged, TimestampCreated, TimestampChanged, Name, Code, Type, Default, TimeEntry, Inactive, Movement, Icon, ExternalKey}`. `Type` values include `"Work"` and `"Break"`.
- `GET /api/v2/People` — query: `ContinuationToken` only. Item: `{Guid, ..., FirstName, LastName, NameSuffix, Code, Email, PhoneNumber, Note, Inactive, ClosedPeriodTo, ExternalKey, Branch, AccessLevel, ...}`.
- `GET /api/v2/TimeTracking` — query: `Contract, Subcontract, Activity, From (date), To (date), ContinuationToken`. **No Person filter** — filter client-side. Item: `{Guid, Revision, ..., Person, Date, From, To, Hours, Activity, Description, Contract, Subcontract, Location, EndLocation, Billable, CostRate, BillingRate, CostPrice, BillingPrice, ExternalKey}`.
- `POST /api/v2/TimeTracking?merge=true` — query param `merge` (boolean) = "Merge overlapping records". Body requires `Person` (guid), `Activity` (guid), `Date` (date), `Billable` (bool); optional `From`/`To` (date-time, **seconds must be `:00`**), `Hours`, `Description` (≤500), `ExternalKey` (≤100). Success = 201 with `{Guid}`.
- All list responses: `{ContinuationToken, Items[]}`. Errors: `{"Error": {"Code", "Message"}}`.
- Pagination contract: response includes `ContinuationToken` unless all records were returned; pass it back with all other parameters to get the next page.

## File structure

| File | Responsibility |
|---|---|
| `backend/src/Anela.Heblo.Domain/Features/Attendance/ILogetoClient.cs` | Client contract used by Application |
| `backend/src/Anela.Heblo.Domain/Features/Attendance/LogetoPerson.cs` | Person DTO (class, not record — project rule) |
| `backend/src/Anela.Heblo.Domain/Features/Attendance/LogetoActivity.cs` | Activity DTO |
| `backend/src/Anela.Heblo.Domain/Features/Attendance/LogetoActivityTypes.cs` | `"Work"` / `"Break"` string constants |
| `backend/src/Anela.Heblo.Domain/Features/Attendance/LogetoTimeEntry.cs` | TimeTracking record DTO |
| `backend/src/Anela.Heblo.Domain/Features/Attendance/LogetoCreateTimeEntryRequest.cs` | POST body DTO |
| `backend/src/Adapters/Anela.Heblo.Adapters.Logeto/Anela.Heblo.Adapters.Logeto.csproj` | Adapter project |
| `backend/src/Adapters/Anela.Heblo.Adapters.Logeto/LogetoOptions.cs` | `Logeto` config section (AccountName, AccessKey) |
| `backend/src/Adapters/Anela.Heblo.Adapters.Logeto/LogetoApiException.cs` | Typed error with status + API error code/message |
| `backend/src/Adapters/Anela.Heblo.Adapters.Logeto/LogetoClient.cs` | HTTP implementation of `ILogetoClient` |
| `backend/src/Adapters/Anela.Heblo.Adapters.Logeto/LogetoAdapterModule.cs` | `AddLogetoAdapter()` DI extension |
| `backend/src/Anela.Heblo.Application/Features/Attendance/AttendanceModule.cs` | Slice DI registration |
| `backend/src/Anela.Heblo.Application/Features/Attendance/BreakInsertionOptions.cs` | `Logeto:BreakInsertion` config |
| `backend/src/Anela.Heblo.Application/Features/Attendance/Services/TimeSlot.cs` | Immutable interval value type |
| `backend/src/Anela.Heblo.Application/Features/Attendance/Services/LogetoTimeConverter.cs` | Prague-local ↔ API time conversion (single conversion point) |
| `backend/src/Anela.Heblo.Application/Features/Attendance/Services/BreakSlotCalculator.cs` | Pure placement logic (no I/O) |
| `backend/src/Anela.Heblo.Application/Features/Attendance/Services/BreakInsertionService.cs` | Day-walk orchestration |
| `backend/src/Anela.Heblo.Application/Features/Attendance/Infrastructure/Jobs/BreakInsertionJob.cs` | `IRecurringJob` (auto-discovered) |
| `backend/test/Anela.Heblo.Adapters.Logeto.Tests/*` | Client tests (mocked `HttpMessageHandler`) |
| `backend/test/Anela.Heblo.Tests/Features/Attendance/*` | Calculator + service unit tests |

Modify: `backend/src/Anela.Heblo.API/Program.cs` (~line 126), `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` (project reference), `backend/src/Anela.Heblo.Application/ApplicationModule.cs` (~line 117), `backend/src/Anela.Heblo.API/appsettings.json`, `Anela.Heblo.sln`.

---

### Task 1: Verification spike (requires user's Logeto credentials — cannot be done autonomously)

**Files:**
- Create: `docs/superpowers/specs/2026-08-05-logeto-spike-results.md`

This task gates the rest of the plan. It answers three unknowns: (a) does `merge=true` split an overlapping work record, (b) are `From`/`To` UTC or local wall time, (c) the account's actual break activity name and the integration worker's guid.

- [ ] **Step 1: Obtain credentials from the user**

Ask the user for the Logeto `AccountName` (subdomain) and an `AccessKey`. If they don't have a key yet, they create one in the Logeto web app (or via podpora@vykazprace.cz). Do not commit these values anywhere.

- [ ] **Step 2: Fetch Activities and People**

```bash
ACCOUNT="<AccountName>" KEY="<AccessKey>"
curl -s -H "AccessKey: $KEY" "https://$ACCOUNT.logeto.com/api/v2/Activities" | python3 -m json.tool
curl -s -H "AccessKey: $KEY" "https://$ACCOUNT.logeto.com/api/v2/People" | python3 -m json.tool
```

Expected: JSON with `Items[]`. Record in the results doc: every activity's `Name`/`Type`/`Guid` (identify the Break-type activity to use, e.g. "Oběd"), and the guid + `Note` of the worker whose Note is `integration`.

- [ ] **Step 3: Fetch TimeTracking for a recent range and determine the time representation**

```bash
curl -s -H "AccessKey: $KEY" "https://$ACCOUNT.logeto.com/api/v2/TimeTracking?From=2026-08-01&To=2026-08-04" | python3 -m json.tool
```

Pick one record whose start time the user can see in the Logeto UI. Compare: if UI shows 08:00 (Prague, summer = UTC+2) and API `From` is `...T06:00:00Z`, times are **UTC** (`ApiTimesAreUtc: true`). If API shows `...T08:00:00Z`, times are **local wall time with a misleading Z** (`ApiTimesAreUtc: false`). Record the verdict.

- [ ] **Step 4: POST a break with merge=true onto a real ≥ 6 h work day**

Choose a day where the integration worker has a single work record ≥ 6 h and no break. Compute a 30-min window inside it (respect the Step 3 verdict; seconds must be `:00`):

```bash
curl -s -X POST -H "AccessKey: $KEY" -H "Content-Type: application/json" \
  -d '{"Person":"<worker-guid>","Activity":"<break-activity-guid>","Date":"<yyyy-MM-dd>","From":"<yyyy-MM-ddTHH:mm:00Z>","To":"<yyyy-MM-ddTHH:mm:00Z>","Billable":false,"Description":"API merge test","ExternalKey":"autobreak-spike-test"}' \
  "https://$ACCOUNT.logeto.com/api/v2/TimeTracking?merge=true"
```

Expected: HTTP 201 `{"Guid": "..."}`. Then the user checks the Logeto UI for that day.

- [ ] **Step 5: Record the merge verdict and STOP if it failed**

In `docs/superpowers/specs/2026-08-05-logeto-spike-results.md` record: did the work record split into `work | break | work`? Any leftovers/overlaps?

**If merge did NOT split the record:** STOP. Amend the design spec with the manual split path (PUT to shorten the work record + POST second work part + POST break) before continuing — Task 7's insert call and tests must then be extended accordingly. Delete the test break + restore the original record in the UI.

**If merge worked:** optionally keep the test break (it is a correct break) or let the user delete it.

- [ ] **Step 6: Commit the spike results doc**

```bash
git add docs/superpowers/specs/2026-08-05-logeto-spike-results.md
git commit -m "docs: record Logeto API spike results (merge behavior, time representation)"
```

---

### Task 2: Domain contract and DTOs

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Attendance/ILogetoClient.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Attendance/LogetoPerson.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Attendance/LogetoActivity.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Attendance/LogetoActivityTypes.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Attendance/LogetoTimeEntry.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Attendance/LogetoCreateTimeEntryRequest.cs`

Pure data + interface — no behavior, so no TDD cycle; the compiler is the test.

- [ ] **Step 1: Create the DTO classes** (classes, not records — project rule for external contracts)

`LogetoPerson.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Attendance;

public class LogetoPerson
{
    public Guid Guid { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Note { get; init; }
    public bool Inactive { get; init; }
}
```

`LogetoActivity.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Attendance;

public class LogetoActivity
{
    public Guid Guid { get; init; }
    public string? Name { get; init; }
    public string Type { get; init; } = string.Empty;
    public bool Inactive { get; init; }
}
```

`LogetoActivityTypes.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Attendance;

public static class LogetoActivityTypes
{
    public const string Work = "Work";
    public const string Break = "Break";
}
```

`LogetoTimeEntry.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Attendance;

public class LogetoTimeEntry
{
    public Guid Guid { get; init; }
    public Guid Person { get; init; }
    public DateOnly Date { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }

    /// <summary>Duration for records entered without a clock window, e.g. "08:04:00".</summary>
    public string? Hours { get; init; }

    public Guid Activity { get; init; }
    public string? Description { get; init; }
    public string? ExternalKey { get; init; }
}
```

`LogetoCreateTimeEntryRequest.cs` — `From`/`To` are pre-formatted strings because the API requires `:00` seconds and the UTC-vs-local formatting is decided by `LogetoTimeConverter` (Task 6):

```csharp
namespace Anela.Heblo.Domain.Features.Attendance;

public class LogetoCreateTimeEntryRequest
{
    public required Guid Person { get; init; }
    public required Guid Activity { get; init; }
    public required DateOnly Date { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public required bool Billable { get; init; }
    public string? Description { get; init; }
    public string? ExternalKey { get; init; }
}
```

`ILogetoClient.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Attendance;

public interface ILogetoClient
{
    Task<IReadOnlyList<LogetoActivity>> GetActivitiesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<LogetoPerson>> GetPeopleAsync(CancellationToken cancellationToken);

    /// <summary>Returns all records in the date range for all people (the API has no person filter).</summary>
    Task<IReadOnlyList<LogetoTimeEntry>> GetTimeTrackingAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Creates a time entry. With merge=true, overlapping records are merged/split by Logeto.</summary>
    Task CreateTimeEntryAsync(LogetoCreateTimeEntryRequest request, bool merge, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Attendance/
git commit -m "feat: add Logeto client contract and DTOs for attendance"
```

---

### Task 3: Adapter project scaffold

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Logeto/Anela.Heblo.Adapters.Logeto.csproj`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Logeto/LogetoOptions.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Logeto/LogetoApiException.cs`
- Modify: `Anela.Heblo.sln`

- [ ] **Step 1: Create the csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Anela.Heblo.Adapters.Logeto</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Http.Resilience" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Anela.Heblo.Domain\Anela.Heblo.Domain.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create LogetoOptions**

```csharp
namespace Anela.Heblo.Adapters.Logeto;

public class LogetoOptions
{
    public const string ConfigKey = "Logeto";

    /// <summary>Account subdomain: https://{AccountName}.logeto.com. Empty = adapter unconfigured.</summary>
    public string AccountName { get; set; } = string.Empty;

    /// <summary>API key sent in the AccessKey header. Comes from Key Vault (Logeto--AccessKey).</summary>
    public string AccessKey { get; set; } = string.Empty;

    public int RetryCount { get; set; } = 3;
    public int RequestTimeoutSeconds { get; set; } = 30;
}
```

- [ ] **Step 3: Create LogetoApiException**

```csharp
namespace Anela.Heblo.Adapters.Logeto;

public class LogetoApiException : Exception
{
    public int StatusCode { get; }
    public string? ApiErrorCode { get; }

    public LogetoApiException(int statusCode, string? apiErrorCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ApiErrorCode = apiErrorCode;
    }
}
```

- [ ] **Step 4: Add to solution and build**

```bash
dotnet sln Anela.Heblo.sln add backend/src/Adapters/Anela.Heblo.Adapters.Logeto/Anela.Heblo.Adapters.Logeto.csproj
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Logeto/Anela.Heblo.Adapters.Logeto.csproj
```

Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add Anela.Heblo.sln backend/src/Adapters/Anela.Heblo.Adapters.Logeto/
git commit -m "feat: scaffold Logeto adapter project"
```

---

### Task 4: LogetoClient (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Adapters.Logeto.Tests/Anela.Heblo.Adapters.Logeto.Tests.csproj`
- Create: `backend/test/Anela.Heblo.Adapters.Logeto.Tests/LogetoClientTests.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Logeto/LogetoClient.cs`
- Modify: `Anela.Heblo.sln`

- [ ] **Step 1: Create the test project**

`Anela.Heblo.Adapters.Logeto.Tests.csproj` (mirrors OpenMeteo.Tests):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="8.0.2" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="Moq" Version="4.20.70" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Adapters\Anela.Heblo.Adapters.Logeto\Anela.Heblo.Adapters.Logeto.csproj" />
  </ItemGroup>
</Project>
```

```bash
dotnet sln Anela.Heblo.sln add backend/test/Anela.Heblo.Adapters.Logeto.Tests/Anela.Heblo.Adapters.Logeto.Tests.csproj
```

- [ ] **Step 2: Write the failing tests**

`LogetoClientTests.cs` — a recording `HttpMessageHandler` stub, then behavior tests:

```csharp
using System.Net;
using System.Text;
using Anela.Heblo.Adapters.Logeto;
using Anela.Heblo.Domain.Features.Attendance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Logeto.Tests;

public class LogetoClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> RequestBodies { get; } = new();

        public StubHandler(params HttpResponseMessage[] responses)
            => _responses = new Queue<HttpResponseMessage>(responses);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return _responses.Dequeue();
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static LogetoClient CreateClient(StubHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://acme.logeto.com") };
        var options = Options.Create(new LogetoOptions
        {
            AccountName = "acme",
            AccessKey = "test-key"
        });
        return new LogetoClient(httpClient, options, NullLogger<LogetoClient>.Instance);
    }

    [Fact]
    public async Task GetActivitiesAsync_SendsAccessKeyHeaderAndCorrectPath()
    {
        var handler = new StubHandler(Json("""{"ContinuationToken":null,"Items":[]}"""));
        var client = CreateClient(handler);

        await client.GetActivitiesAsync(CancellationToken.None);

        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/v2/Activities");
        handler.Requests[0].Headers.GetValues("AccessKey").Should().ContainSingle()
            .Which.Should().Be("test-key");
    }

    [Fact]
    public async Task GetActivitiesAsync_DeserializesItems()
    {
        var guid = Guid.NewGuid();
        var handler = new StubHandler(Json($$"""
            {"ContinuationToken":null,"Items":[
              {"Guid":"{{guid}}","Name":"Oběd","Type":"Break","Inactive":false}
            ]}
            """));
        var client = CreateClient(handler);

        var activities = await client.GetActivitiesAsync(CancellationToken.None);

        activities.Should().HaveCount(1);
        activities[0].Guid.Should().Be(guid);
        activities[0].Name.Should().Be("Oběd");
        activities[0].Type.Should().Be(LogetoActivityTypes.Break);
    }

    [Fact]
    public async Task GetPeopleAsync_FollowsContinuationTokenAcrossPages()
    {
        var handler = new StubHandler(
            Json("""{"ContinuationToken":"page2","Items":[{"Guid":"11111111-1111-1111-1111-111111111111","Note":"integration","Inactive":false}]}"""),
            Json("""{"ContinuationToken":null,"Items":[{"Guid":"22222222-2222-2222-2222-222222222222","Note":null,"Inactive":false}]}"""));
        var client = CreateClient(handler);

        var people = await client.GetPeopleAsync(CancellationToken.None);

        people.Should().HaveCount(2);
        handler.Requests.Should().HaveCount(2);
        handler.Requests[1].RequestUri!.Query.Should().Contain("ContinuationToken=page2");
    }

    [Fact]
    public async Task GetTimeTrackingAsync_PassesDateRangeAndRepeatsItOnNextPages()
    {
        var handler = new StubHandler(
            Json("""{"ContinuationToken":"t2","Items":[]}"""),
            Json("""{"ContinuationToken":null,"Items":[]}"""));
        var client = CreateClient(handler);

        await client.GetTimeTrackingAsync(
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 4), CancellationToken.None);

        handler.Requests[0].RequestUri!.Query.Should().Contain("From=2026-08-01").And.Contain("To=2026-08-04");
        handler.Requests[1].RequestUri!.Query
            .Should().Contain("From=2026-08-01").And.Contain("To=2026-08-04").And.Contain("ContinuationToken=t2");
    }

    [Fact]
    public async Task CreateTimeEntryAsync_PostsMergeQueryAndPascalCaseBody()
    {
        var handler = new StubHandler(Json("""{"Guid":"33333333-3333-3333-3333-333333333333"}""", HttpStatusCode.Created));
        var client = CreateClient(handler);

        await client.CreateTimeEntryAsync(new LogetoCreateTimeEntryRequest
        {
            Person = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Activity = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Date = new DateOnly(2026, 8, 3),
            From = "2026-08-03T09:00:00Z",
            To = "2026-08-03T09:30:00Z",
            Billable = false,
            Description = "Automatická přestávka",
            ExternalKey = "autobreak-x-2026-08-03"
        }, merge: true, CancellationToken.None);

        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.PathAndQuery.Should().Be("/api/v2/TimeTracking?merge=true");
        handler.RequestBodies[0].Should().Contain("\"Person\"").And.Contain("\"Billable\":false");
        handler.RequestBodies[0].Should().NotContain("\"Hours\"", "null members must be omitted");
    }

    [Fact]
    public async Task ErrorResponse_ThrowsLogetoApiExceptionWithApiMessage()
    {
        var handler = new StubHandler(Json(
            """{"Error":{"Code":"InvalidTime","Message":"Seconds must be zero"}}""",
            HttpStatusCode.BadRequest));
        var client = CreateClient(handler);

        var act = () => client.GetActivitiesAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<LogetoApiException>();
        ex.Which.StatusCode.Should().Be(400);
        ex.Which.ApiErrorCode.Should().Be("InvalidTime");
        ex.Which.Message.Should().Contain("Seconds must be zero");
    }

    [Fact]
    public async Task RepeatedContinuationToken_StopsInsteadOfLoopingForever()
    {
        var handler = new StubHandler(
            Json("""{"ContinuationToken":"same","Items":[]}"""),
            Json("""{"ContinuationToken":"same","Items":[]}"""));
        var client = CreateClient(handler);

        var people = await client.GetPeopleAsync(CancellationToken.None);

        people.Should().BeEmpty();
        handler.Requests.Should().HaveCount(2);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Logeto.Tests --filter LogetoClientTests`
Expected: FAIL — `LogetoClient` does not exist (compile error).

- [ ] **Step 4: Implement LogetoClient**

`LogetoClient.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using Anela.Heblo.Domain.Features.Attendance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Logeto;

public class LogetoClient : ILogetoClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<LogetoClient> _logger;

    public LogetoClient(
        HttpClient httpClient,
        IOptions<LogetoOptions> options,
        ILogger<LogetoClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        if (!_httpClient.DefaultRequestHeaders.Contains("AccessKey"))
        {
            _httpClient.DefaultRequestHeaders.Add("AccessKey", options.Value.AccessKey);
        }
    }

    public Task<IReadOnlyList<LogetoActivity>> GetActivitiesAsync(CancellationToken cancellationToken) =>
        GetPagedAsync<LogetoActivity>("/api/v2/Activities", baseQuery: null, cancellationToken);

    public Task<IReadOnlyList<LogetoPerson>> GetPeopleAsync(CancellationToken cancellationToken) =>
        GetPagedAsync<LogetoPerson>("/api/v2/People", baseQuery: null, cancellationToken);

    public Task<IReadOnlyList<LogetoTimeEntry>> GetTimeTrackingAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        GetPagedAsync<LogetoTimeEntry>(
            "/api/v2/TimeTracking",
            $"From={from:yyyy-MM-dd}&To={to:yyyy-MM-dd}",
            cancellationToken);

    public async Task CreateTimeEntryAsync(
        LogetoCreateTimeEntryRequest request, bool merge, CancellationToken cancellationToken)
    {
        var url = $"/api/v2/TimeTracking?merge={(merge ? "true" : "false")}";
        var response = await _httpClient.PostAsJsonAsync(url, request, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private class Page<T>
    {
        public string? ContinuationToken { get; init; }
        public List<T>? Items { get; init; }
    }

    private class ErrorEnvelope
    {
        public ErrorBody? Error { get; init; }

        public class ErrorBody
        {
            public string? Code { get; init; }
            public string? Message { get; init; }
        }
    }

    private async Task<IReadOnlyList<T>> GetPagedAsync<T>(
        string path, string? baseQuery, CancellationToken cancellationToken)
    {
        var items = new List<T>();
        string? token = null;
        string? previousToken = null;

        do
        {
            var query = string.Join("&", new[]
            {
                baseQuery,
                token is null ? null : $"ContinuationToken={Uri.EscapeDataString(token)}"
            }.Where(q => !string.IsNullOrEmpty(q)));

            var url = string.IsNullOrEmpty(query) ? path : $"{path}?{query}";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            var page = await response.Content.ReadFromJsonAsync<Page<T>>(JsonOptions, cancellationToken)
                ?? throw new LogetoApiException(200, null, $"Logeto returned an empty body for {path}");

            items.AddRange(page.Items ?? new List<T>());

            previousToken = token;
            token = page.ContinuationToken;
        } while (!string.IsNullOrEmpty(token) && token != previousToken);

        return items;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        string? code = null;
        var message = $"Logeto API returned {(int)response.StatusCode}";

        try
        {
            var envelope = JsonSerializer.Deserialize<ErrorEnvelope>(body, JsonOptions);
            if (envelope?.Error is not null)
            {
                code = envelope.Error.Code;
                message = $"{message}: {envelope.Error.Message}";
            }
        }
        catch (JsonException)
        {
            _logger.LogWarning("Logeto error body was not valid JSON: {Body}", body);
        }

        throw new LogetoApiException((int)response.StatusCode, code, message);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Logeto.Tests --filter LogetoClientTests`
Expected: PASS (7 tests).

- [ ] **Step 6: Commit**

```bash
git add Anela.Heblo.sln backend/test/Anela.Heblo.Adapters.Logeto.Tests/ backend/src/Adapters/Anela.Heblo.Adapters.Logeto/LogetoClient.cs
git commit -m "feat: add LogetoClient with pagination, error mapping, and merge support"
```

---

### Task 5: Adapter DI module and API wiring

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Logeto/LogetoAdapterModule.cs`
- Modify: `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` (add project reference alongside the other adapter references)
- Modify: `backend/src/Anela.Heblo.API/Program.cs` — add after line 125 (`builder.Services.AddOpenMeteoAdapter(...)`)
- Modify: `backend/src/Anela.Heblo.API/appsettings.json` — add `Logeto` section

- [ ] **Step 1: Create the module** (resilience pattern copied from HomeAssistant, `HomeAssistantAdapterServiceCollectionExtensions.cs`)

`LogetoAdapterModule.cs`:

```csharp
using Anela.Heblo.Domain.Features.Attendance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace Anela.Heblo.Adapters.Logeto;

public static class LogetoAdapterModule
{
    public static IServiceCollection AddLogetoAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<LogetoOptions>()
            .Bind(configuration.GetSection(LogetoOptions.ConfigKey));

        var clientBuilder = services.AddHttpClient<LogetoClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<LogetoOptions>>().Value;

            if (string.IsNullOrWhiteSpace(options.AccountName))
            {
                // Logeto not configured — the nightly job is disabled by default and any
                // accidental call fails fast with an invalid request URI.
                return;
            }

            client.BaseAddress = new Uri($"https://{options.AccountName}.logeto.com");
            // Per-attempt timeout is enforced by the resilience handler below.
            // Setting HttpClient.Timeout would cancel the entire retry chain.
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        clientBuilder.AddResilienceHandler("logeto", (builder, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptions<LogetoOptions>>().Value;

            builder
                .AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = options.RetryCount,
                    Delay = TimeSpan.FromSeconds(1),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true
                })
                .AddTimeout(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
        });

        services.AddTransient<ILogetoClient>(sp => sp.GetRequiredService<LogetoClient>());

        return services;
    }
}
```

- [ ] **Step 2: Wire into the API project**

In `Anela.Heblo.API.csproj`, add next to the other adapter `<ProjectReference>` entries:

```xml
<ProjectReference Include="..\Adapters\Anela.Heblo.Adapters.Logeto\Anela.Heblo.Adapters.Logeto.csproj" />
```

In `Program.cs`, directly after `builder.Services.AddOpenMeteoAdapter(builder.Configuration);` (line 125):

```csharp
builder.Services.AddLogetoAdapter(builder.Configuration);
```

Add the matching `using Anela.Heblo.Adapters.Logeto;` to Program.cs's using block.

- [ ] **Step 3: Add the config section to appsettings.json**

Next to the existing top-level sections (e.g. `"WeatherForecast"` at line ~572). `BreakActivityName` and `StartDate` values come from the Task 1 spike results doc — fill in the real activity name; `ApiTimesAreUtc` per the spike verdict:

```json
"Logeto": {
  "AccountName": "",
  "AccessKey": "",
  "BreakInsertion": {
    "StartDate": "2026-08-01",
    "NoteMarker": "integration",
    "BreakActivityName": "Oběd",
    "PreferredWindowStart": "11:00",
    "BreakDurationMinutes": 30,
    "MinWorkHours": 6,
    "ApiTimesAreUtc": true
  }
}
```

- [ ] **Step 4: Store real credentials**

Local dev — edit the user-secrets `secrets.json` for the API project directly (project convention: never `dotnet user-secrets set`), adding:

```json
"Logeto": {
  "AccountName": "<real account>",
  "AccessKey": "<real key>"
}
```

Staging (when ready to enable):

```bash
az keyvault secret set --vault-name kv-heblo-stg --name "Logeto--AccessKey" --value "<real key>"
az keyvault secret set --vault-name kv-heblo-stg --name "Logeto--AccountName" --value "<real account>"
```

- [ ] **Step 5: Build and commit**

Run: `dotnet build`
Expected: Build succeeded (whole solution).

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Logeto/LogetoAdapterModule.cs backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj backend/src/Anela.Heblo.API/Program.cs backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat: register Logeto adapter with resilience and configuration"
```

---

### Task 6: TimeSlot, LogetoTimeConverter, and BreakSlotCalculator (TDD)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Attendance/Services/TimeSlot.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Attendance/Services/LogetoTimeConverter.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Attendance/Services/BreakSlotCalculator.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Attendance/BreakSlotCalculatorTests.cs`

- [ ] **Step 1: Write the failing tests**

`BreakSlotCalculatorTests.cs` (uses local wall-clock `DateTime`s; `Slot` helper builds a `TimeSlot` on 2026-08-03):

```csharp
using Anela.Heblo.Application.Features.Attendance.Services;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.Attendance;

public class BreakSlotCalculatorTests
{
    private static readonly TimeSpan BreakDuration = TimeSpan.FromMinutes(30);

    private static TimeSlot Slot(int fromHour, int fromMin, int toHour, int toMin) => new(
        new DateTime(2026, 8, 3, fromHour, fromMin, 0),
        new DateTime(2026, 8, 3, toHour, toMin, 0));

    private static readonly TimeSlot Preferred = Slot(11, 0, 11, 30);

    [Fact]
    public void ReturnsPreferredWindow_WhenFullyInsideAWorkSegment()
    {
        var slot = BreakSlotCalculator.ComputeBreakSlot(
            new[] { Slot(8, 0, 16, 30) }, Preferred, BreakDuration);

        slot.Should().Be(Preferred);
    }

    [Fact]
    public void FallsBackToMidpoint_WhenWorkStartsInsidePreferredWindow()
    {
        // Work 11:15–19:15 (8h) — preferred window is not fully inside.
        // Center: 11:15 + (8:00 − 0:30)/2 = 11:15 + 3:45 = 15:00 → 15:00–15:30.
        var slot = BreakSlotCalculator.ComputeBreakSlot(
            new[] { Slot(11, 15, 19, 15) }, Preferred, BreakDuration);

        slot.Should().Be(Slot(15, 0, 15, 30));
    }

    [Fact]
    public void FallsBackToMidpoint_ForAfternoonShift()
    {
        // Work 13:00–20:00 (7h) — center: 16:15–16:45.
        var slot = BreakSlotCalculator.ComputeBreakSlot(
            new[] { Slot(13, 0, 20, 0) }, Preferred, BreakDuration);

        slot.Should().Be(Slot(16, 15, 16, 45));
    }

    [Fact]
    public void PreferredWindowTouchingSegmentStart_DoesNotCount_BreakMustInterrupt()
    {
        // Work starts exactly at 11:00 — a break at 11:00 would sit at the shift edge,
        // not interrupt it. Center of 11:00–19:00 → 14:45–15:15.
        var slot = BreakSlotCalculator.ComputeBreakSlot(
            new[] { Slot(11, 0, 19, 0) }, Preferred, BreakDuration);

        slot.Should().Be(Slot(14, 45, 15, 15));
    }

    [Fact]
    public void PicksLongestSegment_WhenMultipleSegmentsExist()
    {
        // 6:00–8:00 (2h) and 9:00–14:00 (5h): longest is 9:00–14:00, center 11:15–11:45.
        // Preferred 11:00–11:30 IS inside 9:00–14:00, so preferred wins.
        var slot = BreakSlotCalculator.ComputeBreakSlot(
            new[] { Slot(6, 0, 8, 0), Slot(9, 0, 14, 0) }, Preferred, BreakDuration);

        slot.Should().Be(Preferred);
    }

    [Fact]
    public void PicksLongestSegment_WhenPreferredWindowIsInNoSegment()
    {
        // 6:00–10:00 (4h) and 12:00–18:00 (6h): preferred 11:00–11:30 in a gap.
        // Longest 12:00–18:00 → center 14:45–15:15.
        var slot = BreakSlotCalculator.ComputeBreakSlot(
            new[] { Slot(6, 0, 10, 0), Slot(12, 0, 18, 0) }, Preferred, BreakDuration);

        slot.Should().Be(Slot(14, 45, 15, 15));
    }

    [Fact]
    public void RoundsMidpointToNearestFiveMinutes()
    {
        // Work 8:07–16:33 → duration 8:26, center start = 8:07 + 3:58 = 12:05 (already rounds cleanly);
        // use 8:06–16:33 → center start 12:04:30 → rounds to 12:05.
        var slot = BreakSlotCalculator.ComputeBreakSlot(
            new[] { Slot(8, 6, 16, 33) }, Preferred, BreakDuration);

        slot!.Start.Minute.Should().Be(5);
        slot.Start.Hour.Should().Be(12);
    }

    [Fact]
    public void ReturnsNull_WhenNoSegmentCanFitTheBreakWithMargins()
    {
        // Longest segment 35 min < 30 min break + 5 min margin each side.
        var slot = BreakSlotCalculator.ComputeBreakSlot(
            new[] { Slot(9, 0, 9, 35) }, Preferred, BreakDuration);

        slot.Should().BeNull();
    }

    [Fact]
    public void ReturnsNull_ForEmptySegments()
    {
        BreakSlotCalculator.ComputeBreakSlot(
            Array.Empty<TimeSlot>(), Preferred, BreakDuration).Should().BeNull();
    }

    [Fact]
    public void BuildSegments_MergesOverlappingAndAdjacentIntervals()
    {
        var segments = BreakSlotCalculator.BuildSegments(new[]
        {
            Slot(8, 0, 12, 0),
            Slot(12, 0, 14, 0),   // adjacent — merges
            Slot(13, 30, 15, 0),  // overlapping — merges
            Slot(16, 0, 17, 0)    // separate
        });

        segments.Should().HaveCount(2);
        segments[0].Should().Be(Slot(8, 0, 15, 0));
        segments[1].Should().Be(Slot(16, 0, 17, 0));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter BreakSlotCalculatorTests`
Expected: FAIL — compile error, types don't exist.

- [ ] **Step 3: Implement**

`TimeSlot.cs` (internal domain type — record allowed by project rules):

```csharp
namespace Anela.Heblo.Application.Features.Attendance.Services;

/// <summary>Half-open local-time interval [Start, End). Times are Prague wall clock.</summary>
public sealed record TimeSlot(DateTime Start, DateTime End)
{
    public TimeSpan Duration => End - Start;
}
```

`LogetoTimeConverter.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Attendance.Services;

/// <summary>
/// Single conversion point between Logeto API timestamps and Prague wall-clock time.
/// The API's From/To representation (UTC vs local-with-Z) was determined by the
/// verification spike — see docs/superpowers/specs/2026-08-05-logeto-spike-results.md.
/// </summary>
public static class LogetoTimeConverter
{
    public static readonly TimeZoneInfo PragueTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");

    public static DateTime ToPragueLocal(DateTimeOffset apiTime, bool apiTimesAreUtc) =>
        apiTimesAreUtc
            ? TimeZoneInfo.ConvertTime(apiTime, PragueTimeZone).DateTime
            : apiTime.DateTime;

    /// <summary>Formats a Prague wall-clock time for the API. Seconds are always :00 (API requirement).</summary>
    public static string ToApiTime(DateTime pragueLocal, bool apiTimesAreUtc)
    {
        if (!apiTimesAreUtc)
        {
            return pragueLocal.ToString("yyyy-MM-ddTHH:mm:00");
        }

        var utc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(pragueLocal, DateTimeKind.Unspecified), PragueTimeZone);
        return utc.ToString("yyyy-MM-ddTHH:mm:00Z");
    }
}
```

`BreakSlotCalculator.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Attendance.Services;

public static class BreakSlotCalculator
{
    private static readonly TimeSpan Rounding = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan EdgeMargin = TimeSpan.FromMinutes(5);

    /// <summary>Merges overlapping/adjacent work intervals into continuous segments, sorted by start.</summary>
    public static IReadOnlyList<TimeSlot> BuildSegments(IEnumerable<TimeSlot> intervals)
    {
        var sorted = intervals.OrderBy(i => i.Start).ToList();
        var result = new List<TimeSlot>();

        foreach (var interval in sorted)
        {
            if (result.Count > 0 && interval.Start <= result[^1].End)
            {
                if (interval.End > result[^1].End)
                {
                    result[^1] = new TimeSlot(result[^1].Start, interval.End);
                }
            }
            else
            {
                result.Add(interval);
            }
        }

        return result;
    }

    /// <summary>
    /// Picks where the break goes. Preferred window wins when it lies strictly inside a
    /// segment (a break touching the segment edge would not interrupt the work).
    /// Otherwise the break is centered in the longest segment, rounded to 5 minutes.
    /// Returns null when no segment can contain the break away from its edges.
    /// </summary>
    public static TimeSlot? ComputeBreakSlot(
        IReadOnlyList<TimeSlot> workSegments,
        TimeSlot preferredWindow,
        TimeSpan breakDuration)
    {
        if (workSegments.Count == 0)
        {
            return null;
        }

        foreach (var segment in workSegments)
        {
            if (preferredWindow.Start > segment.Start && preferredWindow.End < segment.End)
            {
                return preferredWindow;
            }
        }

        var longest = workSegments.MaxBy(s => s.Duration)!;
        if (longest.Duration < breakDuration + EdgeMargin + EdgeMargin)
        {
            return null;
        }

        var center = longest.Start + (longest.Duration - breakDuration) / 2;
        var rounded = RoundToNearest(center, Rounding);
        var earliest = longest.Start + EdgeMargin;
        var latest = longest.End - breakDuration - EdgeMargin;
        var start = rounded < earliest ? earliest : (rounded > latest ? latest : rounded);

        return new TimeSlot(start, start + breakDuration);
    }

    private static DateTime RoundToNearest(DateTime value, TimeSpan interval)
    {
        var ticks = (long)Math.Round(value.Ticks / (double)interval.Ticks) * interval.Ticks;
        return new DateTime(ticks, value.Kind);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter BreakSlotCalculatorTests`
Expected: PASS (10 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Attendance/Services/ backend/test/Anela.Heblo.Tests/Features/Attendance/BreakSlotCalculatorTests.cs
git commit -m "feat: add break slot placement calculator with preferred-window and midpoint fallback"
```

---

### Task 7: BreakInsertionService (TDD)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Attendance/BreakInsertionOptions.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Attendance/Services/BreakInsertionService.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Attendance/BreakInsertionServiceTests.cs`

- [ ] **Step 1: Create the options class** (no test — plain data)

`BreakInsertionOptions.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Attendance;

public class BreakInsertionOptions
{
    public const string ConfigKey = "Logeto:BreakInsertion";

    /// <summary>First day of the daily walk (fixed start date; idempotent skipping keeps re-runs cheap).</summary>
    public DateOnly StartDate { get; set; } = new(2026, 8, 1);

    /// <summary>People whose Note equals this marker (trimmed, case-insensitive) are processed.</summary>
    public string NoteMarker { get; set; } = "integration";

    /// <summary>Name of the Break-type Logeto activity to insert (account-specific, e.g. "Oběd").</summary>
    public string BreakActivityName { get; set; } = string.Empty;

    /// <summary>Preferred break start, Prague wall clock.</summary>
    public TimeOnly PreferredWindowStart { get; set; } = new(11, 0);

    public int BreakDurationMinutes { get; set; } = 30;

    /// <summary>Daily worked-hours threshold (inclusive) that requires a break.</summary>
    public int MinWorkHours { get; set; } = 6;

    /// <summary>Whether API From/To timestamps are UTC (spike verdict). False = local wall time.</summary>
    public bool ApiTimesAreUtc { get; set; } = true;
}
```

- [ ] **Step 2: Write the failing service tests**

`BreakInsertionServiceTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Attendance;
using Anela.Heblo.Application.Features.Attendance.Services;
using Anela.Heblo.Domain.Features.Attendance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.Attendance;

public class BreakInsertionServiceTests
{
    private static readonly Guid WorkActivity = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BreakActivity = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Worker = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateOnly Day = new(2026, 8, 3);

    private readonly Mock<ILogetoClient> _client = new();

    private BreakInsertionService CreateService(BreakInsertionOptions? options = null)
    {
        options ??= new BreakInsertionOptions
        {
            StartDate = new DateOnly(2026, 8, 1),
            BreakActivityName = "Oběd",
            ApiTimesAreUtc = false // tests use wall-clock times directly for readability
        };

        // Fixed "now": 2026-08-04 08:00 Prague — so "yesterday" = 2026-08-03.
        var timeProvider = new Mock<TimeProvider>();
        timeProvider.Setup(t => t.GetUtcNow())
            .Returns(new DateTimeOffset(2026, 8, 4, 6, 0, 0, TimeSpan.Zero));

        return new BreakInsertionService(
            _client.Object,
            Options.Create(options),
            timeProvider.Object,
            NullLogger<BreakInsertionService>.Instance);
    }

    private void SetupDefaults(params LogetoTimeEntry[] entries)
    {
        _client.Setup(c => c.GetActivitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoActivity>
            {
                new() { Guid = WorkActivity, Name = "Práce", Type = LogetoActivityTypes.Work },
                new() { Guid = BreakActivity, Name = "Oběd", Type = LogetoActivityTypes.Break }
            });

        _client.Setup(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoPerson>
            {
                new() { Guid = Worker, Note = "integration", Inactive = false },
                new() { Guid = Guid.NewGuid(), Note = "somebody else", Inactive = false }
            });

        _client.Setup(c => c.GetTimeTrackingAsync(
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries.ToList());
    }

    private static LogetoTimeEntry WorkEntry(int fromHour, int fromMin, int toHour, int toMin) => new()
    {
        Guid = Guid.NewGuid(),
        Person = Worker,
        Date = Day,
        Activity = WorkActivity,
        From = new DateTimeOffset(2026, 8, 3, fromHour, fromMin, 0, TimeSpan.Zero),
        To = new DateTimeOffset(2026, 8, 3, toHour, toMin, 0, TimeSpan.Zero)
    };

    [Fact]
    public async Task InsertsBreak_ForEightHourDayWithoutBreak()
    {
        SetupDefaults(WorkEntry(8, 0, 16, 30));

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.BreaksInserted.Should().Be(1);
        _client.Verify(c => c.CreateTimeEntryAsync(
            It.Is<LogetoCreateTimeEntryRequest>(r =>
                r.Person == Worker
                && r.Activity == BreakActivity
                && r.Date == Day
                && r.From == "2026-08-03T11:00:00"
                && r.To == "2026-08-03T11:30:00"
                && r.Billable == false
                && r.ExternalKey == $"autobreak-{Worker}-2026-08-03"),
            true,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SkipsDay_WhenAnyBreakAlreadyExists()
    {
        var existingBreak = new LogetoTimeEntry
        {
            Guid = Guid.NewGuid(), Person = Worker, Date = Day, Activity = BreakActivity,
            From = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero),
            To = new DateTimeOffset(2026, 8, 3, 12, 10, 0, TimeSpan.Zero)
        };
        SetupDefaults(WorkEntry(8, 0, 16, 30), existingBreak);

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.BreaksInserted.Should().Be(0);
        summary.SkippedExistingBreak.Should().Be(1);
        _client.Verify(c => c.CreateTimeEntryAsync(
            It.IsAny<LogetoCreateTimeEntryRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SkipsDay_BelowSixHours()
    {
        SetupDefaults(WorkEntry(8, 0, 13, 30)); // 5.5 h

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.BreaksInserted.Should().Be(0);
        summary.SkippedBelowThreshold.Should().Be(1);
    }

    [Fact]
    public async Task InsertsBreak_AtExactlySixHours()
    {
        SetupDefaults(WorkEntry(8, 0, 14, 0)); // exactly 6 h — inclusive threshold

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.BreaksInserted.Should().Be(1);
    }

    [Fact]
    public async Task SkipsDayWithWarning_WhenThresholdOnlyReachedByHoursOnlyRecords()
    {
        var hoursOnly = new LogetoTimeEntry
        {
            Guid = Guid.NewGuid(), Person = Worker, Date = Day, Activity = WorkActivity,
            Hours = "05:00:00" // no From/To window
        };
        SetupDefaults(WorkEntry(8, 0, 10, 0), hoursOnly); // 2h windowed + 5h duration-only = 7h total

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.BreaksInserted.Should().Be(0);
        summary.SkippedHoursOnly.Should().Be(1);
    }

    [Fact]
    public async Task IgnoresPeople_WithoutTheNoteMarker()
    {
        SetupDefaults(WorkEntry(8, 0, 16, 30));
        _client.Setup(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoPerson>
            {
                new() { Guid = Worker, Note = "  Integration  ", Inactive = false } // trims + case-insensitive
            });

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.BreaksInserted.Should().Be(1);
    }

    [Fact]
    public async Task Throws_WhenBreakActivityNameNotFound()
    {
        SetupDefaults();
        _client.Setup(c => c.GetActivitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoActivity>
            {
                new() { Guid = WorkActivity, Name = "Práce", Type = LogetoActivityTypes.Work }
            });

        var act = () => CreateService().RunAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Oběd*");
    }

    [Fact]
    public async Task ContinuesWithNextDay_WhenOneInsertFails()
    {
        var day2 = new DateOnly(2026, 8, 2);
        var entryDay2 = new LogetoTimeEntry
        {
            Guid = Guid.NewGuid(), Person = Worker, Date = day2, Activity = WorkActivity,
            From = new DateTimeOffset(2026, 8, 2, 8, 0, 0, TimeSpan.Zero),
            To = new DateTimeOffset(2026, 8, 2, 16, 30, 0, TimeSpan.Zero)
        };
        SetupDefaults(entryDay2, WorkEntry(8, 0, 16, 30));

        _client.SetupSequence(c => c.CreateTimeEntryAsync(
                It.IsAny<LogetoCreateTimeEntryRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"))
            .Returns(Task.CompletedTask);

        var summary = await CreateService().RunAsync(CancellationToken.None);

        summary.BreaksInserted.Should().Be(1);
        summary.Failed.Should().Be(1);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter BreakInsertionServiceTests`
Expected: FAIL — `BreakInsertionService` does not exist.

- [ ] **Step 4: Implement the service**

`BreakInsertionService.cs`:

```csharp
using Anela.Heblo.Domain.Features.Attendance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Attendance.Services;

public class BreakInsertionService
{
    private readonly ILogetoClient _client;
    private readonly IOptions<BreakInsertionOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BreakInsertionService> _logger;

    public BreakInsertionService(
        ILogetoClient client,
        IOptions<BreakInsertionOptions> options,
        TimeProvider timeProvider,
        ILogger<BreakInsertionService> logger)
    {
        _client = client;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<BreakInsertionSummary> RunAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var summary = new BreakInsertionSummary();

        var activities = await _client.GetActivitiesAsync(cancellationToken);
        var breakActivity = activities.FirstOrDefault(a =>
                a.Type == LogetoActivityTypes.Break
                && string.Equals(a.Name?.Trim(), options.BreakActivityName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Break activity '{options.BreakActivityName}' not found in Logeto or is not of type Break.");

        var typeByActivity = activities.ToDictionary(a => a.Guid, a => a.Type);

        var people = (await _client.GetPeopleAsync(cancellationToken))
            .Where(p => !p.Inactive
                && string.Equals(p.Note?.Trim(), options.NoteMarker, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (people.Count == 0)
        {
            _logger.LogWarning("No active Logeto workers found with note marker '{NoteMarker}'", options.NoteMarker);
            return summary;
        }

        var pragueNow = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), LogetoTimeConverter.PragueTimeZone);
        var lastDay = DateOnly.FromDateTime(pragueNow.Date).AddDays(-1);

        var entries = await _client.GetTimeTrackingAsync(options.StartDate, lastDay, cancellationToken);

        foreach (var person in people)
        {
            var days = entries
                .Where(e => e.Person == person.Guid && e.Date >= options.StartDate && e.Date <= lastDay)
                .GroupBy(e => e.Date)
                .OrderBy(g => g.Key);

            foreach (var day in days)
            {
                try
                {
                    await ProcessDayAsync(
                        person, day.Key, day.ToList(), typeByActivity, breakActivity, options, summary, cancellationToken);
                }
                catch (Exception ex)
                {
                    summary.Failed++;
                    _logger.LogError(ex,
                        "Failed to insert break for person {PersonGuid} on {Date}", person.Guid, day.Key);
                }
            }
        }

        _logger.LogInformation(
            "Break insertion finished: {Scanned} days scanned, {Inserted} breaks inserted, " +
            "{ExistingBreak} had a break, {BelowThreshold} below threshold, {HoursOnly} hours-only, " +
            "{NoSlot} no slot, {Failed} failed",
            summary.DaysScanned, summary.BreaksInserted, summary.SkippedExistingBreak,
            summary.SkippedBelowThreshold, summary.SkippedHoursOnly, summary.SkippedNoSlot, summary.Failed);

        return summary;
    }

    private async Task ProcessDayAsync(
        LogetoPerson person,
        DateOnly date,
        IReadOnlyList<LogetoTimeEntry> dayEntries,
        IReadOnlyDictionary<Guid, string> typeByActivity,
        LogetoActivity breakActivity,
        BreakInsertionOptions options,
        BreakInsertionSummary summary,
        CancellationToken cancellationToken)
    {
        summary.DaysScanned++;

        if (dayEntries.Any(e => typeByActivity.GetValueOrDefault(e.Activity) == LogetoActivityTypes.Break))
        {
            summary.SkippedExistingBreak++;
            return;
        }

        var workEntries = dayEntries
            .Where(e => typeByActivity.GetValueOrDefault(e.Activity) == LogetoActivityTypes.Work)
            .ToList();

        var windowed = workEntries
            .Where(e => e.From.HasValue && e.To.HasValue && e.To > e.From)
            .ToList();

        var windowedTotal = windowed.Aggregate(TimeSpan.Zero, (sum, e) => sum + (e.To!.Value - e.From!.Value));
        var hoursOnlyTotal = workEntries
            .Where(e => !e.From.HasValue || !e.To.HasValue)
            .Aggregate(TimeSpan.Zero, (sum, e) =>
                TimeSpan.TryParse(e.Hours, out var h) ? sum + h : sum);

        var threshold = TimeSpan.FromHours(options.MinWorkHours);

        if (windowedTotal + hoursOnlyTotal < threshold)
        {
            summary.SkippedBelowThreshold++;
            return;
        }

        if (windowedTotal < threshold)
        {
            summary.SkippedHoursOnly++;
            _logger.LogWarning(
                "Day {Date} for person {PersonGuid} reaches the threshold only with duration-only records; " +
                "cannot place a break automatically — fix manually in Logeto.",
                date, person.Guid);
            return;
        }

        var segments = BreakSlotCalculator.BuildSegments(windowed.Select(e => new TimeSlot(
            LogetoTimeConverter.ToPragueLocal(e.From!.Value, options.ApiTimesAreUtc),
            LogetoTimeConverter.ToPragueLocal(e.To!.Value, options.ApiTimesAreUtc))));

        var breakDuration = TimeSpan.FromMinutes(options.BreakDurationMinutes);
        var preferredStart = date.ToDateTime(options.PreferredWindowStart);
        var preferred = new TimeSlot(preferredStart, preferredStart + breakDuration);

        var slot = BreakSlotCalculator.ComputeBreakSlot(segments, preferred, breakDuration);
        if (slot is null)
        {
            summary.SkippedNoSlot++;
            _logger.LogWarning(
                "No suitable break slot found for person {PersonGuid} on {Date} (segments too short)",
                person.Guid, date);
            return;
        }

        var request = new LogetoCreateTimeEntryRequest
        {
            Person = person.Guid,
            Activity = breakActivity.Guid,
            Date = date,
            From = LogetoTimeConverter.ToApiTime(slot.Start, options.ApiTimesAreUtc),
            To = LogetoTimeConverter.ToApiTime(slot.End, options.ApiTimesAreUtc),
            Billable = false,
            Description = "Automatická přestávka",
            ExternalKey = $"autobreak-{person.Guid}-{date:yyyy-MM-dd}"
        };

        await _client.CreateTimeEntryAsync(request, merge: true, cancellationToken);
        summary.BreaksInserted++;

        _logger.LogInformation(
            "Inserted {Minutes}-minute break {From}–{To} for person {PersonGuid} on {Date}",
            options.BreakDurationMinutes, request.From, request.To, person.Guid, date);
    }
}

public class BreakInsertionSummary
{
    public int DaysScanned { get; set; }
    public int BreaksInserted { get; set; }
    public int SkippedExistingBreak { get; set; }
    public int SkippedBelowThreshold { get; set; }
    public int SkippedHoursOnly { get; set; }
    public int SkippedNoSlot { get; set; }
    public int Failed { get; set; }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter BreakInsertionServiceTests`
Expected: PASS (8 tests).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Attendance/ backend/test/Anela.Heblo.Tests/Features/Attendance/BreakInsertionServiceTests.cs
git commit -m "feat: add break insertion day-walk service"
```

---

### Task 8: Recurring job and module wiring

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Attendance/Infrastructure/Jobs/BreakInsertionJob.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Attendance/AttendanceModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — add next to `services.AddMeetingTasksModule(configuration);` (~line 117)

- [ ] **Step 1: Create the job** (pattern: `PlaudPollingJob.cs`; jobs are auto-discovered via the `IRecurringJob` assembly scan in `AddRecurringJobs()`, per-job enablement via `IRecurringJobStatusChecker`)

`BreakInsertionJob.cs`:

```csharp
using Anela.Heblo.Application.Features.Attendance.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Attendance.Infrastructure.Jobs;

public class BreakInsertionJob : IRecurringJob
{
    private readonly BreakInsertionService _service;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<BreakInsertionJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "logeto-break-insertion",
        DisplayName = "Logeto — insert missing lunch breaks",
        Description = "Walks each opted-in worker's days in Logeto (Výkaz práce) and inserts a 30-minute " +
                      "break into any ≥6h working day that has none, splitting the work record via merge=true.",
        CronExpression = "0 3 * * *",
        DefaultIsEnabled = false
    };

    public BreakInsertionJob(
        BreakInsertionService service,
        IRecurringJobStatusChecker statusChecker,
        ILogger<BreakInsertionJob> logger)
    {
        _service = service;
        _statusChecker = statusChecker;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        _logger.LogInformation("Starting {JobName}", Metadata.JobName);
        await _service.RunAsync(cancellationToken);
    }
}
```

- [ ] **Step 2: Create the module**

`AttendanceModule.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Anela.Heblo.Application.Features.Attendance;

public static class AttendanceModule
{
    public static IServiceCollection AddAttendanceModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<BreakInsertionOptions>()
            .Bind(configuration.GetSection(BreakInsertionOptions.ConfigKey));

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<Services.BreakInsertionService>();

        // BreakInsertionJob is auto-discovered via the IRecurringJob assembly scan in AddRecurringJobs().

        return services;
    }
}
```

- [ ] **Step 3: Register in ApplicationModule.cs**

Add next to the other module registrations (~line 117), with the matching `using Anela.Heblo.Application.Features.Attendance;`:

```csharp
services.AddAttendanceModule(configuration);
```

- [ ] **Step 4: Build and run all touched tests**

```bash
dotnet build
dotnet test backend/test/Anela.Heblo.Tests --filter "BreakSlotCalculatorTests|BreakInsertionServiceTests" --no-build
dotnet test backend/test/Anela.Heblo.Adapters.Logeto.Tests --no-build
```

Expected: Build succeeded; all tests PASS. (If `dotnet test` hangs at 0 % CPU another worktree is running tests — re-run with `-p:UseSharedCompilation=false`.)

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Attendance/ backend/src/Anela.Heblo.Application/ApplicationModule.cs
git commit -m "feat: add nightly Logeto break insertion recurring job (disabled by default)"
```

---

### Task 9: Full validation

**Files:** none new.

- [ ] **Step 1: Backend validation gates** (project rule: build + format before done)

```bash
dotnet build
dotnet format --verify-no-changes
```

Expected: Build succeeded; no formatting changes needed. If `dotnet format` reports changes, run `dotnet format`, re-run `dotnet build`, and amend the previous commit or commit as `chore: dotnet format`.

- [ ] **Step 2: Run the BackgroundJobs contract tests** (registry/metadata reflection tests exist for recurring jobs)

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~BackgroundJobs" --no-build
```

Expected: PASS — the new job's metadata satisfies the existing contract tests.

- [ ] **Step 3: Run the complete backend test suite**

```bash
dotnet test --no-build -p:UseSharedCompilation=false
```

Expected: all green (the AccessMatrixGen crash, if it appears, is known non-fatal noise).

- [ ] **Step 4: Commit any stragglers and report**

```bash
git status
```

Expected: clean tree. Report the summary of what was built and the one manual follow-up: enable the `logeto-break-insertion` job (BackgroundJobs admin) after credentials are in Key Vault and the spike doc confirms merge behavior.

**Frontend note:** no FE changes in this feature — `npm run build` / `npm run lint` gates do not apply. No E2E changes (no UI surface).
