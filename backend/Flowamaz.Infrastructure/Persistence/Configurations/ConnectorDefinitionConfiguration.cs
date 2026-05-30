using Flowamaz.Core.Entities.Connector;
using Flowamaz.Core.Enums;
using Flowamaz.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class ConnectorDefinitionConfiguration : IEntityTypeConfiguration<ConnectorDefinition>
{
    public void Configure(EntityTypeBuilder<ConnectorDefinition> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.ConnectorId).HasMaxLength(128).IsRequired();
        builder.Property(c => c.PublisherId).HasMaxLength(128).IsRequired();
        builder.Property(c => c.DisplayName).HasMaxLength(256).IsRequired();
        builder.Property(c => c.Version).HasMaxLength(64).IsRequired();
        builder.Property(c => c.Category).HasMaxLength(64).IsRequired();
        builder.Property(c => c.SourceUrl).HasMaxLength(1024);

        builder.Property(c => c.Tier)
            .HasConversion<string>()
            .HasMaxLength(32);

        var tagsConverter = new ValueConverter<string[], string>(
            v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
            v => System.Text.Json.JsonSerializer.Deserialize<string[]>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? Array.Empty<string>());

        var tagsComparer = new ValueComparer<string[]>(
            (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
            v => v.Aggregate(0, (a, x) => HashCode.Combine(a, x.GetHashCode())),
            v => v.ToArray());

        builder.Property(c => c.Tags)
            .HasConversion(tagsConverter, tagsComparer)
            .HasColumnType("jsonb");

        builder.Property(c => c.ManifestJson)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(c => c.AverageRating).HasColumnType("numeric(3,2)");

        builder.HasIndex(c => c.ConnectorId);
        builder.HasIndex(c => c.Category);
        builder.HasIndex(c => c.Tier);

        builder.HasData(ConnectorSeedData.OfficialConnectors);
    }
}
