using Flowamaz.Core.Entities.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flowamaz.Infrastructure.Persistence.Configurations;

public sealed class OrgSsoConfigConfiguration : IEntityTypeConfiguration<OrgSsoConfig>
{
    public void Configure(EntityTypeBuilder<OrgSsoConfig> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Provider).HasMaxLength(16).IsRequired();
        builder.HasIndex(c => c.OrgId).IsUnique();

        builder.Property(c => c.Scopes)
            .HasConversion(JsonColumn.Converter<List<string>>(), JsonColumn.Comparer<List<string>>())
            .HasColumnType("jsonb");
    }
}
