using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Smartsupp;

public class SmartsuppSyncStateConfiguration : IEntityTypeConfiguration<SmartsuppSyncState>
{
    public void Configure(EntityTypeBuilder<SmartsuppSyncState> builder)
    {
        builder.ToTable("SmartsuppSyncState", "public");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.LastUpdatedAtSeen).HasColumnType("timestamp without time zone");
        builder.Property(e => e.LastSyncStartedAt).HasColumnType("timestamp without time zone");
    }
}
