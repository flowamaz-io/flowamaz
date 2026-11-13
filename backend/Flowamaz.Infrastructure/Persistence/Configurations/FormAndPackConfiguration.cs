using Flowamaz.Core.Entities.Library;
using Flowamaz.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

internal static class StringArrayColumn
{
    public static PropertyBuilder<string[]> AsJsonb(this PropertyBuilder<string[]> property) => property
        .HasConversion(
            new ValueConverter<string[], string>(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<string[]>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? Array.Empty<string>()),
            new ValueComparer<string[]>(
                (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
                v => v.Aggregate(0, (a, x) => HashCode.Combine(a, x.GetHashCode())),
                v => v.ToArray()))
        .HasColumnType("jsonb");
}

public sealed class FormDefinitionConfiguration : IEntityTypeConfiguration<FormDefinition>
{
    public void Configure(EntityTypeBuilder<FormDefinition> builder)
    {
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Slug).HasMaxLength(128).IsRequired();
        builder.Property(f => f.Name).HasMaxLength(200).IsRequired();
        builder.Property(f => f.Description).HasMaxLength(1024).IsRequired();
        builder.Property(f => f.Category).HasMaxLength(64).IsRequired();
        builder.Property(f => f.Version).HasMaxLength(64).IsRequired();
        builder.Property(f => f.PublisherId).HasMaxLength(128);
        builder.Property(f => f.FieldsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(f => f.Tags).AsJsonb();
        builder.Property(f => f.DeprecationNote).HasMaxLength(1024);
        // A slug is unique in the Library and within each workspace's own forms.
        builder.HasIndex(f => new { f.WorkspaceId, f.Slug }).IsUnique().AreNullsDistinct(false);
        builder.HasData(LibraryFormsAndPacksSeed.OfficialForms);
    }
}

public sealed class PackDefinitionConfiguration : IEntityTypeConfiguration<PackDefinition>
{
    public void Configure(EntityTypeBuilder<PackDefinition> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Slug).HasMaxLength(128).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(1024).IsRequired();
        builder.Property(p => p.Category).HasMaxLength(64).IsRequired();
        builder.Property(p => p.Version).HasMaxLength(64).IsRequired();
        builder.Property(p => p.PublisherId).HasMaxLength(128);
        builder.Property(p => p.Connectors).AsJsonb();
        builder.Property(p => p.Templates).AsJsonb();
        builder.Property(p => p.Forms).AsJsonb();
        builder.Property(p => p.Tags).AsJsonb();
        builder.Property(p => p.DeprecationNote).HasMaxLength(1024);
        builder.HasIndex(p => p.Slug).IsUnique();
        builder.HasData(LibraryFormsAndPacksSeed.OfficialPacks);
    }
}
