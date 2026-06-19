using System.Diagnostics;
using Hangfire.Common;
using Hangfire.Server;

namespace Anela.Heblo.API.Infrastructure.Hangfire;

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
