using Anela.Heblo.Domain.Features.Authorization.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Authorization;

public class UserGroupConfiguration : IEntityTypeConfiguration<UserGroup>
{
    public void Configure(EntityTypeBuilder<UserGroup> builder)
    {
        builder.ToTable("UserGroups", "public");
        builder.HasKey(ug => new { ug.UserId, ug.GroupId });
        builder.HasOne(ug => ug.Group).WithMany(g => g.UserGroups)
            .HasForeignKey(ug => ug.GroupId).OnDelete(DeleteBehavior.Cascade);
    }
}
