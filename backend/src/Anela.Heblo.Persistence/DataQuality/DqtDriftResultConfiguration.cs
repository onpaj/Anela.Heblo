using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.DataQuality;

public class DqtDriftResultConfiguration : IEntityTypeConfiguration<DqtDriftResult>
{
    public void Configure(EntityTypeBuilder<DqtDriftResult> builder)
    {
        builder.ToTable("DqtDriftResults", "public");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TestType).HasConversion<int>();
        builder.Property(x => x.EntityKey).IsRequired();
        builder.Property(x => x.Details).HasMaxLength(4000);
        builder.HasIndex(x => x.DqtRunId);
        builder.HasIndex(x => new { x.TestType, x.EntityKey });
    }
}
