using Anela.Heblo.Domain.Features.Authorization.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Authorization;

public class GroupParentConfiguration : IEntityTypeConfiguration<GroupParent>
{
    public void Configure(EntityTypeBuilder<GroupParent> builder)
    {
        builder.ToTable("GroupParents", "public");
        builder.HasKey(gp => new { gp.GroupId, gp.ParentGroupId });
        builder.HasOne(gp => gp.ParentGroup).WithMany()
            .HasForeignKey(gp => gp.ParentGroupId).OnDelete(DeleteBehavior.Restrict);
    }
}
