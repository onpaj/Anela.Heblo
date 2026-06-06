using Anela.Heblo.Domain.Features.Authorization.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Authorization;

public class PermissionGroupConfiguration : IEntityTypeConfiguration<PermissionGroup>
{
    public void Configure(EntityTypeBuilder<PermissionGroup> builder)
    {
        builder.ToTable("PermissionGroups", "public");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Name).IsRequired().HasMaxLength(100);
        builder.Property(g => g.Description).HasMaxLength(500);
        builder.Property(g => g.IsSystem).IsRequired();
        builder.Property(g => g.CreatedAt).IsRequired();
        builder.Property(g => g.CreatedBy).HasMaxLength(255);
        builder.HasIndex(g => g.Name).IsUnique();
        builder.HasMany(g => g.Permissions).WithOne(p => p.Group)
            .HasForeignKey(p => p.GroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(g => g.Parents).WithOne(gp => gp.Group)
            .HasForeignKey(gp => gp.GroupId).OnDelete(DeleteBehavior.Cascade);
    }
}
