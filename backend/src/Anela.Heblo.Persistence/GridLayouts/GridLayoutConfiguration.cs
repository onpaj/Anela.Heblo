using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.GridLayouts;

public class GridLayoutConfiguration : IEntityTypeConfiguration<GridLayout>
{
    public void Configure(EntityTypeBuilder<GridLayout> builder)
    {
        builder.ToTable("GridLayouts", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.GridKey)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.LayoutJson)
            .IsRequired();

        builder.Property(x => x.LastModified)
            .AsUtcTimestamp()
            .IsRequired();

        builder.HasIndex(x => new { x.UserId, x.GridKey })
            .IsUnique();
    }
}
