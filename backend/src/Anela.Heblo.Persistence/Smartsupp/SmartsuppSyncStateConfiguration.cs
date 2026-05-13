using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Smartsupp;

public sealed class SmartsuppSyncStateConfiguration : IEntityTypeConfiguration<SmartsuppSyncState>
{
    public void Configure(EntityTypeBuilder<SmartsuppSyncState> builder)
    {
        builder.ToTable("SmartsuppSyncState", "public", t =>
            t.HasCheckConstraint("CK_SmartsuppSyncState_SingleRow", "\"Id\" = 1"));
        builder.HasKey(e => e.Id);
        builder.Property(e => e.LastSyncStartedAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.LastUpdatedAtSeen).HasColumnType("timestamp without time zone");
        builder.HasData(new SmartsuppSyncState
        {
            Id = 1,
            LastSyncStartedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)
        });
    }
}
