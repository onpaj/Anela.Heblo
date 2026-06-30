### task: create-hangfire-activity-filter

Create a global Hangfire server filter that opens a named `Activity` per job execution so App Insights can correlate Hangfire telemetry under the correct `operation_Name`.

**Files:**
- Create: `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobActivityFilter.cs`
- Modify: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`

- [ ] **Step 1:** Create the filter file. The `ActivitySource` name `"Anela.Heblo.Hangfire"` must be registered with the App Insights SDK (version 2.22 already supports custom `ActivitySource` via `AddActivitySourceListener` — no extra package needed).

  Full file content:
  ```csharp
  using System.Diagnostics;
  using Hangfire.Common;
  using Hangfire.Server;

  namespace Anela.Heblo.API.Infrastructure.Hangfire;

  /// <summary>
  /// Global Hangfire server filter that starts a named <see cref="Activity"/> for each job
  /// execution so Application Insights can associate Hangfire telemetry under the correct
  /// operation_Name instead of the generic "PUT" dependency.
  /// </summary>
  public sealed class HangfireJobActivityFilter : JobFilterAttribute, IServerFilter
  {
      private static readonly ActivitySource Source = new("Anela.Heblo.Hangfire");

      public void OnPerforming(PerformingContext context)
      {
          var jobName = context.BackgroundJob.Job.Type.Name;
          var activity = Source.StartActivity($"Hangfire.Job.{jobName}", ActivityKind.Internal);
          if (activity is not null)
          {
              activity.SetTag("hangfire.job.id", context.BackgroundJob.Id);
              activity.SetTag("hangfire.job.type", context.BackgroundJob.Job.Type.FullName);
              context.Items["HangfireActivity"] = activity;
          }
      }

      public void OnPerformed(PerformedContext context)
      {
          if (context.Items.TryGetValue("HangfireActivity", out var obj) && obj is Activity activity)
          {
              if (context.Exception is not null)
                  activity.SetStatus(ActivityStatusCode.Error, context.Exception.Message);
              activity.Dispose();
          }
      }
  }
  ```

- [ ] **Step 2:** Register the filter as a global Hangfire filter in `ServiceCollectionExtensions.cs`. Open the file and find the `AddHangfireServices` method (around line 273). The filter must be added **after** `services.AddHangfire(...)` is called, using `GlobalJobFilters.Filters.Add`. Add it at the end of the `AddHangfireServices` method, just before `return services;`:

  ```csharp
  // Register global Hangfire server filter for Activity-based telemetry
  GlobalJobFilters.Filters.Add(new HangfireJobActivityFilter());
  ```

  The `using Hangfire;` directive is already present at the top of `ServiceCollectionExtensions.cs` (line 14). Add `using Anela.Heblo.API.Infrastructure.Hangfire;` if not already present — check the existing usings block (lines 1–31).

- [ ] **Step 3:** Register the ActivitySource with the App Insights SDK so it listens to the `"Anela.Heblo.Hangfire"` source. Open `ServiceCollectionExtensions.cs`, find `AddApplicationInsightsServices` (line 37). Inside the `if (!string.IsNullOrEmpty(appInsightsConnectionString))` block, after `services.AddOptimizedApplicationInsights(...)`, add:

  ```csharp
  services.Configure<Microsoft.ApplicationInsights.Extensibility.TelemetryConfiguration>(telemetryConfig =>
  {
      telemetryConfig.AddActivitySourceListener("Anela.Heblo.Hangfire");
  });
  ```

  > **Check first:** `TelemetryConfiguration.AddActivitySourceListener` was added in `Microsoft.ApplicationInsights.AspNetCore` 2.21. The csproj shows 2.22 — this is safe. If IntelliSense can't find it, check `Microsoft.ApplicationInsights` namespace — the method is on `TelemetryConfiguration`.

- [ ] **Step 4:** Build the API project.

  ```bash
  cd /home/user/worktrees/feature-3193-socket-exception-polly/backend
  dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj --no-restore -v q
  ```

  Expected: `Build succeeded.` 0 errors.

- [ ] **Step 5:** Commit.

  ```bash
  git add backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobActivityFilter.cs \
          backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs
  git commit -m "feat: add HangfireJobActivityFilter for Activity-based telemetry enrichment"
  ```

---