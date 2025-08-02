using Anela.Heblo.Tasks;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Anela.Heblo.EntityFrameworkCore;

public static class ScheduledTaskDbMapper
{
    public static ModelBuilder ConfigureScheduledTasks(this ModelBuilder builder)
    {
        builder.Entity<ScheduledTask>(b =>
        {
            b.ToTable("ScheduledTask", "dbo");
            b.Property(x => x.TaskType).IsRequired().HasMaxLength(128);
        });

        return builder;
    }
}