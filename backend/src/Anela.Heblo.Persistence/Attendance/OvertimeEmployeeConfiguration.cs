using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Attendance;

public class OvertimeEmployeeConfiguration : IEntityTypeConfiguration<OvertimeEmployee>
{
    public void Configure(EntityTypeBuilder<OvertimeEmployee> builder)
    {
        builder.ToTable("OvertimeEmployees", "public");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.PersonId).IsUnique();
        builder.Property(e => e.DisplayName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.BaselineHours).HasPrecision(8, 2);
    }
}
