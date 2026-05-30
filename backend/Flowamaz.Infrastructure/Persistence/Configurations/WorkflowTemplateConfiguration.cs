using Flowamaz.Core.Entities.Library;
using Flowamaz.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class WorkflowTemplateConfiguration : IEntityTypeConfiguration<WorkflowTemplate>
{
    public void Configure(EntityTypeBuilder<WorkflowTemplate> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).HasMaxLength(256).IsRequired();
        builder.Property(t => t.Slug).HasMaxLength(128).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(1024).IsRequired();
        builder.Property(t => t.Category).HasMaxLength(64).IsRequired();
        builder.Property(t => t.PreviewImageUrl).HasMaxLength(1024);
        builder.Property(t => t.Version).HasMaxLength(64).IsRequired();
        builder.Property(t => t.ReviewStatus).HasMaxLength(16).IsRequired();
        builder.Property(t => t.YamlContent).IsRequired();
        builder.Property(t => t.AverageRating).HasColumnType("numeric(3,2)");

        var tagsConverter = new ValueConverter<string[], string>(
            v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
            v => System.Text.Json.JsonSerializer.Deserialize<string[]>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? Array.Empty<string>());

        var tagsComparer = new ValueComparer<string[]>(
            (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
            v => v.Aggregate(0, (a, x) => HashCode.Combine(a, x.GetHashCode())),
            v => v.ToArray());

        builder.Property(t => t.Tags)
            .HasConversion(tagsConverter, tagsComparer)
            .HasColumnType("jsonb");

        builder.HasIndex(t => t.Slug).IsUnique();
        builder.HasIndex(t => t.Category);
        builder.HasIndex(t => t.IsOfficial);

        builder.HasData(WorkflowTemplateSeeder.OfficialTemplates);
    }
}
