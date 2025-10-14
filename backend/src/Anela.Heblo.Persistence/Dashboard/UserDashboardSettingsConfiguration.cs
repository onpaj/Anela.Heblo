using Anela.Heblo.Xcc.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Anela.Heblo.Persistence.Extensions;

namespace Anela.Heblo.Persistence.Dashboard;

public class UserDashboardSettingsConfiguration : IEntityTypeConfiguration<UserDashboardSettings>
{
    public void Configure(EntityTypeBuilder<UserDashboardSettings> builder)
    {
        builder.ToTable("UserDashboardSettings");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId)
            .HasMaxLength(255)
            .IsRequired();

        builder.HasIndex(x => x.UserId)
            .IsUnique();

        // Navigation property configuration is handled by UserDashboardTileConfiguration
        builder.Property(x => x.LastModified)
            .AsUtcTimestamp()
            .IsRequired();
    }
}