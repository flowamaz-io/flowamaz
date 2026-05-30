using Flowamaz.Core.Entities.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class ConnectorRatingConfiguration : IEntityTypeConfiguration<ConnectorRating>
{
    public void Configure(EntityTypeBuilder<ConnectorRating> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Review).HasMaxLength(500);
        // One rating per org per connector.
        builder.HasIndex(r => new { r.ConnectorDefinitionId, r.OrgId }).IsUnique();
    }
}

public sealed class ConnectorSubmissionConfiguration : IEntityTypeConfiguration<ConnectorSubmission>
{
    public void Configure(EntityTypeBuilder<ConnectorSubmission> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.ConnectorName).HasMaxLength(128).IsRequired();
        builder.Property(s => s.ManifestYaml).IsRequired();
        builder.Property(s => s.Status).HasMaxLength(16).IsRequired();
        builder.HasIndex(s => s.OrgId);
    }
}
