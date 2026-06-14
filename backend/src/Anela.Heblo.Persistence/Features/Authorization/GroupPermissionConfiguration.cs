using Anela.Heblo.Domain.Features.Authorization.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Authorization;

public class GroupPermissionConfiguration : IEntityTypeConfiguration<GroupPermission>
{
    public void Configure(EntityTypeBuilder<GroupPermission> builder)
    {
        builder.ToTable("GroupPermissions", "public");
        builder.HasKey(p => new { p.GroupId, p.PermissionValue });
        builder.Property(p => p.PermissionValue).IsRequired().HasMaxLength(100);
    }
}
