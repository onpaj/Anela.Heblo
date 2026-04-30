using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.KnowledgeBase;

public class KnowledgeBaseQuestionLogConfiguration : IEntityTypeConfiguration<KnowledgeBaseQuestionLog>
{
    public void Configure(EntityTypeBuilder<KnowledgeBaseQuestionLog> builder)
    {
        builder.ToTable("KnowledgeBaseQuestionLogs", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Question).IsRequired();
        builder.Property(x => x.Answer).IsRequired();
        builder.Property(x => x.TopK).IsRequired();
        builder.Property(x => x.SourceCount).IsRequired();
        builder.Property(x => x.DurationMs).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UserId).IsRequired(false);
        builder.Property(x => x.PrecisionScore).IsRequired(false);
        builder.Property(x => x.StyleScore).IsRequired(false);
        builder.Property(x => x.FeedbackComment).IsRequired(false).HasColumnType("text");
    }
}
