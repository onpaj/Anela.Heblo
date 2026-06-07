using Anela.Heblo.Domain.Features.Authorization.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Authorization;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("AppUsers", "public");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.EntraObjectId).IsRequired().HasMaxLength(100);
        builder.Property(u => u.Email).IsRequired().HasMaxLength(255);
        builder.Property(u => u.DisplayName).IsRequired().HasMaxLength(255);
        builder.Property(u => u.IsActive).IsRequired();
        builder.Property(u => u.CreatedAt).IsRequired();
        builder.HasIndex(u => u.EntraObjectId).IsUnique();
        builder.HasMany(u => u.UserGroups).WithOne(ug => ug.User)
            .HasForeignKey(ug => ug.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
