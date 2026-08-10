using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Attendance;

public class OvertimeMonthlyStatementConfiguration : IEntityTypeConfiguration<OvertimeMonthlyStatement>
{
    public void Configure(EntityTypeBuilder<OvertimeMonthlyStatement> builder)
    {
        builder.ToTable("OvertimeMonthlyStatements", "public");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.PersonId, e.Year, e.Month }).IsUnique();
        builder.Property(e => e.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.RequiredHours).HasPrecision(8, 2);
        builder.Property(e => e.WorkedHours).HasPrecision(8, 2);
        builder.Property(e => e.VacationHours).HasPrecision(8, 2);
        builder.Property(e => e.SickHours).HasPrecision(8, 2);
        builder.Property(e => e.DoctorHours).HasPrecision(8, 2);
        builder.Property(e => e.CompTimeHours).HasPrecision(8, 2);
        builder.Property(e => e.OtherAbsenceHours).HasPrecision(8, 2);
        builder.Property(e => e.DeltaHours).HasPrecision(8, 2);
        builder.Property(e => e.BalanceAfter).HasPrecision(8, 2);
        builder.Property(e => e.ClosedAtUtc).HasColumnType("timestamp without time zone");
        builder.Property(e => e.ClosedBy).HasMaxLength(200);
    }
}
