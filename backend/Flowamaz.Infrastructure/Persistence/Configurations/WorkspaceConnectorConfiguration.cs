using Flowamaz.Core.Entities.Connector;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class WorkspaceConnectorConfiguration : IEntityTypeConfiguration<WorkspaceConnector>
{
    public void Configure(EntityTypeBuilder<WorkspaceConnector> builder)
    {
        builder.HasKey(wc => wc.Id);

        builder.HasIndex(wc => new { wc.WorkspaceId, wc.ConnectorDefinitionId }).IsUnique();
        builder.HasIndex(wc => wc.WorkspaceId);

        builder.HasOne(wc => wc.ConnectorDefinition)
            .WithMany()
            .HasForeignKey(wc => wc.ConnectorDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(wc => wc.Credential)
            .WithMany()
            .HasForeignKey(wc => wc.CredentialId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);
    }
}
