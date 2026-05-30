using Flowamaz.Core.Entities.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class WebhookEndpointConfiguration : IEntityTypeConfiguration<WebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<WebhookEndpoint> builder)
    {
        builder.HasKey(w => w.Id);
        builder.Property(w => w.EncryptedSecret).IsRequired();
        builder.Property(w => w.Description).HasMaxLength(255);

        builder.Property(w => w.AllowedIps)
            .HasConversion(JsonColumn.Converter<List<string>>(), JsonColumn.Comparer<List<string>>())
            .HasColumnType("jsonb");

        builder.HasIndex(w => w.WorkspaceId);
        builder.HasIndex(w => w.WorkflowDefinitionId);
    }
}
