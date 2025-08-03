using Anela.Heblo.Jobs;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Anela.Heblo.EntityFrameworkCore;

public static class RecurringJobsDbMapper
{
    public static ModelBuilder ConfigureRecurringJobs(this ModelBuilder builder)
    {
        builder.Entity<RecurringJob>(b =>
        {
            b.ToTable("Jobs", "dbo");
            b.HasKey(p => p.Id);
        });

        return builder;
    }
}