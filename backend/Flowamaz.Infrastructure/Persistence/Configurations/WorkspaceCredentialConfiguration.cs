using Flowamaz.Core.Entities.Connector;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class WorkspaceCredentialConfiguration : IEntityTypeConfiguration<WorkspaceCredential>
{
    public void Configure(EntityTypeBuilder<WorkspaceCredential> builder)
    {
        builder.HasKey(wc => wc.Id);

        builder.Property(wc => wc.Name).HasMaxLength(256).IsRequired();
        builder.Property(wc => wc.AuthType).HasMaxLength(64).IsRequired();

        // Encrypted fields — variable length binary
        builder.Property(wc => wc.EncryptedValue).IsRequired();
        builder.Property(wc => wc.EncryptedKey).IsRequired();
        builder.Property(wc => wc.IV).IsRequired();
        builder.Property(wc => wc.Tag).IsRequired();

        builder.HasIndex(wc => wc.WorkspaceId);

        builder.HasOne(wc => wc.ConnectorDefinition)
            .WithMany()
            .HasForeignKey(wc => wc.ConnectorDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
