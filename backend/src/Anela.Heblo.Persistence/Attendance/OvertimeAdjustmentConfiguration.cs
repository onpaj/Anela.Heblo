using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Attendance;

public class OvertimeAdjustmentConfiguration : IEntityTypeConfiguration<OvertimeAdjustment>
{
    public void Configure(EntityTypeBuilder<OvertimeAdjustment> builder)
    {
        builder.ToTable("OvertimeAdjustments", "public");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.PersonId, e.Year, e.Month });
        builder.Property(e => e.Type).IsRequired().HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Hours).HasPrecision(8, 2);
        builder.Property(e => e.Note).IsRequired().HasMaxLength(500);
        builder.Property(e => e.CreatedAtUtc).IsRequired().HasColumnType("timestamp without time zone");
        builder.Property(e => e.CreatedBy).IsRequired().HasMaxLength(200);
    }
}
