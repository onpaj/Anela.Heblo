using Anela.Heblo.Domain.Features.Article;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Article;

public class ArticleGenerationStepConfiguration : IEntityTypeConfiguration<ArticleGenerationStep>
{
    public void Configure(EntityTypeBuilder<ArticleGenerationStep> builder)
    {
        builder.ToTable("ArticleGenerationSteps");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StepName).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Sequence).IsRequired();
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20);
        builder.Property(x => x.StartedAt).IsRequired();
        builder.Property(x => x.FinishedAt).IsRequired(false);
        builder.Property(x => x.DurationMs).IsRequired(false);
        builder.Property(x => x.Model).IsRequired(false).HasMaxLength(100);
        builder.Property(x => x.InputJson).IsRequired(false).HasColumnType("text");
        builder.Property(x => x.OutputJson).IsRequired(false).HasColumnType("text");
        builder.Property(x => x.ErrorMessage).IsRequired(false).HasMaxLength(2000);

        builder.HasIndex(x => new { x.ArticleId, x.Sequence })
            .HasDatabaseName("IX_ArticleGenerationSteps_ArticleId_Sequence");
    }
}
